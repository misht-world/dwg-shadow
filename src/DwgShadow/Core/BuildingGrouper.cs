using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace DwgShadow.Core
{
    /// <summary>
    /// Splits closed contours into buildings and courtyards by nesting.
    /// A contour nested in a building is a courtyard unless it is marked as a separate
    /// building part (e.g. it has its own height label — a tower on a podium).
    /// </summary>
    public static class BuildingGrouper
    {
        public class Item
        {
            public int Index;
            public PathD Path;
            public double Area;
            public RectD Box;
            public int Parent = -1; // index in items
            public bool IsHole;
        }

        /// <param name="contours">Closed contours.</param>
        /// <param name="courtyards">Treat nested contours as courtyards.</param>
        /// <param name="isSeparatePart">The contour is a separate building part (not a courtyard); may be null.</param>
        /// <returns>Buildings (height not set; SourceIndex = contour index).</returns>
        public static List<Building> Group(IList<PathD> contours, bool courtyards, Func<int, bool> isSeparatePart = null)
        {
            var items = new List<Item>();
            for (int i = 0; i < contours.Count; i++)
            {
                PathD p = contours[i];
                if (p == null || p.Count < 3) continue;
                double area = Math.Abs(Clipper.Area(p));
                if (area < 1e-9) continue;
                items.Add(new Item { Index = i, Path = p, Area = area, Box = ShadowGeometry.Bounds(p) });
            }

            var buildings = new List<Building>();
            if (!courtyards)
            {
                foreach (Item it in items)
                    buildings.Add(new Building { Outer = it.Path, SourceIndex = it.Index });
                return buildings;
            }

            // Largest first: the parent is the smallest already processed contour containing the current one.
            items.Sort((a, b) => b.Area.CompareTo(a.Area));
            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    Item cand = items[j];
                    if (cand.Area <= cur.Area) continue;
                    if (!Contains(cand.Box, cur.Box)) continue;
                    if (IsInside(cur.Path, cand.Path)) { cur.Parent = j; break; }
                }
            }

            // A courtyard is a contour nested in a building (not in a courtyard) and not marked as a part.
            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];
                if (cur.Parent < 0) continue;
                Item parent = items[cur.Parent];
                bool separate = isSeparatePart != null && isSeparatePart(cur.Index);
                cur.IsHole = !parent.IsHole && !separate;
            }

            var map = new Dictionary<int, Building>();
            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];
                if (cur.IsHole) continue;
                var b = new Building { Outer = cur.Path, SourceIndex = cur.Index };
                map[i] = b;
                buildings.Add(b);
            }
            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];
                if (cur.IsHole && map.TryGetValue(cur.Parent, out Building owner))
                {
                    owner.Holes.Add(cur.Path);
                    owner.HoleIndices.Add(cur.Index);
                }
            }
            return buildings;
        }

        static bool Contains(RectD outer, RectD inner) =>
            inner.left >= outer.left - 1e-9 && inner.right <= outer.right + 1e-9 &&
            inner.top >= outer.top - 1e-9 && inner.bottom <= outer.bottom + 1e-9;

        /// <summary>Most vertices of inner lie inside outer (robust to touching edges).</summary>
        static bool IsInside(PathD inner, PathD outer)
        {
            int step = Math.Max(1, inner.Count / 12);
            int inside = 0, total = 0;
            for (int k = 0; k < inner.Count; k += step)
            {
                total++;
                if (ShadowGeometry.PointInPolygon(outer, inner[k].x, inner[k].y)) inside++;
            }
            return inside * 2 > total;
        }
    }
}
