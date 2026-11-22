using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using LumaWall.Application;
using LumaWall.Contracts.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<object> steamUiLayoutSnapshots=[];
    private sealed record SteamUiFixture(string Root,string Video,string Image,string CopyDestination,string FileDestination);
    private sealed class SteamUiSourceFiles(string outputRoot) : ILibrarySourceFileOperations
    {
        internal readonly List<string> Revealed=[];
        internal readonly List<(string Path,string? WorkshopId)> Managed=[];
        internal readonly List<(LibrarySourceFile Source,bool Permanently)> Deleted=[];
        private string OwnedPath(string path)
        {
            string full=Path.GetFullPath(path),prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot))+Path.DirectorySeparatorChar;
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The UI fixture refuses file operations outside its own results directory.");
            return full;
        }
        public void Reveal(string path)=>Revealed.Add(OwnedPath(path));
        public void ManageSteam(string path,string? workshopId)=>Managed.Add((OwnedPath(path),workshopId));
        public void Delete(LibrarySourceFile source,bool permanently)
        {
            string full=OwnedPath(source.Path);
            if(SteamSourceIdentity.IsManagedPath(full))
                throw new InvalidOperationException("The UI fixture refuses deleting a simulated Steam-managed original.");
            LibrarySourceFiles.RequireUnchanged(source);
            Deleted.Add((source,permanently));
            // Delete only temporary fixture copies. The spy verifies the UI's
            // recycle/permanent request; it never invokes the Windows Shell.
            File.Delete(full);
        }
    }
    private static IEnumerable<FrameworkElement> SteamUiVisuals(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in SteamUiVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private IEnumerable<FrameworkElement> SteamDialogVisuals(ContentDialog dialog)=>SteamUiVisuals(dialog)
        .Concat(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(popup=>SteamUiVisuals(popup.Child))).Distinct();
    private static void InvokeSteamUiAction(Button button)=>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private Button SteamDialogAction(ContentDialog dialog,string text)=>SteamDialogVisuals(dialog).OfType<Button>()
        .Single(button=>button.Visibility==Visibility.Visible&&button.ActualWidth>0&&
            (button.Content as string==text||AutomationProperties.GetName(button)==text));
    private async Task CaptureSteamDialog(string output,string name,ContentDialog dialog)
    {
        var surface=SteamDialogVisuals(dialog).FirstOrDefault(element=>element.Name=="BackgroundElement"&&element.ActualWidth>0)??dialog;
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(surface);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,
            (await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();
    }
    private void CheckSteamBrowserViewport(string output,int width,ContentDialog dialog)
    {
        var viewport=SteamDialogVisuals(dialog).OfType<ScrollViewer>().Single(view=>view.Name=="ContentScrollViewer");
        var content=(FrameworkElement)dialog.Content;
        var status=steamScanStatus??throw new InvalidOperationException("The project count is missing.");
        double top=status.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(0,0)).Y;
        double available=Math.Min(viewport.ActualHeight,viewport.ViewportHeight);
        steamUiLayoutSnapshots.Add(new {
            windowWidth=width,rootWidth=Root.ActualWidth,rootHeight=Root.ActualHeight,
            contentWidth=content.ActualWidth,contentHeight=content.ActualHeight,
            viewportWidth=viewport.ViewportWidth,viewportHeight=viewport.ViewportHeight,
            viewportActualHeight=viewport.ActualHeight,extentHeight=viewport.ExtentHeight,
            gridWidth=steamImportGrid!.ActualWidth,gridHeight=steamImportGrid.ActualHeight,
            statusTop=top,statusHeight=status.ActualHeight,statusText=status.Text
        });
        File.WriteAllText(Path.Combine(output,"layout.json"),
            JsonSerializer.Serialize(steamUiLayoutSnapshots,new JsonSerializerOptions{WriteIndented=true}));
        ReorderCheck(status.ActualHeight>0&&top>=-1&&top+status.ActualHeight<=available+1&&
            status.Text.Contains("找到 4 项",StringComparison.Ordinal)&&status.Text.Contains("已选 2 项",StringComparison.Ordinal),
            "The project count and selected count remain visible within the dialog content viewport at "+width);
    }
    private SteamUiFixture CreateSteamUiFixture(string output)
    {
        string root=Path.Combine(output,"fake-steam","steamapps","workshop","content","431960");
        string Project(string id,string title,string type,string file) {
            string directory=Path.Combine(root,id);Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"project.json"),JsonSerializer.Serialize(new {title,type,file,preview="preview.png"}));
            return Path.Combine(directory,file);
        }
        string video=Project("1001","隔离视频项目","video","wallpaper.mp4");
        string sourceVideo=Path.Combine(AppContext.BaseDirectory,"SteamLibraryFixtures","video.mp4");
        ReorderCheck(File.Exists(sourceVideo),"A real decodable video fixture is available without using personal media");
        File.Copy(sourceVideo,video);
        string image=Project("1002","隔离图片项目","image","wallpaper.png");
        string sourceImage=Path.Combine(AppContext.BaseDirectory,"Assets","Branding","icon.png");
        ReorderCheck(File.Exists(sourceImage),"A valid bundled PNG fixture is available without using personal images");
        File.Copy(sourceImage,image);
        File.WriteAllText(Project("1003","场景项目素材","scene","scene.pkg"),"isolated scene fixture pending package");
        File.WriteAllText(Project("1004","网页项目不导入预览图","web","index.html"),"<html><body>isolated unsupported web</body></html>");
        foreach(string directory in Directory.GetDirectories(root))File.Copy(image,Path.Combine(directory,"preview.png"));
        CreateSteamSceneUiFiles(root,image,video);
        return new(root,video,image,Path.Combine(output,"copied-steam-media"),Path.Combine(output,"file-operation-target"));
    }
    internal async void CheckSteamLibraryUi(string output)
    {
        bool passed=false;
        try {
            PrepareEffectReorderFixture();await ReorderSettle();ReorderCheckIsolation();
            await CheckSteamLibraryWorkflowUi(output);
            ReorderCheckIsolation();
            ReorderCheck(App.ReorderIsolation is {ForegroundActivated:false},
                "No observation during the Steam library fixture recorded foreground activation");
            passed=true;
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;
        } finally {
            effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.Observe("before Steam library fixture close");App.ReorderIsolation?.WriteReport();
            if(App.ReorderIsolation is {ForegroundActivated:true}) {
                passed=false;
                if(!File.Exists(Path.Combine(output,"failure.txt")))
                    File.WriteAllText(Path.Combine(output,"failure.txt"),"The Steam library fixture recorded foreground activation; see activation-guard.json.");
                Environment.ExitCode=1;
            }
            if(passed)File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {
                passed,checks=reorderChecks,
                scope="Isolated off-screen Steam project scanning, selection, reference/copy import and temporary-library file operations; no system picker, global input, personal files or desktop hosts.",
                foregroundActivated=App.ReorderIsolation!.ForegroundActivated,productionDataAccessed=false,desktopHostsStarted=false,
                nativeShellActionsExecuted=false,sourceFileAdapter="Fixture-only adapter records recycle/permanent intent and deletes only owned temporary copies"
            },new JsonSerializerOptions {WriteIndented=true}));
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    private async Task<ContentDialog> AwaitSteamDialog(Func<ContentDialog?> current,string label)
    {
        for(int attempt=0;attempt<160&&current()?.Content is not FrameworkElement {IsLoaded:true,ActualWidth:>0};attempt++)await ReorderSettle(50);
        var dialog=current()??throw new InvalidOperationException("The Steam fixture did not reach "+label+".");
        await ReorderSettle(100);ReorderCheckIsolation();return dialog;
    }
    private async Task<(Task Operation,ContentDialog Browser,SteamWallpaperProject[] Projects)> OpenSteamFixture(SteamUiFixture fixture)
    {
        Task operation=OpenSteamImportAsync([fixture.Root]);
        var browser=await AwaitSteamDialog(()=>steamImportDialog,"the project browser");
        for(int attempt=0;attempt<160&&(steamScanTask==null||!steamScanTask.IsCompleted);attempt++)await ReorderSettle(50);
        if(steamScanTask!=null)await steamScanTask;
        await ReorderSettle(100);
        ReorderCheck(steamRows.Count==4&&steamRows.Count(row=>row.CanImport)==4,
            "Scanning shows two directly playable projects and two projects with browsable source assets");
        ReorderCheck(steamRows.Where(row=>row.Project.Status==SteamProjectStatus.Supported).Select(row=>row.Project.Type).Order()
            .SequenceEqual(new[]{"image","video"}),"Both supported image and video project types are available");
        ReorderCheck(steamRows.Count(row=>row.HasAssets)==2&&steamRows.Where(row=>row.HasAssets).All(row=>row.Project.Status==SteamProjectStatus.Unsupported),
            "Scene and web projects expose assets without claiming that their scenes can be played");
        ReorderCheck(steamImportGrid is {SelectionMode:ListViewSelectionMode.Multiple or ListViewSelectionMode.Extended},
            "The Steam project grid permits multiple project selection");
        ReorderCheck(steamRows.Where(row=>row.HasAssets).All(row=>
            steamImportGrid!.ContainerFromItem(row) is GridViewItem {IsEnabled:true}),
            "Scene and web project cards can be selected to inspect their assets");
        return(operation,browser,steamRows.Where(row=>row.Project.Status==SteamProjectStatus.Supported).Select(row=>row.Project).ToArray());
    }
    private async Task SelectSupportedSteamProjects(ContentDialog browser)
    {
        var select=SteamDialogVisuals(browser).OfType<Button>().Single(button=>
            AutomationProperties.GetAutomationId(button)=="SteamSelectVisible");
        steamSearchBox!.Text="隔离";await ReorderSettle(100);
        InvokeSteamUiAction(select);await ReorderSettle(100);
        ReorderCheck(steamImportGrid!.SelectedItems.Count==2&&
            steamImportGrid.SelectedItems.Cast<SteamWallpaperRow>().All(row=>row.Project.Status==SteamProjectStatus.Supported)&&browser.IsPrimaryButtonEnabled,
            "Select-visible chooses the two filtered playable projects and enables import");
        steamSearchBox.Text="";await ReorderSettle(100);
        ReorderCheck(steamImportGrid.Items.Count==4&&steamImportGrid.SelectedItems.Count==2,
            "Clearing the filter restores scene/web cards without adding them to the media selection");
        steamSearchBox.Text="no-such-project";
        steamSearchBox.Text="";
        ReorderCheck(steamImportGrid.Items.Count==4&&steamImportGrid.SelectedItems.Count==2,
            "Clearing a pending project search restores its rows and selection immediately");
        await ReorderSettle(250);
        ReorderCheck(steamImportGrid.Items.Count==4&&steamImportGrid.SelectedItems.Count==2,
            "An obsolete debounced project search never replaces a cleared query");
    }
    private async Task CheckSteamLibraryWorkflowUi(string output)
    {
        var fixture=CreateSteamUiFixture(output);Navigate("library");await ReorderSettle();
        ReorderCheck(SteamImportButton.IsEnabled&&SteamImportButton.ActualWidth>0&&
            AutomationProperties.GetAutomationId(SteamImportButton)=="SteamImportEntry",
            "The library exposes its named Wallpaper Engine import entry");
        byte[] originalVideo=await File.ReadAllBytesAsync(fixture.Video),originalImage=await File.ReadAllBytesAsync(fixture.Image);
        ReorderCheck(modules.Library.Assets().Count==0,"The Steam UI fixture starts with an isolated empty library");

        var first=await OpenSteamFixture(fixture);
        ReorderCheck(!first.Browser.IsPrimaryButtonEnabled,"The project browser requires an explicit selection");
        ReorderCheck(steamSearchBox is {IsReadOnly:false},"The project search field accepts direct editing");
        steamSearchBox!.Text="隔离图片";
        for(int attempt=0;attempt<50&&steamImportGrid!.Items.Count!=1;attempt++)await ReorderSettle(50);
        ReorderCheck(steamImportGrid!.Items.Count==1&&((SteamWallpaperRow)steamImportGrid.Items[0]).Project.Type=="image",
            "Searching filters project titles without scanning unrelated directories");
        steamSearchBox.Text="";
        for(int attempt=0;attempt<50&&steamImportGrid.Items.Count!=4;attempt++)await ReorderSettle(50);
        await SelectSupportedSteamProjects(first.Browser);
        foreach(int width in new[]{1440,960}) {
            ReorderResize(width,1100);await ReorderSettle(150);
            ReorderCheck(steamImportGrid.ActualWidth>0&&steamImportGrid.ActualWidth<=Root.ActualWidth&&
                steamImportGrid.SelectedItems.Count==2,"The real project grid fits the window and retains multi-selection at "+width);
            await CaptureSteamDialog(output,"steam-projects-"+width,first.Browser);
            CheckSteamBrowserViewport(output,width,first.Browser);
        }
        InvokeSteamUiAction(SteamDialogAction(first.Browser,first.Browser.PrimaryButtonText));
        var options=await AwaitSteamDialog(()=>steamOptionsDialog,"reference/copy confirmation");
        ReorderCheck(steamReferenceChoice!.IsChecked==true&&steamCopyChoice!.IsChecked!=true,
            "Steam import defaults to referencing original media without copying");
        ReorderCheck(modules.Library.Assets().Count==0&&!Directory.Exists(fixture.CopyDestination),
            "Waiting for import confirmation leaves the library and destination unchanged");
        await CaptureSteamDialog(output,"steam-reference-options",options);
        InvokeSteamUiAction(SteamDialogAction(options,options.CloseButtonText));await first.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Count==0&&!Directory.Exists(fixture.CopyDestination),
            "Cancelling reference import creates neither library records nor media copies");
        await ReorderCapture(output,"steam-library-entry");

        var reference=await OpenSteamFixture(fixture);await SelectSupportedSteamProjects(reference.Browser);
        InvokeSteamUiAction(SteamDialogAction(reference.Browser,reference.Browser.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"reference confirmation");
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await reference.Operation;await ReorderSettle();
        var referenced=modules.Library.Assets();
        ReorderCheck(referenced.Count==2&&referenced.Select(asset=>asset.Path).Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(new[]{fixture.Image,fixture.Video}.Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase),
            "Confirmed reference import records the real original image/video paths");
        ReorderCheck(referenced.All(asset=>asset.Width>0&&asset.Height>0&&File.Exists(modules.Library.Poster(asset.Id))),
            "The normal media probe creates metadata and posters for both real media types");
        var ids=referenced.Select(asset=>asset.Id).Order().ToArray();
        await ImportSteamProjectsAsync(reference.Projects,null);await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Select(asset=>asset.Id).Order().SequenceEqual(ids),
            "Repeating reference import reuses existing library records");

        await CheckSteamManagedFileProtectionUi(fixture);
        // Removing isolated library references prepares the separate copy-mode
        // case; the protected Steam original files remain untouched.
        foreach(var asset in modules.Library.Assets())modules.Library.Remove(asset.Id);
        RefreshLibrary();await ReorderSettle();
        var copy=await OpenSteamFixture(fixture);await SelectSupportedSteamProjects(copy.Browser);
        InvokeSteamUiAction(SteamDialogAction(copy.Browser,copy.Browser.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"copy confirmation");
        steamCopyChoice!.IsChecked=true;await ReorderSettle(80);
        ReorderCheck(steamDestinationEditor is {IsReadOnly:false},"The copy destination supports direct path editing");
        steamDestinationEditor!.Text="relative-directory";await ReorderSettle(80);
        ReorderCheck(!options.IsPrimaryButtonEnabled,"Copy import rejects a relative destination directory");
        steamDestinationEditor.Text=fixture.CopyDestination;await ReorderSettle(80);
        ReorderCheck(options.IsPrimaryButtonEnabled,"An absolute destination enables copy import confirmation");
        await CaptureSteamDialog(output,"steam-copy-options",options);
        InvokeSteamUiAction(SteamDialogAction(options,options.CloseButtonText));await copy.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Count==0&&!Directory.Exists(fixture.CopyDestination),
            "Cancelling copy import creates neither library records nor destination files");

        copy=await OpenSteamFixture(fixture);await SelectSupportedSteamProjects(copy.Browser);
        InvokeSteamUiAction(SteamDialogAction(copy.Browser,copy.Browser.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"copy confirmation");
        steamCopyChoice.IsChecked=true;steamDestinationEditor.Text=fixture.CopyDestination;await ReorderSettle(80);
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await copy.Operation;await ReorderSettle();
        var copied=modules.Library.Assets();
        ReorderCheck(copied.Count==2&&copied.All(asset=>asset.Path.StartsWith(fixture.CopyDestination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)),
            "Confirmed copy import stores both media files under the chosen destination");
        ReorderCheck(copied.All(asset=>File.ReadAllBytes(asset.Path).SequenceEqual(asset.IsImage?originalImage:originalVideo)),
            "Copied image and video bytes exactly match their source files");
        string[] copiedFiles=Directory.GetFiles(fixture.CopyDestination).Order().ToArray();
        ids=copied.Select(asset=>asset.Id).Order().ToArray();
        await ImportSteamProjectsAsync(copy.Projects,fixture.CopyDestination);await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Select(asset=>asset.Id).Order().SequenceEqual(ids)&&
            Directory.GetFiles(fixture.CopyDestination).Order().SequenceEqual(copiedFiles),
            "Repeating copy import reuses library entries and existing copied media");
        await CheckSteamLibraryFileOperationsUi(fixture,output);
        await CheckSteamSceneAssetWorkflowUi(fixture,output);
        ReorderCheck(File.ReadAllBytes(fixture.Video).SequenceEqual(originalVideo)&&File.ReadAllBytes(fixture.Image).SequenceEqual(originalImage),
            "All Steam UI operations preserve both managed original media files");
        ReorderCheck(NativePlayback.ActiveProbes.Count==0,"Every media metadata probe has exited before the fixture finishes");
    }
    private async Task SelectSteamLibraryAsset(string id)
    {
        Navigate("library");RefreshLibrary(id);await ReorderSettle();
        ReorderCheck(SelectedLibraryAssets() is [var asset]&&asset.Id==id,
            "The file-operation fixture selects exactly its intended library asset");
        ReorderCheckIsolation();
    }
    private async Task CheckSteamManagedFileProtectionUi(SteamUiFixture fixture)
    {
        string output=EffectReorderRegressionProgram.Output;
        var original=librarySourceFiles;var files=new SteamUiSourceFiles(output);librarySourceFiles=files;
        try {
            var asset=modules.Library.Assets().Single(asset=>asset.Path==fixture.Video);
            await SelectSteamLibraryAsset(asset.Id);
            ReorderCheck(LibraryDeleteSourceButton.IsEnabled&&LibraryDeleteSourceButton.Content as string=="在 Steam 中管理",
                "A referenced Steam original exposes management instead of a source deletion action");
            string revision=store.ConfigurationRevision();long history=store.HistoryRevision;
            var ids=modules.Library.Assets().Select(item=>item.Id).Order().ToArray();
            foreach(string action in new[]{"取消","在 Steam 中管理","打开文件夹"}) {
                Task protection=DeleteSelectedLibrarySourceAsync();
                var dialog=await AwaitSteamDialog(()=>libraryFileDialog,"Steam-managed source protection");
                ReorderCheck(AutomationProperties.GetAutomationId(dialog)=="LibrarySteamManagedDialog"&&
                    dialog.Title as string=="由 Steam 管理的原文件"&&libraryPermanentDelete==null&&
                    dialog.PrimaryButtonText=="在 Steam 中管理"&&dialog.SecondaryButtonText=="打开文件夹",
                    "Managed originals show only safe Steam/folder actions and no permanent-deletion checkbox");
                ReorderCheck(SteamDialogVisuals(dialog).OfType<TextBlock>().Any(block=>
                    block.Text.Contains(asset.Path,StringComparison.Ordinal)),
                    "The Steam protection dialog identifies the exact managed original path");
                if(action=="取消")await CaptureSteamDialog(output,"library-steam-protection",dialog);
                InvokeSteamUiAction(SteamDialogAction(dialog,action));await protection;await ReorderSettle();
                ReorderCheck(files.Deleted.Count==0&&File.Exists(fixture.Video)&&File.Exists(fixture.Image)&&
                    modules.Library.Assets().Select(item=>item.Id).Order().SequenceEqual(ids)&&
                    store.ConfigurationRevision()==revision&&store.HistoryRevision==history,
                    "The managed-source action "+action+" preserves original files, library entries and history");
            }
            ReorderCheck(files.Managed is [{WorkshopId:"1001"}]&&files.Managed[0].Path==fixture.Video&&
                files.Revealed.SequenceEqual(new[]{fixture.Video}),
                "Steam and folder actions target the selected original through the isolated adapter only");
        } finally {librarySourceFiles=original;}
    }
    private async Task CheckSteamLibraryFileOperationsUi(SteamUiFixture fixture,string output)
    {
        var original=librarySourceFiles;var files=new SteamUiSourceFiles(output);librarySourceFiles=files;
        try {
            var assets=modules.Library.Assets();var image=assets.Single(asset=>asset.IsImage);var video=assets.Single(asset=>!asset.IsImage);
            await SelectSteamLibraryAsset(image.Id);
            ReorderCheck(LibraryRevealSourceButton.IsEnabled&&LibraryDeleteSourceButton.IsEnabled&&
                AutomationProperties.GetAutomationId(LibraryRevealSourceButton)=="LibraryRevealSource"&&
                AutomationProperties.GetAutomationId(LibraryDeleteSourceButton)=="LibraryDeleteSource",
                "A selected copied material exposes source reveal and source deletion actions");
            InvokeSteamUiAction(LibraryRevealSourceButton);await ReorderSettle(80);
            ReorderCheck(files.Revealed.SequenceEqual(new[]{image.Path}),
                "The reveal action targets only the selected fixture source without launching Explorer");
            string revision=store.ConfigurationRevision();long history=store.HistoryRevision;
            Task deletion=DeleteSelectedLibrarySourceAsync();
            var dialog=await AwaitSteamDialog(()=>libraryFileDialog,"source deletion confirmation");
            ReorderCheck(libraryPermanentDelete is {IsChecked:false}&&dialog.PrimaryButtonText=="移到回收站",
                "Source deletion defaults to the recycle-bin request with permanent deletion unselected");
            string text=string.Join("\n",SteamDialogVisuals(dialog).Select(element=>element switch {TextBlock block=>block.Text,TextBox box=>box.Text,_=>""}));
            ReorderCheck(text.Contains(image.Path,StringComparison.Ordinal)&&text.Contains(LibraryPresentation.FileSize(image.Bytes),StringComparison.Ordinal),
                "Deletion confirmation identifies the actual selected source path and file size");
            await CaptureSteamDialog(output,"library-delete-cancel",dialog);
            InvokeSteamUiAction(SteamDialogAction(dialog,dialog.CloseButtonText));await deletion;await ReorderSettle();
            ReorderCheck(File.Exists(image.Path)&&modules.Library.Assets().Any(asset=>asset.Id==image.Id)&&files.Deleted.Count==0&&
                store.ConfigurationRevision()==revision&&store.HistoryRevision==history,
                "Cancelling source deletion preserves the file, library record, configuration and undo history");

            deletion=DeleteSelectedLibrarySourceAsync();dialog=await AwaitSteamDialog(()=>libraryFileDialog,"recycle request confirmation");
            InvokeSteamUiAction(SteamDialogAction(dialog,dialog.PrimaryButtonText));await deletion;await ReorderSettle();
            ReorderCheck(files.Deleted is [{Permanently:false}]&&files.Deleted[0].Source.Path==image.Path&&
                !File.Exists(image.Path)&&modules.Library.Assets().All(asset=>asset.Id!=image.Id)&&File.Exists(video.Path),
                "Confirming the default request deletes only the temporary selected copy and removes its library reference");

            await SelectSteamLibraryAsset(video.Id);
            deletion=DeleteSelectedLibrarySourceAsync();dialog=await AwaitSteamDialog(()=>libraryFileDialog,"permanent request confirmation");
            ReorderCheck(libraryPermanentDelete is {IsChecked:false},
                "Each new source deletion confirmation starts without permanent deletion selected");
            libraryPermanentDelete!.IsChecked=true;await ReorderSettle(80);
            ReorderCheck(dialog.PrimaryButtonText.Contains("永久",StringComparison.Ordinal),
                "Selecting permanent deletion changes the confirmation action to its destructive intent");
            await CaptureSteamDialog(output,"library-delete-permanent",dialog);
            InvokeSteamUiAction(SteamDialogAction(dialog,dialog.PrimaryButtonText));await deletion;await ReorderSettle();
            ReorderCheck(files.Deleted.Count==2&&files.Deleted[1].Permanently&&files.Deleted[1].Source.Path==video.Path&&
                !File.Exists(video.Path)&&modules.Library.Assets().Count==0,
                "Explicit permanent confirmation is passed to the fixture adapter and removes only the second temporary copy");
            ReorderCheck(files.Managed.Count==0,"Deleting ordinary copied media never attempts to launch Steam management");
            await ReorderCapture(output,"library-after-source-deletions");
        } finally {librarySourceFiles=original;}
    }
}
