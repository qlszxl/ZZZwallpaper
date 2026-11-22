using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

// Shared ComboBox styles register once on load; no visual-tree scans or layout writes.
public sealed class DiagnosticInput : DependencyObject
{
    internal static Action<ComboBox>? Register;
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(DiagnosticInput),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject item)=>(bool)item.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject item,bool value)=>item.SetValue(EnabledProperty,value);
    public static readonly DependencyProperty TargetProperty=DependencyProperty.RegisterAttached("Target",typeof(string),typeof(DiagnosticInput),new PropertyMetadata(""));
    public static string GetTarget(DependencyObject item)=>(string)item.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject item,string value)=>item.SetValue(TargetProperty,value);
    private static void Changed(DependencyObject item,DependencyPropertyChangedEventArgs args)
    {
        if(item is not ComboBox combo)return;
        combo.Loaded-=Loaded;
        if(args.NewValue is true) {combo.Loaded+=Loaded;if(combo.IsLoaded)Loaded(combo,null!);}
    }
    private static void Loaded(object sender,RoutedEventArgs args) {try {if(sender is ComboBox combo)Register?.Invoke(combo);}catch { }}
}
