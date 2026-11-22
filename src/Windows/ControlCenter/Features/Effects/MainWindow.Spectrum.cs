using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private static readonly string[] SpectrumShapeNames=[UiLanguage.Text("柱状"),UiLanguage.Text("连续波形"),UiLanguage.Text("面积波形"),UiLanguage.Text("圆环波形"),UiLanguage.Text("径向柱形"),UiLanguage.Text("点阵波形"),UiLanguage.Text("分段柱状")];
    private Action BuildSpectrumLayer(StackPanel host,string id,SpectrumOptions initial,int? index,Func<bool> isActive)
    {
        var sync=new List<Action<SpectrumOptions>>();
        SpectrumOptions Current() {
            var root=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id)?.Spectrum;
            if(root==null||!isActive())return initial;
            return index is { } i?i<root.SubSpectra.Length?root.SubSpectra[i]:initial:root;
        }
        void Change(Func<SpectrumOptions,SpectrumOptions> change) {if(!isActive()||syncingWidgetEditor)return;LiveWidget(id,w=>w with {Spectrum=index is { } i?w.Spectrum with {SubSpectra=w.Spectrum.SubSpectra.Select((s,n)=>n==i?change(s):s).ToArray()}:change(w.Spectrum)});}
        StackPanel Section(string title) {var p=new StackPanel {Spacing=16};p.Children.Add(UtilityText(title,"UtilityHeading"));host.Children.Add(UtilityCard(p));return p;}
        Slider Add(StackPanel p,string title,string unit,double min,double max,double step,int precision,Func<SpectrumOptions,double> read,Func<SpectrumOptions,double,SpectrumOptions> set) {
            var s=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=max,StepFrequency=step,Value=Math.Clamp(read(initial),min,max)};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(s,UiLanguage.Text("频谱")+title);p.Children.Add(NumericSliderRow(title,s,unit,precision));sync.Add(v=>s.Value=Math.Clamp(read(v),s.Minimum,s.Maximum));s.ValueChanged+=(_,_)=>Change(v=>set(v,s.Value));return s;
        }
        ToggleSwitch Toggle(StackPanel p,string title,Func<SpectrumOptions,bool> read,Func<SpectrumOptions,bool,SpectrumOptions> set) {var c=WidgetToggle(title,read(initial));p.Children.Add(WidgetToggleRow(title,c));c.Toggled+=(_,_)=>Change(s=>set(s,c.IsOn));sync.Add(s=>c.IsOn=read(s));return c;}
        Slider? width=null,height=null;
        {
            var dimensions=Section(UiLanguage.Text("尺寸"));var widget=widgets!.State.Widgets.First(w=>w.Id==id);var max=WidgetPlacement.MaximumSize(widget,WidgetPlacement.Resolve(widget.Screen));
            width=Add(dimensions,UiLanguage.Text("宽度"),"DIP",24,max.Width,1,0,s=>s.Width,(s,v)=>s with {Width=(int)v});height=Add(dimensions,UiLanguage.Text("高度"),"DIP",24,max.Height,1,0,s=>s.Height,(s,v)=>s with {Height=(int)v});
        }
        var syncWeighting=BuildSpectrumWeighting(host,id,index,isActive);sync.Add(_=>syncWeighting());
        var form=Section(UiLanguage.Text("形状与外观"));Toggle(form,UiLanguage.Text("显示这一层"),s=>s.Enabled,(s,v)=>s with {Enabled=v});
        var shape=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=SpectrumShapeNames,SelectedIndex=(int)initial.Shape};form.Children.Add(WidgetField(UiLanguage.Text("形状"),shape));shape.SelectionChanged+=(_,_)=> {if(shape.SelectedIndex>=0)Change(s=>s with {Shape=(SpectrumShape)shape.SelectedIndex});};sync.Add(s=>shape.SelectedIndex=(int)s.Shape);
        Add(form,UiLanguage.Text("频段数量"),UiLanguage.Text("段"),1,256,1,0,s=>s.Bands,(s,v)=>s with {Bands=(int)v});
        Add(form,UiLanguage.Text("柱间距"),"%",0,90,1,0,s=>s.Gap*100,(s,v)=>s with {Gap=v/100});Add(form,UiLanguage.Text("柱形圆角"),"DIP",0,40,1,0,s=>s.Radius,(s,v)=>s with {Radius=v});
        var segments=new StackPanel {Spacing=12};form.Children.Add(segments);
        Add(segments,UiLanguage.Text("格子高度"),"DIP",1,40,1,0,s=>s.SegmentHeight,(s,v)=>s with {SegmentHeight=v});
        Add(segments,UiLanguage.Text("格子间距"),"DIP",0,20,1,0,s=>s.SegmentGap,(s,v)=>s with {SegmentGap=v});
        void SegmentVisibility(SpectrumOptions s)=>segments.Visibility=s.Shape==SpectrumShape.SegmentedBars?Visibility.Visible:Visibility.Collapsed;
        shape.SelectionChanged+=(_,_)=>SegmentVisibility(Current());sync.Add(SegmentVisibility);SegmentVisibility(initial);
        Add(form,UiLanguage.Text("波线／圆环线宽"),"DIP",.5,20,.5,1,s=>s.StrokeWidth,(s,v)=>s with {StrokeWidth=v});Add(form,UiLanguage.Text("圆环基础半径"),"%",5,85,1,0,s=>s.RingRadius*100,(s,v)=>s with {RingRadius=v/100});
        Add(form,UiLanguage.Text("静音时最小长度"),"DIP",0,20,1,0,s=>s.MinimumHeight,(s,v)=>s with {MinimumHeight=v});
        
        var direction=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=new[]{UiLanguage.Text("向上"),UiLanguage.Text("向下"),UiLanguage.Text("向右"),UiLanguage.Text("向左")},SelectedIndex=initial.Direction};form.Children.Add(WidgetField(UiLanguage.Text("增长方向"),direction));direction.SelectionChanged+=(_,_)=> {if(direction.SelectedIndex>=0)Change(s=>s with {Direction=direction.SelectedIndex});};sync.Add(s=>direction.SelectedIndex=s.Direction);
        Toggle(form,UiLanguage.Text("双向镜像"),s=>s.Mirror,(s,v)=>s with {Mirror=v});Toggle(form,UiLanguage.Text("反转频段顺序"),s=>s.Reverse,(s,v)=>s with {Reverse=v});Toggle(form,UiLanguage.Text("柱形峰值标记"),s=>s.Peaks,(s,v)=>s with {Peaks=v});
        var colors=Section(UiLanguage.Text("颜色与合成"));Toggle(colors,UiLanguage.Text("渐变颜色"),s=>s.Gradient,(s,v)=>s with {Gradient=v});
        colors.Children.Add(WidgetColorField(UiLanguage.Text("主色"),initial.Color,()=>Current().Color,v=>Change(s=>s with {Color=v})));colors.Children.Add(WidgetColorField(UiLanguage.Text("渐变末端颜色"),initial.ColorEnd,()=>Current().ColorEnd,v=>Change(s=>s with {ColorEnd=v})));
        Add(colors,UiLanguage.Text("不透明度"),"%",0,100,1,0,s=>s.Opacity*100,(s,v)=>s with {Opacity=v/100});Add(colors,UiLanguage.Text("缩放"),"%",10,200,1,0,s=>s.Scale*100,(s,v)=>s with {Scale=v/100});
        Add(colors,UiLanguage.Text("水平偏移"),"%",-100,100,1,0,s=>s.OffsetX*100,(s,v)=>s with {OffsetX=v/100});Add(colors,UiLanguage.Text("垂直偏移"),"%",-100,100,1,0,s=>s.OffsetY*100,(s,v)=>s with {OffsetY=v/100});Add(colors,UiLanguage.Text("旋转"),"°",0,360,1,0,s=>s.Rotation,(s,v)=>s with {Rotation=v});
        var glow=Section(UiLanguage.Text("辉光"));var glowSwitch=Toggle(glow,UiLanguage.Text("启用辉光"),s=>s.Glow,(s,v)=>s with {Glow=v});
        var glowStrength=Add(glow,UiLanguage.Text("辉光强度"),"%",0,100,1,0,s=>s.GlowIntensity*100,(s,v)=>s with {GlowIntensity=v/100});
        var glowRange=Add(glow,UiLanguage.Text("辉光范围"),"DIP",0,80,1,0,s=>s.GlowRadius,(s,v)=>s with {GlowRadius=v});
        var follow=Toggle(glow,UiLanguage.Text("跟随主色"),s=>s.GlowFollowColor,(s,v)=>s with {GlowFollowColor=v});
        var glowColor=new ContentControl {Content=WidgetColorField(UiLanguage.Text("辉光颜色"),initial.GlowColor,()=>Current().GlowColor,v=>Change(s=>s with {GlowColor=v})),HorizontalContentAlignment=HorizontalAlignment.Stretch,IsTabStop=false};glow.Children.Add(glowColor);
        void GlowEnabled(SpectrumOptions s) {glowStrength.IsEnabled=glowRange.IsEnabled=follow.IsEnabled=s.Glow;glowColor.IsEnabled=s.Glow&&!s.GlowFollowColor;}
        glowSwitch.Toggled+=(_,_)=>GlowEnabled(Current());follow.Toggled+=(_,_)=>GlowEnabled(Current());sync.Add(GlowEnabled);GlowEnabled(initial);
        var response=Section(UiLanguage.Text("声音响应"));Add(response,UiLanguage.Text("灵敏度"),"×",.25,4,.05,2,s=>s.Sensitivity,(s,v)=>s with {Sensitivity=v});Add(response,UiLanguage.Text("平滑回落"),UiLanguage.Text("毫秒"),50,800,10,0,s=>s.SmoothingMs,(s,v)=>s with {SmoothingMs=(int)v});
        
        return ()=> {var widget=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(!isActive()||widget==null||index is { } n&&n>=widget.Spectrum.SubSpectra.Length)return;
            if(width!=null&&height!=null) {var bounds=WidgetPlacement.MaximumSize(widget,WidgetPlacement.Resolve(widgets!.State.LayoutGroups.FirstOrDefault(g=>g.Id==widget.LayoutGroupId)?.Screen??widget.Screen));width.Maximum=bounds.Width;height.Maximum=bounds.Height;}
            foreach(var action in sync)action(Current());};
    }
}
