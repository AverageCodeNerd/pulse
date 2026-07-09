using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Pulse.Converters;

/// <summary>Colors a status string: Running = green, Not responding = red, else default text.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromArgb(255, 0x3F, 0xB9, 0x50));
    private static readonly SolidColorBrush Red = new(Color.FromArgb(255, 0xE0, 0x5B, 0x5F));

    public object Convert(object value, System.Type targetType, object parameter, string language)
    {
        string? s = value as string;
        if (s == "Running") return Green;
        if (s == "Not responding") return Red;
        return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => throw new System.NotImplementedException();
}
