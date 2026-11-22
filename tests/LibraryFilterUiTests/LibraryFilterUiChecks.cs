using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    internal async void CheckLibraryFilters(string output)
    {
        try {
            MediaAsset Asset(string id,string extension,int width,int height,long bytes=100,double duration=10)=>new(id,Path.Combine(output,id+extension),id,bytes,duration,width,height,extension.TrimStart('.'),"sdr","fixture");
            MediaAsset[] samples=[Asset("A 山水",".png",1920,1080),Asset("B 夜景",".mp4",1080,1920,300,30),Asset("C 正方形",".png",720,720,200),Asset("D 未知",".mp4",0,0),Asset("E 4K",".mp4",3840,2160,400,60)];
            foreach(var asset in samples)store.Upsert(asset);
            var folder=store.SaveFolder(new("fixture-folder",Path.Combine(output,"folder"),false));
            store.ReplaceFolderAssets(folder.Id,[samples[0].Id,samples[1].Id]);
            var folderResults=await store.QueryLibraryAssetsAsync(folder.Id,new(Kind:"video"),CancellationToken.None);
            ReorderCheck(folderResults.Total==2&&folderResults.Items.Single().Id=="B 夜景","Folder scope combines with metadata filters and retains unfiltered count");
            ReorderCheck((await store.ReadLibraryAssetsAsync(folder.Id,CancellationToken.None))[0].Id==samples[0].Id,"Legacy folder reads preserve playlist order");
            var basic=await store.QueryLibraryAssetsAsync(null,new(),CancellationToken.None);
            ReorderCheck(basic.Total==5&&basic.Items[0].Id=="E 4K","Default query preserves newest-first ordering");
            ReorderCheck(new LibraryFilter(Query:"山水",Kind:"image",Orientation:"landscape",Resolution:"1080p").Apply(samples).Single().Id=="A 山水","Combined filters match metadata without reading source files");
            ReorderCheck(new LibraryFilter(Query:".MP4").Apply(samples).Count==3,"Filename search is case insensitive");
            ReorderCheck(new LibraryFilter(Query:"%").Apply(samples).Count==0,"Search treats wildcard characters literally");
            ReorderCheck(new LibraryFilter(Resolution:"1080p").Apply(samples).Count==2,"Resolution uses short edge for portrait and landscape");
            ReorderCheck(new LibraryFilter(Orientation:"unknown").Apply(samples).Single().Id=="D 未知","Unknown dimensions have their own filter");
            foreach(var (edge,bucket) in new[]{(719,"below720"),(720,"720p"),(1079,"720p"),(1080,"1080p"),(1439,"1080p"),(1440,"1440p"),(2159,"1440p"),(2160,"2160p")})
                ReorderCheck(new LibraryFilter(Resolution:bucket).Matches(Asset("edge",".mp4",edge,edge*2)),"Resolution boundary "+edge);
            ReorderCheck(new LibraryFilter(Sort:"size").Apply(samples)[0].Bytes==400&&new LibraryFilter(Sort:"duration").Apply(samples)[0].Duration==60,"Size and duration sorts are descending");
            ReorderCheck(new LibraryFilter(Sort:"name").Apply(samples.Reverse())[0].Id=="A 山水","Name sort is deterministic");
            using(var cancel=new CancellationTokenSource()) {
                cancel.Cancel();bool cancelled=false;
                try {await store.QueryLibraryAssetsAsync(null,new(),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
                ReorderCheck(cancelled,"Cancelled database query stops before publishing results");
            }
            RefreshFolders();NavigateCore("library");await RefreshLibraryAsync(null);await ReorderSettle();
            ReorderCheck(!DoubleClickEdit.GetEnabled(librarySearch)&&!librarySearch.IsReadOnly,"Search accepts direct typing without parameter double-click editing");
            long revision=store.HistoryRevision;
            var oldRows=LibraryList.Items.OfType<LibraryRow>().ToArray();LibraryList.SelectedItems.Add(oldRows.Single(r=>r.Asset.Id=="B 夜景"));
            SetLibraryFilter(new(Kind:"image"),false);await RefreshLibraryAsync(null);
            ReorderCheck(LibraryList.Items.Count==2&&LibraryList.SelectedItems.Count==0,"Filtering removes hidden selections from batch actions");
            var removed=oldRows.Single(r=>r.Asset.Id=="B 夜景");
            ReorderCheck(removed.RefreshSourceStatusAsync(true).IsCompleted,"Removed rows no longer enqueue source checks");
            librarySearch.Text="山水";await ReorderSettle(450);
            ReorderCheck(LibraryList.Items.Count==1&&libraryFilter.Query=="山水","Search debounce updates actual library results");
            NavigateCore("wallpaper");NavigateCore("library");await RefreshLibraryAsync(null);
            ReorderCheck(libraryFilter.Query=="山水"&&LibraryList.Items.Count==1,"Library filters survive navigation within this session");
            var args=ToolCatalog.Json(new {query="",kind="video",orientation="portrait",sort="size",folderId="all"});ToolCatalog.Validate("ui_library_set_filter",args);
            var reply=await AutomationCoverage(new("ui_library_set_filter",args),CancellationToken.None);await RefreshLibraryAsync(null);
            ReorderCheck(!reply.IsError&&LibraryList.Items.Count==1&&((LibraryRow)LibraryList.Items[0]).Asset.Id=="B 夜景","MCP filter setter uses actual controls and query");
            ReorderCheck(reply.Data.GetProperty("kind").GetString()=="video"&&reply.Data.GetProperty("query").GetString()=="","MCP returns normalized filter state including clearing search");
            var apiRead=await store.QueryLibraryAssetsAsync(null,AutomationLibraryFilter(ToolCatalog.Json(new {kind="image"}),new()),CancellationToken.None);
            ReorderCheck(apiRead.Items.Count==2&&libraryFilter.Kind=="video","Read-only API filtering does not mutate UI filters");
            var api=await ExecuteAutomation(new("media_list",ToolCatalog.Json(new {kind="image",offset=1,limit=1})),CancellationToken.None);
            ReorderCheck(api.Data.GetProperty("total").GetInt32()==2&&api.Data.GetProperty("items").GetArrayLength()==1,"Actual media_list handler filters before pagination");
            await AutomationCoverage(new("ui_library_set_filter",ToolCatalog.Json(new {folderId=folder.Id,orientation="all",kind="all"})),CancellationToken.None);await RefreshLibraryAsync(null);
            ReorderCheck(LibraryList.Items.Count==2&&SelectedLibraryFolder?.Id==folder.Id,"MCP folder scope updates the visible source picker");
            SetLibraryFilter(new(Query:"不存在"),false);await RefreshLibraryAsync(null);
            ReorderCheck(LibraryList.Items.Count==0&&EmptyLibrary.Text.Contains("筛选"),"Empty filtered state is distinct from an empty library");
            ClearLibraryFilter();await RefreshLibraryAsync(null);
            ReorderCheck(libraryFilter==new LibraryFilter()&&LibraryList.Items.Count==5,"Reset restores all media and default sort");
            ReorderCheck(store.HistoryRevision==revision,"Searching and filtering leave configuration undo history unchanged");
            libraryFilterOptions.Visibility=Visibility.Visible;ReorderResize(1400,920);await ReorderSettle();
            ReorderCheck(libraryKind.ActualWidth>100&&librarySort.ActualWidth>100&&librarySearch.ActualHeight>=40,"Filter controls retain usable layout dimensions");
            await ReorderCapture(output,"library-wide");
            ReorderResize(900,760);await ReorderSettle();await ReorderCapture(output,"library-narrow");
            ReorderCheck(librarySearch.ActualWidth>200&&libraryResolution.ActualWidth>100,"Compact layout stacks controls without zero-width dropdowns");
            ReorderCheck(OwnedAuxiliaryProcesses.IsOwned(@"D:\App\Mcp\ZZZwallpaper.Mcp.exe",2,@"d:\app\mcp\zzzwallpaper.mcp.exe",2),"Owned bridge path comparison is case insensitive");
            ReorderCheck(!OwnedAuxiliaryProcesses.IsOwned(@"D:\Other\ZZZwallpaper.Mcp.exe",2,@"D:\App\Mcp\ZZZwallpaper.Mcp.exe",2)&&!OwnedAuxiliaryProcesses.IsOwned("same",3,"same",2),"Other installations and Windows sessions are excluded");
            var targets=await Task.Run(()=>ownedAuxiliaryProcesses.Include([new(Environment.ProcessId,"self","self",""),new(Environment.ProcessId,"duplicate","duplicate","")]));
            ReorderCheck(targets.Count(t=>t.ProcessId==Environment.ProcessId)==1,"App process IDs are deduplicated before sampling");
            for(int i=0;i<150;i++)PosterImageCache.Load(Path.Combine(output,"cache-"+i+".png"),512,1);
            ReorderCheck(PosterImageCache.Count<=96&&PosterImageCache.EstimatedBytes<=48L*1024*1024,"Poster cache remains bounded during repeated loads");
            PosterImageCache.TrimIdle();
            ReorderCheck(PosterImageCache.Count<=16&&PosterImageCache.EstimatedBytes<=8L*1024*1024,"Idle trimming releases LRU references to the lower budget");
            for(int i=0;i<1000;i++)store.Upsert(Asset("large-"+i,".png",1920,1080));
            ClearLibraryFilter();await RefreshLibraryAsync(null);await ReorderSettle();
            ReorderCheck(LibraryList.Items.Count==1005&&LibraryList.ItemsPanelRoot is Panel panel&&panel.Children.Count<100,"Large filtered library retains grid virtualization");
            var row=new LibraryRow(samples[0]);row.SetActive(true);var pending=row.RefreshSourceStatusAsync(true);row.SetActive(false);await pending;
            ReorderCheck(row.RefreshSourceStatusAsync(true).IsCompleted,"Deactivating a row cancels queued work safely");
            ReorderCheckIsolation();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks,foregroundActivated=false,productionDataAccessed=false,desktopHostsStarted=false},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception error) {File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;}
        finally {effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();App.ReorderIsolation?.WriteReport();Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();}
    }
}
