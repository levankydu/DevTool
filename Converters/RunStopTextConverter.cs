using System.Globalization;
using System.Windows.Data;

namespace PathwayDevTool.Converters
{
    public class RunStopTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
                return isRunning ? "Stop" : "Run";

            return "Run";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
