using Autodesk.AutoCAD.ApplicationServices;
using DwgShadow.Cad;
using DwgShadow.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DwgShadow.UI
{
    /// <summary>Opens dialogs from commands. Building runs after the dialog closes, in command context.</summary>
    static class Ui
    {
        public static void ShowShadowDialog(Document doc)
        {
            ShadowSettings s = Settings.Load();
            while (true)
            {
                var win = new ShadowWindow(new DialogModel(s, CadReader.LayerNames(doc.Database)));
                bool? ok = AcApp.ShowModalWindow(AcApp.MainWindow.Handle, win, true);
                Settings.Save(s); // remember what was entered even on Cancel
                if (win.Result == DialogOutcome.Reopen) continue;
                if (ok == true) ShadowRunner.Build(doc, s);
                else if (win.Result == DialogOutcome.OpenSeries) ShowSeriesDialog(doc);
                return;
            }
        }

        public static void ShowSeriesDialog(Document doc)
        {
            ShadowSettings s = Settings.Load();
            while (true)
            {
                var win = new SeriesWindow(new DialogModel(s, CadReader.LayerNames(doc.Database)));
                bool? ok = AcApp.ShowModalWindow(AcApp.MainWindow.Handle, win, true);
                Settings.Save(s);
                if (win.Result == DialogOutcome.Reopen) continue;
                if (ok == true) ShadowRunner.BuildSeries(doc, s);
                return;
            }
        }
    }
}
