using Microsoft.UI.Xaml;

namespace LumaWall.ControlCenter;

internal sealed class AutomationControlIdentity:DependencyObject
{
    // Stored on the native DependencyObject, so recreating a managed WinRT
    // projection does not generate a new ID for the same live element.
    private static readonly DependencyProperty IdProperty=DependencyProperty.RegisterAttached(
        "AutomationControlId",typeof(string),typeof(AutomationControlIdentity),new PropertyMetadata(null));
    internal static string Get(DependencyObject element)
    {
        if(element.GetValue(IdProperty) is string id)return id;
        id=Guid.NewGuid().ToString("N");element.SetValue(IdProperty,id);return id;
    }
}
