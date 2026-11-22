using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<ContentDialog> AwaitPresetLocationDialog()
    {
        for(int i=0;i<160&&(presetImportLocationDialog==null||presetImportLocationEditor?.ActualWidth is not >0);i++)await ReorderSettle(50);
        await ReorderSettle();
        return presetImportLocationDialog??throw new InvalidOperationException("Import did not ask for the media destination.");
    }

    private async Task CheckPresetImportLocationUi(string output)
    {
        string source=Path.Combine(output,"import-source.png");
        File.WriteAllBytes(source,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6dSAAAAAASUVORK5CYII="));
        string zip=Path.Combine(output,"media-import.zip"),destination=Path.Combine(output,"selected-media-directory");
        var document=new PresetDocument {
            Name="选择素材目录",Selection=new() {Playing=false,Library=true,Effects=false,Widgets=false},
            Assets=[new("import-image",source,"导入图片",new FileInfo(source).Length,0,1,1,"png","sdr","fixture")]
        };
        await modules.Presets.ExportAsync(document,zip);
        int count=modules.Presets.List().Count;string revision=store.ConfigurationRevision();
        string[] resources=Directory.GetFiles(Path.Combine(store.Root,"preset-resources"));
        var operation=ImportPresetPath(zip);
        var dialog=await AwaitPresetLocationDialog();
        ReorderCheck(dialog.Title as string=="导入预设"&&dialog.PrimaryButtonText=="导入"&&dialog.CloseButtonText=="取消",
            "Importing wallpaper/library media opens a destination dialog before copying files");
        ReorderCheck(modules.Presets.List().Count==count&&!Directory.Exists(destination)&&
            Directory.GetFiles(Path.Combine(store.Root,"preset-resources")).Order().SequenceEqual(resources.Order()),
            "Waiting for a media destination creates neither a preset nor a default-drive media copy");
        await InputPresetName(presetImportLocationEditor!,"relative-folder","reject relative import folder");
        ReorderCheck(!dialog.IsPrimaryButtonEnabled,"The location dialog rejects a relative directory");
        await InputPresetName(presetImportLocationEditor!,destination,"choose import media location");
        ReorderCheck(dialog.IsPrimaryButtonEnabled,"An absolute media directory can be confirmed");
        await CapturePresetDialog(output,"presets-import-location",dialog);
        InvokePresetAction(PresetDialogAction(dialog,"取消"));await operation;await ReorderSettle();
        ReorderCheck(!presetBusy&&presetImportLocationDialog==null&&modules.Presets.List().Count==count&&!Directory.Exists(destination),
            "Cancelling media import leaves no preset or destination copy");

        operation=ImportPresetPath(zip);dialog=await AwaitPresetLocationDialog();
        await InputPresetName(presetImportLocationEditor!,destination,"confirm import destination");
        InvokePresetAction(PresetDialogAction(dialog,"导入"));await operation;await ReorderSettle();
        var imported=modules.Presets.List().Single(preset=>preset.Name=="选择素材目录");
        var read=modules.Presets.Read(imported.Id);
        ReorderCheck(read.Assets.Single().Path.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
            File.ReadAllBytes(read.Assets[0].Path).SequenceEqual(File.ReadAllBytes(source)),
            "Confirmed ZIP media is stored under the selected directory with its original bytes");
        ReorderCheck(store.ConfigurationRevision()==revision&&store.Assets().All(asset=>asset.Id!="import-image")&&
            modules.Settings.Get(PresetMediaDirectorySetting)==destination,
            "Import remembers the selected location without applying the imported library");
        ReorderCheck(Directory.GetFiles(Path.Combine(store.Root,"preset-resources")).Order().SequenceEqual(resources.Order()),
            "Importing media into the selected directory leaves the default resource directory unchanged");
        modules.Presets.Delete(imported.Id);RefreshPresetCards();
        for(int i=0;i<120&&presetCards.Children.Count!=count;i++)await ReorderSettle(50);
        ReorderCheckIsolation();
    }
}
