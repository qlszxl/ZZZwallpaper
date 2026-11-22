using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool librarySelectionScrollQueued;

    private LibraryRow? ResolveLibrarySelectionRow(LibraryRow? preferred=null)
    {
        var rows=LibraryList.SelectedItems.OfType<LibraryRow>().ToHashSet();
        if(preferred!=null&&rows.Contains(preferred))return preferred;
        // The primary item belongs to the selection, never to hover or focus.
        // Use item order as the stable fallback when that item is removed.
        return rows.FirstOrDefault(row=>row.Asset.Id==librarySelectionId)
            ??SelectedLibraryRows().LastOrDefault();
    }

    private void CommitLibrarySelection(LibraryRow? row)
    {
        librarySelectionId=row?.Asset.Id;
        if(row!=null)SetSelected(row.Asset);
        else SetSelected(SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset,followWallpaper:true);
        QueueLibrarySelectionScroll();
    }

    private void QueueLibrarySelectionScroll()
    {
        if(libraryShutdown||LibraryPanel.Visibility!=Visibility.Visible||LibraryList.SelectedItems.Count==0||librarySelectionScrollQueued)return;
        librarySelectionScrollQueued=true;
        // Let WinUI arrange the changed details panel naturally. High-priority
        // forced layout on every selection stalls input and defeats batching.
        if(!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=> {
            try {
                if(libraryShutdown||LibraryPanel.Visibility!=Visibility.Visible||LibraryList.SelectedItems.Count==0)return;
                var row=ResolveLibrarySelectionRow();
                if(row!=null) {
                    if(LibraryList.ContainerFromItem(row) is FrameworkElement container) {
                        container.StartBringIntoView(new BringIntoViewOptions {AnimationDesired=false});
                    } else
                        // Let the virtualizing grid realize an off-screen item.
                        LibraryList.ScrollIntoView(row,ScrollIntoViewAlignment.Default);
                }
            }finally {librarySelectionScrollQueued=false;}
        }))librarySelectionScrollQueued=false;
    }
}
