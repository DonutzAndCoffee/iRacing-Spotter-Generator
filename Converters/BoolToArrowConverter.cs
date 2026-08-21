using System.Globalization;
using System.Windows.Data;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Converts a bool (e.g. a ToggleButton's IsChecked state) to a small arrow
    /// glyph, used to visually indicate that a button expands/collapses a panel.
    /// </summary>
    public class BoolToArrowConverter : IValueConverter
    {
        public static readonly BoolToArrowConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "\u25B2" : "\u25BC";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
