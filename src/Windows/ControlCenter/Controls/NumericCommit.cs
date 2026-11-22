using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace LumaWall.ControlCenter;

internal static class NumericCommit
{
    private sealed class AcceptedValue {internal double Value;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NumberBox,AcceptedValue> accepted=new();
    internal static void Observe(NumberBox box)
    {
        if(accepted.TryGetValue(box,out _))return;
        var state=new AcceptedValue {Value=box.Value};accepted.Add(box,state);
        box.ValueChanged+=(_,e)=> {if(double.IsFinite(e.NewValue))state.Value=e.NewValue;};
    }
    internal static TextBox? TextEditor(DependencyObject root)
    {
        if(root is TextBox text)return text;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(TextEditor(VisualTreeHelper.GetChild(root,i)) is { } found)return found;
        return null;
    }
    // Read the draft before changing focus: NumberBox.Value can still contain the
    // previous value, and hiding its template first would discard the draft.
    internal static void Commit(NumberBox box,double? fallback=null)
    {
        string draft=TextEditor(box)?.Text??box.Text;
        double value=fallback??box.Value;
        if(!double.IsFinite(value)&&accepted.TryGetValue(box,out var previous))value=previous.Value;
        double? parsed=(box.NumberFormatter as INumberParser)?.ParseDouble(draft.Trim());
        if(parsed is double number&&double.IsFinite(number))value=number;
        if(!double.IsFinite(value))value=double.IsFinite(box.Minimum)?box.Minimum:0;
        box.Value=Math.Clamp(value,box.Minimum,box.Maximum);
        box.Text=box.NumberFormatter.FormatDouble(box.Value);
        if(TextEditor(box) is { } editor)editor.Select(editor.Text.Length,0);
    }
}
