using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace LumaWall.ControlCenter;

// Every filled frame layer uses the same center of curvature, including controls
// that override CornerRadius. No transparent border bands meet at the corners.
public sealed class InsetCornerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not CornerRadius radius) return new CornerRadius(0);
        double inset = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        return new CornerRadius(Math.Max(0, radius.TopLeft - inset), Math.Max(0, radius.TopRight - inset),
            Math.Max(0, radius.BottomRight - inset), Math.Max(0, radius.BottomLeft - inset));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
