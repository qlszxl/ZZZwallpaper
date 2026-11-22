using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private FrameworkElement AdvancedChoice(string label,string[] items,Func<VideoEffects,int> get,Func<VideoEffects,int,VideoEffects> set,bool stableText=false)
    {
        var picker=new ComboBox {Style=HomeStyle(stableText?"StringChoiceComboBox":"UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var item in items)picker.Items.Add(stableText?new ComboBoxItem {Content=item}:item);picker.SelectedIndex=get(effectSession.ExtendedDraft);
        picker.SelectionChanged+=(_,_)=> {if(loadingEffects||picker.SelectedIndex<0)return;effectSession.ExtendedDraft=set(effectSession.ExtendedDraft,picker.SelectedIndex);RefreshSunShapeFields();DraftChanged();};
        extendedLoaders.Add(e=>picker.SelectedIndex=get(e));return WidgetField(label,picker);
    }
    private FrameworkElement AdvancedToggle(string label,Func<VideoEffects,bool> get,Func<VideoEffects,bool,VideoEffects> set)
    {
        var toggle=WidgetToggle(label,get(effectSession.ExtendedDraft));toggle.Toggled+=(_,_)=> {if(loadingEffects)return;effectSession.ExtendedDraft=set(effectSession.ExtendedDraft,toggle.IsOn);DraftChanged();};
        extendedLoaders.Add(e=>toggle.IsOn=get(e));return WidgetToggleRow(label,toggle);
    }
    private FrameworkElement AdvancedColor(string label,Func<VideoEffects,string> get,Func<VideoEffects,string,VideoEffects> set)
    {
        var button=ColorChoiceButton(label,()=>SkinColor(get(effectSession.ExtendedDraft)),c=> {effectSession.ExtendedDraft=set(effectSession.ExtendedDraft,ColorHex(c));DraftChanged();});
        extendedLoaders.Add(_=>((Action)button.Tag)());return WidgetField(label,button);
    }
    private void InitializeAdvancedEffects()
    {
        EffectsNoiseEditor.Children.Add(UtilityCard(AdvancedToggle(UiLanguage.Text("彩色颗粒"),e=>e.ColoredNoise,(e,v)=>e with {ColoredNoise=v}),UiLanguage.Text("颗粒颜色")));
        var ripple=new StackPanel {Spacing=14};ripple.Children.Add(UtilityText(UiLanguage.Text("点阵颜色"),"UtilityHeading"));
        ripple.Children.Add(AdvancedChoice(UiLanguage.Text("颜色来源"),[UiLanguage.Text("沿用视频颜色"),UiLanguage.Text("自定义颜色"),UiLanguage.Text("全色相随机"),UiLanguage.Text("范围随机")],e=>e.RippleColor.Mode,(e,v)=>e with {RippleColor=e.RippleColor with {Mode=v}}));
        ripple.Children.Add(AdvancedColor(UiLanguage.Text("固定颜色"),e=>e.RippleColor.Color,(e,v)=>e with {RippleColor=e.RippleColor with {Color=v}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("色相起点"),0,360,1,"°",0,e=>e.RippleColor.HueMin,(e,v)=>e with {RippleColor=e.RippleColor with {HueMin=v}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("色相终点"),0,360,1,"°",0,e=>e.RippleColor.HueMax,(e,v)=>e with {RippleColor=e.RippleColor with {HueMax=v}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("饱和度下限"),0,100,1,"%",0,e=>e.RippleColor.SaturationMin*100,(e,v)=>e with {RippleColor=e.RippleColor with {SaturationMin=v/100,SaturationMax=Math.Max(v/100,e.RippleColor.SaturationMax)}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("饱和度上限"),0,100,1,"%",0,e=>e.RippleColor.SaturationMax*100,(e,v)=>e with {RippleColor=e.RippleColor with {SaturationMax=v/100,SaturationMin=Math.Min(v/100,e.RippleColor.SaturationMin)}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("明度下限"),0,100,1,"%",0,e=>e.RippleColor.ValueMin*100,(e,v)=>e with {RippleColor=e.RippleColor with {ValueMin=v/100,ValueMax=Math.Max(v/100,e.RippleColor.ValueMax)}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("明度上限"),0,100,1,"%",0,e=>e.RippleColor.ValueMax*100,(e,v)=>e with {RippleColor=e.RippleColor with {ValueMax=v/100,ValueMin=Math.Min(v/100,e.RippleColor.ValueMin)}}));
        ripple.Children.Add(ExtendedSlider(UiLanguage.Text("随机换色频率"),0,10,.1,UiLanguage.Text("次/秒"),1,e=>e.RippleColor.Rate,(e,v)=>e with {RippleColor=e.RippleColor with {Rate=v}}));
        EffectsRippleEditor.Children.Add(UtilityCard(ripple));
        var blur=new StackPanel {Spacing=14};
        blur.Children.Add(AdvancedChoice(UiLanguage.Text("模糊方式"),[UiLanguage.Text("高斯模糊"),UiLanguage.Text("方框模糊"),UiLanguage.Text("方向模糊"),UiLanguage.Text("径向模糊"),UiLanguage.Text("旋转模糊")],e=>e.BlurMethod,(e,v)=>e with {BlurMethod=v}));
        blur.Children.Add(ExtendedSlider(UiLanguage.Text("采样次数"),5,257,1,UiLanguage.Text("次"),0,e=>e.BlurSamples,(e,v)=>e with {BlurSamples=(int)Math.Round(v)}));
        blur.Children.Add(ExtendedSlider(UiLanguage.Text("方向角度"),-180,180,1,"°",0,e=>e.BlurDirection,(e,v)=>e with {BlurDirection=v}));
        blur.Children.Add(ExtendedSlider(UiLanguage.Text("中心横坐标"),0,100,1,"%",0,e=>e.BlurCenterX*100,(e,v)=>e with {BlurCenterX=v/100}));
        blur.Children.Add(ExtendedSlider(UiLanguage.Text("中心纵坐标"),0,100,1,"%",0,e=>e.BlurCenterY*100,(e,v)=>e with {BlurCenterY=v/100}));
        EffectsBlurEditor.Children.Insert(0,UtilityCard(blur,UiLanguage.Text("模糊方式与采样")));
        var rain=new StackPanel {Spacing=14};rain.Children.Add(AdvancedToggle(UiLanguage.Text("显示扩散圆环"),e=>e.RainRipples.Outline,(e,v)=>e with {RainRipples=e.RainRipples with {Outline=v}}));
        rain.Children.Add(AdvancedColor(UiLanguage.Text("圆环颜色"),e=>e.RainRipples.Color,(e,v)=>e with {RainRipples=e.RainRipples with {Color=v}}));
        rain.Children.Add(ExtendedSlider(UiLanguage.Text("圆环不透明度"),0,100,1,"%",0,e=>e.RainRipples.OutlineStrength*100,(e,v)=>e with {RainRipples=e.RainRipples with {OutlineStrength=v/100}}));
        rain.Children.Add(ExtendedSlider(UiLanguage.Text("圆环线宽"),.5,10,.1,"px",1,e=>e.RainRipples.OutlineWidth,(e,v)=>e with {RainRipples=e.RainRipples with {OutlineWidth=v}}));
        particleRainPanel!.Children.Add(UtilityCard(rain,UiLanguage.Text("雨滴圆环")));
        var sun=new StackPanel {Spacing=14};sun.Children.Add(AdvancedChoice(UiLanguage.Text("光晕形状"),[UiLanguage.Text("光带"),UiLanguage.Text("圆环"),UiLanguage.Text("弧光"),UiLanguage.Text("光斑"),UiLanguage.Text("星形"),UiLanguage.Text("锥形光束")],e=>e.SunPrism.Shape,(e,v)=>e with {SunPrism=e.SunPrism with {Shape=v}}));
        sun.Children.Add(ExtendedSlider(UiLanguage.Text("弯曲程度"),-2,2,.05,"",2,e=>e.SunPrism.Curvature,(e,v)=>e with {SunPrism=e.SunPrism with {Curvature=v}}));
        sun.Children.Add(SunShapeField(false,ExtendedSlider(UiLanguage.Text("弧光角度"),1,360,1,"°",0,e=>e.SunPrism.Arc,(e,v)=>e with {SunPrism=e.SunPrism with {Arc=v}})));
        sun.Children.Add(SunShapeField(false,ExtendedSlider(UiLanguage.Text("形状半径"),.05,1.5,.01,"",2,e=>e.SunPrism.Radius,(e,v)=>e with {SunPrism=e.SunPrism with {Radius=v}})));
        sun.Children.Add(SunShapeField(true,ExtendedSlider(UiLanguage.Text("锥形张角"),1,170,1,"°",0,e=>e.SunPrism.ConeAngle,(e,v)=>e with {SunPrism=e.SunPrism with {ConeAngle=v}})));
        sun.Children.Add(SunShapeField(true,AdvancedChoice(UiLanguage.Text("照射起点"),[UiLanguage.Text("左上角"),UiLanguage.Text("右上角"),UiLanguage.Text("左下角"),UiLanguage.Text("右下角")],e=>e.SunPrism.ConeCorner,(e,v)=>e with {SunPrism=e.SunPrism with {ConeCorner=v}})));
        sun.Children.Add(SunShapeField(true,ExtendedSlider(UiLanguage.Text("照射方向"),0,90,1,"°",0,e=>e.SunPrism.ConeDirection,(e,v)=>e with {SunPrism=e.SunPrism with {ConeDirection=v}})));
        sun.Children.Add(SunShapeField(true,ExtendedSlider(UiLanguage.Text("照射距离"),10,200,1,"%",0,e=>e.SunPrism.ConeLength*100,(e,v)=>e with {SunPrism=e.SunPrism with {ConeLength=v/100}})));
        sun.Children.Add(SunShapeField(true,ExtendedSlider(UiLanguage.Text("摆动幅度"),0,45,1,"°",0,e=>e.SunPrism.ConeSwing,(e,v)=>e with {SunPrism=e.SunPrism with {ConeSwing=v}})));
        sun.Children.Add(ExtendedSlider(UiLanguage.Text("色带重复次数"),1,12,1,"",0,e=>e.SunPrism.Repeat,(e,v)=>e with {SunPrism=e.SunPrism with {Repeat=v}}));
        sun.Children.Add(CreateSunPaletteEditor());
        var motion=new StackPanel {Spacing=14};
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("中心横坐标"),0,100,1,"%",0,e=>e.SunPrism.CenterX*100,(e,v)=>e with {SunPrism=e.SunPrism with {CenterX=v/100}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("中心纵坐标"),0,100,1,"%",0,e=>e.SunPrism.CenterY*100,(e,v)=>e with {SunPrism=e.SunPrism with {CenterY=v/100}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("水平移动幅度"),0,100,1,"%",0,e=>e.SunPrism.MoveX*100,(e,v)=>e with {SunPrism=e.SunPrism with {MoveX=v/100}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("垂直移动幅度"),0,100,1,"%",0,e=>e.SunPrism.MoveY*100,(e,v)=>e with {SunPrism=e.SunPrism with {MoveY=v/100}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("水平移动速度"),0,2,.01,UiLanguage.Text("周期/秒"),2,e=>e.SunPrism.MoveSpeedX,(e,v)=>e with {SunPrism=e.SunPrism with {MoveSpeedX=v}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("垂直移动速度"),0,2,.01,UiLanguage.Text("周期/秒"),2,e=>e.SunPrism.MoveSpeedY,(e,v)=>e with {SunPrism=e.SunPrism with {MoveSpeedY=v}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("移动相位差"),-180,180,1,"°",0,e=>e.SunPrism.MovePhase,(e,v)=>e with {SunPrism=e.SunPrism with {MovePhase=v}}));
        motion.Children.Add(ExtendedSlider(UiLanguage.Text("旋转速度"),-180,180,1,UiLanguage.Text("°/秒"),0,e=>e.SunPrism.RotationSpeed,(e,v)=>e with {SunPrism=e.SunPrism with {RotationSpeed=v}}));
        
        sun.Children.Add(SunShapeField(false,new Expander {Header=UiLanguage.Text("位置与动态"),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Content=motion}));
        extendedLoaders.Add(_=>RefreshSunShapeFields());RefreshSunShapeFields();
        ((StackPanel)effectDefinitions.First(d=>d.Kind==EffectKind.SunPrism).Panel).Children.Add(UtilityCard(sun,UiLanguage.Text("光晕形状与运动")));
    }
}
