using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace LumaWall.ControlCenter;

// Rendering-only extension into the thumb's hit area. The native Slider keeps
// ownership of value, dragging and decrease-rectangle dimensions.
public sealed class SliderJoinConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double length = value is double size && double.IsFinite(size) ? Math.Max(0, size) : 0;
        double overlap = Math.Min(3, length);
        return (parameter as string) switch
        {
            "Visibility" => length > 0 ? Visibility.Visible : Visibility.Collapsed,
            "NegativeOffset" => -overlap,
            "Offset" => overlap,
            _ => 14 + overlap
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
