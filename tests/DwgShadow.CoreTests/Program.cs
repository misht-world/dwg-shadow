using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clipper2Lib;
using DwgShadow.Core;

static class Program
{
    static int failures;

    static void Check(bool ok, string name, string details = "")
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {details}");
        if (!ok) failures++;
    }

    static void Near(double actual, double expected, double tol, string name) =>
        Check(Math.Abs(actual - expected) <= tol, name, $"(got {actual:0.###}, expected {expected:0.###} ± {tol})");

    static PathD Rect(double x0, double y0, double x1, double y1) =>
        new PathD { new PointD(x0, y0), new PointD(x1, y0), new PointD(x1, y1), new PointD(x0, y1) };

    static SunState MaxOfDay(double lat, double lon, DateTime day, double tz) =>
        Enumerable.Range(0, 24 * 60).Select(m => SunPosition.Compute(lat, lon, day.AddMinutes(m), tz))
                  .OrderByDescending(s => s.AltitudeDeg).First();

    static int Main()
    {
        // ---------- Sun (Moscow 55.7558 N, 37.6173 E, UTC+3)
        // Noon altitude = 90 − φ + δ; δ(Jun 22) ≈ +23.44, δ(Dec 21) ≈ −23.44, δ(Mar 20) ≈ 0.
        double lat = 55.7558, lon = 37.6173;
        var summer = MaxOfDay(lat, lon, new DateTime(2026, 6, 22), 3);
        Near(summer.AltitudeDeg, 90 - lat + 23.43, 0.15, "Moscow Jun 22: noon altitude");
        Near(summer.AzimuthDeg, 180, 0.5, "Moscow Jun 22: noon azimuth");
        var winter = MaxOfDay(lat, lon, new DateTime(2026, 12, 21), 3);
        Near(winter.AltitudeDeg, 90 - lat - 23.44 + 0.08, 0.2, "Moscow Dec 21: noon altitude (with refraction)");
        var eq = MaxOfDay(lat, lon, new DateTime(2026, 3, 20), 3);
        Near(eq.AltitudeDeg, 90 - lat, 0.5, "Moscow Mar 20: noon altitude");

        // Solar noon in Moscow on Jun 22 ≈ 12:31 UTC+3 (longitude 37.6° and equation of time ≈ −2 min)
        int noonMinute = Enumerable.Range(0, 24 * 60)
            .OrderByDescending(m => SunPosition.Compute(lat, lon, new DateTime(2026, 6, 22).AddMinutes(m), 3).AltitudeDeg).First();
        Near(noonMinute, 12 * 60 + 31, 3, "Moscow Jun 22: solar noon, minutes");

        var morning = SunPosition.Compute(lat, lon, new DateTime(2026, 6, 22, 9, 0, 0), 3);
        Check(morning.AzimuthDeg > 90 && morning.AzimuthDeg < 135, "Moscow Jun 22 09:00: sun in the south-east", morning.ToString());
        var night = SunPosition.Compute(lat, lon, new DateTime(2026, 12, 21, 23, 0, 0), 3);
        Check(night.AltitudeDeg < 0, "Night: sun below horizon", night.ToString());
        var syd = MaxOfDay(-33.87, 151.21, new DateTime(2026, 12, 21), 11);
        Check(syd.AzimuthDeg < 20 || syd.AzimuthDeg > 340 || syd.AltitudeDeg > 75, "Sydney: noon sun in the north / near zenith", syd.ToString());

        // ---------- Shadow direction
        var dirNoon = ShadowCalc.FromSun(new SunState { AzimuthDeg = 180, AltitudeDeg = 45 }, 0, 5);
        Near(dirNoon.DirectionDeg, 90, 1e-9, "Sun in the south → shadow to the north (up, 90°)");
        Near(ShadowCalc.FromSun(new SunState { AzimuthDeg = 90, AltitudeDeg = 30 }, 0, 5).DirectionDeg, 180, 1e-9, "Sun in the east → shadow to the west (180°)");
        Near(ShadowCalc.FromSun(new SunState { AzimuthDeg = 180, AltitudeDeg = 45 }, 30, 5).DirectionDeg, 120, 1e-9, "North rotated 30° counter-clockwise");
        Check(ShadowCalc.FromSun(new SunState { AzimuthDeg = 180, AltitudeDeg = 3 }, 0, 5).BelowThreshold, "Safety limit: sun too low");
        Near(dirNoon.LengthRatio, 1, 1e-9, "45° → shadow length = height");

        // ---------- Geometry
        var sq = new Building { Outer = Rect(0, 0, 10, 10), HeightM = 10 };
        Near(Math.Abs(Clipper.Area(ShadowGeometry.BuildingShadow(sq, 10, 0))), 200, 1e-6, "Square: shadow area incl. building");
        Near(Math.Abs(Clipper.Area(ShadowGeometry.BuildingShadow(sq, 5, 5))), 200, 1e-6, "Square: diagonal shadow");
        var longSh = ShadowGeometry.BuildingShadow(sq, 50, 0);
        var polys = ShadowGeometry.ToPolygons(longSh);
        Check(polys.Count == 1 && polys[0].Holes.Count == 0, "Long shadow: one contour without holes");
        Near(Math.Abs(Clipper.Area(longSh)), 600, 1e-6, "Long shadow: area 60×10");

        var b2 = new Building { Outer = Rect(100, 0, 110, 10), HeightM = 10, SourceIndex = 1 };
        var east = new ShadowDirection { DirectionDeg = 0, AltitudeDeg = 45 };
        Check(ShadowGeometry.ToPolygons(ShadowCalc.Shadows(new[] { sq, b2 }, east, 1, false)).Count == 2, "Two buildings → two contours");
        Near(Math.Abs(Clipper.Area(ShadowCalc.Shadows(new[] { sq, b2 }, east, 1, true))), 200, 1e-6, "Buildings cut out of shadows");

        var ring = new Building { Outer = Rect(0, 0, 30, 30), Holes = new PathsD { Rect(10, 10, 20, 20) }, HeightM = 5 };
        Near(Math.Abs(Clipper.Area(ShadowGeometry.BuildingShadow(ring, 5, 0))), 35 * 30 - 5 * 10, 1e-6, "Courtyard: partly shaded");

        // ---------- Attribution: which buildings each result polygon belongs to
        var b3 = new Building { Outer = Rect(15, 0, 25, 10), HeightM = 10, SourceIndex = 2 };   // shadow merges with sq
        var all = new List<Building> { sq, b2, b3 };
        ShadowResult res = ShadowCalc.Compute(all, east, 1, false);
        var resPolys = ShadowGeometry.ToPolygons(res.Union);
        var owners = ShadowCalc.Attribute(resPolys, res);
        bool merged = owners.Any(o => o.Count == 2 && o.Contains(0) && o.Contains(2));
        bool single = owners.Any(o => o.Count == 1 && o[0] == 1);
        Check(resPolys.Count == 2 && merged && single, "Attribution: merged shadow → both buildings, separate → one");

        // ---------- Grouping
        var groups = BuildingGrouper.Group(new List<PathD> { Rect(0, 0, 30, 30), Rect(10, 10, 20, 20), Rect(50, 0, 60, 10) }, true);
        Check(groups.Count == 2 && groups.Any(g => g.Holes.Count == 1 && g.HoleIndices.SequenceEqual(new[] { 1 })), "Grouping: 2 buildings, one courtyard with its index");
        groups = BuildingGrouper.Group(new List<PathD> { Rect(0, 0, 30, 30), Rect(10, 10, 20, 20) }, true, i => i == 1);
        Check(groups.Count == 2 && groups.All(g => g.Holes.Count == 0), "Grouping: nested tower is a separate part");
        Check(BuildingGrouper.Group(new List<PathD> { Rect(0, 0, 30, 30), Rect(10, 10, 20, 20) }, false).Count == 2, "Grouping: courtyards off");

        // ---------- Arcs (AutoCAD bulge)
        var arc = new PathD();
        ShadowGeometry.AddArcPoints(arc, new PointD(0, 0), new PointD(10, 0), 1.0);
        Check(arc.Count > 10 && arc.All(p => Math.Abs(Math.Sqrt((p.x - 5) * (p.x - 5) + p.y * p.y) - 5) < 1e-9), "Arc: points on the circle");
        Check(arc.All(p => p.y <= 1e-9), "Arc bulge>0 from (0,0) to (10,0) is counter-clockwise, below the chord");
        var arc2 = new PathD();
        ShadowGeometry.AddArcPoints(arc2, new PointD(0, 0), new PointD(10, 0), -0.25);
        Check(arc2.Count > 0 && arc2.All(p => p.y >= -1e-9), "Arc bulge<0 is above the chord");

        // ---------- Robustness
        var flat = new Building { Outer = new PathD { new PointD(0, 0), new PointD(5, 0), new PointD(10, 0) }, HeightM = 10, SourceIndex = 7 };
        Check(ShadowCalc.Shadows(new[] { flat, sq }, new ShadowDirection { DirectionDeg = 30, AltitudeDeg = 45 }, 1, false).Count >= 1, "Degenerate contour does not break others");
        var big = new Building { Outer = Rect(2_200_000_000, 480_000_000, 2_200_030_000, 480_012_000), HeightM = 24 };
        Near(Math.Abs(Clipper.Area(ShadowCalc.Shadows(new[] { big }, new ShadowDirection { DirectionDeg = 90, AltitudeDeg = 45 }, 1000, false))),
             30_000.0 * 36_000.0, 1, "Large local coordinates in millimetres");
        Check(ShadowCalc.ParseNumber("9 эт.") == 9 && ShadowCalc.ParseNumber("H=27,5") == 27.5 && ShadowCalc.ParseNumber("fl") == null, "Label parsing");

        // ---------- Settings
        var s = new ShadowSettings { SeriesStart = "9:00", SeriesEnd = "18:00", SeriesStep = 60 };
        Check(ShadowCalc.SeriesTimes(s).Count == 10, "Series 9–18 every hour: 10 times");
        string tmp = Path.Combine(Path.GetTempPath(), "dwgshadow-test-" + Guid.NewGuid() + ".xml");
        try
        {
            var a = new ShadowSettings { SourceMode = SourceMode.Selection, SourceLayer = "DPT_OKS_PL", Floors = 10, ManualDirection = 20.5,
                                         Date = new DateTime(2026, 3, 1), Language = UiLanguage.English, ExpStyle = true, OutputLayer = "Тени" };
            Check(a.Save(tmp) == null, "Settings: save");
            var b = ShadowSettings.Load(out string err, tmp);
            Check(err == null && b.SourceMode == SourceMode.Selection && b.SourceLayer == "DPT_OKS_PL" && b.Floors == 10 &&
                  b.ManualDirection == 20.5 && b.Date == new DateTime(2026, 3, 1) && b.Language == UiLanguage.English &&
                  b.ExpStyle && b.OutputLayer == "Тени", "Settings: round trip", err ?? "");

            // A file written by the previous version (XmlSerializer format)
            File.WriteAllText(tmp, "<?xml version=\"1.0\"?>\n<ShadowSettings xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<SourceMode>Selection</SourceMode><SourceLayer>DPT_OKS_PL</SourceLayer><Floors>10</Floors><Date>2026-06-22T00:00:00</Date>" +
                "<ManualDirection>20</ManualDirection><ClearLayerBefore>true</ClearLayerBefore><TextLayer /></ShadowSettings>");
            var c = ShadowSettings.Load(out err, tmp);
            Check(err == null && c.SourceLayer == "DPT_OKS_PL" && c.Floors == 10 && c.ManualDirection == 20 && c.Date == new DateTime(2026, 6, 22),
                  "Settings: previous version file is read", err ?? "");
            Check(ShadowSettings.Load(out err, tmp + ".missing").Floors == 8 && err == null, "Settings: missing file → defaults");
            File.WriteAllText(tmp, "garbage");
            Check(ShadowSettings.Load(out err, tmp).Floors == 8 && err != null, "Settings: broken file → defaults + error");
        }
        finally { File.Delete(tmp); }
        Check(s.Clone().SeriesStart == "9:00", "Settings: clone");

        // ---------- Localization
        L.Apply(UiLanguage.English);
        string en = L.T("btn.build"), enLayer = new ShadowSettings().Validate(false);
        L.Apply(UiLanguage.Russian);
        Check(en == "Build" && L.T("btn.build") == "Построить" && enLayer == "Building layer is not selected.", "Localization: EN / RU");
        Check(L.F("pv.building", 24.0).Contains("24"), "Localization: formatted strings");

        Console.WriteLine(failures == 0 ? "\nAll checks passed" : $"\nFailures: {failures}");
        return failures == 0 ? 0 : 1;
    }
}
