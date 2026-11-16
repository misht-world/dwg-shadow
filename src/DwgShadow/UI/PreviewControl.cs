using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Clipper2Lib;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    /// <summary>
    /// Preview: plan of a sample building with its shadow (top) and a section with the sun ray (bottom).
    /// In series mode — shadows of all times on one plan.
    /// </summary>
    public class PreviewControl : FrameworkElement
    {
        public bool SeriesMode { get; set; }

        ShadowSettings _s;
        public ShadowSettings Settings
        {
            get => _s;
            set
            {
                if (_s != null) _s.PropertyChanged -= OnChanged;
                _s = value;
                if (_s != null) _s.PropertyChanged += OnChanged;
                InvalidateVisual();
            }
        }

        void OnChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

        static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(250, 250, 248)));
        static readonly Brush BuildingFill = Freeze(new SolidColorBrush(Color.FromRgb(214, 208, 196)));
        static readonly Pen BuildingPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(70, 60, 50)), 1.5));
        static readonly Brush ShadowFill = Freeze(new SolidColorBrush(Color.FromArgb(110, 70, 80, 110)));
        static readonly Pen ShadowPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(60, 70, 100)), 1));
        static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(40, 40, 40)));
        static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(190, 30, 30)));
        static readonly Brush Sun = Freeze(new SolidColorBrush(Color.FromRgb(240, 170, 20)));
        static readonly Pen SunPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(220, 140, 0)), 1.5) { DashStyle = DashStyles.Dash });
        static readonly Pen GroundPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(120, 110, 100)), 1.5));
        static readonly Pen FramePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1));

        static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override void OnRender(DrawingContext dc)
        {
            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Bg, FramePen, full);
            if (_s == null || ActualWidth < 50 || ActualHeight < 50) return;

            try
            {
                if (SeriesMode)
                    DrawSeriesPlan(dc, new Rect(0, 0, ActualWidth, ActualHeight));
                else
                {
                    double planH = ActualHeight * 0.68;
                    DrawPlan(dc, new Rect(0, 0, ActualWidth, planH));
                    dc.DrawLine(FramePen, new Point(0, planH), new Point(ActualWidth, planH));
                    DrawSection(dc, new Rect(0, planH, ActualWidth, ActualHeight - planH));
                }
            }
            catch (Exception ex)
            {
                DrawText(dc, L.F("pv.error", ex.Message), new Point(10, 10), 11, Warn);
            }
        }

        double SampleHeight => _s.Floors > 0 && _s.FloorHeight > 0 ? _s.FixedHeightM : 24;

        // ------------------------------------------------------------ plan

        void DrawPlan(DrawingContext dc, Rect area)
        {
            Building b = ShadowCalc.SampleBuilding(SampleHeight);
            ShadowDirection dir = ShadowCalc.Direction(_s);
            bool valid = dir.AltitudeDeg > 0.5 && dir.AltitudeDeg < 89.9;

            PathsD shadow = null;
            if (valid)
            {
                double ratio = Math.Min(dir.LengthRatio, 15); // limit for the picture only
                var d = dir; d.AltitudeDeg = Math.Atan(1 / ratio) * 180 / Math.PI;
                d.Offset(b.HeightM, 1, out double dx, out double dy);
                shadow = ShadowGeometry.BuildingShadow(b, dx, dy);
            }

            var all = new List<PathD> { b.Outer };
            if (shadow != null) all.AddRange(shadow);
            var map = Fit(all, Deflate(area, 36, 30, 36, 42));

            if (shadow != null)
                DrawPaths(dc, shadow, dir.BelowThreshold ? Freeze(new SolidColorBrush(Color.FromArgb(60, 190, 30, 30))) : ShadowFill, ShadowPen, map);
            DrawPaths(dc, new PathsD { b.Outer }, BuildingFill, BuildingPen, map);

            DrawNorth(dc, new Point(area.Right - 20, area.Top + 26));
            if (valid) DrawSunArrow(dc, area, dir.DirectionDeg + 180);

            DrawText(dc, L.F("pv.building", b.HeightM), new Point(area.Left + 6, area.Top + 4), 10.5, Text);

            string info;
            Brush brush = Text;
            if (dir.AltitudeDeg <= 0)
            {
                info = L.F("pv.belowh", dir.AltitudeDeg);
                brush = Warn;
            }
            else if (dir.BelowThreshold)
            {
                info = L.F("pv.low", _s.MinAltitude, dir.AltitudeDeg);
                brush = Warn;
            }
            else
            {
                string az = dir.SunAzimuthDeg.HasValue ? L.F("pv.az", dir.SunAzimuthDeg.Value) : "";
                info = az + L.F("pv.info", dir.AltitudeDeg, dir.LengthRatio, b.HeightM * dir.LengthRatio);
                if (dir.LengthRatio > 15) info += L.T("pv.short");
            }
            DrawText(dc, info, new Point(area.Left + 6, area.Bottom - 18), 10, brush, area.Width - 12);
        }

        void DrawSeriesPlan(DrawingContext dc, Rect area)
        {
            Building b = ShadowCalc.SampleBuilding(SampleHeight);
            List<TimeSpan> times = ShadowCalc.SeriesTimes(_s);
            var shadows = new List<(TimeSpan t, PathsD p, ShadowDirection d)>();
            foreach (TimeSpan t in times)
            {
                ShadowDirection d = ShadowCalc.Direction(_s, t, true);
                if (d.BelowThreshold) continue;
                d.Offset(b.HeightM, 1, out double dx, out double dy);
                shadows.Add((t, ShadowGeometry.BuildingShadow(b, dx, dy), d));
            }

            var all = new List<PathD> { b.Outer };
            foreach (var s in shadows) all.AddRange(s.p);
            var map = Fit(all, Deflate(area, 24, 26, 24, 30));

            for (int i = 0; i < shadows.Count; i++)
            {
                Color c = SeriesColor(i, shadows.Count);
                var fill = Freeze(new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)));
                var pen = Freeze(new Pen(new SolidColorBrush(c), 1.3));
                DrawPaths(dc, shadows[i].p, fill, pen, map);
            }
            DrawPaths(dc, new PathsD { b.Outer }, BuildingFill, BuildingPen, map);

            for (int i = 0; i < shadows.Count; i++)
            {
                var (t, p, d) = shadows[i];
                double a = d.DirectionDeg * Math.PI / 180;
                PointD tip = p.SelectMany(x => x).OrderByDescending(q => q.x * Math.Cos(a) + q.y * Math.Sin(a)).First();
                Color c = SeriesColor(i, shadows.Count);
                DrawText(dc, t.ToString(@"hh\:mm"), map(tip.x, tip.y) + new Vector(2, -12), 9.5, Freeze(new SolidColorBrush(c)));
            }

            DrawNorth(dc, new Point(area.Right - 20, area.Top + 26));
            DrawText(dc, L.F("pv.building", b.HeightM) + $" · {_s.Date:d}", new Point(area.Left + 6, area.Top + 4), 10.5, Text);
            string info = shadows.Count == 0 ? L.F("pv.none", _s.MinAltitude) : L.F("pv.moments", shadows.Count, times.Count);
            DrawText(dc, info, new Point(area.Left + 6, area.Bottom - 18), 10, shadows.Count == 0 ? Warn : Text);
        }

        public static Color SeriesColor(int i, int n)
        {
            // From blue (morning) through green to orange-red (evening)
            double t = n <= 1 ? 0 : (double)i / (n - 1);
            double hue = 220 - 210 * t;
            return FromHsv(hue, 0.8, 0.75);
        }

        static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        // ------------------------------------------------------------ section

        void DrawSection(DrawingContext dc, Rect area)
        {
            ShadowDirection dir = ShadowCalc.Direction(_s);
            double H = SampleHeight;
            double W = 12; // block width in section
            bool valid = dir.AltitudeDeg > 0.5 && dir.AltitudeDeg < 89.9;
            double L = valid ? H * dir.LengthRatio : 0;
            double Ldraw = Math.Min(L, H * 15);

            Rect inner = Deflate(area, 24, 20, 24, 18);
            double worldW = W + Math.Max(Ldraw, W) + 4;
            double k = Math.Min(inner.Width / worldW, inner.Height / (H * 1.05));
            double groundY = inner.Bottom;
            double x0 = inner.Left + 4 * k;
            Point P(double x, double y) => new Point(x0 + x * k, groundY - y * k);

            dc.DrawLine(GroundPen, new Point(area.Left + 10, groundY), new Point(area.Right - 10, groundY));
            dc.DrawRectangle(BuildingFill, BuildingPen, new Rect(P(0, H), P(W, 0)));

            if (valid)
            {
                var shadowBrush = dir.BelowThreshold ? Freeze(new SolidColorBrush(Color.FromArgb(90, 190, 30, 30))) : ShadowFill;
                dc.DrawRectangle(shadowBrush, null, new Rect(P(W, 0) + new Vector(0, -4), P(W + Ldraw, 0)));
                // sun ray: through the top corner to the shadow tip
                Point top = P(W, H), end = P(W + Ldraw, 0);
                Vector v = top - end; v.Normalize();
                Point sunPt = top + v * Math.Min(60, (top - end).Length * 0.4);
                if (sunPt.Y < area.Top + 8) sunPt = top + v * Math.Max(0, (top.Y - area.Top - 8) / Math.Max(0.01, -v.Y));
                dc.DrawLine(SunPen, sunPt, end);
                dc.DrawEllipse(Sun, null, sunPt, 6, 6);

                DrawText(dc, $"α = {dir.AltitudeDeg:0.#}°", end + new Vector(-58, -16), 10, Text);
                DrawText(dc, $"L = {L:0.#} {DwgShadow.Core.L.T("m")}" + (L > Ldraw + 0.01 ? DwgShadow.Core.L.T("pv.short") : ""), P(W + Ldraw / 2, 0) + new Vector(-24, 2), 10, Text);
            }
            DrawText(dc, $"H = {H:0.#} {DwgShadow.Core.L.T("m")}", P(0, H / 2) + new Vector(3, -7), 10, Text);
            DrawText(dc, DwgShadow.Core.L.T("pv.section"), new Point(area.Left + 6, area.Top + 2), 10.5, Text);
        }

        // ------------------------------------------------------------ helpers

        static Rect Deflate(Rect r, double l, double t, double rr, double b) =>
            new Rect(r.Left + l, r.Top + t, Math.Max(10, r.Width - l - rr), Math.Max(10, r.Height - t - b));

        static Func<double, double, Point> Fit(IEnumerable<PathD> paths, Rect area)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (PathD p in paths)
                foreach (PointD q in p)
                {
                    x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y);
                    x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y);
                }
            double w = Math.Max(1e-6, x1 - x0), h = Math.Max(1e-6, y1 - y0);
            double k = Math.Min(area.Width / w, area.Height / h);
            double ox = area.Left + (area.Width - w * k) / 2, oy = area.Top + (area.Height - h * k) / 2;
            return (x, y) => new Point(ox + (x - x0) * k, oy + (y1 - y) * k); // Y axis up
        }

        static void DrawPaths(DrawingContext dc, PathsD paths, Brush fill, Pen pen, Func<double, double, Point> map)
        {
            var geo = new StreamGeometry { FillRule = System.Windows.Media.FillRule.EvenOdd };
            using (StreamGeometryContext g = geo.Open())
                foreach (PathD p in paths)
                {
                    if (p.Count < 2) continue;
                    g.BeginFigure(map(p[0].x, p[0].y), true, true);
                    for (int i = 1; i < p.Count; i++) g.LineTo(map(p[i].x, p[i].y), true, true);
                }
            geo.Freeze();
            dc.DrawGeometry(fill, pen, geo);
        }

        void DrawNorth(DrawingContext dc, Point c)
        {
            // North arrow: up, or rotated by the north angle (counter-clockwise)
            double ang = (_s.UseNorthAngle ? _s.NorthAngle : 0) * Math.PI / 180;
            Vector up = new Vector(-Math.Sin(ang), -Math.Cos(ang));
            Vector side = new Vector(-up.Y, up.X);
            Point tip = c + up * 13, tail = c - up * 10;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(tail + side * 6, true, true);
                g.LineTo(c - up * 4, true, true);
                g.LineTo(tail - side * 6, true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(Text, null, geo);
            DrawText(dc, L.T("pv.north"), tip + up * 8 + new Vector(-4, -7), 10.5, Text);
        }

        void DrawSunArrow(DrawingContext dc, Rect area, double towardSunDeg)
        {
            // The sun is on the side opposite to the shadow
            double a = towardSunDeg * Math.PI / 180;
            Vector v = new Vector(Math.Cos(a), -Math.Sin(a));
            // On the edge of the preview, towards the sun (away from the building and shadow)
            Rect box = Deflate(area, 14, 24, 14, 30);
            Point center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
            double tx = Math.Abs(v.X) < 1e-9 ? double.MaxValue : box.Width / 2 / Math.Abs(v.X);
            double ty = Math.Abs(v.Y) < 1e-9 ? double.MaxValue : box.Height / 2 / Math.Abs(v.Y);
            Point p = center + v * Math.Min(tx, ty);
            dc.DrawLine(SunPen, p, p - v * 28);
            dc.DrawEllipse(Sun, null, p, 6, 6);
            for (int i = 0; i < 8; i++)
            {
                double b = i * Math.PI / 4;
                Vector u = new Vector(Math.Cos(b), Math.Sin(b));
                dc.DrawLine(new Pen(Sun, 1.3), p + u * 8, p + u * 11);
            }
        }

        void DrawText(DrawingContext dc, string text, Point at, double size, Brush brush, double maxWidth = 0)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                       new Typeface("Segoe UI"), size, brush, dpi);
            if (maxWidth > 0) { ft.MaxTextWidth = maxWidth; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis; }
            dc.DrawText(ft, at);
        }
    }
}
