using TravelJournal.Core.Models;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TravelJournal.Wpf.Converters;

public class PhotoStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Selected   = new(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush Deselected = new(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly SolidColorBrush None       = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush Start      = new(Color.FromRgb(0xF5, 0x7F, 0x17));
    private static readonly SolidColorBrush End        = new(Color.FromRgb(0x6A, 0x1B, 0x9A));

    static PhotoStateToBrushConverter()
    {
        Selected.Freeze();
        Deselected.Freeze();
        None.Freeze();
        Start.Freeze();
        End.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PhotoState state
            ? state switch
            {
                PhotoState.Selected   => Selected,
                PhotoState.Deselected => Deselected,
                PhotoState.Start      => Start,
                PhotoState.End        => End,
                _                     => None
            }
            : None;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
