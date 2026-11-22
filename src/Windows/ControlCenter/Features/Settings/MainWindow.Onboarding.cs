using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private bool onboardingFirstRun;
    private ContentDialog? onboardingDialog;
    internal async Task ShowInitialOnboarding()
    {
        if(!onboardingFirstRun||libraryShutdown||Environment.GetCommandLineArgs().Contains("--startup"))return;
        await ShowOnboarding();
    }
    private async Task ShowOnboarding()
    {
        if(onboardingDialog!=null||Root.XamlRoot==null||libraryShutdown)return;
        var steps=new (string Title,string Text)[]{
            (UiLanguage.Text("添加你的壁纸"),UiLanguage.Text("将图片或视频拖入素材库，选中后点击“应用所选素材”。")),
            (UiLanguage.Text("选择屏幕与构图"),UiLanguage.Text("在顶部选择显示器，在预览下方调整位置和缩放。")),
            (UiLanguage.Text("调整画面效果"),UiLanguage.Text("拖动效果卡片可调整叠加顺序。")),
            (UiLanguage.Text("布置桌面组件"),UiLanguage.Text("拖动组件可移动位置，也可在位置设置中选择对齐参照物。")),
            (UiLanguage.Text("保存与继续使用"),UiLanguage.Text("配置可保存为预设。关闭窗口后，可从托盘暂停或退出。"))
        };
        int step=0;var progress=UtilityText("","UtilityLabel");var title=UtilityText("","UtilityHeading");
        var text=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=16,FontFamily=UiFonts.Body,Foreground=HomeBrush("HomeControlText")};
        var body=new StackPanel {Spacing=18,MaxWidth=500};body.Children.Add(progress);body.Children.Add(title);body.Children.Add(text);
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("欢迎使用 ZZZwallpaper"),Content=body,PrimaryButtonText=UiLanguage.Text("上一步"),SecondaryButtonText=UiLanguage.Text("下一步"),CloseButtonText=UiLanguage.Text("跳过"),DefaultButton=ContentDialogButton.Secondary,Style=HomeStyle("LibraryDialog")};
        void Refresh() {progress.Text=$"{step+1} / {steps.Length}";title.Text=steps[step].Title;text.Text=steps[step].Text;dialog.IsPrimaryButtonEnabled=step>0;dialog.SecondaryButtonText=step==steps.Length-1?UiLanguage.Text("开始使用"):UiLanguage.Text("下一步");}
        dialog.PrimaryButtonClick+=(_,e)=> {e.Cancel=true;if(step>0)step--;Refresh();};
        dialog.SecondaryButtonClick+=(_,e)=> {if(step<steps.Length-1){e.Cancel=true;step++;Refresh();}};
        onboardingDialog=dialog;Refresh();
        try {await dialog.ShowAsync();modules.Settings.Set("onboarding.v1","seen");onboardingFirstRun=false;}
        catch(Exception error) {if(!libraryShutdown)ShowError(UiLanguage.Text("新手引导未打开：")+error.Message,error);}
        finally {onboardingDialog=null;}
    }
}
