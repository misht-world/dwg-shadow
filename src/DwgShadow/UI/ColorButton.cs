using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    /// <summary>Colour picker button (ACI index). Opens the standard AutoCAD colour dialog.</summary>
    public class ColorButton : Button
    {
        readonly Rectangle _swatch = new Rectangle { Width = 26, Height = 14, Stroke = Brushes.Gray, Margin = new Thickness(0, 0, 6, 0) };
        readonly TextBlock _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        DialogModel _model;

        public ColorButton()
        {
            Margin = new Thickness(4, 2, 4, 2);
            Padding = new Thickness(6, 2, 6, 2);
            HorizontalAlignment = HorizontalAlignment.Left;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_swatch);
            sp.Children.Add(_text);
            Content = sp;
            DataContextChanged += (s, e) =>
            {
                if (_model != null) _model.S.PropertyChanged -= OnSettingsChanged;
                _model = DataContext as DialogModel;
                if (_model != null) _model.S.PropertyChanged += OnSettingsChanged;
                Refresh();
            };
            Click += OnClick;
        }

        void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Core.ShadowSettings.ColorIndex)) Refresh();
        }

        void Refresh()
        {
            if (_model == null) return;
            short aci = _model.S.ColorIndex;
            _text.Text = aci == 256 ? L.T("color.bylayer") : aci == 0 ? L.T("color.byblock") : L.F("color.n", aci);
            _swatch.Fill = aci == 256 || aci == 0 ? (Brush)Brushes.Transparent : new SolidColorBrush(AciToColor(aci));
        }

        void OnClick(object sender, RoutedEventArgs e)
        {
            if (_model == null) return;
            try
            {
                short? picked = PickWithAutoCad(_model.S.ColorIndex);
                if (picked.HasValue) _model.S.ColorIndex = picked.Value;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), L.F("color.err", ex.Message), "DwgShadow");
            }
        }

        /// <summary>Running inside AutoCAD (not in the snapshot test harness).</summary>
        static readonly bool InAutoCad = System.Diagnostics.Process.GetCurrentProcess().ProcessName
            .StartsWith("acad", StringComparison.OrdinalIgnoreCase);

        static Color AciToColor(short aci)
        {
            if (!InAutoCad) return Colors.Gray;
            try { return AciToColorAcad(aci); }
            catch { return Colors.Gray; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Color AciToColorAcad(short aci)
        {
            System.Drawing.Color c = Autodesk.AutoCAD.Colors.Color
                .FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci).ColorValue;
            return Color.FromRgb(c.R, c.G, c.B);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static short? PickWithAutoCad(short current)
        {
            var dlg = new Autodesk.AutoCAD.Windows.ColorDialog
            {
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, current),
                IncludeByBlockByLayer = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
            Autodesk.AutoCAD.Colors.Color col = dlg.Color;
            if (col.IsByLayer) return 256;
            if (col.IsByBlock) return 0;
            if (col.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci) return col.ColorIndex;
            // True colour / colour book — use the nearest ACI colour
            return (short)Autodesk.AutoCAD.Colors.EntityColor.LookUpAci(col.Red, col.Green, col.Blue);
        }
    }
}
