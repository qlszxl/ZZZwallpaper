using System.Diagnostics;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void InitializeDiagnosticsSettings()
    {
        var panel=new StackPanel {Spacing=12};
        panel.Children.Add(UtilityText(UiLanguage.Text("日志与问题反馈"),"UtilityHeading"));
        
        var path=UtilityText(AppLog.DirectoryPath??UiLanguage.Text("日志目录不可用"),"UtilityLabel");
        path.IsTextSelectionEnabled=true;path.TextWrapping=TextWrapping.Wrap;panel.Children.Add(path);
        
        var status=UtilityText("");status.Visibility=Visibility.Collapsed;
        void SetStatus(string text) {status.Text=text;status.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;}
        if(AppLog.LastError is { } failure)SetStatus(UiLanguage.Text("日志暂不可写：")+failure);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        var open=new Button {Style=HomeStyle("HomeButton"),Content=UiLanguage.Text("打开日志文件夹")};
        var export=new Button {Style=HomeStyle("HomeButton"),Content=UiLanguage.Text("导出诊断包")};
        open.Click+=(_,_)=> {
            try {
                string directory=AppLog.DirectoryPath??throw new IOException(UiLanguage.Text("日志目录不可用：")+AppLog.LastError);
                Directory.CreateDirectory(directory);Process.Start(new ProcessStartInfo(directory) {UseShellExecute=true});
                SetStatus(AppLog.LastError is { } error?UiLanguage.Text("日志暂不可写：")+error:UiLanguage.Text("已打开日志文件夹。"));
            } catch(Exception ex) {SetStatus(UiLanguage.Text("打开失败：")+ex.Message);ShowError(status.Text,ex);}
        };
        export.Click+=async(_,_)=> {
            export.IsEnabled=false;
            try {
                var options=await ChooseDiagnosticExportOptions();if(options==null)return;
                var picker=new FileSavePicker {SuggestedFileName=UiLanguage.Format($"ZZZwallpaper-诊断-{DateTime.Now:yyyyMMdd-HHmmss}"),SuggestedStartLocation=PickerLocationId.Desktop};
                picker.FileTypeChoices.Add(UiLanguage.Text("ZIP 诊断包"),new List<string> {".zip"});
                WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file=await picker.PickSaveFileAsync();if(file==null)return;
                SetStatus(UiLanguage.Text("正在导出诊断包…"));
                await ExportDiagnosticBundleAsync(file.Path,options);
                SetStatus(UiLanguage.Text("诊断包已保存：")+file.Path+(!options.IncludeLogs||AppLog.LastError==null?"":UiLanguage.Text("；部分日志可能未写入：")+AppLog.LastError));
            } catch(Exception ex) {SetStatus(UiLanguage.Text("导出失败：")+ex.Message);ShowError(status.Text,ex);}
            finally {export.IsEnabled=true;}
        };
        buttons.Children.Add(open);buttons.Children.Add(export);panel.Children.Add(buttons);panel.Children.Add(status);
        SettingsDiagnosticsHost.Content=panel;
    }
    private async Task<DiagnosticExportOptions?> ChooseDiagnosticExportOptions()
    {
        var logs=SelectionCard(UiLanguage.Text("运行日志"),true,source=>UtilityIcon("Save",source,24));
        var environment=SelectionCard(UiLanguage.Text("运行环境"),true,source=>UtilityIcon("Processor",source,24));
        var configuration=SelectionCard(UiLanguage.Text("配置信息（脱敏）"),false,source=>UtilityIcon("Settings",source,24));
        var content=new StackPanel {Spacing=12};
        content.Children.Add(logs);content.Children.Add(environment);content.Children.Add(configuration);
        var hint=UtilityText(UiLanguage.Text("日志可能包含路径和错误上下文。配置不包含素材文件。"));
        hint.TextWrapping=TextWrapping.Wrap;content.Children.Add(hint);
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("导出诊断包"),Content=content,
            PrimaryButtonText=UiLanguage.Text("选择保存位置"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Primary,Style=HomeStyle("LibraryDialog")};
        dialog.Resources["ContentDialogMaxWidth"]=520d;
        DiagnosticExportOptions Options()=>new(logs.IsChecked==true,environment.IsChecked==true,configuration.IsChecked==true);
        foreach(var choice in new[]{logs,environment,configuration}) {
            choice.Checked+=(_,_)=>dialog.IsPrimaryButtonEnabled=Options().Any;
            choice.Unchecked+=(_,_)=>dialog.IsPrimaryButtonEnabled=Options().Any;
        }
        return await dialog.ShowAsync()==ContentDialogResult.Primary?Options():null;
    }
    private JsonElement CaptureDiagnosticConfiguration()
    {
        var bindings=store.Bindings();
        var screens=System.Windows.Forms.Screen.AllScreens.Select(s=>s.DeviceName).Concat(bindings.Select(b=>b.ScreenId)).Distinct().ToArray();
        // A typed, bounded application snapshot only: no database, media bytes, authorization settings or arbitrary setting keys.
        return DiagnosticConfiguration.Redact(new {
            appearance=Appearance,window=new {widthDip=AppWindow.Size.Width/Root.XamlRoot.RasterizationScale,heightDip=AppWindow.Size.Height/Root.XamlRoot.RasterizationScale},fpsLimit=modules.Settings.FpsLimit(),decoder=modules.Settings.Decoder(),
            audioAutoAdapt=modules.Settings.Get(AudioAutoAdaptKey)!="false",fullscreenPause=modules.Settings.Get("fullscreenPause")!="false",pausePolicy=modules.Settings.PausePolicy(),
            screens=screens.Select(screen=>new {screen,profile=store.ScreenProfile(screen),playback=store.Playback(screen),volume=store.WallpaperVolume(screen),sound=store.GetSetting("videoSound:"+screen)!="0"}).ToArray(),
            assetEffects=bindings.Select(b=>b.AssetId).Distinct().Select(id=>new {assetId=id,effects=store.AssetProfile(id)}).ToArray(),
            widgets=widgets?.State??WidgetConfiguration.FromJson(modules.Settings.Get("widgets.v1"))
        });
    }
    private async Task ExportDiagnosticBundleAsync(string destination,DiagnosticExportOptions options,CancellationToken token=default)
    {
        options.Validate();token.ThrowIfCancellationRequested();
        // UI/store reads happen once on their owning thread; compression and file I/O run in the background.
        JsonElement? configuration=options.IncludeConfiguration?CaptureDiagnosticConfiguration():null;
        object? environment=options.IncludeEnvironment?new {
            screens=System.Windows.Forms.Screen.AllScreens.Select(s=>new {s.Primary,s.Bounds,s.WorkingArea,scale=WidgetPlacement.Scale(s)}).ToArray(),
            effectsVersion=VideoEffects.Original.Version,widgetsVersion=new WidgetConfiguration().Version
        }:null;
        await Task.Run(()=>AppLog.Export(destination,options,environment,configuration,token),token);
    }
}
