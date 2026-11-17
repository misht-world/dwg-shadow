using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Clipper2Lib;
using DwgShadow.Core;

namespace DwgShadow.Cad
{
    /// <summary>Source contour of a shadow and the parameters its building was built with.</summary>
    public struct SourceRef
    {
        public ObjectId Id;
        public bool HasParams;
        public double HeightM, DirectionDeg, AltitudeDeg;

        public static SourceRef Of(ObjectId id, double heightM, ShadowDirection dir) => new SourceRef
        {
            Id = id, HasParams = true, HeightM = heightM, DirectionDeg = dir.DirectionDeg, AltitudeDeg = dir.AltitudeDeg
        };
    }

    /// <summary>A shadow object already in the drawing, with the buildings it was built from.</summary>
    public class TaggedObject
    {
        public ObjectId Id;
        public string Layer;
        public List<SourceRef> Sources;  // null = not created by DwgShadow
        public PathD Outline;            // closed polyline outline, otherwise null
    }

    /// <summary>
    /// Tags on created objects: an extension-dictionary Xrecord with soft pointers to the source
    /// building contours, each followed by the height and sun used for that building.
    /// Soft pointers survive copy/paste and WBLOCK.
    /// </summary>
    public static class ShadowTags
    {
        public const string Key = "DWGSHADOW";

        public static void Write(Transaction tr, Entity ent, IEnumerable<SourceRef> sources)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var values = new List<TypedValue> { new TypedValue((int)DxfCode.Text, "v2") };
            var seen = new HashSet<ObjectId>();
            foreach (SourceRef r in sources)
            {
                if (!seen.Add(r.Id)) continue;
                values.Add(new TypedValue((int)DxfCode.SoftPointerId, r.Id));
                if (!r.HasParams) continue;
                values.Add(new TypedValue((int)DxfCode.Real, r.HeightM));
                values.Add(new TypedValue((int)DxfCode.Real, r.DirectionDeg));
                values.Add(new TypedValue((int)DxfCode.Real, r.AltitudeDeg));
            }
            var xr = new Xrecord { Data = new ResultBuffer(values.ToArray()) };
            dict.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        /// <summary>Sources of a DwgShadow object, or null for foreign objects. Reads v1 (ids only) and v2.</summary>
        public static List<SourceRef> Read(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return null;
            var dict = tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (dict == null || !dict.Contains(Key)) return null;
            var xr = tr.GetObject(dict.GetAt(Key), OpenMode.ForRead) as Xrecord;
            var list = new List<SourceRef>();
            if (xr?.Data == null) return list;
            var reals = new List<double>();
            void Flush()
            {
                if (list.Count == 0) return;
                if (reals.Count >= 3)
                {
                    SourceRef last = list[list.Count - 1];
                    last.HasParams = true;
                    last.HeightM = reals[0]; last.DirectionDeg = reals[1]; last.AltitudeDeg = reals[2];
                    list[list.Count - 1] = last;
                }
                reals.Clear();
            }
            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode == (int)DxfCode.SoftPointerId && tv.Value is ObjectId id)
                {
                    Flush();
                    list.Add(new SourceRef { Id = id });
                }
                else if (tv.TypeCode == (int)DxfCode.Real && tv.Value is double d) reals.Add(d);
            }
            Flush();
            return list;
        }

        /// <summary>All objects in model space on layers matching the predicate.</summary>
        public static List<TaggedObject> Scan(Transaction tr, Database db, Func<string, bool> layerMatch)
        {
            var list = new List<TaggedObject>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !layerMatch(ent.Layer)) continue;
                var t = new TaggedObject { Id = id, Layer = ent.Layer };
                try { t.Sources = Read(tr, ent); } catch { t.Sources = null; }
                if (ent is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3)
                {
                    t.Outline = new PathD(pl.NumberOfVertices);
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        Point2d p = pl.GetPoint2dAt(i);
                        t.Outline.Add(new PointD(p.X, p.Y));
                    }
                }
                list.Add(t);
            }
            return list;
        }
    }

    /// <summary>Writes the result into the drawing. Everything runs inside the given transaction.</summary>
    public class CadWriter
    {
        readonly Database _db;
        readonly Transaction _tr;
        readonly BlockTableRecord _space;
        readonly List<ObjectId> _hatches = new List<ObjectId>();
        readonly List<ObjectId> _outlines = new List<ObjectId>();

        public int Contours { get; private set; }
        public int Hatches { get; private set; }
        public int HatchFailures { get; private set; }

        public CadWriter(Database db, Transaction tr)
        {
            _db = db;
            _tr = tr;
            _space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        }

        /// <summary>Creates the layer if needed. A locked or frozen layer is an error with a clear message.</summary>
        public void EnsureLayer(string name, short colorIndex)
        {
            SymbolUtilityServices.ValidateSymbolName(name, false);
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                var ltr = (LayerTableRecord)_tr.GetObject(lt[name], OpenMode.ForRead);
                if (ltr.IsLocked) throw new InvalidOperationException(L.F("msg.locked", name));
                if (ltr.IsFrozen) throw new InvalidOperationException(L.F("msg.frozen", name));
                return;
            }
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex >= 1 && colorIndex <= 255 ? colorIndex : (short)7)
            };
            lt.Add(rec);
            _tr.AddNewlyCreatedDBObject(rec, true);
        }

        public int Erase(IEnumerable<ObjectId> ids)
        {
            int n = 0;
            foreach (ObjectId id in ids)
            {
                if (id.IsErased) continue;
                var ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
                n++;
            }
            return n;
        }

        public void WritePolygon(PolygonWithHoles poly, string layer, short color, bool contour, bool hatch,
                                 ShadowSettings s, ICollection<SourceRef> sources)
        {
            if (contour)
            {
                AddPolyline(poly.Outer, layer, color, sources);
                foreach (PathD h in poly.Holes) AddPolyline(h, layer, color, sources);
            }
            if (hatch) AddHatch(poly, layer, color, s, sources);
        }

        ObjectId AddPolyline(PathD path, string layer, short color, ICollection<SourceRef> sources)
        {
            var pl = new Polyline(path.Count);
            pl.SetDatabaseDefaults(_db);
            for (int i = 0; i < path.Count; i++)
                pl.AddVertexAt(i, new Point2d(path[i].x, path[i].y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.ColorIndex = color;
            ObjectId id = _space.AppendEntity(pl);
            _tr.AddNewlyCreatedDBObject(pl, true);
            ShadowTags.Write(_tr, pl, sources);
            _outlines.Add(id);
            Contours++;
            return id;
        }

        void AddHatch(PolygonWithHoles poly, string layer, short color, ShadowSettings s, ICollection<SourceRef> sources)
        {
            Hatch h = null;
            try
            {
                h = new Hatch();
                h.SetDatabaseDefaults(_db);
                h.Layer = layer;
                h.ColorIndex = color;
                ObjectId id = _space.AppendEntity(h);
                _tr.AddNewlyCreatedDBObject(h, true);

                string pattern = string.IsNullOrWhiteSpace(s.HatchPattern) ? "SOLID" : s.HatchPattern.Trim().ToUpperInvariant();
                // Scale is given "in metres" so the pattern does not collapse in millimetre drawings.
                if (pattern != "SOLID") h.PatternScale = s.HatchScale * s.UnitsPerMeter;
                h.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                h.Associative = false;

                h.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, Loop(poly.Outer, out DoubleCollection b0), b0);
                foreach (PathD hole in poly.Holes)
                    h.AppendLoop(HatchLoopTypes.Default | HatchLoopTypes.Polyline, Loop(hole, out DoubleCollection b1), b1);
                h.EvaluateHatch(true);

                if (s.HatchTransparency > 0)
                    h.Transparency = new Transparency((byte)Math.Round(255 * (1 - s.HatchTransparency / 100.0)));
                ShadowTags.Write(_tr, h, sources);
                _hatches.Add(id);
                Hatches++;
            }
            catch
            {
                // Hatch failed on a very complex contour — keep the outline, drop the hatch.
                HatchFailures++;
                try { if (h != null && !h.IsErased && h.ObjectId.IsValid) h.Erase(); } catch { }
            }
        }

        static Point2dCollection Loop(PathD path, out DoubleCollection bulges)
        {
            var pts = new Point2dCollection();
            bulges = new DoubleCollection();
            foreach (PointD p in path)
            {
                pts.Add(new Point2d(p.x, p.y));
                bulges.Add(0);
            }
            pts.Add(new Point2d(path[0].x, path[0].y)); // hatch loops must be explicitly closed
            bulges.Add(0);
            return pts;
        }

        public void AddText(string text, double x, double y, double height, string layer, short color, ICollection<SourceRef> sources)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(_db);
            t.TextString = text;
            t.Height = height;
            t.Position = new Point3d(x, y, 0);
            t.Layer = layer;
            t.ColorIndex = color;
            _space.AppendEntity(t);
            _tr.AddNewlyCreatedDBObject(t, true);
            ShadowTags.Write(_tr, t, sources);
        }

        /// <summary>
        /// Puts the created shadows (hatches, then outlines) right below the lowest object on the
        /// building layers, so shadows never cover building contours or fills — also after rebuilds.
        /// Objects below the buildings (underlays, base maps) stay below the shadows.
        /// </summary>
        public void PlaceUnderBuildings(IEnumerable<ObjectId> buildingContours)
        {
            var shadows = _hatches.Concat(_outlines).Where(id => !id.IsErased).ToArray();
            if (shadows.Length == 0) return;

            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in buildingContours)
                if (!id.IsErased && _tr.GetObject(id, OpenMode.ForRead) is Entity e) layers.Add(e.Layer);

            var dot = (DrawOrderTable)_tr.GetObject(_space.DrawOrderTableId, OpenMode.ForWrite);
            var mine = new HashSet<ObjectId>(shadows);
            ObjectId target = ObjectId.Null;
            foreach (ObjectId id in dot.GetFullDrawOrder(0))      // bottom → top
            {
                if (mine.Contains(id) || id.IsErased) continue;
                if (_tr.GetObject(id, OpenMode.ForRead) is Entity e && layers.Contains(e.Layer)) { target = id; break; }
            }
            // Two passes keep hatches under the outlines: [... hatches, outlines, buildings ...]
            var hatches = new ObjectIdCollection(_hatches.Where(id => !id.IsErased).ToArray());
            var outlines = new ObjectIdCollection(_outlines.Where(id => !id.IsErased).ToArray());
            if (target.IsNull)
            {
                if (outlines.Count > 0) dot.MoveToBottom(outlines);
                if (hatches.Count > 0) dot.MoveToBottom(hatches);
            }
            else
            {
                if (hatches.Count > 0) dot.MoveBelow(hatches, target);
                if (outlines.Count > 0) dot.MoveBelow(outlines, target);
            }
        }
    }
}
