using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DwgShadow.Core;
using DwgShadow.UI;

// Renders the plug-in dialogs to PNG without AutoCAD — for checking the layout.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Assembly attributes of the plug-in reference AutoCAD types — resolve them from the installed AutoCAD.
        const string acad = @"C:\Program Files\Autodesk\AutoCAD 2021";
        Environment.SetEnvironmentVariable("PATH", acad + ";" + Environment.GetEnvironmentVariable("PATH"));
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string f = Path.Combine(acad, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
        };
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var layers = new List<string> { "0", "Defpoints", "Buildings", "Building_labels", "Shadows", "Roads" };

        foreach (var lang in new[] { UiLanguage.Russian, UiLanguage.English })
        {
            string sfx = lang == UiLanguage.Russian ? "ru" : "en";
            L.Apply(lang);

            var s1 = new ShadowSettings { Language = lang, SunMode = SunMode.Astronomical, Time = "10:00", Date = new DateTime(2026, 6, 22) };
            s1.FillDefaults();
            Snap(new ShadowWindow(new DialogModel(s1, layers)), Path.Combine(outDir, $"shadow-{sfx}.png"));

            var s2 = new ShadowSettings { Language = lang, ExpLocation = true, ExpLabels = true, ExpStyle = true, HeightMode = HeightMode.FromText };
            s2.FillDefaults();
            Snap(new ShadowWindow(new DialogModel(s2, layers)), Path.Combine(outDir, $"shadow-expanded-{sfx}.png"));

            var s3 = new ShadowSettings { Language = lang, Date = new DateTime(2026, 3, 21), SeriesStart = "8:00", SeriesEnd = "17:00", SeriesStep = 60 };
            s3.FillDefaults();
            Snap(new SeriesWindow(new DialogModel(s3, layers)), Path.Combine(outDir, $"series-{sfx}.png"));
        }
        return 0;
    }

    static void Snap(Window w, string file)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = 0; w.Top = 0;
        w.ShowActivated = false;
        w.Show();
        w.UpdateLayout();
        Dispatch();
        var root = (FrameworkElement)w.Content;
        int width = (int)root.ActualWidth + 20, height = (int)root.ActualHeight + 20;
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(10, 10, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) enc.Save(fs);
        Console.WriteLine($"saved {file} ({width}x{height})");
        w.Close();
    }

    static void Dispatch()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
