using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed record LibraryFolderRow(MediaFolder? Folder)
{
    public override string ToString() => Folder?.Path??UiLanguage.Text("全部素材");
}

public sealed partial class MainWindow
{
    private bool refreshingFolders,libraryShutdown;
    private static readonly HashSet<string> VideoExtensions=new(StringComparer.OrdinalIgnoreCase)
    {".mp4",".mkv",".mov",".webm",".avi",".m4v",".wmv",".mpg",".mpeg",".ts",".mts",".m2ts",".flv",".ogv",".3gp",".3g2",".m2v",".vob"};
    private MediaFolder? SelectedLibraryFolder => (LibraryFolderPicker.SelectedItem as LibraryFolderRow)?.Folder;

    public void ShutdownImports() {noticeTimer?.Stop();libraryShutdown=true;librarySearchDelay.Stop();libraryCacheIdle.Stop();libraryRefreshCancellation?.Cancel();ShutdownSteamImport();libraryFileDialog?.Hide();DisposeNativeLibraryDrop();importLifetime.Shutdown();posterRepairCancellation.Cancel();mediaOperationLifetime.Cancel();}

    private void InitializeLibrarySelection()
    {
        InitializeLibraryDrop();
        LibraryList.SelectionInteractionCompleted+=item=> {
            if(refreshingLibrary||libraryShutdown||item is not LibraryRow row||!LibraryList.SelectedItems.Contains(row)||row.Asset.Id==librarySelectionId)return;
            CommitLibrarySelection(ResolveLibrarySelectionRow(row));
        };
        LibraryWorkspace.AddHandler(UIElement.TappedEvent,new TappedEventHandler((_,e)=> {
            if(LibraryList.SelectedItems.Count==0)return;
            if(InteractionFocus.IsInteractive(e.OriginalSource as DependencyObject))return;
            for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=LibraryWorkspace;node=VisualTreeHelper.GetParent(node))
                if(node==LibraryInformationPanel)return;
            ClearLibrarySelection();
            SetSelected(SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset,followWallpaper:true);
        }),true);
    }

    private void RefreshFolders()
    {
        if(libraryShutdown)return;
        string? selectedId=SelectedLibraryFolder?.Id;
        refreshingFolders=true;
        try
        {
            var rows=new List<LibraryFolderRow> {new(null)};
            rows.AddRange(modules.Library.Folders().Select(folder=>new LibraryFolderRow(folder)));
            LibraryFolderPicker.ItemsSource=rows;
            LibraryFolderPicker.SelectedItem=rows.FirstOrDefault(row=>row.Folder?.Id==selectedId)??rows[0];
        }
        finally {refreshingFolders=false;}
        RefreshFolderControls();
    }

    private void ClearLibraryFilter()
    {
        SetLibraryFilter(new(),false);
        refreshingFolders=true;
        try {LibraryFolderPicker.SelectedIndex=0;}
        finally {refreshingFolders=false;}
        RefreshFolderControls();
    }

    private long folderStatusRequest;
    private async void RefreshFolderControls()
    {
        if(libraryShutdown)return;
        long request=++folderStatusRequest;
        var folder=SelectedLibraryFolder;
        bool busy=importLifetime.Active!=null||libraryFileBusy;
        ImportButton.IsEnabled=!busy;
        ImportFolderButton.IsEnabled=!busy;
        SteamImportButton.IsEnabled=!busy;
        if(!LibraryView.IsActive)return;
        RefreshFolderButton.IsEnabled=folder!=null&&!busy;
        RemoveFolderButton.IsEnabled=folder!=null&&!busy;
        LibraryFolderPicker.IsEnabled=!busy;
        IncludeSubfoldersCheck.IsEnabled=!busy;
        RefreshLibraryFileActions(SelectedLibraryAssets());
        FolderStatus.Visibility=folder==null?Visibility.Collapsed:Visibility.Visible;
        if(folder==null)FolderStatus.Text="";
        else
        {
            try {
                var read=store.ReadLibraryAssetsAsync(folder.Id,CancellationToken.None);
                bool exists=await Task.Run(()=>Directory.Exists(folder.Path));
                int count=(await read).Count;
                if(libraryShutdown||!LibraryView.IsActive||request!=folderStatusRequest||SelectedLibraryFolder?.Id!=folder.Id)return;
                FolderStatus.Text=UiLanguage.Format($"{count} 个素材 · {(folder.IncludeSubfolders?UiLanguage.Text("包含子文件夹"):UiLanguage.Text("仅当前文件夹"))} · ")+(exists?UiLanguage.Text("可用"):UiLanguage.Text("来源不可用"));
            }catch(Exception error) {
                AppLog.Warning("Library","Cannot read folder status",error);
            }
        }
    }

    private void LibraryFolder_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(refreshingFolders||libraryShutdown)return;
        if(SelectedLibraryFolder is { } folder)IncludeSubfoldersCheck.IsChecked=folder.IncludeSubfolders;
        RefreshFolderControls();RefreshLibrary();
    }

    private CancellationTokenSource BeginLibraryImport()
    {
        if(libraryFileBusy)throw new InvalidOperationException(UiLanguage.Text("正在处理素材文件，请稍后再导入。"));
        // A manual import/relink takes precedence over startup repair of old paths.
        posterRepairCancellation.Cancel();
        var operation=importLifetime.Begin();
        BusyRing.IsActive=true;ImportButton.IsEnabled=false;CancelImportButton.Visibility=Visibility.Visible;
        RefreshFolderControls();RefreshNativeLibraryDrop();return operation;
    }

    private void CompleteLibraryImport(CancellationTokenSource operation)
    {
        importLifetime.Complete(operation);
        if(libraryShutdown)return;
        BusyRing.IsActive=applying>0;ImportButton.IsEnabled=true;CancelImportButton.Visibility=Visibility.Collapsed;
        RefreshFolderControls();RefreshNativeLibraryDrop();
    }

    private void CheckImportActive(CancellationToken token)=>importLifetime.Check(token);

    public async Task ImportVideos()
    {
        if(importLifetime.Active!=null||libraryShutdown||libraryFileBusy)return;
        var operation=BeginLibraryImport();var summary=new ImportSummary();bool picked=false;
        try
        {
            var picker=new FileOpenPicker {SuggestedStartLocation=PickerLocationId.VideosLibrary,ViewMode=PickerViewMode.Thumbnail};
            foreach(var extension in MediaFormats.Videos.Concat(MediaFormats.Images))picker.FileTypeFilter.Add(extension);WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var files=await picker.PickMultipleFilesAsync();CheckImportActive(operation.Token);
            if(files.Count==0)return;
            picked=true;Inform(UiLanguage.Text("正在导入素材…"));
            await ImportPaths(files.Select(file=>file.Path).ToList(),false,summary,operation.Token);
            CheckImportActive(operation.Token);ShowImportSummary(summary,UiLanguage.Text("导入完成。"));
        }
        catch(OperationCanceledException) {if(!libraryShutdown)Inform(UiLanguage.Text("导入已取消。已完成的条目保留，")+summary.Text+"。");}
        catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("导入未完成：")+ex.Message,ex);}
        finally
        {
            try {if(!libraryShutdown&&picked) {ClearLibraryFilter();RefreshFolders();RefreshLibrary();Navigate("library");}}
            finally {CompleteLibraryImport(operation);}
        }
    }

    private async void ImportFolder_Click(object sender,RoutedEventArgs e)
    {
        if(importLifetime.Active!=null||libraryShutdown||libraryFileBusy)return;
        bool includeSubfolders=IncludeSubfoldersCheck.IsChecked==true;
        var operation=BeginLibraryImport();
        try
        {
            var picker=new FolderPicker {SuggestedStartLocation=PickerLocationId.VideosLibrary};foreach(var extension in MediaFormats.Videos.Concat(MediaFormats.Images))picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var chosen=await picker.PickSingleFolderAsync();CheckImportActive(operation.Token);
            if(chosen==null)return;
            var folder=new MediaFolder(Guid.NewGuid().ToString("N"),chosen.Path,includeSubfolders);
            await ImportFolderSource(folder,false,operation.Token);
        }
        catch(OperationCanceledException) {if(!libraryShutdown)Inform(UiLanguage.Text("文件夹导入已取消，已完成的条目保留。"));}
        catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("文件夹导入未完成：")+ex.Message,ex);}
        finally {CompleteLibraryImport(operation);}
    }

    private async void RefreshFolder_Click(object sender,RoutedEventArgs e)
    {
        if(importLifetime.Active!=null||libraryShutdown||libraryFileBusy||SelectedLibraryFolder is not { } folder)return;
        var operation=BeginLibraryImport();
        try {await ImportFolderSource(folder with {IncludeSubfolders=IncludeSubfoldersCheck.IsChecked==true},true,operation.Token);}
        catch(OperationCanceledException) {if(!libraryShutdown)Inform(UiLanguage.Text("文件夹刷新已取消，原有引用和已完成条目保留。"));}
        catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("文件夹刷新未完成：")+ex.Message,ex);}
        finally {CompleteLibraryImport(operation);}
    }

    private async Task ImportFolderSource(MediaFolder folder,bool refresh,CancellationToken token,ImportSummary? batchSummary=null)
    {
        CheckImportActive(token);
        FolderStatus.Text=UiLanguage.Text("正在扫描素材…");Inform(UiLanguage.Text("正在扫描文件夹…"));
        var scan=await Task.Run(()=>ScanFolder(folder.Path,folder.IncludeSubfolders,token),token);
        CheckImportActive(token);folder=modules.Library.SaveFolder(folder);
        var previous=modules.Library.FolderAssets(folder.Id).Select(asset=>asset.Id).ToList();
        var summary=new ImportSummary();bool complete=false;
        try
        {
            await ImportPaths(scan.Paths,refresh,summary,token);CheckImportActive(token);
            complete=true;
            if(batchSummary==null) {
                ShowImportSummary(summary,refresh?UiLanguage.Text("文件夹刷新完成。"):UiLanguage.Text("文件夹已添加。"));
                if(scan.SkippedDirectories>0)Inform(UiLanguage.Format($"{summary.Text}。有 {scan.SkippedDirectories} 处目录或条目不可读取，其旧引用已保留。")+(summary.Errors.Count==0?"":UiLanguage.Text(" 跳过示例：")+string.Join("；",summary.Errors)));
            }
        }
        finally
        {
            if(batchSummary!=null)MergeLibraryImportSummary(batchSummary,summary,scan.SkippedDirectories);
            if(!libraryShutdown)
            {
                // A cancelled or incomplete scan must not discard older folder membership.
                modules.Library.ReplaceFolderAssets(folder.Id,complete&&scan.SkippedDirectories==0?summary.Members:summary.Members.Concat(previous));
                if(batchSummary==null) {
                    RefreshFolders();refreshingFolders=true;
                    try
                    {
                        if(LibraryFolderPicker.ItemsSource is IEnumerable<LibraryFolderRow> rows)LibraryFolderPicker.SelectedItem=rows.FirstOrDefault(row=>row.Folder?.Id==folder.Id);
                        IncludeSubfoldersCheck.IsChecked=folder.IncludeSubfolders;
                    }
                    finally {refreshingFolders=false;}
                    RefreshLibrary();Navigate("library");
                }
            }
        }
    }

    private static FolderScan ScanFolder(string path,bool recursive,CancellationToken token)=>LibraryImportCoordinator.ScanFolder(path,recursive,token);
    private Task ImportPaths(IReadOnlyList<string> paths,bool refresh,ImportSummary summary,CancellationToken token,Action? beforeCommit=null)
        =>libraryImports.ImportPaths(paths,refresh,summary,token,text=> {FolderStatus.Text=text;Inform(text);},beforeCommit);

    private void ShowImportSummary(ImportSummary summary,string prefix)
    {
        Inform(prefix+summary.Text+"。"+(summary.Errors.Count==0?"":UiLanguage.Text(" 跳过示例：")+string.Join("；",summary.Errors)));
    }

    private async void RemoveFolder_Click(object sender,RoutedEventArgs e)
    {
        if(importLifetime.Active!=null||libraryShutdown||libraryFileBusy||SelectedLibraryFolder is not { } folder)return;
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("移除文件夹来源？"),Content=UiLanguage.Text("保留源文件、素材库条目和播放列表。"),PrimaryButtonText=UiLanguage.Text("移除来源"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close};
        dialog.Style = HomeStyle("LibraryDialog");
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary||libraryShutdown||importLifetime.Active!=null)return;
        try {modules.Library.RemoveFolder(folder.Id);RefreshFolders();RefreshLibrary();Inform(UiLanguage.Text("文件夹来源已移除，素材与播放列表已保留。"));}
        catch(Exception ex) {ShowError(UiLanguage.Text("移除文件夹来源失败：")+ex.Message,ex);}
    }
}
