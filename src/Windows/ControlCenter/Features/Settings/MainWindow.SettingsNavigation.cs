using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,Button> settingsCategoryButtons=new();
    private static bool IsSettingsPage(string page)=>page is "settings" or "appearance" or "performance" or "history" or "packs";
    private static string MainNavigationPage(string page)=>IsSettingsPage(page)?"settings":page;

    private void InitializeSettingsNavigation()
    {
        foreach(var (route,title,icon) in new[] {
            ("settings",UiLanguage.Text("通用"),"Settings"),
            ("appearance",UiLanguage.Text("外观"),"Appearance"),
            ("performance",UiLanguage.Text("性能监控"),"Performance"),
            ("packs",UiLanguage.Text("扩展包"),"Widgets"),
            ("history",UiLanguage.Text("更新历史"),"Queue")}) {
            var button=new Button {Style=HomeStyle("HomeButton"),HorizontalAlignment=HorizontalAlignment.Stretch,
                HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(14,12,14,12),MinHeight=80};
            var row=new Grid {ColumnSpacing=10};row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
            row.Children.Add(CreateHomeVectorIcon(icon,22,button));
            var text=UtilityText(title,"UtilityHeading");Grid.SetColumn(text,1);row.Children.Add(text);
            text.SetBinding(TextBlock.ForegroundProperty,new Microsoft.UI.Xaml.Data.Binding {Source=button,Path=new PropertyPath("Foreground")});
            button.Content=row;AutomationProperties.SetName(button,UiLanguage.Text("设置 · ")+title);
            button.Click+=(_,_)=>Navigate(route);settingsCategoryButtons.Add(route,button);SettingsCategoryGrid.Children.Add(button);
        }
        void Layout()=>FlowCards(SettingsCategoryGrid,SettingsCategoryGrid.ActualWidth>=880?5:SettingsCategoryGrid.ActualWidth>=440?2:1);
        SettingsCategoryGrid.SizeChanged+=(_,_)=>Layout();Layout();
    }

    private void UpdateSettingsNavigation(string page)
    {
        SettingsWorkspace.Visibility=IsSettingsPage(page)?Visibility.Visible:Visibility.Collapsed;
        foreach(var (route,button) in settingsCategoryButtons) {
            bool selected=route==page;
            ChoiceSelection.SetIsSelected(button,selected);
            AutomationProperties.SetHelpText(button,selected?UiLanguage.Text("当前设置分类"):UiLanguage.Text("切换设置分类"));
        }
        SettingsSectionHint.Visibility=Visibility.Collapsed;
        if(IsSettingsPage(page))DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,RefreshSettingsHints);
    }
}
