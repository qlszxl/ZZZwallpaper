using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private StackPanel effectExplanationBody=null!;
    private Image effectPreviewImage=null!;
    private TextBlock effectPreviewStatus=null!;
    private Border effectPreviewTaskbar=null!;
    private DispatcherTimer? effectPreviewTimer;
    private EffectImagePreviewClient? effectImageClient;
    private bool effectPreviewBusy,effectPreviewFailed;
    private int effectPreviewEpoch;
    private string? effectPreviewFrameKey;
    private readonly System.Diagnostics.Stopwatch effectPreviewClock=new();
    private string EffectPreviewPath() {
        string choice=modules.Settings.Get("effectPreviewImage")??"calibration";
        if(choice=="custom") {string path=modules.Settings.Get("effectPreviewCustom")??"";if(File.Exists(path))return path;}
        return Path.Combine(AppContext.BaseDirectory,"Assets","EffectPreviews",choice=="landscape"?"landscape.png":"calibration.png");
    }
    private void InitializeEffectImagePreview(StackPanel body)
    {
        effectExplanationBody=body;InitializeImageComparison(body);
        effectPreviewStatus=UtilityText("");effectPreviewStatus.Visibility=Visibility.Collapsed;body.Children.Add(effectPreviewStatus);
        effectPreviewTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(125)};effectPreviewTimer.Tick+=async (_,_)=>await UpdateEffectImage();
        Closed+=(_,_)=>StopEffectImage();Root.Unloaded+=(_,_)=>StopEffectImage();
    }
    private void StartEffectImage()
    {
        effectPreviewFailed=false;effectPreviewFrameKey=null;effectPreviewClock.Restart();effectPreviewTimer?.Start();
    }
    private void StopEffectImage()
    {
        StopLightFlareLocator();
        effectPreviewTimer?.Stop();endEffectComparisonDrag?.Invoke();effectPreviewClock.Stop();effectPreviewEpoch++;effectPreviewFrameKey=null;effectImageClient?.Dispose();effectImageClient=null;
    }
    private VideoEffects IsolatedImageEffects(VideoEffects effects,EffectKind kind)
    {
        return EffectProjection.Isolate(effects,kind) with {MaskPreviewId=kind==EffectKind.MaskLibrary?selectedMaskId:null};
    }
    private async Task UpdateEffectImage()
    {
        if(effectPreviewBusy||effectPreviewFailed||effectComparisonDragging||!effectExplanationOpen||selectedEffectKind is not { } kind||DisplayPanel.Visibility!=Visibility.Visible||loadingEffects)return;
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);if(!IsWindowVisible(handle)||IsIconic(handle))return;
        effectPreviewBusy=true;int epoch=effectPreviewEpoch;string? target=effectSession.Key;long revision=effectSession.Revision;
        bool Current()=>!libraryShutdown&&epoch==effectPreviewEpoch&&target==effectSession.Key&&revision==effectSession.Revision
            &&selectedEffectKind==kind&&effectExplanationOpen&&DisplayPanel.Visibility==Visibility.Visible&&!loadingEffects&&!effectComparisonDragging;
        try {
            string path=EffectPreviewPath();
            var effects=IsolatedImageEffects(DraftEffects,kind);
            // First use can decode and materialize image atlases. Keep that work
            // off the UI thread and reuse the exact prepared wire for both frames.
            var prepared=await Task.Run(()=> {
                string wire=effects.ToWire();return (Wire:wire,Key:$"{path}\n{kind}\n{effects.HidesTaskbar}\n{wire}");
            });
            if(!Current())return;
            string frameKey=prepared.Key;
            bool staticImage=kind is EffectKind.Color or EffectKind.RgbSplit or EffectKind.Glow or EffectKind.Vignette or EffectKind.HideTaskbar or EffectKind.EdgeOutline or EffectKind.Pixelate;
            if(kind==EffectKind.Transform)staticImage=true;
            if(kind==EffectKind.LightFlare)staticImage=!effects.LightFlare.Animation||(effects.LightFlare.PulseAmount==0&&effects.LightFlare.RotationSpeed==0);
            if(effects.EffectMasks.TryGetValue(kind,out var maskId)&&effects.Masks.Any(m=>m.Id==maskId&&m.Shape>=6&&m.Speed!=0))staticImage=false;
            if(staticImage&&effectPreviewFrameKey==frameKey)return;
            if(effectImageClient?.Source!=path) {effectImageClient?.Dispose();effectImageClient=new EffectImagePreviewClient(path,store.Root);}
            var client=effectImageClient!;
            if(effectOriginalImagePath!=path) {
                var originalBytes=await client.Render(prepared.Wire,true,0,false);if(!Current())return;
                var original=await DecodeEffectImage(originalBytes);if(!Current())return;effectOriginalImage.Source=original;effectOriginalImagePath=path;
            }
            if(!Current())return;
            var bytes=await client.Render(prepared.Wire,false,effectPreviewClock.Elapsed.TotalSeconds,kind==EffectKind.AudioReactive);
            if(!Current())return;
            var image=await DecodeEffectImage(bytes);if(!Current())return;effectPreviewImage.Source=image;effectPreviewFrameKey=frameKey;
            effectOriginalTaskbar.Visibility=kind==EffectKind.HideTaskbar?Visibility.Visible:Visibility.Collapsed;
            effectPreviewTaskbar.Visibility=kind==EffectKind.HideTaskbar&&!effects.HidesTaskbar?Visibility.Visible:Visibility.Collapsed;
            effectPreviewStatus.Text="";effectPreviewStatus.Visibility=Visibility.Collapsed;
        } catch(Exception ex) {
            if(Current()&&ex is not OperationCanceledException)AppLog.Error("ImagePreview","Rendering preview failed",ex);
            if(Current()) {effectPreviewFailed=true;effectPreviewStatus.Text=UiLanguage.Text("图片预览暂不可用：")+ex.Message+UiLanguage.Text("。收起后展开可重试。");effectPreviewStatus.Visibility=Visibility.Visible;effectImageClient?.Dispose();effectImageClient=null;}
        } finally {effectPreviewBusy=false;}
    }
    private static async Task<BitmapImage> DecodeEffectImage(byte[] bytes)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) {writer.WriteBytes(bytes);await writer.StoreAsync();await writer.FlushAsync();}
        stream.Seek(0);var image=new BitmapImage();await image.SetSourceAsync(stream);return image;
    }
    private void InitializeEffectPreviewSettings()
    {
        var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(UiLanguage.Text("效果预览图片"),"UtilityHeading"));
        var choice=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=new[]{UiLanguage.Text("校准网格 · 色彩与细节"),UiLanguage.Text("二次元湖畔风景"),UiLanguage.Text("自定义图片")}};
        string saved=modules.Settings.Get("effectPreviewImage")??"calibration";choice.SelectedIndex=saved=="landscape"?1:saved=="custom"?2:0;panel.Children.Add(choice);
        var thumbnail=new Image {Height=180,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(thumbnail);
        var imageStatus=UtilityText("");panel.Children.Add(imageStatus);int thumbnailEpoch=0;
        async void Refresh() {
            int epoch=++thumbnailEpoch;StopEffectImage();effectPreviewFailed=false;
            try {var file=await StorageFile.GetFileFromPathAsync(EffectPreviewPath());using var input=await file.OpenReadAsync();var image=new BitmapImage();await image.SetSourceAsync(input);if(epoch==thumbnailEpoch) {thumbnail.Source=image;imageStatus.Text="";}}
            catch(Exception ex) {AppLog.Error("MainWindow.EffectsImage","Operation failed",ex);if(epoch==thumbnailEpoch) {thumbnail.Source=null;imageStatus.Text=UiLanguage.Text("预览图暂不可用：")+ex.Message;}}
        }
        Refresh();bool selecting=false;
        async Task Import() {
            try {
                var picker=new FileOpenPicker();foreach(var extension in new[]{".png",".jpg",".jpeg",".webp"})picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
                using var input=await file.OpenAsync(FileAccessMode.Read);if(input.Size>32_000_000)throw new ArgumentException(UiLanguage.Text("图片文件最多 32 MB"));var decoder=await BitmapDecoder.CreateAsync(input);
                if(decoder.PixelWidth==0||decoder.PixelHeight==0||decoder.PixelWidth>16384||decoder.PixelHeight>16384||(ulong)decoder.PixelWidth*decoder.PixelHeight>32_000_000)throw new ArgumentException(UiLanguage.Text("图片最多 3200 万像素，单边不超过 16384 像素"));
                string folder=Path.Combine(store.Root,"effect-preview");Directory.CreateDirectory(folder);string target=Path.Combine(folder,"custom-"+Guid.NewGuid().ToString("N")+Path.GetExtension(file.Path));File.Copy(file.Path,target);
                modules.Settings.Set("effectPreviewCustom",target);modules.Settings.Set("effectPreviewImage","custom");selecting=true;choice.SelectedIndex=2;selecting=false;Refresh();store.MaintainPreviewFiles();
            }catch(Exception ex) {ShowError(UiLanguage.Text("预览图片未更换：")+ex.Message,ex);}
        }
        choice.SelectionChanged+=async (_,_)=> {
            if(selecting)return;if(choice.SelectedIndex==2&&!File.Exists(modules.Settings.Get("effectPreviewCustom")??"")) {await Import();selecting=true;choice.SelectedIndex=modules.Settings.Get("effectPreviewImage")=="custom"?2:modules.Settings.Get("effectPreviewImage")=="landscape"?1:0;selecting=false;return;}
            modules.Settings.Set("effectPreviewImage",choice.SelectedIndex==1?"landscape":choice.SelectedIndex==2?"custom":"calibration");Refresh();
        };
        var import=WidgetButton(UiLanguage.Text("选择自定义图片"),()=>{},"Add");import.HorizontalAlignment=HorizontalAlignment.Left;import.Click+=async (_,_)=>await Import();panel.Children.Add(import);AppearancePanel.Children.Add(UtilityCard(panel));
    }
}
