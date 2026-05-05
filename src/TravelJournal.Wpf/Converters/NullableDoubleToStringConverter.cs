using System.Globalization;
using System.Windows.Data;

namespace TravelJournal.Wpf.Converters;

public class NullableDoubleToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return "—";
        var format = parameter as string ?? "F6";
        return d.ToString(format, CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
