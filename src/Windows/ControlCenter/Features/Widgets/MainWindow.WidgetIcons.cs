using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static readonly (string File,string Name)[] WidgetIcons=[("folder",UiLanguage.Text("文件夹")),("web",UiLanguage.Text("网络")),("terminal",UiLanguage.Text("命令")),("performance",UiLanguage.Text("性能")),("music",UiLanguage.Text("音乐")),("games",UiLanguage.Text("游戏")),.."0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c=>("glyph-"+c,c.ToString()))];
    private FrameworkElement WidgetIconPicker(string? current,string fallback,Func<string?,bool> select,string? inheritedIcon=null)
    {
        var panel=new StackPanel {Spacing=12};panel.Children.Add(UtilityText(UiLanguage.Text("组件图标"),"UtilityLabel"));
        var preview=new ContentControl {Width=32,Height=32,Foreground=HomeBrush("HomeControlText"),HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        preview.Content=WidgetImage(current??inheritedIcon,fallback,preview,28);
        var status=UtilityText(current!=null?UiLanguage.Text("当前图标"):inheritedIcon!=null?UiLanguage.Text("沿用卡片原图标"):UiLanguage.Text("默认图标"));
        status.VerticalAlignment=VerticalAlignment.Center;
        var heading=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};heading.Children.Add(preview);heading.Children.Add(status);panel.Children.Add(heading);
        var digits=new Grid {ColumnSpacing=8,RowSpacing=8};var letters=new Grid {ColumnSpacing=8,RowSpacing=8};
        var choices=new Grid {ColumnSpacing=8,RowSpacing=8};var buttons=new List<Button>();
        string? chosen=current;Button? resetButton=null,uploadButton=null;
        void Mark(Button? selected) {
            foreach(var b in buttons) {ChoiceSelection.SetIsSelected(b,b==selected);Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(b,b==selected?UiLanguage.Text("已选中"):UiLanguage.Text("未选中"));}
            if(resetButton!=null)ChoiceSelection.SetIsSelected(resetButton,selected==null&&chosen==null);
            if(uploadButton!=null)ChoiceSelection.SetIsSelected(uploadButton,selected==null&&chosen!=null);
        }
        int request=0;
        void Apply(string? encoded,string text,Button? button) {if(!select(encoded))return;chosen=encoded;preview.Content=WidgetImage(encoded??inheritedIcon,fallback,preview,28);status.Text=encoded==null&&inheritedIcon!=null?UiLanguage.Text("沿用卡片原图标"):text;Mark(button);}
        foreach(var entry in WidgetIcons) {
            var file=Path.Combine(AppContext.BaseDirectory,"Assets","WidgetIcons",entry.File+".png");if(!File.Exists(file))continue;
            var button=WidgetButton(entry.Name,()=>{});
            var row=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
            row.Children.Add(new Image {Source=new BitmapImage {DecodePixelWidth=64,UriSource=new Uri(file)},Width=24,Height=24,VerticalAlignment=VerticalAlignment.Center,Stretch=Stretch.Uniform});
            if(!entry.File.StartsWith("glyph-",StringComparison.Ordinal))row.Children.Add(new TextBlock {Text=entry.Name,FontFamily=UiFonts.Body,FontSize=13,VerticalAlignment=VerticalAlignment.Center});button.Content=row;
            button.Click+=async(_,_)=> {int currentRequest=++request;try {var encoded=await ReadWidgetIcon(await StorageFile.GetFileFromPathAsync(file));if(currentRequest==request)Apply(encoded,entry.Name,button);}catch(Exception ex) {AppLog.Error("MainWindow.WidgetIcons","Operation failed",ex);status.Text=UiLanguage.Text("无法读取图标：")+ex.Message;}};
            buttons.Add(button);if(entry.File.StartsWith("glyph-",StringComparison.Ordinal)) {button.Padding=new Thickness(0);button.HorizontalContentAlignment=HorizontalAlignment.Center;button.Height=48;(char.IsDigit(entry.Name[0])?digits:letters).Children.Add(button);}else choices.Children.Add(button);
            async Task MatchCurrent() {try {var encoded=await ReadWidgetIcon(await StorageFile.GetFileFromPathAsync(file));if(request==0&&encoded==current)Mark(button);}catch { }}
            _=MatchCurrent();
        }
        UtilityAdaptiveGrid(choices,3,580);panel.Children.Add(choices);
        panel.Children.Add(UtilityText(UiLanguage.Text("数字 0–9"),"UtilityLabel"));UtilityAdaptiveGrid(digits,10,600);panel.Children.Add(digits);
        panel.Children.Add(UtilityText(UiLanguage.Text("字母 A–Z"),"UtilityLabel"));UtilityAdaptiveGrid(letters,10,600);panel.Children.Add(letters);
        var actions=new Grid {ColumnSpacing=8};var upload=WidgetButton(UiLanguage.Text("上传图标…"),()=>{},"Add");
        upload.Click+=async(_,_)=> {int currentRequest=++request;try {var value=await PickShortcutIcon();if(value!=null&&currentRequest==request)Apply(value,UiLanguage.Text("自定义图标"),null);}catch(Exception ex) {AppLog.Error("MainWindow.WidgetIcons","Operation failed",ex);status.Text=UiLanguage.Text("图标无法导入：")+ex.Message;}};
        actions.Children.Add(upload);var reset=WidgetButton(inheritedIcon==null?UiLanguage.Text("默认图标"):UiLanguage.Text("卡片原图标"),()=> {request++;Apply(null,UiLanguage.Text("默认图标"),null);},"Restart");actions.Children.Add(reset);FlowCards(actions,2);panel.Children.Add(actions);resetButton=reset;uploadButton=upload;Mark(null);
        return panel;
    }
}
