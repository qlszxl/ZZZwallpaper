using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LumaWall.ControlCenter;

// A separate enabled ancestor preserves the field's own conditional IsEnabled
// value. Disclosure headers and their help buttons never enter this gate.
internal static class ParameterInputGate
{
    private sealed class Gate : ContentControl { }
    private static readonly ConditionalWeakTable<FrameworkElement,Gate> gates=new();
    private static readonly ConditionalWeakTable<FrameworkElement,Scope> scopes=new();
    private sealed class Scope {public bool Enabled=true;}
    private static readonly DependencyProperty ExemptProperty=DependencyProperty.RegisterAttached("Exempt",typeof(bool),typeof(ParameterInputGate),new PropertyMetadata(false));
    private static readonly DependencyProperty SurfaceProperty=DependencyProperty.RegisterAttached("Surface",typeof(bool),typeof(ParameterInputGate),new PropertyMetadata(false));
    internal static void SetExempt(DependencyObject element,bool value)=>element.SetValue(ExemptProperty,value);
    internal static void SetSurface(DependencyObject element)=>element.SetValue(SurfaceProperty,true);
    internal static FrameworkElement Host(FrameworkElement element)=>gates.TryGetValue(element,out var gate)?gate:element;
    internal static FrameworkElement Unwrap(FrameworkElement element)=>element is Gate {Content:FrameworkElement child}?child:element;
    internal static void Apply(FrameworkElement root,bool enabled,bool refresh=true)
    {
        if(!scopes.TryGetValue(root,out var scope)) {
            scope=new();scopes.Add(root,scope);
            root.Loaded+=(_,_)=>root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>Walk(root,EffectiveEnabled(root)));
        }
        bool changed=scope.Enabled!=enabled;scope.Enabled=enabled;
        if(changed||refresh)Walk(root,EffectiveEnabled(root));
    }
    private static bool EffectiveEnabled(FrameworkElement root)
    {
        for(DependencyObject? node=root;node!=null;node=node is FrameworkElement {Parent:{ } parent}?parent:Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if(node is FrameworkElement element&&scopes.TryGetValue(element,out var scope)&&!scope.Enabled)return false;
        return true;
    }
    private static bool IsInput(FrameworkElement node)=>(bool)node.GetValue(SurfaceProperty)||node is ButtonBase or Slider or ToggleSwitch or ComboBox or RadioButtons or TextBox or NumberBox or DatePicker or TimePicker or ColorPicker
        or EffectValueInput or ScrubNumberInput or DiscColorPicker or DiscHueSelector or SpectrumWeightEditor;
    private static void Walk(FrameworkElement node,bool enabled)
    {
        if(scopes.TryGetValue(node,out var scope))enabled&=scope.Enabled;
        if(node is Gate gate) {gate.IsEnabled=enabled;return;}
        if((bool)node.GetValue(ExemptProperty))return;
        if(node is Button button&&Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button)=="ParameterSectionToggle")return;
        if(IsInput(node)) {SetEnabled(node,enabled);return;}
        IEnumerable<FrameworkElement> children=node switch {
            Panel panel=>panel.Children.OfType<FrameworkElement>().ToArray(),
            Border {Child:FrameworkElement child}=>[child],
            ContentControl control=>new[] {SectionChrome.GetHeader(control) as FrameworkElement,control is Expander expander?expander.Header as FrameworkElement:null,control.Content as FrameworkElement}.OfType<FrameworkElement>(),
            UserControl {Content:FrameworkElement child}=>[child],
            _=>[]
        };
        foreach(var child in children)Walk(child,enabled);
    }
    internal static void SetEnabled(FrameworkElement field,bool enabled)
    {
        if(gates.TryGetValue(field,out var existing)) {existing.IsEnabled=enabled;return;}
        if(enabled)return;
        if(field.Parent is not Panel parent)return;
        int index=parent.Children.IndexOf(field);if(index<0)return;
        var gate=new Gate {IsTabStop=false,IsEnabled=enabled,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,
            HorizontalAlignment=field.HorizontalAlignment,VerticalAlignment=field.VerticalAlignment,Margin=field.Margin};
        Grid.SetRow(gate,Grid.GetRow(field));Grid.SetColumn(gate,Grid.GetColumn(field));Grid.SetRowSpan(gate,Grid.GetRowSpan(field));Grid.SetColumnSpan(gate,Grid.GetColumnSpan(field));
        parent.Children.RemoveAt(index);field.Margin=new Thickness(0);Grid.SetRow(field,0);Grid.SetColumn(field,0);Grid.SetRowSpan(field,1);Grid.SetColumnSpan(field,1);
        gate.Content=field;gates.Add(field,gate);parent.Children.Insert(index,gate);
    }
}
