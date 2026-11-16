using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    static class DialogHelper
    {
        /// <summary>Does the window contain input fields with errors (red border)?</summary>
        public static bool HasInputErrors(DependencyObject root)
        {
            if (Validation.GetHasError(root)) return true;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                if (HasInputErrors(VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }

        public static bool Confirm(Window w, ShadowSettings s, bool series)
        {
            string err = HasInputErrors(w) ? L.T("v.fields") : s.Validate(series);
            if (err == null) return true;
            MessageBox.Show(w, err, w.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary>Common window setup: height limit and the language switch (reopens the window).</summary>
        public static void Init(Window w, ComboBox langBox, ShadowSettings s, Action reopen)
        {
            w.MaxHeight = SystemParameters.WorkArea.Height - 20;
            w.Title += "  —  DwgShadow v" + PluginInfo.Version;
            langBox.SelectedValue = s.Language;
            langBox.SelectionChanged += (o, e) =>
            {
                if (!(langBox.SelectedValue is UiLanguage lang) || lang == s.Language) return;
                s.Language = lang;
                L.Apply(lang);
                reopen();
            };
        }
    }

    public partial class ShadowWindow : Window
    {
        readonly DialogModel _model;

        public DialogOutcome Result { get; private set; }

        public ShadowWindow(DialogModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            Preview.Settings = model.S;
            DialogHelper.Init(this, LangBox, model.S, () => { Result = DialogOutcome.Reopen; DialogResult = false; });
        }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!DialogHelper.Confirm(this, _model.S, false)) return;
            ShadowDirection d = ShadowCalc.Direction(_model.S);
            if (d.BelowThreshold)
            {
                string msg = d.AltitudeDeg <= 0 ? L.F("v.sunbelow", d.AltitudeDeg) : L.F("v.sunlow", _model.S.MinAltitude, d.AltitudeDeg);
                MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        void Series_Click(object sender, RoutedEventArgs e)
        {
            Result = DialogOutcome.OpenSeries;
            DialogResult = false;
        }
    }

    public partial class SeriesWindow : Window
    {
        public class Row
        {
            public string Time { get; set; }
            public string Azimuth { get; set; }
            public string Altitude { get; set; }
            public string Ratio { get; set; }
            public string Status { get; set; }
        }

        readonly DialogModel _model;

        public DialogOutcome Result { get; private set; }

        public SeriesWindow(DialogModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            Preview.Settings = model.S;
            model.S.PropertyChanged += OnChanged;
            Closed += (s, e) => model.S.PropertyChanged -= OnChanged;
            DialogHelper.Init(this, LangBox, model.S, () => { Result = DialogOutcome.Reopen; DialogResult = false; });
            RefreshTable();
        }

        void OnChanged(object sender, PropertyChangedEventArgs e) => RefreshTable();

        void RefreshTable()
        {
            var rows = new List<Row>();
            ShadowSettings s = _model.S;
            foreach (TimeSpan t in ShadowCalc.SeriesTimes(s).Take(200))
            {
                ShadowDirection d = ShadowCalc.Direction(s, t, true);
                rows.Add(new Row
                {
                    Time = t.ToString(@"hh\:mm"),
                    Azimuth = d.SunAzimuthDeg?.ToString("0.0") ?? "",
                    Altitude = d.AltitudeDeg.ToString("0.0"),
                    Ratio = d.AltitudeDeg > 0 ? d.LengthRatio.ToString("0.00") : "—",
                    Status = d.AltitudeDeg <= 0 ? L.T("st.below") : d.BelowThreshold ? L.T("st.low") : ""
                });
            }
            Table.ItemsSource = rows;
        }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!DialogHelper.Confirm(this, _model.S, true)) return;
            if (ShadowCalc.SeriesTimes(_model.S).All(t => ShadowCalc.Direction(_model.S, t, true).BelowThreshold))
            {
                MessageBox.Show(this, L.F("v.seriesnone", _model.S.MinAltitude), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
