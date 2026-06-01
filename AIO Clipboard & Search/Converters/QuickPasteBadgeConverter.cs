using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIO_Hybrid_Clipboard.Converters
{
    /// <summary>
    /// Maps a 0-based item index (ItemsControl.AlternationIndex) to the quick-paste
    /// number badge. The first three items map to "1", "2", "3" (matching Alt+1/2/3).
    /// Pass ConverterParameter="vis" to get a Visibility instead of the label text.
    /// </summary>
    public sealed class QuickPasteBadgeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index = value is int i ? i : -1;
            bool inRange = index >= 0 && index < 3;

            if (parameter as string == "vis")
                return inRange ? Visibility.Visible : Visibility.Collapsed;

            return inRange ? (index + 1).ToString() : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
