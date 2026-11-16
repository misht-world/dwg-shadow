using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Clipper2Lib;
using DwgShadow.Core;

namespace DwgShadow.Cad
{
    /// <summary>Text label with candidate points and numeric value.</summary>
    public class Label
    {
        /// <summary>Candidate points, most reliable first.</summary>
        public List<PointD> Points = new List<PointD>();
        public double Value;
    }

    /// <summary>Closed contour read from the drawing.</summary>
    public class Contour
    {
        public ObjectId Id;
        public PathD Path;
    }

    /// <summary>Reads contours and labels from the drawing.</summary>
    public static class CadReader
    {
        const string ContourTypes = "LWPOLYLINE,POLYLINE,CIRCLE";

        public static List<string> LayerNames(Database db)
        {
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!ltr.IsErased && !ltr.IsDependent) names.Add(ltr.Name);
                }
                tr.Commit();
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        public static bool IsContourClass(ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return false;
            string n = id.ObjectClass.DxfName;
            return n == "LWPOLYLINE" || n == "POLYLINE" || n == "CIRCLE";
        }

        /// <summary>Contour objects on the given layers in model space.</summary>
        public static List<ObjectId> ContoursOnLayers(Database db, ICollection<string> layers)
        {
            var set = new HashSet<string>(layers, StringComparer.OrdinalIgnoreCase);
            var ids = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!IsContourClass(id)) continue;
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (set.Contains(ent.Layer)) ids.Add(id);
                }
                tr.Commit();
            }
            return ids;
        }

        /// <summary>User selection of contours. Null when cancelled.</summary>
        public static List<ObjectId> SelectContours(Editor ed)
        {
            var opts = new PromptSelectionOptions { MessageForAdding = L.T("msg.select"), AllowDuplicates = false };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, ContourTypes) });
            PromptSelectionResult res = ed.GetSelection(opts, filter);
            if (res.Status != PromptStatus.OK) return null;
            return res.Value.GetObjectIds().ToList();
        }

        /// <summary>Reads contours. Open and unsupported objects are skipped.</summary>
        public static List<Contour> ReadContours(Database db, IEnumerable<ObjectId> ids, out int skipped)
        {
            skipped = 0;
            var result = new List<Contour>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    PathD p = null;
                    try
                    {
                        if (!id.IsErased && IsContourClass(id))
                            p = ToPath(tr, tr.GetObject(id, OpenMode.ForRead) as Entity);
                    }
                    catch { p = null; }

                    if (p == null || p.Count < 3) skipped++;
                    else result.Add(new Contour { Id = id, Path = p });
                }
                tr.Commit();
            }
            return result;
        }

        /// <summary>
        /// Adds contours (from the same layers) that lie inside the picked ones: courtyards
        /// and building parts belong to the building even if the user picked only its outline.
        /// </summary>
        public static int AddNested(Database db, List<Contour> picked)
        {
            if (picked.Count == 0) return 0;
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (Contour c in picked) layers.Add(((Entity)tr.GetObject(c.Id, OpenMode.ForRead)).Layer);
                tr.Commit();
            }
            var have = new HashSet<ObjectId>(picked.Select(c => c.Id));
            var boxes = picked.Select(c => ShadowGeometry.Bounds(c.Path)).ToList();
            var candidates = ContoursOnLayers(db, layers).Where(id => !have.Contains(id)).ToList();
            int added = 0;
            foreach (Contour c in ReadContours(db, candidates, out _))
            {
                RectD box = ShadowGeometry.Bounds(c.Path);
                for (int i = 0; i < picked.Count && i < boxes.Count; i++)
                {
                    RectD b = boxes[i];
                    if (box.left < b.left || box.right > b.right || box.top < b.top || box.bottom > b.bottom) continue;
                    if (MostlyInside(c.Path, picked[i].Path)) { picked.Add(c); added++; break; }
                }
            }
            return added;
        }

        static bool MostlyInside(PathD inner, PathD outer)
        {
            int step = Math.Max(1, inner.Count / 12), inside = 0, total = 0;
            for (int k = 0; k < inner.Count; k += step)
            {
                total++;
                if (ShadowGeometry.PointInPolygon(outer, inner[k].x, inner[k].y)) inside++;
            }
            return inside * 2 > total;
        }

        static PathD ToPath(Transaction tr, Entity ent)
        {
            switch (ent)
            {
                case Polyline pl:
                    return FromLwPolyline(pl);
                case Polyline2d p2:
                    return FromPolyline2d(tr, p2);
                case Circle c:
                {
                    var path = new PathD();
                    const int n = 72;
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2 * Math.PI * i / n;
                        path.Add(new PointD(c.Center.X + c.Radius * Math.Cos(a), c.Center.Y + c.Radius * Math.Sin(a)));
                    }
                    return path;
                }
                default:
                    return null;
            }
        }

        static PathD FromLwPolyline(Polyline pl)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return null;
            Point3d first = pl.GetPoint3dAt(0), last = pl.GetPoint3dAt(n - 1);
            bool closedByPoints = first.DistanceTo(last) < 1e-6;
            if (!pl.Closed && !closedByPoints) return null;
            // A polyline mirrored into another plane flips its bulge sign in world coordinates
            double flip = pl.Normal.Z < 0 ? -1 : 1;
            int count = closedByPoints ? n - 1 : n;

            var path = new PathD();
            for (int i = 0; i < count; i++)
            {
                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt((i + 1) % n);
                path.Add(new PointD(a.X, a.Y));
                double bulge = pl.GetBulgeAt(i) * flip;
                if (Math.Abs(bulge) > 1e-9)
                    ShadowGeometry.AddArcPoints(path, new PointD(a.X, a.Y), new PointD(b.X, b.Y), bulge);
            }
            return path;
        }

        static PathD FromPolyline2d(Transaction tr, Polyline2d p2)
        {
            var pts = new List<Point3d>();
            var bulges = new List<double>();
            foreach (ObjectId vid in p2)
            {
                var v = (Vertex2d)tr.GetObject(vid, OpenMode.ForRead);
                if (v.VertexType == Vertex2dType.SplineControlVertex) continue;
                pts.Add(p2.VertexPosition(v));
                bulges.Add(v.Bulge);
            }
            if (pts.Count < 2) return null;
            bool closedByPoints = pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-6;
            if (!p2.Closed && !closedByPoints) return null;
            if (closedByPoints) { pts.RemoveAt(pts.Count - 1); bulges.RemoveAt(bulges.Count - 1); }
            double flip = p2.Normal.Z < 0 ? -1 : 1;

            var path = new PathD();
            for (int i = 0; i < pts.Count; i++)
            {
                Point3d a = pts[i], b = pts[(i + 1) % pts.Count];
                path.Add(new PointD(a.X, a.Y));
                double bulge = bulges[i] * flip;
                if (Math.Abs(bulge) > 1e-9)
                    ShadowGeometry.AddArcPoints(path, new PointD(a.X, a.Y), new PointD(b.X, b.Y), bulge);
            }
            return path;
        }

        /// <summary>Numeric labels (TEXT/MTEXT) in model space. Empty layer = any layer.</summary>
        public static List<Label> ReadLabels(Database db, string layer)
        {
            var list = new List<Label>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    string dxf = id.ObjectClass.DxfName;
                    if (dxf != "TEXT" && dxf != "MTEXT") continue;
                    try
                    {
                        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (!string.IsNullOrEmpty(layer) && !string.Equals(ent.Layer, layer, StringComparison.OrdinalIgnoreCase))
                            continue;
                        string text = ent is DBText t ? t.TextString : ent is MText m ? m.Text : null;
                        double? v = ShadowCalc.ParseNumber(text);
                        if (v == null) continue;

                        // The insertion point decides which contour a label belongs to (large text may
                        // stick out of the contour); the extents centre is a fallback.
                        var lb = new Label { Value = v.Value };
                        Point3d ins = ent is DBText t2
                            ? (t2.Justify == AttachmentPoint.BaseLeft ? t2.Position : t2.AlignmentPoint)
                            : ((MText)ent).Location;
                        lb.Points.Add(new PointD(ins.X, ins.Y));
                        try
                        {
                            Extents3d ext = ent.GeometricExtents;
                            Point3d c = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) / 2.0;
                            lb.Points.Add(new PointD(c.X, c.Y));
                        }
                        catch { }
                        list.Add(lb);
                    }
                    catch { /* skip broken text */ }
                }
                tr.Commit();
            }
            return list;
        }

        /// <summary>Assigns each label to the smallest contour containing it. Key = contour index.</summary>
        public static Dictionary<int, double> AssignLabels(IList<PathD> contours, IEnumerable<Label> labels)
        {
            var areas = contours.Select(c => Math.Abs(Clipper.Area(c))).ToArray();
            var boxes = contours.Select(ShadowGeometry.Bounds).ToArray();
            var result = new Dictionary<int, double>();
            foreach (Label lb in labels)
            {
                foreach (PointD pt in lb.Points)
                {
                    int best = -1;
                    for (int i = 0; i < contours.Count; i++)
                    {
                        RectD b = boxes[i];
                        if (pt.x < b.left || pt.x > b.right || pt.y < b.top || pt.y > b.bottom) continue;
                        if (!ShadowGeometry.PointInPolygon(contours[i], pt.x, pt.y)) continue;
                        if (best < 0 || areas[i] < areas[best]) best = i;
                    }
                    if (best < 0) continue;
                    if (!result.ContainsKey(best)) result[best] = lb.Value;
                    break;
                }
            }
            return result;
        }
    }
}
