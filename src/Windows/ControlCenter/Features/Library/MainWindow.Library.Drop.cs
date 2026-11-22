using LumaWall.Contracts;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Border libraryDropOverlay=null!;
    private TextBlock libraryDropFolderHint=null!;
    private long libraryDropEventRevision;
    private NativeFileDropTarget? nativeLibraryDrop;
    private bool nativeLibraryDropQueued;
    private sealed record LibraryDropItem(string Path,bool IsFolder);

    [StructLayout(LayoutKind.Sequential)] private struct LibraryDropPoint {public int X,Y;}
    [DllImport("user32.dll",EntryPoint="ClientToScreen")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool LibraryDropClientToScreen(nint window,ref LibraryDropPoint point);
    [DllImport("user32.dll",EntryPoint="ScreenToClient")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool LibraryDropScreenToClient(nint window,ref LibraryDropPoint point);
    [DllImport("user32.dll",EntryPoint="IsWindowEnabled")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool LibraryDropWindowEnabled(nint window);

    private void InitializeLibraryDrop()
    {
        var message=new StackPanel {Spacing=12};
        var title=UtilityText(UiLanguage.Text("松开即可添加"),"UtilityHeading");title.TextAlignment=TextAlignment.Center;title.HorizontalAlignment=HorizontalAlignment.Stretch;title.TextWrapping=TextWrapping.Wrap;
        var formats=UtilityText(UiLanguage.Text("图片、视频和文件夹"),"UtilityLabel");formats.TextAlignment=TextAlignment.Center;formats.HorizontalAlignment=HorizontalAlignment.Stretch;formats.TextWrapping=TextWrapping.Wrap;
        libraryDropFolderHint=UtilityText("");libraryDropFolderHint.TextAlignment=TextAlignment.Center;libraryDropFolderHint.HorizontalAlignment=HorizontalAlignment.Stretch;libraryDropFolderHint.TextWrapping=TextWrapping.Wrap;
        message.Children.Add(title);message.Children.Add(formats);message.Children.Add(libraryDropFolderHint);
        libraryDropOverlay=new Border {
            IsHitTestVisible=false,Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(20),
            BorderThickness=new Thickness(2),BorderBrush=HomeBrush("InteractionAccentBrush"),
            Background=new SolidColorBrush(Windows.UI.Color.FromArgb(200,9,9,9)),Padding=new Thickness(24),
            Child=new ContentControl {Style=HomeStyle("HomeFrame"),Padding=new Thickness(24),Content=message,
                MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsTabStop=false}
        };
        Grid.SetRowSpan(libraryDropOverlay,2);Canvas.SetZIndex(libraryDropOverlay,10);
        LibraryPanel.Children.Add(libraryDropOverlay);
        foreach(var target in new[]{LibraryPanel,PageHeader}) {
            target.AllowDrop=true;target.Background??=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            // Cards and their child controls may mark routed drag events handled.
            target.AddHandler(UIElement.DragEnterEvent,new DragEventHandler(Library_DragOver),true);
            target.AddHandler(UIElement.DragOverEvent,new DragEventHandler(Library_DragOver),true);
            target.AddHandler(UIElement.DragLeaveEvent,new DragEventHandler(Library_DragLeave),true);
            target.AddHandler(UIElement.DropEvent,new DragEventHandler(Library_Drop),true);
        }
        LibraryPanel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(_,_)=> {
            if(LibraryPanel.Visibility!=Visibility.Visible)HideLibraryDrop();
            RefreshNativeLibraryDrop();
        });
        Root.Loaded+=(_,_)=>RefreshNativeLibraryDrop();
        Root.SizeChanged+=(_,_)=>RefreshNativeLibraryDrop();
        Activated+=(_,_)=>RefreshNativeLibraryDrop();
        Root.Unloaded+=(_,_)=> {HideLibraryDrop();nativeLibraryDrop?.Refresh(false);};
        Closed+=(_,_)=>DisposeNativeLibraryDrop();
    }

    private bool LibraryDropAvailable => !libraryShutdown&&!libraryFileBusy&&!nativeLibraryDropQueued&&importLifetime.Active==null&&!historyBusy
        &&!automationJobs.Values.Any(job=>job.FinishedAt==null);

    private bool LibraryDropSurfaceAvailable => LibraryDropAvailable&&navigationPage=="library"
        &&Root.IsLoaded&&Root.XamlRoot!=null&&LibraryPanel.Visibility==Visibility.Visible
        &&QueueOverlay.Visibility!=Visibility.Visible&&WidgetDrawerOverlay.Visibility!=Visibility.Visible
        &&EffectsTargetOverlay.Visibility!=Visibility.Visible&&EffectsAddOverlay.Visibility!=Visibility.Visible
        &&LibraryDropWindowEnabled(WinRT.Interop.WindowNative.GetWindowHandle(this))
        &&!VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p=>p.IsOpen&&p.Tag as string!="numeric-input-hint");

    private void RefreshNativeLibraryDrop()
    {
        if(libraryShutdown)return;
        // Registration follows durable page/import state. A transient popup or
        // automation job is checked at the actual drop, so closing it cannot
        // leave native reception disabled until the next resize/activation.
        bool enabled=navigationPage=="library"&&Root.IsLoaded&&Root.XamlRoot!=null
            &&LibraryPanel.Visibility==Visibility.Visible&&importLifetime.Active==null;
        if(enabled)nativeLibraryDrop??=new NativeFileDropTarget(WinRT.Interop.WindowNative.GetWindowHandle(this),CanAcceptNativeLibraryDrop,QueueNativeLibraryDrop);
        nativeLibraryDrop?.Refresh(enabled);
    }

    private void DisposeNativeLibraryDrop()
    {
        nativeLibraryDrop?.Dispose();nativeLibraryDrop=null;HideLibraryDrop();
    }

    private bool CanAcceptNativeLibraryDrop(nint receiver,int x,int y)
    {
        if(!LibraryDropSurfaceAvailable&&!PresetDropSurfaceAvailable)return false;
        var point=new LibraryDropPoint {X=x,Y=y};
        nint window=WinRT.Interop.WindowNative.GetWindowHandle(this);
        if(!LibraryDropClientToScreen(receiver,ref point)||!LibraryDropScreenToClient(window,ref point))return false;
        double scale=Root.XamlRoot.RasterizationScale;
        var position=new Windows.Foundation.Point(point.X/scale,point.Y/scale);
        // Child HWND client pixels must become root XAML DIPs before hit testing.
        // Only the frontmost branch can accept; overlays and the sidebar cannot.
        var hit=VisualTreeHelper.FindElementsInHostCoordinates(position,Root).FirstOrDefault();
        for(DependencyObject? node=hit;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node==PageHeader||navigationPage=="library"&&node==LibraryPanel||navigationPage=="presets"&&node==SharingScroll)return true;
        return false;
    }

    private void QueueNativeLibraryDrop(string[] paths)
    {
        if(!LibraryDropSurfaceAvailable)return;
        bool recursive=IncludeSubfoldersCheck.IsChecked==true;
        nativeLibraryDropQueued=true;
        if(!DispatcherQueue.TryEnqueue(async ()=> {
            nativeLibraryDropQueued=false;
            if(!LibraryDropSurfaceAvailable)return;
            HideLibraryDrop();
            await ImportLibraryDropAsync(token=>Task.Run<IReadOnlyList<LibraryDropItem>>(()=> {
                var items=new List<LibraryDropItem>();
                foreach(string path in paths) {
                    token.ThrowIfCancellationRequested();
                    // Do not probe network/device paths before the shared validator.
                    bool local=Path.IsPathFullyQualified(path)&&!path.StartsWith(@"\\");
                    items.Add(new(path,local&&Directory.Exists(path)));
                }
                return items;
            },token),recursive);
        }))nativeLibraryDropQueued=false;
    }

    private void HideLibraryDrop() {libraryDropEventRevision++;libraryDropOverlay.Visibility=Visibility.Collapsed;}

    private void Library_DragOver(object sender,DragEventArgs e)
    {
        if(navigationPage!="library")return;
        bool accepted=LibraryDropSurfaceAvailable&&e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation=accepted?DataPackageOperation.Copy:DataPackageOperation.None;
        e.Handled=true;
        if(!accepted) {HideLibraryDrop();return;}
        libraryDropEventRevision++;
        e.DragUIOverride.Caption=UiLanguage.Text("添加到素材库");
        libraryDropFolderHint.Text=IncludeSubfoldersCheck.IsChecked==true?UiLanguage.Text("文件夹将包含子文件夹"):UiLanguage.Text("文件夹仅添加当前层级的素材");
        libraryDropOverlay.Visibility=Visibility.Visible;
    }

    private void Library_DragLeave(object sender,DragEventArgs e)
    {
        // An Esc cancellation can end a drag while its last position is still
        // inside this panel. A subsequent child Enter/Over cancels this cleanup.
        long revision=++libraryDropEventRevision;
        if(!DispatcherQueue.TryEnqueue(()=> {if(revision==libraryDropEventRevision)HideLibraryDrop();}))HideLibraryDrop();
    }

    private async void Library_Drop(object sender,DragEventArgs e)
    {
        if(navigationPage!="library")return;
        HideLibraryDrop();e.Handled=true;e.AcceptedOperation=DataPackageOperation.None;
        if(!LibraryDropSurfaceAvailable||!e.DataView.Contains(StandardDataFormats.StorageItems))return;
        bool recursive=IncludeSubfoldersCheck.IsChecked==true;
        var deferral=e.GetDeferral();
        var data=e.DataView;
        await ImportLibraryDropAsync(async token=> {
            var storageItems=await data.GetStorageItemsAsync();CheckImportActive(token);
            return storageItems.Select(item=>new LibraryDropItem(item.Path,item.IsOfType(StorageItemTypes.Folder))).ToArray();
        },recursive,accepted=> {
            try {e.AcceptedOperation=accepted?DataPackageOperation.Copy:DataPackageOperation.None;}
            finally {deferral.Complete();}
        });
    }

    private async Task ImportLibraryDropAsync(Func<CancellationToken,Task<IReadOnlyList<LibraryDropItem>>> readItems,bool recursive,Action<bool>? completeRead=null)
    {
        CancellationTokenSource? operation=null;
        var summary=new ImportSummary();var items=new List<LibraryDropItem>();bool refresh=false,readCompleted=false;
        try {
            try {
                // Reserve the normal import slot before awaiting Explorer's data.
                operation=BeginLibraryImport();
                var droppedItems=await readItems(operation.Token);CheckImportActive(operation.Token);
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var item in droppedItems) {
                    CheckImportActive(operation.Token);
                    try {
                        string path=item.Path;
                        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\"))
                            throw new ArgumentException(UiLanguage.Text("请拖入本机保存的文件或文件夹。"));
                        bool folder=item.IsFolder;
                        path=folder?LibraryStore.NormalizePath(path):MediaFormats.LocalImportPath(path);
                        if(seen.Add(path))items.Add(new(path,folder));
                    } catch(Exception ex) {
                        summary.Failed++;
                        if(summary.Errors.Count<3)summary.Errors.Add(item.Path+"："+Translate(ex.Message));
                    }
                }
                readCompleted=true;
            } finally {
                // Only extracting paths needs the drag source. Probing and folder
                // scans continue through the normal cancellable import workflow.
                completeRead?.Invoke(readCompleted&&items.Count>0);
            }
            if(items.Count==0) {ShowImportSummary(summary,UiLanguage.Text("未找到可添加的图片、视频或文件夹。"));return;}
            refresh=true;
            var files=items.Where(item=>!item.IsFolder).Select(item=>item.Path).ToList();
            await ImportPaths(files,false,summary,operation!.Token);
            foreach(var folder in items.Where(item=>item.IsFolder)) {
                CheckImportActive(operation.Token);
                try {await ImportFolderSource(new(Guid.NewGuid().ToString("N"),folder.Path,recursive),false,operation.Token,summary);}
                catch(OperationCanceledException) {throw;}
                catch(Exception ex) {
                    CheckImportActive(operation.Token);AppLog.Warning("Library","Dropped folder import failed",ex);summary.Failed++;
                    if(summary.Errors.Count<3)summary.Errors.Add(Path.GetFileName(folder.Path)+"："+Translate(ex.Message));
                }
            }
            CheckImportActive(operation.Token);ShowImportSummary(summary,UiLanguage.Text("拖放添加完成。"));
        }
        catch(OperationCanceledException) {if(!libraryShutdown)Inform(UiLanguage.Text("拖放添加已取消。已完成的条目保留，")+summary.Text+"。");}
        catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("拖放添加未完成：")+ex.Message,ex);}
        finally {
            try {
                if(refresh&&!libraryShutdown) {
                    ClearLibraryFilter();RefreshFolders();
                    if(items is [{IsFolder:true} single]) {
                        refreshingFolders=true;
                        try {
                            if(LibraryFolderPicker.ItemsSource is IEnumerable<LibraryFolderRow> rows)
                                LibraryFolderPicker.SelectedItem=rows.FirstOrDefault(row=>row.Folder is { } folder&&string.Equals(folder.Path,single.Path,StringComparison.OrdinalIgnoreCase))??rows.First();
                            if(SelectedLibraryFolder is { } selectedFolder)IncludeSubfoldersCheck.IsChecked=selectedFolder.IncludeSubfolders;
                        } finally {refreshingFolders=false;}
                    }
                    RefreshFolderControls();RefreshLibrary();
                }
            } catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("素材已处理，但列表刷新未完成：")+ex.Message,ex);}
            finally {if(operation!=null)CompleteLibraryImport(operation);}
        }
    }

    private static void MergeLibraryImportSummary(ImportSummary batch,ImportSummary folder,int skippedDirectories)
    {
        batch.Added+=folder.Added;batch.Updated+=folder.Updated;batch.Existing+=folder.Existing;
        batch.Failed+=folder.Failed+skippedDirectories;batch.LastId=folder.LastId??batch.LastId;
        foreach(string error in folder.Errors)if(batch.Errors.Count<3)batch.Errors.Add(error);
        if(skippedDirectories>0&&batch.Errors.Count<3)batch.Errors.Add(UiLanguage.Format($"有 {skippedDirectories} 处目录或条目不可读取，其旧引用已保留"));
    }
}
