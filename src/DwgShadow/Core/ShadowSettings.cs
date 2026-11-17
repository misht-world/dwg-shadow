using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DwgShadow.Core
{
    public enum SourceMode { Layer, Selection }
    public enum HeightMode { Fixed, FromText }
    public enum TextMeaning { Floors, Meters }
    public enum SunMode { Manual, Astronomical }
    public enum DrawingUnits { Meters, Centimeters, Millimeters }

    /// <summary>All build parameters. Persisted between sessions in %APPDATA%\DwgShadow\settings.xml.</summary>
    public class ShadowSettings : INotifyPropertyChanged
    {
        // ---- buildings
        SourceMode _sourceMode = SourceMode.Layer;
        string _sourceLayer = "";
        bool _courtyards = true;

        // ---- height
        HeightMode _heightMode = HeightMode.Fixed;
        double _floors = 8;
        double _floorHeight = 3.0;
        TextMeaning _textMeaning = TextMeaning.Floors;
        string _textLayer = "";

        // ---- sun
        SunMode _sunMode = SunMode.Manual;
        double _manualDirection = 45;
        double _manualAltitude = 45;
        double _latitude = 55.7558;
        double _longitude = 37.6173;
        double _utcOffset = 3;
        DateTime _date = new DateTime(DateTime.Today.Year, 6, 22);
        string _time = "12:00";
        bool _useNorthAngle;
        double _northAngle;
        double _minAltitude = 5;

        // ---- appearance
        DrawingUnits _units = DrawingUnits.Meters;
        string _outputLayer = "";
        short _colorIndex = 8;
        bool _drawContour = true;
        bool _drawHatch = true;
        string _hatchPattern = "SOLID";
        double _hatchScale = 1;
        int _hatchTransparency = 50;
        bool _hatchToBack = true;
        bool _subtractFootprints = true;
        bool _replacePrevious = true;

        // ---- series
        string _seriesStart = "09:00";
        string _seriesEnd = "18:00";
        int _seriesStep = 60;
        string _seriesLayerPrefix = "";
        bool _seriesLayerPerTime = true;
        bool _seriesLabels = true;
        double _seriesLabelHeight = 2.5;
        bool _seriesEnvelope = true;

        // ---- UI state
        UiLanguage _language = UiLanguage.Auto;
        bool _expLocation, _expLabels, _expStyle;

        public SourceMode SourceMode { get => _sourceMode; set => Set(ref _sourceMode, value); }
        public string SourceLayer { get => _sourceLayer; set => Set(ref _sourceLayer, value ?? ""); }
        public bool Courtyards { get => _courtyards; set => Set(ref _courtyards, value); }

        public HeightMode HeightMode { get => _heightMode; set { Set(ref _heightMode, value); Raise(nameof(HeightFromLabels)); } }
        /// <summary>Checkbox-friendly view of <see cref="HeightMode"/>.</summary>
        public bool HeightFromLabels { get => HeightMode == HeightMode.FromText; set => HeightMode = value ? HeightMode.FromText : HeightMode.Fixed; }
        public double Floors { get => _floors; set { Set(ref _floors, value); Raise(nameof(FixedHeightM)); } }
        public double FloorHeight { get => _floorHeight; set { Set(ref _floorHeight, value); Raise(nameof(FixedHeightM)); } }
        public TextMeaning TextMeaning { get => _textMeaning; set => Set(ref _textMeaning, value); }
        /// <summary>Layer of height labels; empty = any layer.</summary>
        public string TextLayer { get => _textLayer; set => Set(ref _textLayer, value ?? ""); }

        public SunMode SunMode { get => _sunMode; set => Set(ref _sunMode, value); }
        /// <summary>Manual mode: shadow direction, degrees from X axis counter-clockwise.</summary>
        public double ManualDirection { get => _manualDirection; set => Set(ref _manualDirection, value); }
        /// <summary>Manual mode: sun altitude, degrees (45° → shadow length = height).</summary>
        public double ManualAltitude { get => _manualAltitude; set => Set(ref _manualAltitude, value); }
        public double Latitude { get => _latitude; set => Set(ref _latitude, value); }
        public double Longitude { get => _longitude; set => Set(ref _longitude, value); }
        public double UtcOffset { get => _utcOffset; set => Set(ref _utcOffset, value); }
        public DateTime Date { get => _date; set => Set(ref _date, value.Date); }
        /// <summary>Time "HH:MM".</summary>
        public string Time { get => _time; set => Set(ref _time, value ?? ""); }
        public bool UseNorthAngle { get => _useNorthAngle; set => Set(ref _useNorthAngle, value); }
        /// <summary>Rotation of north relative to the drawing Y axis, degrees counter-clockwise.</summary>
        public double NorthAngle { get => _northAngle; set => Set(ref _northAngle, value); }
        /// <summary>Safety limit: no shadow is built when the sun is lower than this.</summary>
        public double MinAltitude { get => _minAltitude; set => Set(ref _minAltitude, value); }

        public DrawingUnits Units { get => _units; set => Set(ref _units, value); }
        public string OutputLayer { get => _outputLayer; set => Set(ref _outputLayer, value ?? ""); }
        public short ColorIndex { get => _colorIndex; set => Set(ref _colorIndex, value); }
        public bool DrawContour { get => _drawContour; set => Set(ref _drawContour, value); }
        public bool DrawHatch { get => _drawHatch; set => Set(ref _drawHatch, value); }
        public string HatchPattern { get => _hatchPattern; set => Set(ref _hatchPattern, value ?? "SOLID"); }
        public double HatchScale { get => _hatchScale; set => Set(ref _hatchScale, value); }
        public int HatchTransparency { get => _hatchTransparency; set => Set(ref _hatchTransparency, value); }
        /// <summary>Draw order: shadows right below the building layer (name kept for saved settings).</summary>
        public bool HatchToBack { get => _hatchToBack; set => Set(ref _hatchToBack, value); }
        public bool SubtractFootprints { get => _subtractFootprints; set => Set(ref _subtractFootprints, value); }
        /// <summary>Remove previous shadows: whole layer in layer mode, only the picked buildings' shadows in selection mode.</summary>
        public bool ReplacePrevious { get => _replacePrevious; set => Set(ref _replacePrevious, value); }

        public string SeriesStart { get => _seriesStart; set => Set(ref _seriesStart, value ?? ""); }
        public string SeriesEnd { get => _seriesEnd; set => Set(ref _seriesEnd, value ?? ""); }
        public int SeriesStep { get => _seriesStep; set => Set(ref _seriesStep, value); }
        public string SeriesLayerPrefix { get => _seriesLayerPrefix; set => Set(ref _seriesLayerPrefix, value ?? ""); }
        public bool SeriesLayerPerTime { get => _seriesLayerPerTime; set => Set(ref _seriesLayerPerTime, value); }
        public bool SeriesLabels { get => _seriesLabels; set => Set(ref _seriesLabels, value); }
        public double SeriesLabelHeight { get => _seriesLabelHeight; set => Set(ref _seriesLabelHeight, value); }
        public bool SeriesEnvelope { get => _seriesEnvelope; set => Set(ref _seriesEnvelope, value); }

        public UiLanguage Language { get => _language; set => Set(ref _language, value); }
        public bool ExpLocation { get => _expLocation; set => Set(ref _expLocation, value); }
        public bool ExpLabels { get => _expLabels; set => Set(ref _expLabels, value); }
        public bool ExpStyle { get => _expStyle; set => Set(ref _expStyle, value); }

        // ------------------------------------------------------------ derived

        public double UnitsPerMeter => Units == DrawingUnits.Millimeters ? 1000 : Units == DrawingUnits.Centimeters ? 100 : 1;
        public double FixedHeightM => Floors * FloorHeight;

        /// <summary>Fills empty layer names with localized defaults.</summary>
        public void FillDefaults()
        {
            if (string.IsNullOrWhiteSpace(OutputLayer)) OutputLayer = L.T("def.layer");
            if (string.IsNullOrWhiteSpace(SeriesLayerPrefix)) SeriesLayerPrefix = L.T("def.series");
        }

        public static bool TryParseTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace('.', ':').Replace(',', ':');
            string[] fmts = { @"h\:mm", @"hh\:mm", @"h", @"hh", @"h\:mm\:ss", @"hh\:mm\:ss" };
            if (TimeSpan.TryParseExact(s, fmts, CultureInfo.InvariantCulture, out t))
                return t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
            return false;
        }

        /// <summary>Validates parameters. Returns a localized error message or null.</summary>
        public string Validate(bool forSeries)
        {
            if (SourceMode == SourceMode.Layer && string.IsNullOrWhiteSpace(SourceLayer)) return L.T("v.layer");
            // Floors are always checked: in label mode they are the fallback height.
            if (!(Floors > 0 && Floors <= 300)) return L.T("v.floors");
            if (!(FloorHeight > 0 && FloorHeight <= 20)) return L.T("v.floorh");
            if (!(MinAltitude >= 0.5 && MinAltitude <= 60)) return L.T("v.minalt");
            if (forSeries || SunMode == SunMode.Astronomical)
            {
                if (!(Latitude >= -90 && Latitude <= 90)) return L.T("v.lat");
                if (!(Longitude >= -180 && Longitude <= 180)) return L.T("v.lon");
                if (!(UtcOffset >= -12 && UtcOffset <= 14)) return L.T("v.utc");
            }
            if (!forSeries && SunMode == SunMode.Manual && !(ManualAltitude > 0 && ManualAltitude < 90)) return L.T("v.alt");
            if (!forSeries && SunMode == SunMode.Astronomical && !TryParseTime(Time, out _)) return L.T("v.time");
            if (forSeries)
            {
                if (!TryParseTime(SeriesStart, out TimeSpan a)) return L.T("v.start");
                if (!TryParseTime(SeriesEnd, out TimeSpan b)) return L.T("v.end");
                if (b <= a) return L.T("v.order");
                if (SeriesStep < 5 || SeriesStep > 720) return L.T("v.step");
                if ((b - a).TotalMinutes / SeriesStep > 200) return L.T("v.many");
                if (string.IsNullOrWhiteSpace(SeriesLayerPrefix)) return L.T("v.prefix");
                if (SeriesLabels && !(SeriesLabelHeight > 0)) return L.T("v.labelh");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(OutputLayer)) return L.T("v.outlayer");
                if (!DrawContour && !DrawHatch) return L.T("v.nothing");
            }
            if (HatchTransparency < 0 || HatchTransparency > 90) return L.T("v.transp");
            if (!(HatchScale > 0)) return L.T("v.scale");
            return null;
        }

        // ------------------------------------------------------------ persistence
        // Plain reflection-based XML: XmlSerializer generates code at runtime and is unreliable
        // inside AutoCAD plug-ins (the generated assembly may fail to resolve this one).

        static PropertyInfo[] Persisted => typeof(ShadowSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(HeightFromLabels))
            .ToArray();

        public static string DefaultPath
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("DWGSHADOW_SETTINGS");
                if (!string.IsNullOrWhiteSpace(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DwgShadow", "settings.xml");
            }
        }

        /// <summary>Loads settings; unknown or broken values keep their defaults. Errors go to <paramref name="error"/>.</summary>
        public static ShadowSettings Load(out string error, string path = null)
        {
            error = null;
            var s = new ShadowSettings();
            path = path ?? DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    XElement root = XDocument.Load(path).Root;
                    foreach (PropertyInfo p in Persisted)
                    {
                        XElement e = root?.Element(p.Name);
                        if (e == null) continue;
                        try { p.SetValue(s, Parse(e.Value, p.PropertyType)); }
                        catch (Exception ex) { error = $"{p.Name}: {ex.Message}"; }
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; }
            L.Apply(s.Language);
            s.FillDefaults();
            return s;
        }

        public static ShadowSettings Load() => Load(out _);

        /// <summary>Saves settings. Returns an error message or null.</summary>
        public string Save(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var root = new XElement("ShadowSettings",
                    Persisted.Select(p => new XElement(p.Name, Format(p.GetValue(this)))));
                new XDocument(root).Save(path);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public ShadowSettings Clone()
        {
            var c = new ShadowSettings();
            foreach (PropertyInfo p in Persisted) p.SetValue(c, p.GetValue(this));
            return c;
        }

        static string Format(object v)
        {
            switch (v)
            {
                case DateTime d: return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return v?.ToString() ?? "";
            }
        }

        static object Parse(string text, Type t)
        {
            text = text.Trim();
            if (t == typeof(string)) return text;
            if (t == typeof(bool)) return text == "true" || text == "1" || text.Equals("True", StringComparison.OrdinalIgnoreCase);
            if (t == typeof(double)) return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (t == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
            if (t == typeof(short)) return short.Parse(text, CultureInfo.InvariantCulture);
            if (t == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
            if (t.IsEnum) return Enum.Parse(t, text, true);
            throw new NotSupportedException(t.Name);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            Raise(name);
        }
    }
}
