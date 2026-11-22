using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<(ContentControl Field,Func<LightFlareOptions,bool> Enabled)> lightFlareFields=new();
    private Button? lightFlareLocateButton;
    private UserControl? lightFlareLocator;
    private bool lightFlareLocating;
    private Action? refreshLightFlareMarker,endLightFlareDrag,showLightFlarePreview;

    private FrameworkElement FlareSlider(string label,double min,double max,double step,string unit,int precision,
        Func<LightFlareOptions,double> get,Func<LightFlareOptions,double,LightFlareOptions> set)=>
        ExtendedSlider(label,min,max,step,unit,precision,e=>get(e.LightFlare),(e,v)=>e with {LightFlare=set(e.LightFlare,v)});

    private void SetLightFlare(LightFlareOptions flare)
    {
        if(loadingEffects)return;
        var draft=effectSession.ExtendedDraft with {LightFlare=flare};
        effectSession.ExtendedDraft=draft;loadingEffects=true;
        try {foreach(var load in extendedLoaders)load(draft);}
        finally {effectSession.ExtendedDraft=draft;loadingEffects=false;}
        RefreshLightFlareFields();DraftChanged();
    }

    private EffectDefinition CreateLightFlareEffect()
    {
        var panel=new StackPanel {Spacing=16};
        var enabled=WidgetToggle(UiLanguage.Text("启用光芒"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
        enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {LightFlare=effectSession.ExtendedDraft.LightFlare with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
        extendedLoaders.Add(e=>enabled.IsOn=e.LightFlare.Enabled);
        void Section(string title,StackPanel content,bool expanded=true) {
            var card=UtilityCard(content,title);SectionChrome.SetIsExpanded(card,expanded);panel.Children.Add(card);
        }
        FrameworkElement Dependent(FrameworkElement field,Func<LightFlareOptions,bool> active) {
            var host=new ContentControl {Content=field,HorizontalContentAlignment=HorizontalAlignment.Stretch,IsTabStop=false};
            lightFlareFields.Add((host,active));return host;
        }
        var basic=new StackPanel {Spacing=16};
        var presets=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch,PlaceholderText=UiLanguage.Text("选择预设")};
        foreach(string text in new[]{UiLanguage.Text("蓝白星芒"),UiLanguage.Text("镜头耀斑"),UiLanguage.Text("柔和亮光")})presets.Items.Add(text);
        presets.SelectionChanged+=(_,_)=> {
            if(loadingEffects||presets.SelectedIndex<0)return;
            var current=effectSession.ExtendedDraft.LightFlare;
            SetLightFlare(LightFlareOptions.Preset(presets.SelectedIndex) with {Enabled=current.Enabled,CenterX=current.CenterX,CenterY=current.CenterY});
            presets.SelectedIndex=-1;
        };
        basic.Children.Add(WidgetField(UiLanguage.Text("外观预设"),presets));
        basic.Children.Add(AdvancedChoice(UiLanguage.Text("叠加模式"),[UiLanguage.Text("滤色"),UiLanguage.Text("线性减淡（添加）"),UiLanguage.Text("正常"),UiLanguage.Text("叠加"),UiLanguage.Text("柔光")],e=>e.LightFlare.BlendMode,(e,v)=>e with {LightFlare=e.LightFlare with {BlendMode=v}}));
        basic.Children.Add(FlareSlider(UiLanguage.Text("中心横坐标"),0,100,.1,"%",1,f=>f.CenterX*100,(f,v)=>f with {CenterX=v/100}));
        basic.Children.Add(FlareSlider(UiLanguage.Text("中心纵坐标"),0,100,.1,"%",1,f=>f.CenterY*100,(f,v)=>f with {CenterY=v/100}));
        lightFlareLocateButton=WidgetButton(UiLanguage.Text("在预览中定位"),()=> {
            if(lightFlareLocating)StopLightFlareLocator();
            else {if(!effectExplanationOpen)SetEffectExplanation(true);lightFlareLocating=true;showLightFlarePreview?.Invoke();RefreshLightFlareLocator();EffectsExplanationHost.StartBringIntoView();}
        });
        lightFlareLocateButton.HorizontalAlignment=HorizontalAlignment.Left;basic.Children.Add(lightFlareLocateButton);
        basic.Children.Add(FlareSlider(UiLanguage.Text("整体大小"),1,80,.1,"%",1,f=>f.Size*100,(f,v)=>f with {Size=v/100}));
        basic.Children.Add(FlareSlider(UiLanguage.Text("发光强度"),0,200,1,"%",0,f=>f.Brightness*100,(f,v)=>f with {Brightness=v/100}));
        basic.Children.Add(FlareSlider(UiLanguage.Text("混合比例"),0,100,1,"%",0,f=>f.Strength*100,(f,v)=>f with {Strength=v/100}));
        basic.Children.Add(AdvancedColor(UiLanguage.Text("主色"),e=>e.LightFlare.Color,(e,v)=>e with {LightFlare=e.LightFlare with {Color=v}}));
        basic.Children.Add(AdvancedToggle(UiLanguage.Text("光晕跟随主色"),e=>e.LightFlare.LinkColors,(e,v)=>e with {LightFlare=e.LightFlare with {LinkColors=v}}));
        basic.Children.Add(Dependent(AdvancedColor(UiLanguage.Text("光晕颜色"),e=>e.LightFlare.HaloColor,(e,v)=>e with {LightFlare=e.LightFlare with {HaloColor=v}}),f=>!f.LinkColors));
        Section(UiLanguage.Text("光芒参数"),basic);

        var halo=new StackPanel {Spacing=16};
        halo.Children.Add(FlareSlider(UiLanguage.Text("光核大小"),1,30,.1,"%",1,f=>f.CoreSize*100,(f,v)=>f with {CoreSize=v/100}));
        halo.Children.Add(FlareSlider(UiLanguage.Text("光晕强度"),0,100,1,"%",0,f=>f.HaloStrength*100,(f,v)=>f with {HaloStrength=v/100}));
        halo.Children.Add(FlareSlider(UiLanguage.Text("光晕范围"),10,150,1,"%",0,f=>f.HaloRadius*100,(f,v)=>f with {HaloRadius=v/100}));
        halo.Children.Add(FlareSlider(UiLanguage.Text("边缘柔和度"),0,100,1,"%",0,f=>f.Softness*100,(f,v)=>f with {Softness=v/100}));
        Section(UiLanguage.Text("光核与光晕"),halo,false);
        var rays=new StackPanel {Spacing=16};int[] counts=[0,4,6,8,12];
        rays.Children.Add(AdvancedChoice(UiLanguage.Text("光刺数量"),[UiLanguage.Text("关闭"),UiLanguage.Text("4 根"),UiLanguage.Text("6 根"),UiLanguage.Text("8 根"),UiLanguage.Text("12 根")],e=>Array.IndexOf(counts,e.LightFlare.RayCount),(e,v)=>e with {LightFlare=e.LightFlare with {RayCount=counts[v]}}));
        rays.Children.Add(Dependent(FlareSlider(UiLanguage.Text("光刺长度"),10,200,1,"%",0,f=>f.RayLength*100,(f,v)=>f with {RayLength=v/100}),f=>f.RayCount>0));
        rays.Children.Add(Dependent(FlareSlider(UiLanguage.Text("光刺宽度"),.5,20,.1,"%",1,f=>f.RayWidth*100,(f,v)=>f with {RayWidth=v/100}),f=>f.RayCount>0));
        rays.Children.Add(FlareSlider(UiLanguage.Text("旋转角度"),-180,180,1,"°",0,f=>f.Rotation,(f,v)=>f with {Rotation=v}));
        rays.Children.Add(Dependent(FlareSlider(UiLanguage.Text("长短交错"),0,100,1,"%",0,f=>f.Alternate*100,(f,v)=>f with {Alternate=v/100}),f=>f.RayCount>0));
        Section(UiLanguage.Text("光刺"),rays,false);
        var ghosts=new StackPanel {Spacing=16};
        ghosts.Children.Add(AdvancedToggle(UiLanguage.Text("启用副光斑"),e=>e.LightFlare.GhostsEnabled,(e,v)=>e with {LightFlare=e.LightFlare with {GhostsEnabled=v}}));
        ghosts.Children.Add(Dependent(FlareSlider(UiLanguage.Text("副光斑数量"),1,4,1,UiLanguage.Text("个"),0,f=>f.GhostCount,(f,v)=>f with {GhostCount=(int)v}),f=>f.GhostsEnabled));
        ghosts.Children.Add(Dependent(FlareSlider(UiLanguage.Text("副光斑间距"),10,150,1,"%",0,f=>f.GhostSpacing*100,(f,v)=>f with {GhostSpacing=v/100}),f=>f.GhostsEnabled));
        ghosts.Children.Add(Dependent(FlareSlider(UiLanguage.Text("副光斑大小"),3,50,1,"%",0,f=>f.GhostSize*100,(f,v)=>f with {GhostSize=v/100}),f=>f.GhostsEnabled));
        ghosts.Children.Add(Dependent(FlareSlider(UiLanguage.Text("副光斑强度"),0,100,1,"%",0,f=>f.GhostStrength*100,(f,v)=>f with {GhostStrength=v/100}),f=>f.GhostsEnabled));
        ghosts.Children.Add(Dependent(FlareSlider(UiLanguage.Text("副光斑色散"),0,100,1,"%",0,f=>f.Dispersion*100,(f,v)=>f with {Dispersion=v/100}),f=>f.GhostsEnabled));
        Section(UiLanguage.Text("镜头副光斑"),ghosts,false);
        var motion=new StackPanel {Spacing=16};
        motion.Children.Add(AdvancedToggle(UiLanguage.Text("启用动态"),e=>e.LightFlare.Animation,(e,v)=>e with {LightFlare=e.LightFlare with {Animation=v}}));
        motion.Children.Add(Dependent(FlareSlider(UiLanguage.Text("呼吸幅度"),0,100,1,"%",0,f=>f.PulseAmount*100,(f,v)=>f with {PulseAmount=v/100}),f=>f.Animation));
        motion.Children.Add(Dependent(FlareSlider(UiLanguage.Text("呼吸周期"),1,20,.1,"s",1,f=>f.PulsePeriod,(f,v)=>f with {PulsePeriod=v}),f=>f.Animation));
        motion.Children.Add(Dependent(FlareSlider(UiLanguage.Text("旋转速度"),-30,30,.1,UiLanguage.Text("°/秒"),1,f=>f.RotationSpeed,(f,v)=>f with {RotationSpeed=v}),f=>f.Animation));
        Section(UiLanguage.Text("动态"),motion,false);
        extendedLoaders.Add(_=>RefreshLightFlareFields());
        EffectsDetailSections.Children.Add(panel);RefreshLightFlareFields();
        return new(EffectKind.LightFlare,panel,enabled);
    }

    private void RefreshLightFlareFields()
    {
        foreach(var (field,enabled) in lightFlareFields)field.IsEnabled=enabled(effectSession.ExtendedDraft.LightFlare);
        refreshLightFlareMarker?.Invoke();
    }
    private void StopLightFlareLocator()
    {
        endLightFlareDrag?.Invoke();lightFlareLocating=false;RefreshLightFlareLocator();
    }
    private void RefreshLightFlareLocator()
    {
        bool visible=lightFlareLocating&&selectedEffectKind==EffectKind.LightFlare&&effectExplanationOpen;
        if(!visible&&lightFlareLocating) {endLightFlareDrag?.Invoke();lightFlareLocating=false;}
        if(lightFlareLocator!=null)lightFlareLocator.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(lightFlareLocateButton!=null)UtilityButtonContent(lightFlareLocateButton,visible?UiLanguage.Text("结束定位"):UiLanguage.Text("在预览中定位"));
        refreshLightFlareMarker?.Invoke();
    }
    private void InitializeLightFlareLocator(Grid frame,Action showAll)
    {
        showLightFlarePreview=showAll;
        var canvas=new Canvas {Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        var locator=lightFlareLocator=new UserControl {Content=canvas,IsTabStop=true,Visibility=Visibility.Collapsed,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        AutomationProperties.SetName(locator,UiLanguage.Text("拖动定位光芒，方向键微调，Esc 结束定位"));
        var marker=new Grid {Width=28,Height=28,IsHitTestVisible=false};
        marker.Children.Add(new Border {BorderBrush=HomeBrush("HomeControlText"),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(14),Background=HomeBrush("HomeControlRecess"),Opacity=.8});
        marker.Children.Add(new Border {Width=18,Height=2,Background=HomeBrush("InteractionAccentBrush")});
        marker.Children.Add(new Border {Width=2,Height=18,Background=HomeBrush("InteractionAccentBrush")});canvas.Children.Add(marker);
        refreshLightFlareMarker=()=> {var f=effectSession.ExtendedDraft.LightFlare;Canvas.SetLeft(marker,f.CenterX*frame.ActualWidth-14);Canvas.SetTop(marker,f.CenterY*frame.ActualHeight-14);};
        uint? pointer=null;Point? pending=null;bool rendering=false;string? dragTarget=null;
        void SetPosition(Point point) {
            var f=effectSession.ExtendedDraft.LightFlare;
            double x=Math.Round(Math.Clamp(point.X/Math.Max(1,frame.ActualWidth),0,1),4),y=Math.Round(Math.Clamp(point.Y/Math.Max(1,frame.ActualHeight),0,1),4);
            if(x==f.CenterX&&y==f.CenterY)return;
            SetLightFlare(f with {CenterX=x,CenterY=y});
        }
        void Paint(object? sender,object args) {if(pending is { } point) {pending=null;if(dragTarget==effectSession.Key&&selectedEffectKind==EffectKind.LightFlare)SetPosition(point);}}
        void EndDrag() {Paint(null,EventArgs.Empty);pointer=null;if(rendering) {CompositionTarget.Rendering-=Paint;rendering=false;}}
        endLightFlareDrag=()=> {EndDrag();locator.ReleasePointerCaptures();};
        locator.PointerPressed+=(_,e)=> {
            if(pointer!=null||!e.GetCurrentPoint(locator).Properties.IsLeftButtonPressed||!locator.CapturePointer(e.Pointer))return;
            pointer=e.Pointer.PointerId;dragTarget=effectSession.Key;locator.Focus(FocusState.Pointer);SetPosition(e.GetCurrentPoint(frame).Position);
            CompositionTarget.Rendering+=Paint;rendering=true;e.Handled=true;
        };
        locator.PointerMoved+=(_,e)=> {if(pointer==e.Pointer.PointerId) {if(!e.GetCurrentPoint(locator).Properties.IsLeftButtonPressed) {EndDrag();locator.ReleasePointerCapture(e.Pointer);}else pending=e.GetCurrentPoint(frame).Position;e.Handled=true;}};
        locator.PointerReleased+=(_,e)=> {if(pointer==e.Pointer.PointerId) {pending=e.GetCurrentPoint(frame).Position;EndDrag();locator.ReleasePointerCapture(e.Pointer);e.Handled=true;}};
        locator.PointerCanceled+=(_,_)=>EndDrag();locator.PointerCaptureLost+=(_,_)=>EndDrag();
        locator.Unloaded+=(_,_)=> {EndDrag();locator.ReleasePointerCaptures();};Closed+=(_,_)=>EndDrag();
        locator.KeyDown+=(_,e)=> {
            if(e.Key==VirtualKey.Escape) {StopLightFlareLocator();e.Handled=true;return;}
            var f=effectSession.ExtendedDraft.LightFlare;double dx=e.Key==VirtualKey.Left?-.001:e.Key==VirtualKey.Right?.001:0,dy=e.Key==VirtualKey.Up?-.001:e.Key==VirtualKey.Down?.001:0;
            if(dx!=0||dy!=0) {SetLightFlare(f with {CenterX=Math.Clamp(f.CenterX+dx,0,1),CenterY=Math.Clamp(f.CenterY+dy,0,1)});e.Handled=true;}
        };
        frame.SizeChanged+=(_,_)=>refreshLightFlareMarker?.Invoke();frame.Children.Add(locator);
    }
}
