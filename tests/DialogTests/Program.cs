using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DwgShadow.Core;
using DwgShadow.UI;

// Types bad input into every numeric field of the real dialogs. A converter exception here once
// crashed AutoCAD (Backspace to an empty "Floors" box), so: no exception may escape, the field must
// be flagged as an error, and the settings must keep their last valid value.
static class Program
{
    static int failures;

    static void Check(bool ok, string name)
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}");
        if (!ok) failures++;
    }

    [STAThread]
    static int Main()
    {
        const string acad = @"C:\Program Files\Autodesk\AutoCAD 2021";
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string f = Path.Combine(acad, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? Assembly.LoadFrom(f) : null;
        };
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Exception escaped = null;
        app.DispatcherUnhandledException += (s, e) => { escaped = e.Exception; e.Handled = true; };

        var layers = new List<string> { "0", "Buildings", "Building_labels", "Shadows" };
        foreach (string lang in new[] { "en", "ru" })
        {
            L.Apply(lang == "ru" ? UiLanguage.Russian : UiLanguage.English);
            var s = new ShadowSettings { ExpLocation = true, ExpLabels = true, ExpStyle = true };
            s.FillDefaults();
            RunWindow($"Shadow window ({lang})", new ShadowWindow(new DialogModel(s, layers)), s, ref escaped);

            var s2 = new ShadowSettings { ExpLocation = true, ExpLabels = true, ExpStyle = true };
            s2.FillDefaults();
            RunWindow($"Series window ({lang})", new SeriesWindow(new DialogModel(s2, layers)), s2, ref escaped);
        }

        Console.WriteLine(failures == 0 ? "\nAll dialog checks passed" : $"\nFailures: {failures}");
        return failures == 0 ? 0 : 1;
    }

    static void RunWindow(string name, Window w, ShadowSettings s, ref Exception escaped)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -5000; w.Top = 0; w.ShowActivated = false;
        w.Show();
        Pump();

        var boxes = Descendants<TextBox>(w)
            .Where(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Converter is NumberConverter)
            .ToList();
        Check(boxes.Count >= 8, $"{name}: numeric fields found ({boxes.Count})");

        MethodInfo hasErrors = typeof(ShadowWindow).Assembly.GetType("DwgShadow.UI.DialogHelper")
            .GetMethod("HasInputErrors", BindingFlags.Public | BindingFlags.Static);

        foreach (TextBox box in boxes)
        {
            string path = BindingOperations.GetBinding(box, TextBox.TextProperty).Path.Path; // e.g. "S.Floors"
            PropertyInfo prop = typeof(ShadowSettings).GetProperty(path.Substring(2));
            object before = prop.GetValue(s);
            box.IsEnabled = true;

            foreach (string bad in new[] { "", "abc", "-", "1e999", "," })
            {
                Exception ex = null;
                try { box.Text = bad; Pump(); } catch (Exception e) { ex = e; }
                if (escaped != null) { ex = escaped; escaped = null; }
                bool flagged = Validation.GetHasError(box);
                if (ex != null || !flagged || !Equals(prop.GetValue(s), before))
                    Check(false, $"{name}: {path} = \"{bad}\" → exception: {ex?.GetType().Name ?? "none"}, flagged: {flagged}, value kept: {Equals(prop.GetValue(s), before)}");
            }
            Check((bool)hasErrors.Invoke(null, new object[] { w }), $"{name}: {path} — dialog reports input errors");

            // A valid number again (comma as decimal separator) clears the error and is stored.
            string good = prop.PropertyType == typeof(double) ? "5,5" : "5";
            box.Text = good; Pump();
            object expected = prop.PropertyType == typeof(double) ? (object)5.5 : prop.PropertyType == typeof(short) ? (object)(short)5 : 5;
            Check(!Validation.GetHasError(box) && Equals(prop.GetValue(s), expected), $"{name}: {path} = \"{good}\" accepted");
        }
        Check(!(bool)hasErrors.Invoke(null, new object[] { w }), $"{name}: no errors left");
        w.Close();
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            DependencyObject c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (T d in Descendants<T>(c)) yield return d;
        }
    }

    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
