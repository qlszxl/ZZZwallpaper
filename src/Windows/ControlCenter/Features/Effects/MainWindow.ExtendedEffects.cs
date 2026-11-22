using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<Action<VideoEffects>> extendedLoaders=new();
    private void SyncExtendedEffects(VideoEffects value) {effectSession.ExtendedDraft=value;foreach(var load in extendedLoaders)load(value);effectSession.ExtendedDraft=value;sunDragging=false;RefreshSunPalette();RefreshMaskLibrary();}
    private FrameworkElement ExtendedSlider(string title,double min,double max,double step,string unit,int precision,Func<VideoEffects,double> get,Func<VideoEffects,double,VideoEffects> set)
    {
        var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=max,StepFrequency=step,Value=get(effectSession.ExtendedDraft)};
        AutomationProperties.SetName(slider,title);
        slider.ValueChanged+=(_,_)=> {
            if(loadingEffects)return;effectSession.ExtendedDraft=set(effectSession.ExtendedDraft,slider.Value);
            // Paired color limits can update each other; keep their displayed values in sync.
            loadingEffects=true;try {foreach(var load in extendedLoaders)load(effectSession.ExtendedDraft);}finally {loadingEffects=false;}
            DraftChanged();
        };
        extendedLoaders.Add(value=>slider.Value=get(value));
        return NumericSliderRow(title,slider,unit,precision);
    }
    private EffectDefinition[] CreateExtendedEffects()
    {
        var definitions=new List<EffectDefinition>();
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用缩放穿梭"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {ZoomTunnel=effectSession.ExtendedDraft.ZoomTunnel with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.ZoomTunnel.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("混合比例"),0,100,1,"%",0,e=>e.ZoomTunnel.Strength*100,(e,v)=>e with {ZoomTunnel=e.ZoomTunnel with {Strength=v/100}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("循环周期"),1,20,0.1,"s",2,e=>e.ZoomTunnel.Period,(e,v)=>e with {ZoomTunnel=e.ZoomTunnel with {Period=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("最大放大倍率"),1.1,8,0.1,UiLanguage.Text("倍"),2,e=>e.ZoomTunnel.Zoom,(e,v)=>e with {ZoomTunnel=e.ZoomTunnel with {Zoom=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("中心横坐标"),0,1,0.01,"",2,e=>e.ZoomTunnel.CenterX,(e,v)=>e with {ZoomTunnel=e.ZoomTunnel with {CenterX=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("中心纵坐标"),0,1,0.01,"",2,e=>e.ZoomTunnel.CenterY,(e,v)=>e with {ZoomTunnel=e.ZoomTunnel with {CenterY=v}}));
            panel.Children.Add(UtilityCard(controls,UiLanguage.Text("穿梭参数")));EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.ZoomTunnel,panel,enabled));
        }
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用粒子"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Particles=effectSession.ExtendedDraft.Particles with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.Particles.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("折射强度"),0,100,1,"%",0,e=>e.RainRipples.Strength*100,(e,v)=>e with {RainRipples=e.RainRipples with {Strength=v/100}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("雨滴密度"),2,10,0.1,"",2,e=>e.RainRipples.Density,(e,v)=>e with {RainRipples=e.RainRipples with {Density=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("扩散速度"),0,3,0.05,"",2,e=>e.RainRipples.Speed,(e,v)=>e with {RainRipples=e.RainRipples with {Speed=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("扩散半径"),0.2,1,0.01,"",2,e=>e.RainRipples.Radius,(e,v)=>e with {RainRipples=e.RainRipples with {Radius=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("波环宽度"),0.04,0.3,0.01,"",2,e=>e.RainRipples.Width,(e,v)=>e with {RainRipples=e.RainRipples with {Width=v}}));
            var rain=new StackPanel {Spacing=16};rain.Children.Add(UtilityCard(controls,UiLanguage.Text("雨滴参数")));panel.Children.Add(rain);InitializeParticleControls(panel,enabled,rain);EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.Particles,panel,enabled));
        }
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用水波扭曲"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {WaterWave=effectSession.ExtendedDraft.WaterWave with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.WaterWave.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("折射强度"),0,100,1,"%",0,e=>e.WaterWave.Strength*100,(e,v)=>e with {WaterWave=e.WaterWave with {Strength=v/100}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("波动振幅"),0,40,1,"px",0,e=>e.WaterWave.Amplitude,(e,v)=>e with {WaterWave=e.WaterWave with {Amplitude=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("波纹频率"),1,30,0.1,"",2,e=>e.WaterWave.Frequency,(e,v)=>e with {WaterWave=e.WaterWave with {Frequency=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("流动速度"),0,3,0.05,"",2,e=>e.WaterWave.Speed,(e,v)=>e with {WaterWave=e.WaterWave with {Speed=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("流动方向"),-180,180,1,"°",0,e=>e.WaterWave.Angle,(e,v)=>e with {WaterWave=e.WaterWave with {Angle=v}}));
            panel.Children.Add(UtilityCard(controls,UiLanguage.Text("水波参数")));EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.WaterWave,panel,enabled));
        }
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用边缘描边"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {EdgeOutline=effectSession.ExtendedDraft.EdgeOutline with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.EdgeOutline.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("混合比例"),0,100,1,"%",0,e=>e.EdgeOutline.Strength*100,(e,v)=>e with {EdgeOutline=e.EdgeOutline with {Strength=v/100}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("描边宽度"),0.5,4,0.1,"px",2,e=>e.EdgeOutline.Width,(e,v)=>e with {EdgeOutline=e.EdgeOutline with {Width=v}}));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("检测阈值"),0,1,0.01,"",2,e=>e.EdgeOutline.Threshold,(e,v)=>e with {EdgeOutline=e.EdgeOutline with {Threshold=v}}));
            controls.Children.Add(AdvancedChoice(UiLanguage.Text("描边模式"),[UiLanguage.Text("叠加画面"),UiLanguage.Text("仅轮廓 · 黑色背景")],e=>e.EdgeOutline.Mode==2?1:0,
                (e,v)=>e with {EdgeOutline=e.EdgeOutline with {Mode=v==1?2:0,Color=e.EdgeOutline.ResolvedColor}}));
            controls.Children.Add(AdvancedColor(UiLanguage.Text("描边颜色"),e=>e.EdgeOutline.ResolvedColor,(e,v)=>e with {EdgeOutline=e.EdgeOutline with {Color=v}}));
            panel.Children.Add(UtilityCard(controls,UiLanguage.Text("描边参数")));EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.EdgeOutline,panel,enabled));
        }
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用马赛克"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Pixelate=effectSession.ExtendedDraft.Pixelate with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.Pixelate.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("色块尺寸"),2,128,1,"px",0,e=>e.Pixelate.BlockSize,(e,v)=>e with {Pixelate=e.Pixelate with {BlockSize=v}}));
            panel.Children.Add(UtilityCard(controls,UiLanguage.Text("马赛克参数")));EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.Pixelate,panel,enabled));
        }
        {
            var panel=new StackPanel {Spacing=16};var controls=new StackPanel {Spacing=16};
            var enabled=WidgetToggle(UiLanguage.Text("启用阳光色散"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
            enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {SunPrism=effectSession.ExtendedDraft.SunPrism with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
            extendedLoaders.Add(value=>enabled.IsOn=value.SunPrism.Enabled);
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("混合比例"),0,100,1,"%",0,e=>e.SunPrism.Strength*100,(e,v)=>e with {SunPrism=e.SunPrism with {Strength=v/100}}));
            controls.Children.Add(SunShapeField(false,ExtendedSlider(UiLanguage.Text("形状角度"),-180,180,1,"°",0,e=>e.SunPrism.Angle,(e,v)=>e with {SunPrism=e.SunPrism with {Angle=v}})));
            controls.Children.Add(SunShapeField(false,ExtendedSlider(UiLanguage.Text("光带宽度"),0.05,1,0.01,"",2,e=>e.SunPrism.Width,(e,v)=>e with {SunPrism=e.SunPrism with {Width=v}})));
            controls.Children.Add(SunShapeField(false,ExtendedSlider(UiLanguage.Text("局部纵向偏移"),-1,1,0.01,"",2,e=>e.SunPrism.Position,(e,v)=>e with {SunPrism=e.SunPrism with {Position=v}})));
            controls.Children.Add(ExtendedSlider(UiLanguage.Text("动态速度"),0,1,0.01,"",2,e=>e.SunPrism.Speed,(e,v)=>e with {SunPrism=e.SunPrism with {Speed=v}}));
            panel.Children.Add(UtilityCard(controls,UiLanguage.Text("光晕参数")));EffectsDetailSections.Children.Add(panel);definitions.Add(new(EffectKind.SunPrism,panel,enabled));
        }

        definitions.Add(CreateLightFlareEffect());
        definitions.Add(CreateTransformEffect());
        return definitions.ToArray();
    }
}
