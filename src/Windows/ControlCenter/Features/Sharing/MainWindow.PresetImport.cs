using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private const string PresetMediaDirectorySetting="presets.media-directory.v1";
    private ContentDialog? presetImportLocationDialog;
    private TextBox? presetImportLocationEditor;

    private string InitialPresetMediaDirectory(string zipPath)
    {
        string saved=modules.Settings.Get(PresetMediaDirectorySetting)??"";
        if(Path.IsPathFullyQualified(saved))return saved;
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zipPath))!,"ZZZwallpaper预设素材");
    }

    private async Task<string?> ConfirmPresetMediaDirectory(string name,int count,long bytes,string initial,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body=PresetPanel();body.Spacing=16;
        body.Children.Add(UtilityText(name,"UtilityHeading"));
        body.Children.Add(UtilityText(UiLanguage.Format($"壁纸与素材 · {count} 个 · {LibraryPresentation.FileSize(bytes)}"),"UtilityLabel"));
        var path=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=initial,MaxLength=32760,
            HorizontalAlignment=HorizontalAlignment.Stretch};
        DoubleClickEdit.SetEnabled(path,false);
        AutomationProperties.SetName(path,UiLanguage.Text("素材保存位置"));presetImportLocationEditor=path;
        body.Children.Add(WidgetField(UiLanguage.Text("素材保存位置"),path));
        var actual=UtilityText("","UtilityLabel");actual.TextWrapping=TextWrapping.Wrap;actual.MaxLines=3;actual.TextTrimming=TextTrimming.CharacterEllipsis;
        body.Children.Add(WidgetField(UiLanguage.Text("实际目录"),actual));
        var choose=WidgetButton(UiLanguage.Text("选择文件夹…"),()=>{},"Add");body.Children.Add(choose);
        var error=UtilityText("");error.TextWrapping=TextWrapping.Wrap;error.Visibility=Visibility.Collapsed;body.Children.Add(error);
        var scroll=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=body,
            Width=Math.Max(240,Math.Min(540,Root.ActualWidth-140)),MaxHeight=Math.Max(180,Root.ActualHeight-260),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var dialog=new ContentDialog {Title=UiLanguage.Text("导入预设"),Content=scroll,XamlRoot=Root.XamlRoot,
            PrimaryButtonText=UiLanguage.Text("导入"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
        dialog.Resources["ContentDialogMaxWidth"]=640d;
        bool choosing=false;string pickerError="";
        string? Destination()
        {
            string value=path.Text.Trim();
            try {
                if(!Path.IsPathFullyQualified(value)||value.IndexOfAny(Path.GetInvalidPathChars())>=0)return null;
                string full=Path.GetFullPath(value);
                if(File.Exists(full))return null;
                return full;
            } catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException) {return null;}
        }
        void Validate()
        {
            bool valid=Destination()!=null;dialog.IsPrimaryButtonEnabled=valid&&!choosing;
            actual.Text=valid?Path.Combine(Destination()!,"ZZZwallpaper.PresetResources"):"—";
            error.Text=valid?pickerError:UiLanguage.Text("请选择有效的素材文件夹。");error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
        }
        path.TextChanged+=(_,_)=> {pickerError="";Validate();};
        choose.Click+=async (_,_)=> {
            if(choosing)return;
            choosing=true;choose.IsEnabled=false;pickerError="";Validate();
            try {
                var picker=new FolderPicker {SuggestedStartLocation=PickerLocationId.VideosLibrary};
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder=await picker.PickSingleFolderAsync();
                if(folder!=null&&!token.IsCancellationRequested)path.Text=folder.Path;
            } catch(Exception ex) {
                pickerError=UiLanguage.Text("无法选择文件夹：")+ex.Message;
            } finally {choosing=false;choose.IsEnabled=true;Validate();}
        };
        dialog.PrimaryButtonClick+=(_,args)=> {Validate();if(!dialog.IsPrimaryButtonEnabled)args.Cancel=true;};
        Validate();presetImportLocationDialog=dialog;SetPresetStatus("");
        using var registration=token.Register(()=>DispatcherQueue.TryEnqueue(()=>dialog.Hide()));
        try {
            var result=await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return result==ContentDialogResult.Primary?Destination():null;
        } finally {presetImportLocationDialog=null;presetImportLocationEditor=null;scroll.Content=null;dialog.Content=null;}
    }
}
