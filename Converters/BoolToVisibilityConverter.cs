using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Converts a bool to Visibility.Visible/Collapsed, used to show/hide the
    /// inline settings panel based on the settings toggle button state.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }
}
