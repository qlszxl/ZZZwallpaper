using LumaWall.Contracts;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private CancellationTokenSource? libraryRefreshCancellation;
    private long libraryRefreshRevision;
    private int navigationSaveRequest;

    private void RefreshLibrary(string? id=null)
    {
        LibraryView.NeedsRefresh=true;
        LibraryView.PendingSelection=id;
        if(!LibraryView.IsActive||libraryShutdown)return;
        _=RefreshLibraryAsync(id);
    }

    private async Task RefreshLibraryAsync(string? id)
    {
        libraryRefreshCancellation?.Cancel();
        using var cancellation=new CancellationTokenSource();
        libraryRefreshCancellation=cancellation;
        long request=++libraryRefreshRevision;
        long catalogRevision=store.PlaybackLibraryRevision;
        string? folder=SelectedLibraryFolder?.Id;
        var filter=libraryFilter;
        try {
            var result=await store.QueryLibraryAssetsAsync(folder,filter,cancellation.Token);
            if(cancellation.IsCancellationRequested||libraryShutdown||!LibraryView.IsActive||request!=libraryRefreshRevision)return;
            if(catalogRevision!=store.PlaybackLibraryRevision||folder!=SelectedLibraryFolder?.Id) {RefreshLibrary(id);return;}
            await store.CompleteEffectWriteAsync();
            if(cancellation.IsCancellationRequested||libraryShutdown||!LibraryView.IsActive||request!=libraryRefreshRevision)return;
            if(catalogRevision!=store.PlaybackLibraryRevision||folder!=SelectedLibraryFolder?.Id) {RefreshLibrary(id);return;}
            if(filter!=libraryFilter) {RefreshLibrary(id);return;}
            ApplyLibraryRows(result.Items,id);
            libraryResultCount.Text=UiLanguage.Format($"素材 · {result.Items.Count} / {result.Total}");
        }catch(OperationCanceledException) { }
        catch(Exception error) {if(!libraryShutdown&&request==libraryRefreshRevision)ShowError(UiLanguage.Text("素材库读取失败：")+error.Message,error);}
        finally {if(ReferenceEquals(libraryRefreshCancellation,cancellation))libraryRefreshCancellation=null;}
    }

    private async Task NavigateAfterSaveAsync(string page,int request)
    {
        try {
            await FlushEffectChangesAsync();
            if(!libraryShutdown&&request==navigationSaveRequest)NavigateCore(page);
        }catch(Exception error) {if(!libraryShutdown)ShowError(UiLanguage.Text("画面参数保存失败，未切换页面：")+error.Message,error);}
    }
}
