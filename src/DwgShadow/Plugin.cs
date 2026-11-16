using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using DwgShadow.Cad;
using DwgShadow.Core;
using DwgShadow.UI;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(DwgShadow.Plugin))]
[assembly: CommandClass(typeof(DwgShadow.Commands))]

namespace DwgShadow
{
    public class Plugin : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                ShadowSettings.Load(); // applies the saved UI language
                CoreApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + L.T("msg.loaded") + "\n");
            }
            catch { }
        }

        public void Terminate() { }
    }

    public class Commands
    {
        [CommandMethod("DWGSHADOW", "SHD", CommandFlags.Modal)]
        public void Shadow() => Guard("SHD", doc => Ui.ShowShadowDialog(doc));

        [CommandMethod("DWGSHADOW", "SHDSERIES", CommandFlags.Modal)]
        public void Series() => Guard("SHDSERIES", doc => Ui.ShowSeriesDialog(doc));

        /// <summary>No dialog — uses the saved settings (for scripts and quick rebuilds).</summary>
        [CommandMethod("DWGSHADOW", "-SHD", CommandFlags.Modal)]
        public void ShadowNoDialog() => Guard("-SHD", doc => ShadowRunner.Build(doc, Settings.Load()));

        [CommandMethod("DWGSHADOW", "-SHDSERIES", CommandFlags.Modal)]
        public void SeriesNoDialog() => Guard("-SHDSERIES", doc => ShadowRunner.BuildSeries(doc, Settings.Load()));

        /// <summary>Any error goes to the command line and the log; AutoCAD keeps running.</summary>
        static void Guard(string name, Action<Document> action)
        {
            Document doc = CoreApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                action(doc);
            }
            catch (System.Exception ex)
            {
                string msg = ex is InvalidOperationException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                doc.Editor.WriteMessage($"\n{name}: " + L.F("msg.error", msg, Log.FilePath) + "\n");
                Log.Write(name, ex);
            }
        }
    }

    /// <summary>Settings load/save with logging of problems.</summary>
    static class Settings
    {
        public static ShadowSettings Load()
        {
            ShadowSettings s = ShadowSettings.Load(out string err);
            if (err != null) Log.Write("settings load", new InvalidDataException(err));
            return s;
        }

        public static void Save(ShadowSettings s)
        {
            string err = s.Save();
            if (err != null) Log.Write("settings save", new IOException(err));
        }
    }

    static class Log
    {
        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DwgShadow", "log.txt");

        public static void Write(string context, System.Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n{ex}\n\n");
            }
            catch { }
        }
    }
}
