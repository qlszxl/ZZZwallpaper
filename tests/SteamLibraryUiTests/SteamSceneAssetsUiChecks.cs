using System.Text;
using LumaWall.Contracts.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static void CreateSteamSceneUiFiles(string root,string image,string video)
    {
        string scene=Path.Combine(root,"1003");
        Directory.CreateDirectory(Path.Combine(scene,"images"));
        Directory.CreateDirectory(Path.Combine(scene,"videos"));
        File.Copy(image,Path.Combine(scene,"images","scene-image.png"));
        File.Copy(video,Path.Combine(scene,"videos","scene-video.mp4"));
        File.Copy(image,Path.Combine(root,"1004","web-image.png"));
        var entries=new (string Name,byte[] Bytes)[] {
            ("scene.json",Encoding.UTF8.GetBytes("{\"objects\":[]}")),
            ("preview.png",File.ReadAllBytes(image)),
            ("textures/packaged-image.png",File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","EffectPreviews","calibration.png")))
        };
        using var stream=File.Create(Path.Combine(scene,"scene.pkg"));
        using var writer=new BinaryWriter(stream,Encoding.UTF8,leaveOpen:false);
        static void Name(BinaryWriter writer,string value) {
            byte[] bytes=Encoding.UTF8.GetBytes(value);writer.Write((uint)bytes.Length);writer.Write(bytes);
        }
        Name(writer,"PKGV0005");writer.Write((uint)entries.Length);
        uint offset=0;
        foreach(var entry in entries) {
            Name(writer,entry.Name);writer.Write(offset);writer.Write((uint)entry.Bytes.Length);
            offset=checked(offset+(uint)entry.Bytes.Length);
        }
        foreach(var entry in entries)writer.Write(entry.Bytes);
    }

    private async Task<(Task Operation,ContentDialog Dialog,SteamWallpaperAsset[] Assets)> OpenSteamSceneAssets(
        SteamUiFixture fixture,string type="scene")
    {
        var browser=await OpenSteamFixture(fixture);
        var row=steamRows.Single(row=>row.Project.Type==type);
        steamImportGrid!.SelectedItems.Add(row);await ReorderSettle(80);
        ReorderCheck(browser.Browser.IsPrimaryButtonEnabled,"Selecting a "+type+" project enables browsing its source assets");
        InvokeSteamUiAction(SteamDialogAction(browser.Browser,browser.Browser.PrimaryButtonText));
        var dialog=await AwaitSteamDialog(()=>steamAssetDialog,"the "+type+" asset browser");
        for(int attempt=0;attempt<160&&steamAssetStatus?.Text.StartsWith("正在",StringComparison.Ordinal)==true;attempt++)
            await ReorderSettle(50);
        ReorderCheck(AutomationProperties.GetAutomationId(dialog)=="SteamAssetBrowser"&&
            steamAssetStatus!=null&&!steamAssetStatus.Text.StartsWith("正在",StringComparison.Ordinal),
            "The "+type+" asset scan finishes in its own selection dialog");
        ReorderCheck(!dialog.IsPrimaryButtonEnabled&&steamAssetGrid!.SelectedItems.Count==0,
            "Asset browsing requires explicit file selection before import");
        return(browser.Operation,dialog,steamAssetRows.Select(row=>row.Asset).ToArray());
    }

    private async Task SelectSteamFixtureAssets(ContentDialog dialog,Func<SteamWallpaperAsset,bool> choose)
    {
        steamAssetGrid!.SelectedItems.Clear();
        foreach(var row in steamAssetRows.Where(row=>row.CanImport&&choose(row.Asset)))
            steamAssetGrid.SelectedItems.Add(row);
        await ReorderSettle(80);
        ReorderCheck(steamAssetGrid.SelectedItems.Count>0&&dialog.IsPrimaryButtonEnabled,
            "Selecting actual source assets enables the next step");
    }

    private void CheckSteamAssetViewport(int width,ContentDialog dialog)
    {
        var viewport=SteamDialogVisuals(dialog).OfType<ScrollViewer>().Single(view=>view.Name=="ContentScrollViewer");
        var status=steamAssetStatus!;
        double top=status.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(0,0)).Y;
        double available=Math.Min(viewport.ActualHeight,viewport.ViewportHeight);
        ReorderCheck(steamAssetGrid!.ActualWidth>0&&steamAssetGrid.ActualWidth<=Root.ActualWidth&&
            status.ActualHeight>0&&top>=-1&&top+status.ActualHeight<=available+1,
            "The asset grid and count remain inside the dialog viewport at "+width);
    }

    private async Task ClearSteamFixtureReferences()
    {
        foreach(var asset in modules.Library.Assets())modules.Library.Remove(asset.Id);
        RefreshLibrary();await ReorderSettle();
    }

    private async Task CheckSteamSceneAssetWorkflowUi(SteamUiFixture fixture,string output)
    {
        string scene=Path.Combine(fixture.Root,"1003");
        string sceneImage=Path.Combine(scene,"images","scene-image.png");
        string sceneVideo=Path.Combine(scene,"videos","scene-video.mp4");
        string copyDestination=Path.Combine(output,"scene-copied-media");
        string extractionDestination=Path.Combine(output,"scene-extracted-media");
        var originals=Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories)
            .ToDictionary(path=>path,path=>File.ReadAllBytes(path),StringComparer.OrdinalIgnoreCase);
        ReorderCheck(modules.Library.Assets().Count==0,"Scene asset checks start after earlier fixture references are removed");
        string revision=store.ConfigurationRevision();long history=store.HistoryRevision;

        var inspection=await OpenSteamSceneAssets(fixture);
        ReorderCheck(inspection.Assets.Length==3&&inspection.Assets.All(asset=>asset.CanImport)&&
            inspection.Assets.Count(asset=>asset.RequiresExtraction)==1&&
            inspection.Assets.Count(asset=>asset.Kind==SteamAssetKind.LooseFile)==2,
            "A scene exposes its two loose media files and its packaged image without claiming to render the scene");
        ReorderCheck(inspection.Assets.All(asset=>!asset.RelativePath.Contains("preview",StringComparison.OrdinalIgnoreCase)&&
            !asset.RelativePath.EndsWith(".json",StringComparison.OrdinalIgnoreCase)&&!asset.RelativePath.EndsWith(".pkg",StringComparison.OrdinalIgnoreCase)),
            "Project previews, scene metadata and the package itself are never offered as wallpaper media");
        for(int attempt=0;attempt<100&&steamAssetRows.Any(row=>row.Asset.IsImage&&row.Preview==null);attempt++)
            await ReorderSettle(50);
        ReorderCheck(steamAssetRows.Where(row=>row.Asset.IsImage).All(row=>row.Preview is {PixelWidth:>0,PixelHeight:>0}),
            "Loose and package image cards show decoded content previews directly from their media bytes");
        ReorderCheck(steamAssetSearch is {IsReadOnly:false},"Asset search supports direct typing");
        steamAssetSearch!.Text="scene-image";
        for(int attempt=0;attempt<50&&steamAssetGrid!.Items.Count!=1;attempt++)await ReorderSettle(50);
        ReorderCheck(steamAssetGrid!.Items.Count==1&&((SteamAssetRow)steamAssetGrid.Items[0]).Asset.MediaPath==sceneImage,
            "Asset search matches a source filename without substituting the project preview");
        var select=SteamDialogVisuals(inspection.Dialog).OfType<Button>().Single(button=>
            AutomationProperties.GetAutomationId(button)=="SteamAssetSelectVisible");
        InvokeSteamUiAction(select);await ReorderSettle(80);
        steamAssetSearch.Text="";await ReorderSettle(100);
        ReorderCheck(steamAssetGrid.Items.Count==3&&steamAssetGrid.SelectedItems.Count==1,
            "Filtered select-all preserves exactly its selected source file after clearing the filter");
        steamAssetSearch.Text="no-such-material";
        steamAssetSearch.Text="";
        ReorderCheck(steamAssetGrid.Items.Count==3&&steamAssetGrid.SelectedItems.Count==1,
            "Clearing a pending asset search restores its rows and selection immediately");
        await ReorderSettle(250);
        ReorderCheck(steamAssetGrid.Items.Count==3&&steamAssetGrid.SelectedItems.Count==1,
            "An obsolete debounced asset search never replaces a cleared query");
        foreach(int width in new[]{1440,960}) {
            ReorderResize(width,1100);await ReorderSettle(150);
            CheckSteamAssetViewport(width,inspection.Dialog);
            await CaptureSteamDialog(output,"steam-scene-assets-"+width,inspection.Dialog);
        }
        InvokeSteamUiAction(SteamDialogAction(inspection.Dialog,inspection.Dialog.CloseButtonText));
        await inspection.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Count==0&&store.ConfigurationRevision()==revision&&store.HistoryRevision==history&&
            !Directory.Exists(copyDestination)&&!Directory.Exists(extractionDestination),
            "Cancelling source-asset selection changes neither the library, configuration, history nor destinations");

        var reference=await OpenSteamSceneAssets(fixture);
        await SelectSteamFixtureAssets(reference.Dialog,asset=>!asset.RequiresExtraction);
        InvokeSteamUiAction(SteamDialogAction(reference.Dialog,reference.Dialog.PrimaryButtonText));
        var options=await AwaitSteamDialog(()=>steamOptionsDialog,"scene original-reference confirmation");
        ReorderCheck(steamReferenceChoice!.IsChecked==true&&steamCopyChoice!.IsChecked!=true&&options.IsPrimaryButtonEnabled,
            "Loose scene media defaults to direct original-file references");
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await reference.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Select(asset=>asset.Path).Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(new[]{sceneImage,sceneVideo}.Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase)&&
            modules.Library.Assets().All(asset=>asset.Width>0&&asset.Height>0),
            "Referencing scene image/video assets records their actual paths with valid media dimensions");
        ReorderCheck(modules.Library.Assets().All(asset=>SteamSourceIdentity.IsManagedPath(asset.Path))&&
            !Directory.Exists(copyDestination)&&!Directory.Exists(extractionDestination),
            "Scene reference import creates no media copies and retains managed-original path protection");
        await ClearSteamFixtureReferences();

        var copy=await OpenSteamSceneAssets(fixture);
        await SelectSteamFixtureAssets(copy.Dialog,asset=>!asset.RequiresExtraction);
        InvokeSteamUiAction(SteamDialogAction(copy.Dialog,copy.Dialog.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"scene copy confirmation");
        steamCopyChoice!.IsChecked=true;steamDestinationEditor!.Text=copyDestination;await ReorderSettle(80);
        await CaptureSteamDialog(output,"steam-scene-copy-options",options);
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await copy.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Count==2&&modules.Library.Assets().All(asset=>
            asset.Path.StartsWith(copyDestination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
            File.ReadAllBytes(asset.Path).SequenceEqual(originals[asset.IsImage?sceneImage:sceneVideo])),
            "Scene copy import writes both real source files byte-for-byte only to the chosen directory");
        await ClearSteamFixtureReferences();

        var mixed=await OpenSteamSceneAssets(fixture);
        await SelectSteamFixtureAssets(mixed.Dialog,_=>true);
        InvokeSteamUiAction(SteamDialogAction(mixed.Dialog,mixed.Dialog.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"mixed scene reference and extraction confirmation");
        steamDestinationEditor!.Text="";await ReorderSettle(80);
        ReorderCheck(steamReferenceChoice!.IsChecked==true&&!options.IsPrimaryButtonEnabled&&steamDestinationEditor.IsLoaded&&
            steamDestinationEditor.ActualHeight>0,
            "A mixed loose/package selection still requires an explicit extraction destination in reference mode");
        steamDestinationEditor.Text=extractionDestination;await ReorderSettle(80);
        ReorderCheck(options.IsPrimaryButtonEnabled,"A valid extraction destination enables mixed import");
        await CaptureSteamDialog(output,"steam-scene-extraction-options",options);
        InvokeSteamUiAction(SteamDialogAction(options,options.CloseButtonText));await mixed.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets().Count==0&&!Directory.Exists(extractionDestination),
            "Cancelling mixed import writes no extracted files or library records");

        mixed=await OpenSteamSceneAssets(fixture);
        await SelectSteamFixtureAssets(mixed.Dialog,_=>true);
        InvokeSteamUiAction(SteamDialogAction(mixed.Dialog,mixed.Dialog.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"mixed import confirmation");
        steamDestinationEditor!.Text=extractionDestination;await ReorderSettle(80);
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await mixed.Operation;await ReorderSettle();
        var imported=modules.Library.Assets();
        var extracted=imported.Where(asset=>asset.Path.StartsWith(extractionDestination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray();
        ReorderCheck(imported.Count==3&&imported.Count(asset=>asset.Path==sceneImage||asset.Path==sceneVideo)==2&&
            extracted is [var extractedImage]&&extractedImage.IsImage&&extractedImage.Width>0&&extractedImage.Height>0&&
            File.ReadAllBytes(extractedImage.Path).SequenceEqual(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","EffectPreviews","calibration.png"))),
            "Mixed import references loose originals and extracts only the selected package image with identical pixels/file bytes");
        string extractedPath=extracted.Single().Path;
        ReorderCheck(Directory.GetFiles(extractionDestination,"*",SearchOption.AllDirectories).SequenceEqual(new[]{extractedPath}),
            "Extraction produces only one selected media file without package metadata, preview copies or partial files");
        await ClearSteamFixtureReferences();

        var package=await OpenSteamSceneAssets(fixture);
        await SelectSteamFixtureAssets(package.Dialog,asset=>asset.RequiresExtraction);
        InvokeSteamUiAction(SteamDialogAction(package.Dialog,package.Dialog.PrimaryButtonText));
        options=await AwaitSteamDialog(()=>steamOptionsDialog,"package-only destination confirmation");
        ReorderCheck(options.Title as string=="保存素材"&&steamReferenceChoice!.Visibility==Visibility.Collapsed&&
            steamCopyChoice!.Visibility==Visibility.Collapsed&&steamDestinationEditor!.ActualHeight>0,
            "A package-only selection offers a save destination without a false original-reference option");
        steamDestinationEditor!.Text=extractionDestination;await ReorderSettle(80);
        InvokeSteamUiAction(SteamDialogAction(options,options.PrimaryButtonText));await package.Operation;await ReorderSettle();
        ReorderCheck(modules.Library.Assets() is [var reused]&&reused.Path==extractedPath&&
            Directory.GetFiles(extractionDestination,"*",SearchOption.AllDirectories).SequenceEqual(new[]{extractedPath}),
            "Importing the same package asset again reuses the verified extracted file");
        await ClearSteamFixtureReferences();

        var web=await OpenSteamSceneAssets(fixture,"web");
        ReorderCheck(web.Assets is [{Kind:SteamAssetKind.LooseFile,CanImport:true}]&&
            web.Assets[0].MediaPath==Path.Combine(fixture.Root,"1004","web-image.png"),
            "A web project exposes its ordinary image while excluding its HTML entry and declared preview");
        InvokeSteamUiAction(SteamDialogAction(web.Dialog,web.Dialog.CloseButtonText));await web.Operation;await ReorderSettle();
        ReorderCheck(Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(originals.Keys.Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase)&&
            originals.All(pair=>File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)),
            "Asset inspection, previews, references, copies and extraction preserve every simulated Steam original byte");
    }
}
