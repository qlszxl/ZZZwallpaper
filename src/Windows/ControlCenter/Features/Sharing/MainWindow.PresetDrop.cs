using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private bool presetDropReading;
    private bool PresetDropSurfaceAvailable=>navigationPage=="presets"&&!presetBusy&&!presetDropReading&&LibraryDropAvailable
        &&Root.IsLoaded&&Root.XamlRoot!=null&&SharingScroll.Visibility==Visibility.Visible
        &&QueueOverlay.Visibility!=Visibility.Visible&&WidgetDrawerOverlay.Visibility!=Visibility.Visible
        &&EffectsTargetOverlay.Visibility!=Visibility.Visible&&EffectsAddOverlay.Visibility!=Visibility.Visible
        &&LibraryDropWindowEnabled(WinRT.Interop.WindowNative.GetWindowHandle(this))
        &&!VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p=>p.IsOpen);

    private void InitializePresetDrop()
    {
        SharingScroll.AllowDrop=true;SharingScroll.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach(var target in new UIElement[]{SharingScroll,PageHeader}) {
            target.AllowDrop=true;
            target.AddHandler(UIElement.DragOverEvent,new DragEventHandler(Preset_DragOver),true);
            target.AddHandler(UIElement.DragEnterEvent,new DragEventHandler(Preset_DragOver),true);
            target.AddHandler(UIElement.DropEvent,new DragEventHandler(Preset_Drop),true);
        }
        SharingScroll.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(_,_)=>RefreshNativeLibraryDrop());
    }
    private void Preset_DragOver(object sender,DragEventArgs e)
    {
        if(navigationPage!="presets")return;
        bool accepted=PresetDropSurfaceAvailable&&e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation=accepted?DataPackageOperation.Copy:DataPackageOperation.None;e.Handled=true;
        if(accepted)e.DragUIOverride.Caption=UiLanguage.Text("导入预设 ZIP");
    }
    private async void Preset_Drop(object sender,DragEventArgs e)
    {
        if(navigationPage!="presets")return;
        e.Handled=true;e.AcceptedOperation=DataPackageOperation.None;
        if(!PresetDropSurfaceAvailable||!e.DataView.Contains(StandardDataFormats.StorageItems))return;
        var deferral=e.GetDeferral();presetDropReading=true;
        try {
            var items=await e.DataView.GetStorageItemsAsync();
            if(items.Count!=1||items[0] is not StorageFile file)throw new ArgumentException(UiLanguage.Text("请拖入一个预设 ZIP 文件。"));
            await ImportDroppedPreset([file.Path]);e.AcceptedOperation=DataPackageOperation.Copy;
        }catch(Exception error) {if(!libraryShutdown)ShowError(UiLanguage.Text("导入预设未完成：")+error.Message,error);}
        finally {presetDropReading=false;deferral.Complete();}
    }
    private async Task ImportDroppedPreset(string[] paths)
    {
        if(paths.Length!=1||!Path.IsPathFullyQualified(paths[0])||paths[0].StartsWith(@"\\")
            ||!Path.GetExtension(paths[0]).Equals(".zip",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(UiLanguage.Text("请拖入一个本机保存的预设 ZIP 文件。"));
        if(!libraryShutdown)await ImportPresetPath(paths[0]);
    }
}
