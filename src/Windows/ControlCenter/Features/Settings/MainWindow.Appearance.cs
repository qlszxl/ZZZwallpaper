using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    public AppearanceOptions Appearance {get;private set;}=new();
    public event Action<AppearanceOptions>? AppearanceChanged;
    private readonly Dictionary<string,Button> skinButtons=new();
    private ToggleSwitch? appearancePattern,appearanceMotion;
    private bool syncingAppearance;
    private static Color SkinColor(string hex)=>Color.FromArgb(255,Convert.ToByte(hex[1..3],16),Convert.ToByte(hex[3..5],16),Convert.ToByte(hex[5..7],16));
    private static Color BlendSkin(Color color,Color other,double amount)=>Color.FromArgb(255,(byte)(color.R*(1-amount)+other.R*amount),(byte)(color.G*(1-amount)+other.G*amount),(byte)(color.B*(1-amount)+other.B*amount));
    private void InitializeAppearance()
    {
        Appearance=AppearanceOptions.FromJson(modules.Settings.Get("appearance.v1"));
        var heading=new StackPanel {Spacing=6};heading.Children.Add(UtilityText(UiLanguage.Text("软件皮肤"),"UtilityHeading"));AppearancePanel.Children.Add(heading);
        var choices=new Grid {ColumnSpacing=16,RowSpacing=16};
        foreach(var skin in SkinPalette.All) {
            var button=WidgetButton(UiLanguage.Text(skin.Name),()=>SaveAppearance(Appearance with {Skin=skin.Id}));button.MinHeight=150;button.Padding=new Thickness(18);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var content=new StackPanel {Spacing=14};var preview=new Border {Height=62,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(SkinColor(skin.Background)),Padding=new Thickness(12)};
            var bars=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
            foreach(string color in new[]{skin.Accent,skin.AccentEnd,skin.Surface})bars.Children.Add(new Border {Width=42,Height=28,CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(SkinColor(color))});
            preview.Child=bars;content.Children.Add(preview);var title=UtilityText(UiLanguage.Text(skin.Name),"UtilityHeading");title.SetBinding(TextBlock.ForegroundProperty,new Microsoft.UI.Xaml.Data.Binding {Source=button,Path=new PropertyPath("Foreground")});content.Children.Add(title);button.Content=content;choices.Children.Add(button);skinButtons.Add(skin.Id,button);
        }
        UtilityAdaptiveGrid(choices,2,600);AppearancePanel.Children.Add(choices);
        var options=new StackPanel {Spacing=12};appearancePattern=WidgetToggle(UiLanguage.Text("品牌底纹"),Appearance.Pattern);appearanceMotion=WidgetToggle(UiLanguage.Text("界面动效"),Appearance.Motion);
        options.Children.Add(WidgetToggleRow(UiLanguage.Text("品牌底纹"),appearancePattern));options.Children.Add(WidgetToggleRow(UiLanguage.Text("界面动效"),appearanceMotion));AppearancePanel.Children.Add(UtilityCard(options));
        appearancePattern.Toggled+=(_,_)=> {if(!syncingAppearance)SaveAppearance(Appearance with {Pattern=appearancePattern.IsOn});};
        appearanceMotion.Toggled+=(_,_)=> {if(!syncingAppearance)SaveAppearance(Appearance with {Motion=appearanceMotion.IsOn});};
        var reset=WidgetButton(UiLanguage.Text("恢复默认外观"),()=>SaveAppearance(new()),"Restart");reset.HorizontalAlignment=HorizontalAlignment.Left;AppearancePanel.Children.Add(reset);
        InitializeEffectPreviewSettings();
        ApplyAppearancePalette();
    }
    private void SaveAppearance(AppearanceOptions value)
    {
        try {value=value.Normalize();modules.Settings.Set("appearance.v1",value.ToJson());Appearance=value;ApplyAppearancePalette();AppearanceChanged?.Invoke(value);}
        catch(Exception ex) {ShowError(UiLanguage.Text("外观保存失败：")+ex.Message,ex);}
    }
    private void ApplyAppearancePalette()
    {
        var skin=SkinPalette.Find(Appearance.Skin);
        InteractionAccent.Configure(SkinColor(skin.Accent),SkinColor(skin.AccentEnd));
        var white=Color.FromArgb(255,255,255,255);var black=Color.FromArgb(255,0,0,0);var surface=SkinColor(skin.Surface);
        void Brush(string key,Color color) {if(Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key,out var value)&&value is SolidColorBrush brush)brush.Color=color;}
        Brush("HomeControlSurface",surface);Brush("HomeControlSurfaceHover",BlendSkin(surface,white,.05));Brush("HomeControlSurfacePressed",BlendSkin(surface,black,.25));
        void Gradient(string key,params Color[] colors) {if(Microsoft.UI.Xaml.Application.Current.Resources[key] is LinearGradientBrush brush)for(int i=0;i<brush.GradientStops.Count;i++)brush.GradientStops[i].Color=colors[Math.Min(i,colors.Length-1)];}
        Gradient("HomeWellBrush",BlendSkin(surface,black,.72),BlendSkin(surface,black,.25),BlendSkin(surface,white,.025));
        // Caption resources are self-contained, but follow the same selected skin.
        Brush("ShellCaptionHover",BlendSkin(surface,white,.12));Brush("ShellCaptionPressed",BlendSkin(surface,black,.18));
        Gradient("ShellCaptionWell",BlendSkin(surface,black,.72),BlendSkin(surface,black,.25),BlendSkin(surface,white,.025));
        Gradient("EffectsPanelBrush",surface,BlendSkin(surface,black,.55),BlendSkin(surface,black,.18));
        HomeAnimatedBackground.Background=new LinearGradientBrush {
            StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(1,1),
            GradientStops={
                new GradientStop {Color=SkinColor("#090909"),Offset=0},
                new GradientStop {Color=SkinColor("#050505"),Offset=.55},
                new GradientStop {Color=SkinColor("#020202"),Offset=1}
            }
        };
        homeBrandBackdrop?.SetVisible(homeChromeSelected&&Appearance.Pattern);
        SectionPattern.SetEnabled(ShellViewport,Appearance.Pattern);
        syncingAppearance=true;
        try {
            if(appearancePattern!=null)appearancePattern.IsOn=Appearance.Pattern;if(appearanceMotion!=null)appearanceMotion.IsOn=Appearance.Motion;
            // Skin choices keep their recessed face; selection only accents the existing rim.
            foreach(var pair in skinButtons) {
                bool selected=pair.Key==skin.Id;
                pair.Value.BorderBrush=HomeBrush(selected?"InteractionAccentBrush":"HomeRimBrush");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(pair.Value,selected?UiLanguage.Text("当前皮肤"):UiLanguage.Text("选择皮肤"));
            }
        } finally {syncingAppearance=false;}
        UpdateHomeMotionVisibility();
        UpdateSettingsNavigation(navigationPage);
    }
}
