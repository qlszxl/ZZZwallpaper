using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private Popup? activeHelp;
    private Button? activeHelpButton;
    private bool helpPinned;
    private void DismissHelp() {if(activeHelp!=null)activeHelp.IsOpen=false;activeHelp=null;activeHelpButton=null;helpPinned=false;}
    private void ShowHelp(Button button,Popup tip,bool pin)
    {
        if(pin&&activeHelp==tip&&helpPinned){DismissHelp();return;}
        DismissHelp();if(button.XamlRoot==null)return;
        tip.XamlRoot=button.XamlRoot;
        var surface=(FrameworkElement)tip.Child;double width=Math.Max(1,ShellViewport.ActualWidth),height=Math.Max(1,ShellViewport.ActualHeight);
        surface.MaxWidth=Math.Max(1,Math.Min(400,width-24));surface.Measure(new Size(surface.MaxWidth,double.PositiveInfinity));
        var anchor=button.TransformToVisual(ShellViewport).TransformPoint(new Point(0,0));
        tip.HorizontalOffset=Math.Clamp(anchor.X,12,Math.Max(12,width-surface.DesiredSize.Width-12));
        double below=anchor.Y+button.ActualHeight*interfaceScale+8;
        tip.VerticalOffset=Math.Max(8,below+surface.DesiredSize.Height<=height-8?below:anchor.Y-surface.DesiredSize.Height-8);
        activeHelp=tip;activeHelpButton=button;helpPinned=pin;tip.IsOpen=true;
    }
    private bool IsHelpAnchor(DependencyObject? source)
    {
        if(activeHelpButton==null)return false;
        for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node))if(node==activeHelpButton)return true;
        return false;
    }
    private Button HelpIcon(string name,string text)
    {
        var button=new Button {Style=HomeStyle("CompactHelpButton"),VerticalAlignment=VerticalAlignment.Center,
            Content=new FontIcon {Glyph="\uE946",FontSize=14,IsTextScaleFactorEnabled=false}};
        AutomationProperties.SetName(button,name);AutomationProperties.SetHelpText(button,text);
        button.Visibility=helpHintsEnabled?Visibility.Visible:Visibility.Collapsed;
        if(helpButtons.Count%64==0)helpButtons.RemoveAll(reference=>!reference.TryGetTarget(out _));
        helpButtons.Add(new(button));
        Popup? tip=null;
        Popup Tip() {
            if(tip!=null)return tip;
            var content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,MaxWidth=360,FontSize=16,FontFamily=UiFonts.Body,Foreground=HomeBrush("HomeControlText")};
            var surface=new ContentControl {Style=HomeStyle("HomeFrame"),Padding=new Thickness(20,16,20,16),Content=content,IsTabStop=false};
            surface.PointerPressed+=(_,_)=>DismissHelp();surface.PointerWheelChanged+=(_,_)=>DismissHelp();
            return tip=new Popup {Child=surface,IsLightDismissEnabled=false};
        }
        button.PointerEntered+=(_,_)=>{if(helpHintsEnabled&&!helpPinned)ShowHelp(button,Tip(),false);};
        button.PointerExited+=(_,_)=>{if(activeHelp==tip&&!helpPinned)DismissHelp();};
        ParameterInputGate.SetExempt(button,true);
        button.Click+=(_,_)=>{if(helpHintsEnabled)ShowHelp(button,Tip(),true);};
        button.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>e.Handled=true),true);
        button.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=>e.Handled=true),true);
        button.Tapped+=(_,e)=>e.Handled=true;
        button.Unloaded+=(_,_)=>{if(activeHelp==tip)DismissHelp();};
        return button;
    }
    private void InitializeHelpHints()
    {
        parameterHintOwner=new(this);SetHelpHintsEnabled(modules.Settings.Get(HelpHintsKey)!="false",save:false);
        EffectTargetHelpHost.Content=HelpIcon(UiLanguage.Text("作用对象说明"),UiLanguage.Text("显示器配置作用于该屏幕。播放素材时，素材专属配置覆盖同类显示器效果，其他效果沿用显示器配置。按最终效果列表顺序依次处理；素材设置的顺序覆盖对应效果的显示器顺序。"));
        EffectOrderHelpHost.Content=HelpIcon(UiLanguage.Text("效果加载顺序说明"),UiLanguage.Text("效果按列表顺序依次处理，后面的效果叠加在前面的结果上。拖动卡片可调整顺序。"));
        DesktopMenuHelpHost.Content=HelpIcon(UiLanguage.Text("桌面右键说明"),UiLanguage.Text("开启后，在所选显示器的桌面空白处右键打开软件菜单；关闭后使用系统桌面菜单。"));
        Root.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>{if(!IsHelpAnchor(e.OriginalSource as DependencyObject))DismissHelp();}),true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,_)=>DismissHelp()),true);
        Root.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,_)=>DismissHelp()),true);
        Activated+=(_,e)=>{if(e.WindowActivationState==WindowActivationState.Deactivated)DismissHelp();};
        Root.SizeChanged+=(_,_)=>DismissHelp();
        Closed+=(_,_)=>DismissHelp();
    }
    private static Style DetailFlyoutStyle()
    {
        var style=new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0d));return style;
    }
}
