using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

// Configuration choices own this value. Focus and pointer events never set it.
// Selected choices fill the button face and use dark text, independently of focus.
public sealed class ChoiceSelection : DependencyObject
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(ChoiceSelection), new PropertyMetadata(false, SelectedChanged));
    private static readonly DependencyProperty PaintProperty = DependencyProperty.RegisterAttached(
        "Paint", typeof(object), typeof(ChoiceSelection), new PropertyMetadata(null));

    public static bool GetIsSelected(DependencyObject target) => (bool)target.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject target, bool value) => target.SetValue(IsSelectedProperty, value);

    private static void SelectedChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if(target is not Control control)return;
        if((bool)args.NewValue) {
            control.SetValue(PaintProperty,new SavedPaint(
                SavedValue.Capture(control,Control.BackgroundProperty),
                SavedValue.Capture(control,Control.ForegroundProperty)));
            control.Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["InteractionAccentBrush"];
            control.Foreground=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlInk"];
        } else if(control.GetValue(PaintProperty) is SavedPaint paint) {
            paint.Background.Restore(control,Control.BackgroundProperty);
            paint.Foreground.Restore(control,Control.ForegroundProperty);
            control.ClearValue(PaintProperty);
        }
    }

    private sealed record SavedPaint(SavedValue Background,SavedValue Foreground);
    private sealed record SavedValue(object Value,Binding? Binding)
    {
        internal static SavedValue Capture(Control control,DependencyProperty property)=>
            new(control.ReadLocalValue(property),control.GetBindingExpression(property)?.ParentBinding);
        internal void Restore(Control control,DependencyProperty property)
        {
            if(Binding!=null)control.SetBinding(property,Binding);
            else if(Value==DependencyProperty.UnsetValue)control.ClearValue(property);
            else control.SetValue(property,Value);
        }
    }
}
