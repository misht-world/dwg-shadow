using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    /// <summary>Localized text in XAML: <c>{ui:T key}</c>.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class T : MarkupExtension
    {
        public T() { }
        public T(string key) { Key = key; }
        public string Key { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
    }

    /// <summary>Number ↔ text. Accepts both comma and dot.</summary>
    public class NumberConverter : IValueConverter
    {
        // WPF re-reads the value into the box right after writing it; to keep "5," from turning into
        // "5" while typing, return the text just entered when the number is unchanged.
        string _lastText;
        object _lastValue;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (_lastText != null && Equals(value, _lastValue))
            {
                string t = _lastText;
                _lastText = null;
                return t;
            }
            _lastText = null;
            if (value is IFormattable f) return f.ToString("0.######", CultureInfo.CurrentCulture);
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = (value as string ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                throw new FormatException(L.T("v.number"));
            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            object result;
            if (t == typeof(int))
            {
                if (Math.Abs(d - Math.Round(d)) > 1e-9) throw new FormatException(L.T("v.integer"));
                result = (int)Math.Round(d);
            }
            else if (t == typeof(short)) result = (short)Math.Round(d);
            else result = d;
            _lastText = value as string;
            _lastValue = result;
            return result;
        }
    }

    /// <summary>Enum ↔ radio button (ConverterParameter = value name).</summary>
    public class EnumBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Enum.Parse(targetType, (string)parameter) : Binding.DoNothing;
    }

    public class Choice
    {
        public object Value { get; set; }
        public string Title { get; set; }
        public override string ToString() => Title;
    }
}
