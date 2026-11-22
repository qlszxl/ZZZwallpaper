using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumaWall.ControlCenter;

// Attach only once after the existing section/layout builders have finished.
public static class ParameterHint
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ParameterHint),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not TextBlock text)return;
        text.Loaded-=Loaded;if((bool)args.NewValue)text.Loaded+=Loaded;
    }
    private static void Loaded(object sender,RoutedEventArgs args)
    {
        var label=(TextBlock)sender;
        label.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>MainWindow.AttachParameterHint(label));
    }
}
