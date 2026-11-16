using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Clipper2Lib;

namespace DwgShadow.Core
{
    /// <summary>Direction and slope of sun rays used for building.</summary>
    public struct ShadowDirection
    {
        /// <summary>Shadow direction in the drawing: degrees from the X axis, counter-clockwise.</summary>
        public double DirectionDeg;
        /// <summary>Sun altitude, degrees.</summary>
        public double AltitudeDeg;
        /// <summary>Sun azimuth (from north, clockwise) — astronomical mode only.</summary>
        public double? SunAzimuthDeg;
        /// <summary>The sun is below the safety limit — no shadow is built.</summary>
        public bool BelowThreshold;

        /// <summary>Shadow length divided by building height.</summary>
        public double LengthRatio => 1.0 / Math.Tan(AltitudeDeg * Math.PI / 180.0);

        /// <summary>Shadow offset (drawing units) for a building of <paramref name="heightM"/> metres.</summary>
        public void Offset(double heightM, double unitsPerMeter, out double dx, out double dy)
        {
            double len = heightM * LengthRatio * unitsPerMeter;
            double a = DirectionDeg * Math.PI / 180.0;
            dx = len * Math.Cos(a);
            dy = len * Math.Sin(a);
        }
    }

    /// <summary>Shadows of a set of buildings.</summary>
    public class ShadowResult
    {
        /// <summary>Union of all shadows (buildings cut out if requested).</summary>
        public PathsD Union = new PathsD();
        /// <summary>Shadow of each building, aligned with the input list; null when the building failed.</summary>
        public List<PathsD> PerBuilding = new List<PathsD>();
        /// <summary>Source indices of buildings that failed.</summary>
        public List<int> Failed = new List<int>();
    }

    public static class ShadowCalc
    {
        /// <summary>Shadow direction from settings (the series passes the time explicitly).</summary>
        public static ShadowDirection Direction(ShadowSettings s, TimeSpan? timeOverride = null, bool forceAstronomical = false)
        {
            if (s.SunMode == SunMode.Manual && !forceAstronomical)
            {
                return new ShadowDirection
                {
                    DirectionDeg = s.ManualDirection,
                    AltitudeDeg = s.ManualAltitude,
                    BelowThreshold = s.ManualAltitude < s.MinAltitude
                };
            }

            TimeSpan time;
            if (timeOverride.HasValue) time = timeOverride.Value;
            else if (!ShadowSettings.TryParseTime(s.Time, out time)) time = new TimeSpan(12, 0, 0);

            SunState sun = SunPosition.Compute(s.Latitude, s.Longitude, s.Date.Date + time, s.UtcOffset);
            return FromSun(sun, s.UseNorthAngle ? s.NorthAngle : 0.0, s.MinAltitude);
        }

        /// <summary>Converts sun azimuth into the shadow direction in the drawing.</summary>
        public static ShadowDirection FromSun(SunState sun, double northAngleDeg, double minAltitude)
        {
            // North in the drawing is the Y axis (90° from X) rotated by northAngle counter-clockwise.
            // The shadow points away from the sun (azimuth + 180°); azimuth runs clockwise.
            double shadowAzimuth = sun.AzimuthDeg + 180.0;
            double dir = 90.0 + northAngleDeg - shadowAzimuth;
            dir = ((dir % 360.0) + 360.0) % 360.0;
            return new ShadowDirection
            {
                DirectionDeg = dir,
                AltitudeDeg = sun.AltitudeDeg,
                SunAzimuthDeg = sun.AzimuthDeg,
                BelowThreshold = sun.AltitudeDeg < minAltitude
            };
        }

        /// <summary>Series time points from start to end inclusive.</summary>
        public static List<TimeSpan> SeriesTimes(ShadowSettings s)
        {
            var list = new List<TimeSpan>();
            if (!ShadowSettings.TryParseTime(s.SeriesStart, out TimeSpan a) ||
                !ShadowSettings.TryParseTime(s.SeriesEnd, out TimeSpan b) || s.SeriesStep <= 0)
                return list;
            for (TimeSpan t = a; t <= b && list.Count < 500; t += TimeSpan.FromMinutes(s.SeriesStep))
                list.Add(t);
            return list;
        }

        /// <summary>Shadows of all buildings. A failing building is skipped and reported in <see cref="ShadowResult.Failed"/>.</summary>
        public static ShadowResult Compute(IList<Building> buildings, ShadowDirection dir, double unitsPerMeter, bool subtractFootprints)
        {
            var r = new ShadowResult();
            var footprints = new List<PathsD>();
            foreach (Building b in buildings)
            {
                try
                {
                    dir.Offset(b.HeightM, unitsPerMeter, out double dx, out double dy);
                    r.PerBuilding.Add(ShadowGeometry.BuildingShadow(b, dx, dy));
                    if (subtractFootprints) footprints.Add(ShadowGeometry.Footprint(b));
                }
                catch
                {
                    r.PerBuilding.Add(null);
                    r.Failed.Add(b.SourceIndex);
                }
            }
            r.Union = ShadowGeometry.UnionAll(r.PerBuilding);
            if (subtractFootprints && footprints.Count > 0)
                r.Union = ShadowGeometry.Subtract(r.Union, ShadowGeometry.UnionAll(footprints));
            return r;
        }

        /// <summary>Shadows of all buildings as one set of paths.</summary>
        public static PathsD Shadows(IList<Building> buildings, ShadowDirection dir, double unitsPerMeter,
                                     bool subtractFootprints, List<int> failed = null)
        {
            ShadowResult r = Compute(buildings, dir, unitsPerMeter, subtractFootprints);
            failed?.AddRange(r.Failed);
            return r.Union;
        }

        /// <summary>
        /// For each result polygon — indices of buildings whose shadow overlaps it
        /// (used to tag drawn objects with their source buildings).
        /// </summary>
        public static List<List<int>> Attribute(IList<PolygonWithHoles> polys, ShadowResult r)
        {
            var boxes = r.PerBuilding.Select(p => p == null || p.Count == 0 ? (RectD?)null : Clipper.GetBounds(p)).ToList();
            var result = new List<List<int>>();
            foreach (PolygonWithHoles poly in polys)
            {
                var list = new List<int>();
                RectD pb = ShadowGeometry.Bounds(poly.Outer);
                var subject = new PathsD { poly.Outer };
                for (int i = 0; i < r.PerBuilding.Count; i++)
                {
                    if (boxes[i] == null || !Intersects(pb, boxes[i].Value)) continue;
                    if (ShadowGeometry.OverlapArea(subject, r.PerBuilding[i]) > 1e-9) list.Add(i);
                }
                result.Add(list);
            }
            return result;
        }

        public static bool Intersects(RectD a, RectD b) =>
            a.left <= b.right && b.left <= a.right && a.top <= b.bottom && b.top <= a.bottom;

        static readonly Regex NumberRx = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>First number of a label ("9", "9 fl", "H=27,5") or null.</summary>
        public static double? ParseNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match m = NumberRx.Match(text);
            if (!m.Success) return null;
            if (double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                return v;
            return null;
        }

        /// <summary>Preview building: L-shaped block 30×30 m, 12 m wide.</summary>
        public static Building SampleBuilding(double heightM)
        {
            var p = new PathD
            {
                new PointD(0, 0), new PointD(30, 0), new PointD(30, 12),
                new PointD(12, 12), new PointD(12, 30), new PointD(0, 30)
            };
            return new Building { Outer = p, HeightM = heightM };
        }
    }
}
