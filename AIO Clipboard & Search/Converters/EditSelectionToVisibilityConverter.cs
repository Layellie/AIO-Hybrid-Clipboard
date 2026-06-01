using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AIO_Hybrid_Clipboard.Converters
{
    /// <summary>
    /// Shows the selection checkmark badge only when the item is selected
    /// AND the list is in edit mode (SelectionMode == Multiple).
    /// values[0] = ListBoxItem.IsSelected, values[1] = ListBox.SelectionMode.
    /// </summary>
    public sealed class EditSelectionToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isSelected = values.Length > 0 && values[0] is bool b && b;
            bool editMode   = values.Length > 1 && values[1] is SelectionMode m && m == SelectionMode.Multiple;
            return isSelected && editMode ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
