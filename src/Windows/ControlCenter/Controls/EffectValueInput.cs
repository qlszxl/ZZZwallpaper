using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;

namespace LumaWall.ControlCenter;

// All slider values share the same drag/click editor and enabled-state binding.
public sealed class EffectValueInput : UserControl
{
    private Slider? target;
    private long minimumToken,maximumToken,stepToken;
    private string unit="";
    private int precision;
    private bool synchronizing;
    private readonly ScrubNumberInput input;
    private readonly TextBlock unitLabel;
    public Slider? Target {
        get=>target;
        set {
            if(target==value)return;
            if(target!=null) {
                target.ValueChanged-=SliderChanged;
                target.UnregisterPropertyChangedCallback(RangeBase.MinimumProperty,minimumToken);
                target.UnregisterPropertyChangedCallback(RangeBase.MaximumProperty,maximumToken);
                target.UnregisterPropertyChangedCallback(Slider.StepFrequencyProperty,stepToken);
            }
            target=value;
            if(value!=null) {
                SetBinding(IsEnabledProperty,new Binding {Source=value,Path=new PropertyPath("IsEnabled"),Mode=BindingMode.OneWay});value.ValueChanged+=SliderChanged;
                minimumToken=value.RegisterPropertyChangedCallback(RangeBase.MinimumProperty,(_,_)=>RefreshPresentation());
                maximumToken=value.RegisterPropertyChangedCallback(RangeBase.MaximumProperty,(_,_)=>RefreshPresentation());
                stepToken=value.RegisterPropertyChangedCallback(Slider.StepFrequencyProperty,(_,_)=>RefreshPresentation());
                RefreshPresentation();
            }
        }
    }
    public string Unit {get=>unit;set {unit=value;RefreshPresentation();}}
    public int Precision {get=>precision;set {precision=value;RefreshPresentation();}}
    public EffectValueInput()
    {
        IsTabStop=false;HorizontalAlignment=HorizontalAlignment.Stretch;VerticalAlignment=VerticalAlignment.Center;
        input=new ScrubNumberInput(UiLanguage.Text("参数数值"),true);
        unitLabel=new TextBlock {FontFamily=UiFonts.Body,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=(Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlMuted"],VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        var row=new Grid {ColumnSpacing=8};row.ColumnDefinitions.Add(new() {Width=new GridLength(100)});row.ColumnDefinitions.Add(new());row.Children.Add(input);Grid.SetColumn(unitLabel,1);row.Children.Add(unitLabel);Content=row;
        input.ValueChanged+=value=> {
            if(synchronizing||target==null)return;synchronizing=true;
            try {target.Value=Math.Clamp(value,target.Minimum,target.Maximum);}finally {synchronizing=false;RefreshPresentation();}
        };
        IsEnabledChanged+=(_,_)=>unitLabel.Opacity=IsEnabled?1:.5;
    }
    internal void CommitEdit()=>input.CommitEdit();
    private void SliderChanged(object sender,RangeBaseValueChangedEventArgs args)=>RefreshPresentation();
    private void RefreshPresentation()
    {
        unitLabel.Text=unit;if(target==null||synchronizing)return;
        synchronizing=true;
        try {
            input.Configure(target.Minimum,target.Maximum,target.Value,target.StepFrequency,precision,"");
            input.SetLabel(AutomationProperties.GetName(target)+(unit.Length==0?"":"（"+unit+"）"));
        }finally {synchronizing=false;}
    }
}
