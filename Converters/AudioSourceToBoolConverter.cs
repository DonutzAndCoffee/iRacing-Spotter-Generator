using System.Globalization;
using System.Windows.Data;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Converts an AudioSourceType to a bool indicating whether it equals GoogleAi,
    /// used to enable/disable the AI voice combo box per row.
    /// </summary>
    public class AudioSourceToBoolConverter : IValueConverter
    {
        public static readonly AudioSourceToBoolConverter IsGoogleAi = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is AudioSourceType.GoogleAi;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
