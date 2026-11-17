using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipper2Lib;
using DwgShadow.Core;
using DwgShadow.UI;

// Renders frames of a short promo video (1080×1350, 30 fps) with the plug-in's own shadow calculation.
//
//   DemoVideo <outDir> [--site file.site.txt] [--style paper|night|mono] [--lang en|ru] [--still HH:MM]
//
// --site   site exported from a drawing with export-site.lsp (closed polylines/circles on "Buildings",
//          floor labels on "Building_labels"); without it a built-in sample block is used.
// --still  renders one still frame at the given time instead of the whole video (style previews).
// Then:    ffmpeg -framerate 30 -i <outDir>/f%05d.png -c:v libx264 -pix_fmt yuv420p -crf 18 out.mp4
static class Program
{
    const int W = 1080, H = 1350, Fps = 30;
    static bool Ru;

    // ================================================================= styles

    class Style
    {
        public string Name;
        public Color Paper, PlanBg, PlanBorder, Grid, Ink, Muted, BuildingFill, BuildingLine, Label, Shadow, Accent, CardFill, CardText, CardAccent;
        public double ShadowAlpha = 0.42, BuildingLineWidth = 2;
        public string Font = "Segoe UI";
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
    {
        // Warm paper, beige buildings, blue-grey shadows
        ["paper"] = new Style
        {
            Name = "paper", Paper = C("#F6F3EC"), PlanBg = C("#FBF9F4"), PlanBorder = C("#D7D0C2"), Grid = C("#E6E0D4"),
            Ink = C("#1F2933"), Muted = C("#6E747A"), BuildingFill = C("#DED6C7"), BuildingLine = C("#3E3A33"), Label = C("#3E3A33"),
            Shadow = C("#46567A"), Accent = C("#F2A93B"), CardFill = C("#1F2933"), CardText = Colors.White, CardAccent = C("#F2D296")
        },
        // Dark night-blue canvas, light buildings, glowing blue shadow areas
        ["night"] = new Style
        {
            Name = "night", Paper = C("#0D1520"), PlanBg = C("#111C2A"), PlanBorder = C("#22324A"), Grid = C("#1A2738"),
            Ink = C("#EAF0F7"), Muted = C("#8CA0B6"), BuildingFill = C("#DCD6CA"), BuildingLine = C("#F7F3EA"), Label = C("#1B2430"),
            Shadow = C("#4F8EDB"), Accent = C("#FFB547"), CardFill = C("#EAF0F7"), CardText = C("#0D1520"), CardAccent = C("#C77A00"),
            ShadowAlpha = 0.40
        },
        // White architectural line drawing, charcoal shadows, one orange accent
        ["mono"] = new Style
        {
            Name = "mono", Paper = Colors.White, PlanBg = Colors.White, PlanBorder = C("#E3E5E8"), Grid = C("#F0F1F3"),
            Ink = C("#111111"), Muted = C("#6B7280"), BuildingFill = Colors.White, BuildingLine = C("#111111"), Label = C("#111111"),
            Shadow = C("#1A1A1A"), Accent = C("#FF5A1F"), CardFill = C("#111111"), CardText = Colors.White, CardAccent = C("#FF8A5B"),
            ShadowAlpha = 0.26, BuildingLineWidth = 2.4
        },
    };

    static Style S;

    // ================================================================= site

    const double Lat = 55.7558, Lon = 37.6173, Utc = 3;
    static readonly DateTime Day = new DateTime(2026, 6, 22);

    class Site
    {
        public Building B;
        public string Floors;
        public Point? LabelAt;  // world coordinates of the drawing label
    }

    static List<Site> Sites;
    static Rect PlanRect = new Rect(40, 236, W - 80, 836);
    static Func<double, double, Point> Map;
    static double Scale;

    [STAThread]
    static int Main(string[] args)
    {
        const string acad = @"C:\Program Files\Autodesk\AutoCAD 2021";
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string f = Path.Combine(acad, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
        };

        string outDir = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "frames";
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        Ru = Arg("--lang", "en") == "ru";
        S = Styles[Arg("--style", "paper")];
        string siteFile = Arg("--site", null);
        string still = Arg("--still", null);

        L.Apply(Ru ? UiLanguage.Russian : UiLanguage.English);
        var culture = new CultureInfo(Ru ? "ru-RU" : "en-US");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

        Directory.CreateDirectory(outDir);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (siteFile != null) LoadSite(siteFile); else BuiltInSite();

        int frame = 0;
        void Emit(Action<DrawingContext> draw, string name = null)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen()) draw(dc);
            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(Path.Combine(outDir, name ?? $"f{frame++:00000}.png"))) enc.Save(fs);
        }

        TimeSpan dayStart = new TimeSpan(5, 30, 0), dayEnd = new TimeSpan(20, 0, 0);

        if (still != null)
        {
            TimeSpan time = TimeSpan.Parse(still, CultureInfo.InvariantCulture);
            ShadowDirection d = Sun(time);
            PathsD sh = Shadows(d);
            Emit(dc =>
            {
                Background(dc); Header(dc);
                DrawShadows(dc, sh, 1); DrawBuildings(dc, 1); DrawSunMarker(dc, d);
                Caption(dc, "Real sun position: place, date, time", 1);
                Footer(dc, d, dayStart, dayEnd, (time - dayStart).TotalMinutes / (dayEnd - dayStart).TotalMinutes, time);
            }, $"still-{S.Name}.png");
            Console.WriteLine($"still: {S.Name}");
            return 0;
        }

        foreach (string f in Directory.GetFiles(outDir, "f*.png")) File.Delete(f);
        BitmapSource[] ui = RenderDialogs();

        // 1. Intro — buildings appear (2 s)
        for (int i = 0; i < 2 * Fps; i++)
        {
            double t = Ease(i / (2.0 * Fps));
            Emit(dc =>
            {
                Background(dc); Header(dc);
                DrawBuildings(dc, t);
                Caption(dc, Ru ? "Контуры зданий + этажность" : "Building footprints + floors", t);
                Footer(dc, null, dayStart, dayEnd, 0);
            });
        }

        // 2. Sun over the day (10 s)
        int dayFrames = 10 * Fps;
        for (int i = 0; i < dayFrames; i++)
        {
            TimeSpan time = dayStart + TimeSpan.FromTicks((long)((dayEnd - dayStart).Ticks * (i / (double)(dayFrames - 1))));
            ShadowDirection d = Sun(time);
            PathsD sh = d.BelowThreshold ? new PathsD() : Shadows(d);
            double fadeIn = Math.Min(1, i / 15.0);
            Emit(dc =>
            {
                Background(dc); Header(dc);
                DrawShadows(dc, sh, fadeIn); DrawBuildings(dc, 1); DrawSunMarker(dc, d);
                Caption(dc, Ru ? "Реальное солнце: место, дата, время" : "Real sun position: place, date, time", Math.Min(1, i / 10.0));
                Footer(dc, d, dayStart, dayEnd, (time - dayStart).TotalMinutes / (dayEnd - dayStart).TotalMinutes, time);
            });
        }

        // 3. Shadow series 08–18 every 2 h (5 s)
        var series = Enumerable.Range(0, 6).Select(k => new TimeSpan(8 + 2 * k, 0, 0))
                               .Select(t => { ShadowDirection d = Sun(t); return (t, d, sh: Shadows(d)); }).ToList();
        PathsD zone = ShadowGeometry.UnionAll(series.Select(x => x.sh));
        int seriesFrames = 5 * Fps;
        for (int i = 0; i < seriesFrames; i++)
        {
            double p = i / (double)(seriesFrames - 1);
            int shown = Math.Min(series.Count, (int)(p * 1.35 * series.Count) + 1);
            double zoneAlpha = Math.Max(0, (p - 0.78) / 0.22);
            Emit(dc =>
            {
                Background(dc); Header(dc);
                if (zoneAlpha > 0) FillPaths(dc, zone, A(S.Shadow, 0.28 * zoneAlpha), null);
                for (int k = 0; k < shown; k++)
                {
                    Color c = SeriesColor(k, series.Count);
                    FillPaths(dc, series[k].sh, A(c, 0.10), new Pen(B(c), 2.4));
                    LabelTip(dc, series[k].sh, series[k].d.DirectionDeg, series[k].t.ToString(@"hh\:mm"), c);
                }
                DrawBuildings(dc, 1);
                Caption(dc, Ru ? "Серия теней за день и зона затенения" : "Shadow series for the day + shaded zone", Math.Min(1, i / 10.0));
                Footer(dc, null, dayStart, dayEnd, 0, null, Ru ? "22 июня · 08:00–18:00 · шаг 2 ч" : "June 22 · 08:00–18:00 · every 2 hours");
            });
        }

        // 4. The dialog (4.5 s): cross-fade between preview states
        ShadowDirection noon = Sun(new TimeSpan(13, 0, 0));
        PathsD noonSh = Shadows(noon);
        int uiFrames = (int)(4.5 * Fps);
        for (int i = 0; i < uiFrames; i++)
        {
            double p = i / (double)(uiFrames - 1);
            double pos = p * (ui.Length - 1);
            int a = Math.Min(ui.Length - 1, (int)Math.Floor(pos));
            int b = Math.Min(ui.Length - 1, a + 1);
            double mix = Ease(Math.Max(0, Math.Min(1, (pos - a - 0.6) / 0.4)));
            double appear = Ease(Math.Min(1, i / 12.0));
            Emit(dc =>
            {
                Background(dc); Header(dc);
                DrawShadows(dc, noonSh, 1); DrawBuildings(dc, 1);
                dc.DrawRectangle(B(A(S.Paper, 0.6 * appear)), null, new Rect(0, 0, W, H));
                DrawDialog(dc, ui[a], appear);
                if (mix > 0 && b != a) DrawDialog(dc, ui[b], appear * mix);
                Caption(dc, Ru ? "Живое превью · RU / EN · AutoCAD 2021–2024" : "Live preview · RU / EN · AutoCAD 2021–2024", appear);
            });
        }

        // 5. Outro (3 s)
        for (int i = 0; i < 3 * Fps; i++)
        {
            double t = Ease(Math.Min(1, i / 15.0));
            Emit(dc =>
            {
                Background(dc);
                DrawShadows(dc, noonSh, 1); DrawBuildings(dc, 1);
                dc.DrawRectangle(B(A(S.Paper, 0.93 * t)), null, new Rect(0, 0, W, H));
                Outro(dc, t);
            });
        }

        Console.WriteLine($"frames: {frame}");
        return 0;
    }

    static ShadowDirection Sun(TimeSpan t) => ShadowCalc.FromSun(SunPosition.Compute(Lat, Lon, Day + t, Utc), 0, 3);
    static PathsD Shadows(ShadowDirection d) => ShadowCalc.Compute(Sites.Select(s => s.B).ToList(), d, 1, true).Union;

    // ================================================================= site loading

    static PathD Rect(double x0, double y0, double x1, double y1) =>
        new PathD { new PointD(x0, y0), new PointD(x1, y0), new PointD(x1, y1), new PointD(x0, y1) };

    static PathD Circle(double cx, double cy, double r)
    {
        var p = new PathD();
        for (int i = 0; i < 72; i++) p.Add(new PointD(cx + r * Math.Cos(i * Math.PI / 36), cy + r * Math.Sin(i * Math.PI / 36)));
        return p;
    }

    static string FloorsText(double n) => Ru ? $"{n:0} эт." : $"{n:0} fl";

    /// <summary>Reads a site exported by export-site.lsp. Heights come from floor labels (3 m per floor).</summary>
    static void LoadSite(string file)
    {
        var contours = new List<PathD>();
        var labels = new List<(double x, double y, double v)>();
        foreach (string line in File.ReadAllLines(file))
        {
            string[] p = line.Split('|');
            if (p[0] == "PL" && p[1] == "Buildings" && (int.Parse(p[2]) & 1) == 1)
            {
                var pts = p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(v => v.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray()).ToList();
                var path = new PathD();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = new PointD(pts[i][0], pts[i][1]);
                    path.Add(a);
                    double bulge = pts[i].Length > 2 ? pts[i][2] : 0;
                    if (Math.Abs(bulge) > 1e-9)
                    {
                        var b = pts[(i + 1) % pts.Count];
                        ShadowGeometry.AddArcPoints(path, a, new PointD(b[0], b[1]), bulge);
                    }
                }
                contours.Add(path);
            }
            else if (p[0] == "CIRCLE" && p[1] == "Buildings")
            {
                var c = p[2].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                contours.Add(Circle(c[0], c[1], double.Parse(p[3], CultureInfo.InvariantCulture)));
            }
            else if ((p[0] == "TEXT" || p[0] == "MTEXT") && p[1] == "Building_labels")
            {
                var c = p[2].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                double? v = ShadowCalc.ParseNumber(p[4]);
                if (v != null) labels.Add((c[0], c[1], v.Value));
            }
        }

        // Each label belongs to the smallest contour containing it (same rule as the plug-in)
        var owner = new Dictionary<int, (double x, double y, double v)>();
        foreach (var lb in labels)
        {
            int best = -1;
            for (int i = 0; i < contours.Count; i++)
                if (ShadowGeometry.PointInPolygon(contours[i], lb.x, lb.y) &&
                    (best < 0 || Math.Abs(Clipper.Area(contours[i])) < Math.Abs(Clipper.Area(contours[best])))) best = i;
            if (best >= 0 && !owner.ContainsKey(best)) owner[best] = lb;
        }

        Sites = new List<Site>();
        foreach (Building b in BuildingGrouper.Group(contours, true, i => owner.ContainsKey(i)))
        {
            double floors = owner.TryGetValue(b.SourceIndex, out var lb) ? lb.v : 5;
            b.HeightM = floors * 3;
            Sites.Add(new Site { B = b, Floors = FloorsText(floors), LabelAt = owner.ContainsKey(b.SourceIndex) ? new Point(lb.x, lb.y) : (Point?)null });
        }

        // Local origin and view
        var all = Clipper.GetBounds(new PathsD(Sites.Select(s => s.B.Outer)));
        double ox0 = all.left, oy0 = all.top;
        foreach (Site s in Sites)
        {
            s.B.Outer = Clipper.TranslatePath(s.B.Outer, -ox0, -oy0);
            for (int i = 0; i < s.B.Holes.Count; i++) s.B.Holes[i] = Clipper.TranslatePath(s.B.Holes[i], -ox0, -oy0);
            if (s.LabelAt.HasValue) s.LabelAt = new Point(s.LabelAt.Value.X - ox0, s.LabelAt.Value.Y - oy0);
        }
        double w = all.right - all.left, h = all.bottom - all.top;
        SetView(-0.08 * w, w * 1.08, -0.06 * h, h * 1.16);
    }

    static void Add(PathD outer, int floors, params PathD[] holes)
    {
        var b = new Building { Outer = outer, HeightM = floors * 3.0 };
        b.Holes.AddRange(holes);
        Sites.Add(new Site { B = b, Floors = FloorsText(floors) });
    }

    static void BuiltInSite()
    {
        Sites = new List<Site>();
        Add(Rect(0, 0, 18, 18), 22);
        Add(Rect(32, 0, 92, 13), 9);
        Add(new PathD { new PointD(0, 40), new PointD(40, 40), new PointD(40, 52), new PointD(12, 52), new PointD(12, 80), new PointD(0, 80) }, 5);
        Add(Rect(56, 36, 100, 78), 7, Rect(67, 47, 89, 67));
        Add(Rect(116, 0, 128, 12), 3);
        Add(Rect(116, 24, 128, 36), 3);
        Add(Circle(142, 64, 11), 14);
        Add(Rect(22, 100, 82, 112), 16);
        Add(Rect(104, 96, 116, 140), 10);
        SetView(-45, 190, -25, 200);
    }

    static void SetView(double x0, double x1, double y0, double y1)
    {
        // keep the plan aspect: grow the shorter side
        double sx = PlanRect.Width / (x1 - x0), sy = PlanRect.Height / (y1 - y0);
        Scale = Math.Min(sx, sy);
        double ox = PlanRect.Left + (PlanRect.Width - (x1 - x0) * Scale) / 2;
        double oy = PlanRect.Top + (PlanRect.Height - (y1 - y0) * Scale) / 2;
        Map = (x, y) => new Point(ox + (x - x0) * Scale, oy + (y1 - y) * Scale);
    }

    // ================================================================= drawing

    static double Ease(double t) => t < 0 ? 0 : t > 1 ? 1 : t * t * (3 - 2 * t);
    static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color A(Color c, double a) => Color.FromArgb((byte)(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B);

    static void Background(DrawingContext dc)
    {
        dc.DrawRectangle(B(S.Paper), null, new Rect(0, 0, W, H));
        dc.PushClip(new RectangleGeometry(PlanRect, 18, 18));
        dc.DrawRectangle(B(S.PlanBg), null, PlanRect);
        // grid every 10 m
        var pen = new Pen(B(S.Grid), 1);
        Point tl = PlanRect.TopLeft, br = PlanRect.BottomRight;
        double step = 10 * Scale;
        Point origin = Map(0, 0);
        for (double x = origin.X - Math.Ceiling((origin.X - tl.X) / step) * step; x <= br.X; x += step) dc.DrawLine(pen, new Point(x, tl.Y), new Point(x, br.Y));
        for (double y = origin.Y - Math.Ceiling((origin.Y - tl.Y) / step) * step; y <= br.Y; y += step) dc.DrawLine(pen, new Point(tl.X, y), new Point(br.X, y));
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(B(S.PlanBorder), 1.5), PlanRect, 18, 18);

        Point n = new Point(PlanRect.Right - 46, PlanRect.Top + 78);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(n + new Vector(0, -26), true, true);
            c.LineTo(n + new Vector(12, 14), true, true);
            c.LineTo(n + new Vector(0, 6), true, true);
            c.LineTo(n + new Vector(-12, 14), true, true);
        }
        dc.DrawGeometry(B(S.Ink), null, g);
        Text(dc, Ru ? "С" : "N", n + new Vector(0, -52), 22, S.Ink, true, center: true);

        double meters = 50 * Scale > 260 ? 25 : 50;
        Point s0 = new Point(PlanRect.Left + 36, PlanRect.Bottom - 40);
        double len = meters * Scale;
        var ink = new Pen(B(S.Ink), 3);
        dc.DrawLine(ink, s0, s0 + new Vector(len, 0));
        dc.DrawLine(ink, s0 + new Vector(0, -7), s0 + new Vector(0, 7));
        dc.DrawLine(ink, s0 + new Vector(len, -7), s0 + new Vector(len, 7));
        Text(dc, $"{meters:0} " + (Ru ? "м" : "m"), s0 + new Vector(len / 2, -34), 20, S.Ink, false, center: true);
    }

    static void Header(DrawingContext dc)
    {
        Text(dc, "DwgShadow", new Point(60, 48), 64, S.Ink, true);
        Text(dc, Ru ? "Тени зданий в AutoCAD" : "Building shadows in AutoCAD", new Point(62, 128), 30, S.Muted, false);
        Point c = new Point(W - 92, 104);
        dc.DrawEllipse(B(S.Accent), null, c, 22, 22);
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6;
            var v = new Vector(Math.Cos(a), Math.Sin(a));
            dc.DrawLine(new Pen(B(S.Accent), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, c + v * 31, c + v * 42);
        }
    }

    static void Caption(DrawingContext dc, string text, double alpha)
    {
        var ft = MakeText(text, 34, A(S.Ink, alpha), true);
        double w = ft.Width + 56;
        var r = new Rect((W - w) / 2, PlanRect.Top - 32, w, 64);
        dc.DrawRoundedRectangle(B(A(S.PlanBg, 0.96 * alpha)), new Pen(B(A(S.PlanBorder, alpha)), 1.5), r, 32, 32);
        dc.DrawText(ft, new Point(r.Left + 28, r.Top + (64 - ft.Height) / 2));
    }

    static void Footer(DrawingContext dc, ShadowDirection? d, TimeSpan start, TimeSpan end, double progress, TimeSpan? time = null, string note = null)
    {
        double top = PlanRect.Bottom + 28;
        if (time.HasValue) Text(dc, time.Value.ToString(@"hh\:mm"), new Point(60, top), 76, S.Ink, true);
        string place = Ru ? "22 июня · Москва 55,76° с. ш." : "June 22 · Moscow 55.76° N";
        Text(dc, note ?? place, new Point(time.HasValue ? 290 : 60, top + 12), 28, S.Ink, false);
        if (d.HasValue)
        {
            string info = Ru
                ? $"солнце: высота {d.Value.AltitudeDeg:0}°, азимут {d.Value.SunAzimuthDeg:0}° · тень {d.Value.LengthRatio:0.0}×H"
                : $"sun: altitude {d.Value.AltitudeDeg:0}°, azimuth {d.Value.SunAzimuthDeg:0}° · shadow {d.Value.LengthRatio:0.0}×H";
            Text(dc, info, new Point(292, top + 54), 24, S.Muted, false);
        }

        var r = new Rect(60, H - 112, W - 120, 72);
        var pts = new List<Point>();
        for (int i = 0; i <= 60; i++)
        {
            TimeSpan t = start + TimeSpan.FromTicks((end - start).Ticks * i / 60);
            double alt = SunPosition.Compute(Lat, Lon, Day + t, Utc).AltitudeDeg;
            pts.Add(new Point(r.Left + r.Width * i / 60.0, r.Bottom - Math.Max(0, alt) / 60.0 * r.Height));
        }
        dc.DrawLine(new Pen(B(S.PlanBorder), 2), new Point(r.Left, r.Bottom), new Point(r.Right, r.Bottom));
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
        dc.DrawGeometry(null, new Pen(B(A(S.Accent, 0.55)), 3) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }, g);
        Text(dc, start.ToString(@"hh\:mm"), new Point(r.Left, r.Bottom + 6), 18, S.Muted, false);
        Text(dc, end.ToString(@"hh\:mm"), new Point(r.Right - 50, r.Bottom + 6), 18, S.Muted, false);
        if (time.HasValue)
        {
            double alt = Math.Max(0, d?.AltitudeDeg ?? 0);
            var p = new Point(r.Left + r.Width * progress, r.Bottom - alt / 60.0 * r.Height);
            dc.DrawEllipse(B(A(S.Accent, 0.25)), null, p, 22, 22);
            dc.DrawEllipse(B(S.Accent), null, p, 12, 12);
        }
    }

    static void DrawBuildings(DrawingContext dc, double alpha)
    {
        foreach (Site s in Sites)
        {
            var paths = new PathsD { s.B.Outer };
            paths.AddRange(s.B.Holes);
            FillPaths(dc, paths, A(S.BuildingFill, alpha), new Pen(B(A(S.BuildingLine, alpha)), S.BuildingLineWidth));
        }
        foreach (Site s in Sites)
        {
            Point c;
            if (s.LabelAt.HasValue) c = Map(s.LabelAt.Value.X, s.LabelAt.Value.Y);
            else
            {
                RectD bb = ShadowGeometry.Bounds(s.B.Outer);
                c = Map((bb.left + bb.right) / 2, (bb.top + bb.bottom) / 2);
                if (s.B.Holes.Count > 0) c = Map((bb.left + bb.right) / 2, bb.bottom - 5.5);
                if (s.B.Outer.Count == 6) c = Map(6, 66);
            }
            Text(dc, s.Floors, c, 17, A(S.Label, alpha), true, center: true, middle: true);
        }
    }

    static void DrawShadows(DrawingContext dc, PathsD sh, double alpha) =>
        FillPaths(dc, sh, A(S.Shadow, S.ShadowAlpha * alpha), new Pen(B(A(S.Shadow, 0.8 * alpha)), 1.2));

    static void DrawSunMarker(DrawingContext dc, ShadowDirection d)
    {
        if (d.BelowThreshold) return;
        double a = (d.DirectionDeg + 180) * Math.PI / 180;
        var v = new Vector(Math.Cos(a), -Math.Sin(a));
        Point c = new Point(PlanRect.Left + PlanRect.Width / 2, PlanRect.Top + PlanRect.Height / 2);
        double tx = Math.Abs(v.X) < 1e-9 ? double.MaxValue : (PlanRect.Width / 2 - 46) / Math.Abs(v.X);
        double ty = Math.Abs(v.Y) < 1e-9 ? double.MaxValue : (PlanRect.Height / 2 - 46) / Math.Abs(v.Y);
        Point p = c + v * Math.Min(tx, ty);
        dc.DrawEllipse(B(A(S.Accent, 0.22)), null, p, 34, 34);
        dc.DrawEllipse(B(S.Accent), null, p, 18, 18);
        for (int i = 0; i < 3; i++)
        {
            var off = new Vector(-v.Y, v.X) * (i - 1) * 16;
            dc.DrawLine(new Pen(B(A(S.Accent, 0.8)), 3) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, p - v * 30 + off, p - v * 92 + off);
        }
    }

    static void FillPaths(DrawingContext dc, PathsD paths, Color fill, Pen pen)
    {
        var g = new StreamGeometry { FillRule = System.Windows.Media.FillRule.EvenOdd };
        using (var c = g.Open())
            foreach (PathD p in paths)
            {
                if (p.Count < 2) continue;
                c.BeginFigure(Map(p[0].x, p[0].y), true, true);
                c.PolyLineTo(p.Skip(1).Select(q => Map(q.x, q.y)).ToList(), true, true);
            }
        g.Freeze();
        dc.PushClip(new RectangleGeometry(PlanRect, 18, 18));
        dc.DrawGeometry(B(fill), pen, g);
        dc.Pop();
    }

    static void LabelTip(DrawingContext dc, PathsD sh, double dirDeg, string text, Color c)
    {
        if (sh.Count == 0) return;
        double a = dirDeg * Math.PI / 180;
        PointD tip = sh.SelectMany(p => p).OrderByDescending(q => q.x * Math.Cos(a) + q.y * Math.Sin(a)).First();
        Point p0 = Map(tip.x, tip.y);
        if (!PlanRect.Contains(p0)) return;
        Text(dc, text, p0 + new Vector(4, -24), 19, c, true);
    }

    static Color SeriesColor(int i, int n)
    {
        double t = n <= 1 ? 0 : (double)i / (n - 1);
        double h = 220 - 205 * t, s = 0.75, v = S.Name == "night" ? 0.95 : 0.78;
        double cc = v * s, x = cc * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - cc;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = cc; g = x; } else if (h < 120) { r = x; g = cc; } else if (h < 180) { g = cc; b = x; }
        else if (h < 240) { g = x; b = cc; } else { r = x; b = cc; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    static void DrawDialog(DrawingContext dc, BitmapSource img, double alpha)
    {
        double w = W - 90, h = img.PixelHeight * w / img.PixelWidth;
        var r = new Rect((W - w) / 2, PlanRect.Top + (PlanRect.Height - h) / 2 - 40, w, h);
        dc.PushOpacity(alpha);
        dc.DrawRoundedRectangle(B(Color.FromArgb(50, 0, 0, 0)), null, new Rect(r.X + 6, r.Y + 10, r.Width, r.Height), 10, 10);
        dc.DrawImage(img, r);
        dc.DrawRectangle(null, new Pen(B(S.PlanBorder), 1.5), r);
        dc.Pop();
    }

    static void Outro(DrawingContext dc, double t)
    {
        dc.PushOpacity(t);
        Text(dc, "DwgShadow", new Point(W / 2.0, 420), 96, S.Ink, true, center: true);
        Text(dc, Ru ? "Тени зданий для AutoCAD 2021–2024" : "Building shadows for AutoCAD 2021–2024", new Point(W / 2.0, 545), 36, S.Ink, false, center: true);
        string[] lines = Ru
            ? new[] { "Солнце по координатам, дате и времени", "Серия теней за день", "Перестроение только выбранных зданий", "Русский и английский интерфейс" }
            : new[] { "Sun from coordinates, date and time", "Shadow series for the day", "Rebuild only the buildings you pick", "Russian and English UI" };
        for (int i = 0; i < lines.Length; i++)
        {
            Point p = new Point(250, 650 + i * 58);
            dc.DrawEllipse(B(S.Accent), null, p + new Vector(-24, 21), 8, 8);
            Text(dc, lines[i], p, 32, S.Ink, false);
        }
        var r = new Rect(140, 960, W - 280, 110);
        dc.DrawRoundedRectangle(B(S.CardFill), null, r, 55, 55);
        Text(dc, Ru ? "Бесплатно · открытый код (MIT)" : "Free & open source (MIT)", new Point(W / 2.0, r.Top + 16), 26, S.CardAccent, false, center: true);
        Text(dc, "github.com/misht-world/dwg-shadow", new Point(W / 2.0, r.Top + 52), 34, S.CardText, true, center: true);
        dc.Pop();
    }

    static FormattedText MakeText(string s, double size, Color c, bool bold) =>
        new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(S.Font), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, B(c), 1.0);

    static void Text(DrawingContext dc, string s, Point at, double size, Color c, bool bold, bool center = false, bool middle = false)
    {
        var ft = MakeText(s, size, c, bold);
        dc.DrawText(ft, new Point(center ? at.X - ft.Width / 2 : at.X, middle ? at.Y - ft.Height / 2 : at.Y));
    }

    // ================================================================= dialog snapshots

    static BitmapSource[] RenderDialogs()
    {
        var layers = new List<string> { "0", "Buildings", "Building_labels", "Shadows" };
        var result = new List<BitmapSource>();
        foreach (string time in new[] { "09:00", "13:00", "17:00" })
        {
            var s = new ShadowSettings
            {
                Language = Ru ? UiLanguage.Russian : UiLanguage.English, SunMode = SunMode.Astronomical,
                Time = time, Date = Day, SourceLayer = "Buildings", Floors = 9, ExpLocation = true
            };
            s.FillDefaults();
            var w = new ShadowWindow(new DialogModel(s, layers))
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0, ShowActivated = false
            };
            w.Show();
            w.UpdateLayout();
            Pump();
            var root = (FrameworkElement)w.Content;
            int pw = (int)root.ActualWidth + 20, ph = (int)root.ActualHeight + 20;
            var bmp = new RenderTargetBitmap(pw * 2, ph * 2, 192, 192, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, pw, ph));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(10, 10, root.ActualWidth, root.ActualHeight));
            }
            bmp.Render(dv);
            bmp.Freeze();
            result.Add(bmp);
            w.Close();
        }
        return result.ToArray();
    }

    static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
