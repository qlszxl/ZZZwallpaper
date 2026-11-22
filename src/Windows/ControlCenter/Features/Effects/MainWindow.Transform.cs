using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private EffectDefinition CreateTransformEffect()
    {
        var panel=new StackPanel {Spacing=16};
        var enabled=WidgetToggle(UiLanguage.Text("启用变换"),false);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
        enabled.Toggled+=(_,_)=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Transform=effectSession.ExtendedDraft.Transform with {Enabled=enabled.IsOn}};if(!loadingEffects)DraftChanged();};
        extendedLoaders.Add(e=>enabled.IsOn=e.Transform.Enabled);
        FrameworkElement Number(string title,double min,double max,double step,string unit,int precision,Func<TransformOptions,double> get,Func<TransformOptions,double,TransformOptions> set)=>
            ExtendedSlider(title,min,max,step,unit,precision,e=>get(e.Transform),(e,v)=>e with {Transform=set(e.Transform,v)});
        void Section(string title,params UIElement[] children) {var body=new StackPanel {Spacing=16};foreach(var child in children)body.Children.Add(child);panel.Children.Add(UtilityCard(body,title));}
        var reset=WidgetButton(UiLanguage.Text("重置变换"),()=> {
            if(loadingEffects)return;
            var draft=effectSession.ExtendedDraft with {Transform=new() {Enabled=effectSession.ExtendedDraft.Transform.Enabled}};
            effectSession.ExtendedDraft=draft;loadingEffects=true;
            try {foreach(var load in extendedLoaders)load(draft);}finally {effectSession.ExtendedDraft=draft;loadingEffects=false;}
            DraftChanged();
        });reset.HorizontalAlignment=HorizontalAlignment.Left;
        Section(UiLanguage.Text("缩放与位置"),
            Number(UiLanguage.Text("整体缩放"),10,400,.1,"%",1,t=>t.Scale*100,(t,v)=>t with {Scale=v/100}),
            Number(UiLanguage.Text("横向缩放"),25,400,.1,"%",1,t=>t.ScaleX*100,(t,v)=>t with {ScaleX=v/100}),
            Number(UiLanguage.Text("纵向缩放"),25,400,.1,"%",1,t=>t.ScaleY*100,(t,v)=>t with {ScaleY=v/100}),
            Number(UiLanguage.Text("水平位置"),-100,100,.1,"%",1,t=>t.PositionX*100,(t,v)=>t with {PositionX=v/100}),
            Number(UiLanguage.Text("垂直位置"),-100,100,.1,"%",1,t=>t.PositionY*100,(t,v)=>t with {PositionY=v/100}),reset);
        Section(UiLanguage.Text("旋转与倾斜"),
            Number(UiLanguage.Text("旋转角度"),-180,180,.1,"°",1,t=>t.Rotation,(t,v)=>t with {Rotation=v}),
            Number(UiLanguage.Text("水平倾斜"),-60,60,.1,"°",1,t=>t.SkewX,(t,v)=>t with {SkewX=v}),
            Number(UiLanguage.Text("垂直倾斜"),-60,60,.1,"°",1,t=>t.SkewY,(t,v)=>t with {SkewY=v}));
        Section(UiLanguage.Text("中心与边缘"),
            Number(UiLanguage.Text("中心横坐标"),0,100,.1,"%",1,t=>t.CenterX*100,(t,v)=>t with {CenterX=v/100}),
            Number(UiLanguage.Text("中心纵坐标"),0,100,.1,"%",1,t=>t.CenterY*100,(t,v)=>t with {CenterY=v/100}),
            AdvancedChoice(UiLanguage.Text("超出边缘"),[UiLanguage.Text("黑色填充"),UiLanguage.Text("延伸边缘"),UiLanguage.Text("镜像平铺"),UiLanguage.Text("重复平铺")],e=>e.Transform.BorderMode,(e,v)=>e with {Transform=e.Transform with {BorderMode=v}}));
        EffectsDetailSections.Children.Add(panel);
        return new(EffectKind.Transform,panel,enabled);
    }
}
