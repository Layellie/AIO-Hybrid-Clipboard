using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIO_Hybrid_Clipboard.Converters
{
    /// <summary>
    /// Converts a hex color string (e.g. "#FF5865F2" or "#5865F2") into a cached,
    /// frozen <see cref="SolidColorBrush"/> for zero-allocation swatch rendering.
    /// </summary>
    public sealed class HexToBrushConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string hex || string.IsNullOrWhiteSpace(hex))
                return Brushes.Transparent;

            string key = hex.Trim();
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                if (ColorConverter.ConvertFromString(key) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    // Cap cache size to prevent unbounded growth on exotic sessions.
                    if (Cache.Count < 256)
                        Cache.TryAdd(key, brush);
                    return brush;
                }
            }
            catch (FormatException)
            {
                // Invalid hex string falls back to transparent.
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a string (or integer count) to <see cref="Visibility.Visible"/> when non-empty / &gt; 0.
    /// Pass ConverterParameter="invert" to reverse the result (e.g. for placeholder text or empty states).
    /// </summary>
    public sealed class NonEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool hasContent = value switch
            {
                string s => !string.IsNullOrWhiteSpace(s),
                int i    => i > 0,
                bool b   => b,
                null     => false,
                _        => true
            };

            bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            if (invert) hasContent = !hasContent;

            return hasContent ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Compares a bound enum/value with ConverterParameter for RadioButton pill bindings.
    /// </summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true && parameter != null && targetType.IsEnum)
                return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);

            return Binding.DoNothing;
        }
    }
}
