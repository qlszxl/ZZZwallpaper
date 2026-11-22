using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using LumaWall.Contracts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private const int LibraryPerformanceAssetCount=2000;
    private readonly List<object> libraryPerformanceMeasurements=[];
    private int libraryPerformanceContainerChanges,libraryPerformanceSelectionChanges,libraryPerformancePosterChanges;
    private readonly List<double> libraryPerformanceHeartbeatIntervals=[];
    private long libraryPerformanceHeartbeatAt;
    private DispatcherTimer? libraryPerformanceHeartbeat;
#if LIBRARY_PERF_OPTIMIZED
    private static readonly bool LibraryPerformanceOptimized=true;
#else
    private static readonly bool LibraryPerformanceOptimized=false;
#endif

    private static IEnumerable<FrameworkElement> LibraryPerformanceVisuals(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in LibraryPerformanceVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }

    private ScrollViewer LibraryPerformanceScroller()=>LibraryPerformanceVisuals(LibraryList).OfType<ScrollViewer>().First();
    private int LibraryPerformanceContainers()=>LibraryPerformanceVisuals(LibraryList).OfType<GridViewItem>().Count();

    private void SeedLibraryPerformanceAssets(string output)
    {
        string source=Path.Combine(output,"fixture-media");
        Directory.CreateDirectory(source);
        byte[] image,poster;
        using(var bitmap=new System.Drawing.Bitmap(96,54))
        {
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(22,30,38));
                using var brush=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(197,225,0));
                graphics.FillRectangle(brush,8,8,48,38);
            }
            using var png=new MemoryStream();bitmap.Save(png,System.Drawing.Imaging.ImageFormat.Png);image=png.ToArray();
            using var bmp=new MemoryStream();bitmap.Save(bmp,System.Drawing.Imaging.ImageFormat.Bmp);poster=bmp.ToArray();
        }
        for(int i=0;i<LibraryPerformanceAssetCount;i++)
        {
            string id="library-perf-"+i.ToString("D4");
            string path=Path.Combine(source,id+".png");
            File.WriteAllBytes(path,image);
            File.WriteAllBytes(modules.Library.Poster(id),poster);
            modules.Library.Upsert(new MediaAsset(id,path,"性能样本 "+i.ToString("D4"),image.Length,0,96,54,"png","sdr","fixture"));
        }
    }

    private void LibraryPerformancePosterChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName==nameof(LibraryRow.Poster))libraryPerformancePosterChanges++;
    }

    private Task LibraryPerformanceIdle()
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=>completion.SetResult()))
            throw new InvalidOperationException("The isolated UI dispatcher closed before the measurement completed.");
        return completion.Task;
    }

    private async Task MeasureLibraryPerformance(string operation,Action action)
    {
        int containersBefore=libraryPerformanceContainerChanges,selectionsBefore=libraryPerformanceSelectionChanges,
            postersBefore=libraryPerformancePosterChanges;
        var timer=Stopwatch.StartNew();
        action();
        double synchronousMs=timer.Elapsed.TotalMilliseconds;
        Root.UpdateLayout();
        await LibraryPerformanceIdle();
        Root.UpdateLayout();
        double throughLayoutMs=timer.Elapsed.TotalMilliseconds;
        timer.Stop();
        // This fixed pause is excluded from reported timings. It lets deferred
        // images finish before counting realized controls or starting a sample.
        await Task.Delay(100);
        var scroller=LibraryPerformanceVisuals(LibraryList).OfType<ScrollViewer>().FirstOrDefault();
        using var process=Process.GetCurrentProcess();
        libraryPerformanceMeasurements.Add(new {
            operation,synchronousMs,throughLayoutMs,items=LibraryList.Items.Count,
            realizedContainers=LibraryPerformanceContainers(),
            containerChanges=libraryPerformanceContainerChanges-containersBefore,
            selectionChanges=libraryPerformanceSelectionChanges-selectionsBefore,
            posterNotifications=libraryPerformancePosterChanges-postersBefore,
            selectedIds=LibraryList.SelectedItems.OfType<LibraryRow>().Select(row=>row.Asset.Id).ToArray(),
            selectedAssetId=selected?.Id,verticalOffset=scroller?.VerticalOffset,
            viewportHeight=scroller?.ViewportHeight,extentHeight=scroller?.ExtentHeight,
            privateBytes=process.PrivateMemorySize64,managedBytes=GC.GetTotalMemory(false)
        });
        WriteLibraryPerformanceMeasurements();
    }

    private void WriteLibraryPerformanceMeasurements()
    {
        File.WriteAllText(Path.Combine(EffectReorderRegressionProgram.Output,"measurements.json"),
            JsonSerializer.Serialize(new {
                assetCount=LibraryPerformanceAssetCount,optimizedAssertionsEnabled=LibraryPerformanceOptimized,
                timings="Synchronous call and call through forced layout/low-priority dispatcher delivery, in milliseconds. The following fixed 100 ms settle and fixture file/database creation are excluded. No machine-dependent timing threshold is asserted.",
                heartbeatIntervalMs=16,
                heartbeatMaximumObservedMs=libraryPerformanceHeartbeatIntervals.DefaultIfEmpty(0).Max(),
                heartbeatSamples=libraryPerformanceHeartbeatIntervals.Count,
                measurements=libraryPerformanceMeasurements
            },new JsonSerializerOptions {WriteIndented=true}));
    }

    private void StartLibraryPerformanceHeartbeat()
    {
        libraryPerformanceHeartbeatAt=Stopwatch.GetTimestamp();
        libraryPerformanceHeartbeat=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(16)};
        libraryPerformanceHeartbeat.Tick+=(_,_)=> {
            long now=Stopwatch.GetTimestamp();
            libraryPerformanceHeartbeatIntervals.Add((now-libraryPerformanceHeartbeatAt)*1000d/Stopwatch.Frequency);
            libraryPerformanceHeartbeatAt=now;
        };
        libraryPerformanceHeartbeat.Start();
    }

    private void CheckLibraryPerformancePosterCache(LibraryRow row)
    {
        string path=row.PosterPath??throw new InvalidOperationException("The fixture row has no poster path.");
        var first=PosterImageCache.Load(path);
        ReorderCheck(first!=null,"A valid fixture poster loads into the cache");
        for(int i=0;i<1000;i++)
            if(!ReferenceEquals(first,PosterImageCache.Load(path)))
                throw new InvalidOperationException("A hot poster lookup rebuilt its bitmap.");
        ReorderCheck(true,"1000 consecutive poster lookups reuse the cached bitmap instance");
        PosterImageCache.Invalidate(path);
        var refreshed=PosterImageCache.Load(path);
        ReorderCheck(refreshed!=null&&!ReferenceEquals(first,refreshed),
            "Explicit poster invalidation creates a fresh bitmap for the next lookup");
        ReorderCheck(ReferenceEquals(refreshed,PosterImageCache.Load(path)),
            "The newly invalidated poster is cached for subsequent lookups");
    }

#if LIBRARY_PERF_OPTIMIZED
    private async Task CheckLibraryPerformanceSourceStatusUi(LibraryRow row,string output)
    {
        string source=Path.GetFullPath(row.Asset.Path);
        string ownedRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(output))+Path.DirectorySeparatorChar;
        if(!source.StartsWith(ownedRoot,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source-status regression refuses to move a file outside its own results directory.");
        string unavailable=source+".temporarily-unavailable";
        if(File.Exists(unavailable))throw new InvalidOperationException("The fixture's temporary source-status path is already occupied.");

        LibraryList.SelectedItems.Clear();
        LibraryList.SelectedItems.Add(row);
        await row.RefreshSourceStatusAsync(force:true);await LibraryPerformanceIdle();Root.UpdateLayout();
        ReorderCheck(row.SourceAvailable==true&&LibraryDeleteSourceButton.IsEnabled&&
            !LibraryInfoMetadata.Text.Contains("来源失效",StringComparison.Ordinal),
            "A present selected source enables file deletion and displays current media metadata");
        File.Move(source,unavailable);
        try
        {
            await row.RefreshSourceStatusAsync(force:true);await LibraryPerformanceIdle();Root.UpdateLayout();
            ReorderCheck(row.SourceAvailable==false&&row.Availability=="来源失效"&&
                row.Description.Contains("来源失效",StringComparison.Ordinal),
                "An awaited background status check detects a missing source without a library rebuild");
            ReorderCheck(LibraryInfoMetadata.Text.Contains("来源失效",StringComparison.Ordinal)&&
                !LibraryDeleteSourceButton.IsEnabled&&LibraryRevealSourceButton.IsEnabled&&selected?.Id==row.Asset.Id,
                "The selected source's details update automatically and disable deletion while retaining selection");
            RefreshHomePreview(row.Asset,null,forcePoster:true);
            var homeRow=homePreviewRow??throw new InvalidOperationException("The home preview did not create its cached row.");
            await homeRow.RefreshSourceStatusAsync(force:true);await LibraryPerformanceIdle();
            ReorderCheck(PreviewMeta.Text.Contains("来源失效",StringComparison.Ordinal),
                "The home preview receives asynchronous missing-source metadata");
            RefreshHomePreview(row.Asset,null);
            ReorderCheck(ReferenceEquals(homeRow,homePreviewRow),
                "Repeated home preview refresh reuses the same asset status row");
            await ReorderCapture(output,"library-missing-source");
        }
        finally {if(File.Exists(unavailable)&&!File.Exists(source))File.Move(unavailable,source);}

        await row.RefreshSourceStatusAsync(force:true);await LibraryPerformanceIdle();Root.UpdateLayout();
        ReorderCheck(row.SourceAvailable==true&&row.Availability!="来源失效"&&
            !LibraryInfoMetadata.Text.Contains("来源失效",StringComparison.Ordinal)&&LibraryDeleteSourceButton.IsEnabled,
            "Restoring a fixture source updates its availability and re-enables file deletion");
        RefreshHomePreview(row.Asset,null,forcePoster:true);
        await homePreviewRow!.RefreshSourceStatusAsync(force:true);await LibraryPerformanceIdle();
        ReorderCheck(!PreviewMeta.Text.Contains("来源失效",StringComparison.Ordinal),
            "The home preview clears its stale missing-source warning after the source is restored");

        var renamed=row.Asset with {Name=row.Asset.Name+" · 已更新"};
        modules.Library.Upsert(renamed);
        RefreshLibrary(renamed.Id);await LibraryPerformanceIdle();Root.UpdateLayout();
        var updated=LibraryList.Items.OfType<LibraryRow>().Single(candidate=>candidate.Asset.Id==renamed.Id);
        ReorderCheck(updated.Asset==renamed&&updated.Name==renamed.Name&&
            selected==renamed&&LibraryInfoTitle.Text==renamed.Name,
            "A genuine asset metadata change refreshes its card and selected details despite unchanged-refresh caching");
        ReorderCheck(LibraryList.Items.Count==LibraryPerformanceAssetCount&&
            LibraryList.SelectedItems.Count==1&&ReferenceEquals(LibraryList.SelectedItems[0],updated),
            "Replacing one changed row preserves the library count and resolves selection to that updated row");
    }

    private async Task CheckLibraryPerformancePosterCommitUi(LibraryRow row,string output)
    {
        string staged=Path.Combine(output,"committed-poster-stage.bmp");
        byte[] replacement;
        using(var bitmap=new System.Drawing.Bitmap(96,54))
        {
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.Clear(System.Drawing.Color.DarkMagenta);
            using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Bmp);replacement=stream.ToArray();
        }
        File.WriteAllBytes(staged,replacement);
        var previous=PosterImageCache.Load(row.PosterPath);
        var currentRows=LibraryList.Items.OfType<LibraryRow>().ToArray();
        int targetNotifications=0,otherNotifications=0,committedEvents=0;
        void Changed(object? sender,PropertyChangedEventArgs args)
        {
            if(args.PropertyName!=nameof(LibraryRow.Poster))return;
            if(ReferenceEquals(sender,row))targetNotifications++;else otherNotifications++;
        }
        void Committed(string id) {if(id==row.Asset.Id)committedEvents++;}
        foreach(var current in currentRows)current.PropertyChanged+=Changed;
        posterCatalog.PosterCommitted+=Committed;
        try
        {
            var commit=typeof(PosterCatalogService).GetMethod("CommitPoster",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                ??throw new MissingMethodException(typeof(PosterCatalogService).FullName,"CommitPoster");
            commit.Invoke(posterCatalog,[row.Asset.Id,row.Asset.Path,staged,"auto",0d]);
            await LibraryPerformanceIdle();Root.UpdateLayout();
            ReorderCheck(committedEvents==1&&targetNotifications==1&&otherNotifications==0,
                "A staged poster commit sends one commit event and refreshes only its own library row once");
            ReorderCheck(!File.Exists(staged)&&File.ReadAllBytes(modules.Library.Poster(row.Asset.Id)).SequenceEqual(replacement),
                "The staged fixture bitmap is committed to the existing asset's actual poster path");
            ReorderCheck(!ReferenceEquals(previous,PosterImageCache.Load(row.PosterPath))&&
                posterCatalog.ReadPosterChoice(row.Asset.Id) is {Mode:"auto",Seconds:0},
                "Poster commit invalidates the cached image and persists its choice metadata");
            targetNotifications=otherNotifications=0;
            RefreshLibrary();await LibraryPerformanceIdle();Root.UpdateLayout();
            ReorderCheck(targetNotifications==0&&otherNotifications==0,
                "An unchanged library refresh after a poster commit does not repeat its poster notifications");
        }
        finally
        {
            foreach(var current in currentRows)current.PropertyChanged-=Changed;
            posterCatalog.PosterCommitted-=Committed;
        }
    }

    private async Task CheckLibraryPerformancePosterCandidates(string output)
    {
        var assets=modules.Library.Assets().Take(5).ToArray();
        var sources=assets.Select(asset=>(Asset:asset,Poster:modules.Library.Poster(asset.Id))).ToArray();
        var stamps=assets.ToDictionary(asset=>PosterSelectionKey+asset.Id,asset=>PosterSourceStamp(asset.Path));
        sources[1]=(sources[1].Asset,Path.Combine(output,"missing-fixture-poster.bmp"));
        stamps[PosterSelectionKey+sources[2].Asset.Id]="outdated-fixture-stamp";
        sources[3]=(sources[3].Asset with {Path=Path.Combine(output,"unavailable-fixture-source.png")},sources[3].Poster);
        stamps.Remove(PosterSelectionKey+sources[4].Asset.Id);
        int uiThread=Environment.CurrentManagedThreadId,enumerationThread=uiThread;
        IEnumerable<(MediaAsset Asset,string Poster)> Enumerate()
        {
            enumerationThread=Environment.CurrentManagedThreadId;
            foreach(var source in sources)yield return source;
        }
        var stale=await FindStaleLibraryPostersAsync(Enumerate(),stamps,CancellationToken.None);
        ReorderCheck(enumerationThread!=uiThread,
            "Automatic poster candidate enumeration runs on a worker instead of the XAML thread");
        ReorderCheck(stale.Select(item=>item.Asset.Id).SequenceEqual(new[]{sources[1].Asset.Id,sources[2].Asset.Id,sources[4].Asset.Id}),
            "Poster scanning selects missing or outdated covers while skipping healthy covers and missing sources");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        bool enumerated=false,observedCancellation=false;
        IEnumerable<(MediaAsset Asset,string Poster)> CancelledSources()
        {
            enumerated=true;
            foreach(var source in sources)yield return source;
        }
        try {await FindStaleLibraryPostersAsync(CancelledSources(),stamps,cancelled.Token);}
        catch(OperationCanceledException) {observedCancellation=true;}
        ReorderCheck(observedCancellation&&!enumerated,
            "A cancelled poster scan stops before enumerating files and never starts a native media probe");
    }
#endif

    internal async void CheckLibraryPerformanceUi(string output)
    {
        bool passed=false;
        try
        {
            PrepareEffectReorderFixture();await ReorderSettle();ReorderCheckIsolation();
            Appearance=Appearance with {Motion=false};
            ReorderCheck(posterRepairCancellation.IsCancellationRequested,"Automatic poster repair is cancelled before any fixture media is created");
            ReorderCheck(modules.Library.Assets().Count==0,"The performance fixture starts with an isolated empty library");
            SeedLibraryPerformanceAssets(output);
            ReorderCheck(modules.Library.Assets().Count==LibraryPerformanceAssetCount,"The temporary library contains exactly 2000 self-created image assets and posters");
            LibraryList.ContainerContentChanging+=(_,_)=>libraryPerformanceContainerChanges++;
            LibraryList.SelectionChanged+=(_,_)=>libraryPerformanceSelectionChanges++;
            StartLibraryPerformanceHeartbeat();
            await MeasureLibraryPerformance("first-refresh-hidden",()=>RefreshLibrary());
            var rows=LibraryList.Items.OfType<LibraryRow>().ToArray();
            foreach(var row in rows)row.PropertyChanged+=LibraryPerformancePosterChanged;
            await MeasureLibraryPerformance("open-library",()=>Navigate("library"));
            ReorderCheck(LibraryList.Items.Count==LibraryPerformanceAssetCount&&LibraryList.ActualHeight>0,
                "Opening the real library displays all 2000 records in a measured viewport");
            if(LibraryPerformanceOptimized)ReorderCheck(LibraryPerformanceContainers()<200,
                "The 2000-item library realizes fewer than 200 card containers");
            libraryPerformanceHeartbeat!.Stop();
            await ReorderCapture(output,"library-open-2000");
            StartLibraryPerformanceHeartbeat();

            foreach(double fraction in new[]{.15,.35,.6,1d,.5,0d})
            {
                double target=LibraryPerformanceScroller().ScrollableHeight*fraction;
                await MeasureLibraryPerformance("scroll-"+fraction.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),
                    ()=>LibraryPerformanceScroller().ChangeView(null,target,null,true));
                if(LibraryPerformanceOptimized)ReorderCheck(LibraryPerformanceContainers()<200,
                    "Scrolling to "+fraction+" keeps realization below 200 card containers");
            }

            await MeasureLibraryPerformance("select-32-consecutive",()=> {
                for(int i=0;i<32;i++)LibraryList.SelectedItem=rows[i*3];
            });
            ReorderCheck(selected?.Id==rows[93].Asset.Id&&LibraryList.SelectedItems.Count==1,
                "Rapid selection leaves the last requested asset selected");
            for(int i=0;i<8;i++)
            {
                int index=125+i*173;
                await MeasureLibraryPerformance("select-individual-"+index,()=>LibraryList.SelectedItem=rows[index]);
                ReorderCheck(selected?.Id==rows[index].Asset.Id,"Individual selection updates the current asset at index "+index);
            }
            await MeasureLibraryPerformance("select-out-of-order-multiple",()=> {
                LibraryList.SelectedItems.Clear();
                foreach(int index in new[]{1208,1200,1203})LibraryList.SelectedItems.Add(rows[index]);
            });
            string[] expectedIds=new[]{1200,1203,1208}.Select(index=>rows[index].Asset.Id).ToArray();
            ReorderCheck(SelectedLibraryAssets().Select(asset=>asset.Id).SequenceEqual(expectedIds),
                "A small selection returns assets in library order rather than click order");
            string? primaryBefore=selected?.Id;
            double offsetBefore=LibraryPerformanceScroller().VerticalOffset;
            object? sourceBefore=LibraryList.ItemsSource;
            int posterBefore=libraryPerformancePosterChanges,selectionBefore=libraryPerformanceSelectionChanges;
            for(int i=0;i<6;i++)await MeasureLibraryPerformance("unchanged-refresh-"+(i+1),()=>RefreshLibrary());
            double offsetAfter=LibraryPerformanceScroller().VerticalOffset;
            ReorderCheck(SelectedLibraryAssets().Select(asset=>asset.Id).SequenceEqual(expectedIds)&&selected?.Id==primaryBefore,
                "Repeated unchanged refresh preserves the selected assets and the primary selection");
            ReorderCheck(ReferenceEquals(sourceBefore,LibraryList.ItemsSource)&&
                LibraryList.Items.OfType<LibraryRow>().Zip(rows).All(pair=>ReferenceEquals(pair.First,pair.Second)),
                "Repeated unchanged refresh reuses the ItemsSource and all 2000 row objects");
            ReorderCheck(Math.Abs(offsetAfter-offsetBefore)<=1,"Repeated unchanged refresh preserves the settled scroll position");
            if(LibraryPerformanceOptimized)
            {
                ReorderCheck(libraryPerformancePosterChanges==posterBefore,
                    "Six unchanged refreshes emit zero poster notifications across 2000 assets");
                ReorderCheck(libraryPerformanceSelectionChanges==selectionBefore,
                    "Six unchanged refreshes do not clear or rebuild the selection");
            }
            libraryPerformanceHeartbeat!.Stop();
            await ReorderCapture(output,"library-selection-after-refresh");
            foreach(var row in rows)row.PropertyChanged-=LibraryPerformancePosterChanged;
            if(LibraryPerformanceOptimized)CheckLibraryPerformancePosterCache(rows[1203]);
#if LIBRARY_PERF_OPTIMIZED
            await CheckLibraryPerformanceSourceStatusUi(rows[1203],output);
            var updatedRow=LibraryList.Items.OfType<LibraryRow>().Single(row=>row.Asset.Id==rows[1203].Asset.Id);
            await CheckLibraryPerformancePosterCommitUi(updatedRow,output);
            await CheckLibraryPerformancePosterCandidates(output);
#endif
            ReorderCheckIsolation();
            ReorderCheck(App.ReorderIsolation is {ForegroundActivated:false},
                "The entire library performance fixture remained off-screen without foreground activation");
            passed=true;
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;
        }
        finally
        {
            libraryPerformanceHeartbeat?.Stop();WriteLibraryPerformanceMeasurements();
            effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.Observe("before library performance fixture close");App.ReorderIsolation?.WriteReport();
            if(App.ReorderIsolation is {ForegroundActivated:true}) {passed=false;Environment.ExitCode=1;}
            if(passed)File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {
                passed,checks=reorderChecks,assetCount=LibraryPerformanceAssetCount,
                optimizedAssertionsEnabled=LibraryPerformanceOptimized,
                scope="Isolated off-screen library refresh, scrolling and selection using 2000 temporary self-created PNG files and valid BMP posters; no media probe, system picker, input injection, user data or desktop hosts.",
                foregroundActivated=App.ReorderIsolation!.ForegroundActivated,productionDataAccessed=false,
                desktopHostsStarted=false,nativeShellActionsExecuted=false,mediaProbeExecuted=false
            },new JsonSerializerOptions {WriteIndented=true}));
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
