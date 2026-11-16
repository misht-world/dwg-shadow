using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace DwgShadow.Core
{
    /// <summary>Building: outer contour, courtyards (holes) and height.</summary>
    public class Building
    {
        public PathD Outer;
        public PathsD Holes = new PathsD();
        public double HeightM;
        /// <summary>Index of the source outer contour.</summary>
        public int SourceIndex;
        /// <summary>Indices of the source courtyard contours.</summary>
        public List<int> HoleIndices = new List<int>();
    }

    /// <summary>Polygon with holes — one result item.</summary>
    public class PolygonWithHoles
    {
        public PathD Outer;
        public List<PathD> Holes = new List<PathD>();
    }

    /// <summary>
    /// Shadow geometry. The shadow of a prism with footprint P and shadow offset d is the
    /// Minkowski sum P ⊕ [0, d] = P ∪ (P + d) ∪ the sweeps of all edges.
    /// All boolean operations use Clipper2, which is robust to degenerate input.
    /// </summary>
    public static class ShadowGeometry
    {
        /// <summary>Decimal places used by Clipper (0.1 mm for metre drawings).</summary>
        public const int Precision = 4;

        const double Eps = 1e-9;

        /// <summary>Shadow of one building for offset (dx, dy) in drawing units.</summary>
        public static PathsD BuildingShadow(Building b, double dx, double dy)
        {
            var src = new PathsD { b.Outer };
            src.AddRange(b.Holes);
            // Normalize: proper orientation of outer contour and courtyards, no self-intersections.
            PathsD footprint = Run(ClipType.Union, src, null, FillRule.EvenOdd);
            if (footprint.Count == 0) return footprint;

            if (Math.Abs(dx) < Eps && Math.Abs(dy) < Eps)
                return footprint;

            var moved = new PathsD(footprint.Select(p => Clipper.TranslatePath(p, dx, dy)));

            var quads = new PathsD();
            foreach (PathD path in footprint)
            {
                int n = path.Count;
                for (int i = 0; i < n; i++)
                {
                    PointD a = path[i];
                    PointD c = path[(i + 1) % n];
                    double ex = c.x - a.x, ey = c.y - a.y;
                    double cross = ex * dy - ey * dx;
                    double len = Math.Sqrt(ex * ex + ey * ey) * Math.Sqrt(dx * dx + dy * dy);
                    if (len < Eps || Math.Abs(cross) / len < 1e-9) continue; // edge parallel to the shadow

                    var q = new PathD(4)
                    {
                        a, c,
                        new PointD(c.x + dx, c.y + dy),
                        new PointD(a.x + dx, a.y + dy)
                    };
                    if (!Clipper.IsPositive(q)) q.Reverse();
                    quads.Add(q);
                }
            }

            PathsD both = Run(ClipType.Union, footprint, moved, FillRule.NonZero);
            return Run(ClipType.Union, both, quads, FillRule.NonZero);
        }

        /// <summary>Building footprint (with courtyards) as normalized paths.</summary>
        public static PathsD Footprint(Building b)
        {
            var src = new PathsD { b.Outer };
            src.AddRange(b.Holes);
            return Run(ClipType.Union, src, null, FillRule.EvenOdd);
        }

        /// <summary>Union of polygon sets (each set already normalized).</summary>
        public static PathsD UnionAll(IEnumerable<PathsD> parts)
        {
            var c = new ClipperD(Precision);
            foreach (PathsD p in parts)
                if (p != null && p.Count > 0) c.AddSubject(p);
            var result = new PathsD();
            c.Execute(ClipType.Union, FillRule.NonZero, result);
            return result;
        }

        public static PathsD Subtract(PathsD a, PathsD b) => Run(ClipType.Difference, a, b, FillRule.NonZero);

        /// <summary>Area of the intersection of two polygon sets.</summary>
        public static double OverlapArea(PathsD a, PathsD b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return 0;
            return Math.Abs(Clipper.Area(Run(ClipType.Intersection, a, b, FillRule.NonZero)));
        }

        /// <summary>Splits the result into polygons with holes (islands inside courtyards included).</summary>
        public static List<PolygonWithHoles> ToPolygons(PathsD paths, double minArea = 1e-6)
        {
            var c = new ClipperD(Precision);
            c.AddSubject(paths);
            var tree = new PolyTreeD();
            c.Execute(ClipType.Union, FillRule.NonZero, tree);

            var list = new List<PolygonWithHoles>();
            for (int i = 0; i < tree.Count; i++)
                Collect((PolyPathD)tree[i], list, minArea);
            return list;
        }

        static void Collect(PolyPathD node, List<PolygonWithHoles> list, double minArea)
        {
            if (node.Polygon == null) return;
            if (Math.Abs(Clipper.Area(node.Polygon)) < minArea) return;

            var poly = new PolygonWithHoles { Outer = node.Polygon };
            for (int i = 0; i < node.Count; i++)
            {
                PolyPathD hole = (PolyPathD)node[i];
                if (hole.Polygon != null && Math.Abs(Clipper.Area(hole.Polygon)) >= minArea)
                    poly.Holes.Add(hole.Polygon);
                for (int k = 0; k < hole.Count; k++)
                    Collect((PolyPathD)hole[k], list, minArea); // islands inside courtyards
            }
            list.Add(poly);
        }

        static PathsD Run(ClipType type, PathsD subject, PathsD clip, FillRule rule)
        {
            var c = new ClipperD(Precision);
            c.AddSubject(subject);
            if (clip != null && clip.Count > 0) c.AddClip(clip);
            var result = new PathsD();
            c.Execute(type, rule, result);
            return result;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Appends intermediate points of the arc from a to b with the given bulge
        /// (as in AutoCAD polylines). Points a and b themselves are not added.
        /// </summary>
        public static void AddArcPoints(PathD path, PointD a, PointD b, double bulge, double maxStepDeg = 5.0)
        {
            if (Math.Abs(bulge) < 1e-9) return;
            double chordX = b.x - a.x, chordY = b.y - a.y;
            double chord = Math.Sqrt(chordX * chordX + chordY * chordY);
            if (chord < Eps) return;

            double sweep = 4.0 * Math.Atan(bulge); // signed: + is counter-clockwise
            double radius = chord * (1 + bulge * bulge) / (4.0 * Math.Abs(bulge));

            // Centre: from the chord midpoint along the left normal by a signed distance
            double mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
            double s = chord * (1 - bulge * bulge) / (4.0 * bulge);
            double nx = -chordY / chord, ny = chordX / chord; // left normal of the chord
            double cx = mx + nx * s;
            double cy = my + ny * s;

            double start = Math.Atan2(a.y - cy, a.x - cx);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (maxStepDeg * Math.PI / 180.0)));
            for (int i = 1; i < steps; i++)
            {
                double ang = start + sweep * i / steps;
                path.Add(new PointD(cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang)));
            }
        }

        /// <summary>Point-in-polygon test (ray casting).</summary>
        public static bool PointInPolygon(PathD poly, double x, double y)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                PointD pi = poly[i], pj = poly[j];
                if ((pi.y > y) != (pj.y > y) &&
                    x < (pj.x - pi.x) * (y - pi.y) / (pj.y - pi.y) + pi.x)
                    inside = !inside;
            }
            return inside;
        }

        public static RectD Bounds(PathD p)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (PointD pt in p)
            {
                if (pt.x < x0) x0 = pt.x;
                if (pt.y < y0) y0 = pt.y;
                if (pt.x > x1) x1 = pt.x;
                if (pt.y > y1) y1 = pt.y;
            }
            return new RectD(x0, y0, x1, y1);
        }
    }
}
