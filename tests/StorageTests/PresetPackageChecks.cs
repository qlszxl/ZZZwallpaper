using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Application.Presets;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using LumaWall.Storage;
using Microsoft.Data.Sqlite;

internal static class PresetPackageChecks
{
    private static readonly PresetSelection All = new() { Playing=true, Library=true, Effects=true, Widgets=true, IncludeWidgetPrivateData=true };
    private static readonly PresetSelection None = new() { Playing=false, Library=false, Effects=false, Widgets=false };
    private static readonly string PixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6dSAAAAAASUVORK5CYII=";

    internal static void Run(string root, Action<bool,string> check) => RunAsync(Path.Combine(root,"presets"),check).GetAwaiter().GetResult();

    private static async Task RunAsync(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        string senderRoot=Path.Combine(root,"sender"), recipientRoot=Path.Combine(root,"recipient");
        string exported=Path.Combine(root,"shared.zip");
        using var sender=new LibraryStore(senderRoot);
        Seed(sender,"source-screen","source");
        var sourceWidgets=WidgetConfiguration.FromJson(sender.GetSetting("widgets.v1"));
        var sourceEffects=sender.ScreenProfile("source-screen")!.Effects;
        var service=new PresetService(sender,senderRoot);

        var defaults=service.Capture("默认分享",["source-screen"],new());
        check(defaults.Selection.Playing&&!defaults.Selection.Library&&defaults.Selection.Effects&&defaults.Selection.Widgets&&!defaults.Selection.IncludeWidgetPrivateData,
            "Preset defaults select playing, effects and widgets, with the whole library and private component data opt-in");
        check(defaults.Assets.Select(a=>a.Id).Order().SequenceEqual(new[]{"source-image","source-video"}),
            "Playing includes the active media and playlist media while omitting unrelated library assets");
        check(defaults.Volumes["source-screen"]==37&&!defaults.SoundEnabled["source-screen"],
            "Playing capture retains the remembered volume independently of the current mute state");
        string privateJson=defaults.Widgets!.ToJson();
        check(!privateJson.Contains("private-note")&&!privateJson.Contains("private.example")&&!privateJson.Contains("private-command")&&!privateJson.Contains("private-folder"),
            "Default component privacy removes personal text, search URLs, shortcuts, commands and folder targets");
        var privateText=defaults.Widgets.Widgets.Single(w=>w.Kind==WidgetKind.Text);
        check(privateText.X==.23&&privateText.Y==.37&&privateText.Layer==7&&privateText.Visual.FontSize==39&&defaults.Widgets.Widgets.Single(w=>w.Kind==WidgetKind.Sticker).Visual.Image==PixelPng,
            "Privacy filtering retains component positions, layering, styling and sticker pixels");
        check(sourceWidgets.ToJson()==WidgetConfiguration.FromJson(sender.GetSetting("widgets.v1")).ToJson(),
            "Capturing a privacy-filtered preset leaves the current personal configuration unchanged");

        var full=service.Capture("全部设置",["source-screen"],All);
        check(full.Widgets!.Widgets.Length==sourceWidgets.Widgets.Length&&full.Widgets.Widgets.Single(w=>w.Kind==WidgetKind.Text).Visual.Text=="private-note"&&full.Widgets.Shortcuts.Any(c=>c.Url.Contains("private-command"))&&full.Assets.Length==3,
            "Explicit private-data and library selection capture the complete component configuration and all media");
        var saved=await service.SaveAsync(full);
        check(service.List().Count==1&&service.Read(saved.Id).Name=="全部设置", "Saving creates a persistent named local preset");
        await service.ExportAsync(saved.Id,exported);
        check(File.Exists(exported)&&new FileInfo(exported).Length>0,"Export writes one ZIP package");
        using(var archive=ZipFile.OpenRead(exported)) {
            check(archive.Entries.Count(e=>e.FullName=="manifest.json")==1&&archive.Entries.Any(e=>e.FullName.StartsWith("resources/",StringComparison.Ordinal)),
                "The exported ZIP contains one manifest and bundled resources");
            using var reader=new StreamReader(archive.GetEntry("manifest.json")!.Open());string manifest=reader.ReadToEnd();
            check(!manifest.Contains(senderRoot,StringComparison.OrdinalIgnoreCase)&&!manifest.Contains(senderRoot.Replace('\\','/'),StringComparison.OrdinalIgnoreCase),
                "The portable manifest contains no sender-specific absolute media paths");
        }
        var reopenedService=new PresetService(sender,senderRoot);
        check(reopenedService.List().Single().Id==saved.Id&&reopenedService.Read(saved.Id).Widgets!.ToJson()==full.Widgets.ToJson(),
            "A newly opened preset service restores the saved list and complete settings");

        using var recipient=new LibraryStore(recipientRoot);
        Seed(recipient,"target-screen","target");
        recipient.HistoryEnabled=true;
        var receiver=new PresetService(recipient,recipientRoot);
        string before=recipient.ConfigurationRevision();
        var imported=await receiver.ImportAsync(exported);
        var portable=receiver.Read(imported.Id);
        check(recipient.ConfigurationRevision()==before&&recipient.UndoLabel==null,
            "Importing another person's package only saves a preset and does not apply it to the running configuration");
        check(portable.Assets.All(a=>File.Exists(a.Path)&&IsWithin(a.Path,recipientRoot)&&!IsWithin(a.Path,senderRoot)),
            "Imported media is independently owned under the recipient directory");
        check(portable.Assets.All(a=>File.ReadAllBytes(a.Path).SequenceEqual(File.ReadAllBytes(full.Assets.Single(s=>s.Name==a.Name).Path))),
            "Video and image resources retain their exact bytes across different data directories");
        check(portable.ScreenProfiles["source-screen"].Effects.ToJson()==sourceEffects.ToJson(),
            "Preset ZIP export/import preserves effect parameters, the linked image mask and its pixels");

        var map=new Dictionary<string,string> { ["source-screen"]="target-screen" };
        string oldWidgets=recipient.GetSetting("widgets.v1")!;
        var oldBinding=recipient.Bindings().Single();
        string oldPlayback=JsonSerializer.Serialize(recipient.Playback("target-screen"));
        string oldProfile=recipient.ScreenProfile("target-screen")!.Effects.ToJson();
        FillMode oldFill=recipient.ScreenProfile("target-screen")!.Fill;
        string[] oldAssets=recipient.Assets().Select(a=>a.Id).Order().ToArray();
        string[] PlaybackNames()=>recipient.Playback("target-screen").AssetIds.Select(id=>recipient.Assets().Single(a=>a.Id==id).Name).ToArray();
        string PlayingName()=>recipient.Assets().Single(a=>a.Id==recipient.Bindings().Single().AssetId).Name;

        receiver.Apply(imported.Id,map,None with {Effects=true});
        check(recipient.ScreenProfile("target-screen")!.Effects.ToJson()==sourceEffects.ToJson(),"Effects-only application replaces the selected display's complete effects");
        check(recipient.GetSetting("widgets.v1")==oldWidgets&&recipient.Bindings().Single().AssetId==oldBinding.AssetId&&JsonSerializer.Serialize(recipient.Playback("target-screen"))==oldPlayback&&recipient.Assets().Select(a=>a.Id).Order().SequenceEqual(oldAssets),
            "Effects-only application preserves components, current media, playback queue and library");
        recipient.ReplayUndo(false);
        check(recipient.ScreenProfile("target-screen")!.Effects.ToJson()==oldProfile,"One undo restores an effects-only preset application");

        receiver.Apply(imported.Id,map,None with {Widgets=true,IncludeWidgetPrivateData=true});
        var appliedWidgets=WidgetConfiguration.FromJson(recipient.GetSetting("widgets.v1"));
        check(appliedWidgets.Widgets.All(w=>w.Screen=="target-screen")&&appliedWidgets.Widgets.Single(w=>w.Kind==WidgetKind.Text).Visual.Text=="private-note",
            "Component application remaps display identities and honors explicitly included private data");
        check(recipient.ScreenProfile("target-screen")!.Effects.ToJson()==oldProfile&&recipient.Bindings().Single().AssetId==oldBinding.AssetId&&JsonSerializer.Serialize(recipient.Playback("target-screen"))==oldPlayback,
            "Component-only application preserves effects and playback");
        recipient.ReplayUndo(false);
        check(recipient.GetSetting("widgets.v1")==oldWidgets,"One undo restores components replaced by a preset");

        receiver.Apply(imported.Id,map,None with {Library=true});
        check(recipient.Assets().Count==oldAssets.Length+3&&recipient.GetSetting("widgets.v1")==oldWidgets&&recipient.ScreenProfile("target-screen")!.Effects.ToJson()==oldProfile&&recipient.Bindings().Single().AssetId==oldBinding.AssetId,
            "Library-only application adds packaged media while preserving playback, effects and components");
        recipient.ReplayUndo(false);
        check(recipient.Assets().Select(a=>a.Id).Order().SequenceEqual(oldAssets),"Undo removes only preset-added library references");

        receiver.Apply(imported.Id,map,None with {Playing=true});
        check(PlayingName()=="source video"&&PlaybackNames().SequenceEqual(new[]{"source video","source image"})&&recipient.Playback("target-screen").Mode==PlaybackMode.Sequential,
            "Playing-only application restores the current wallpaper and ordered playback settings");
        check(recipient.Bindings().Single().Fill==FillMode.Contain&&recipient.ScreenProfile("target-screen")!.Fill==FillMode.Contain&&recipient.WallpaperVolume("target-screen")==37&&recipient.GetSetting("videoSound:target-screen")=="0",
            "Playing-only application restores fill mode, muted state and the remembered audible volume");
        check(recipient.GetSetting("widgets.v1")==oldWidgets&&recipient.ScreenProfile("target-screen")!.Effects.ToJson()==oldProfile&&!recipient.Assets().Any(a=>a.Name=="source unused"),
            "Playing-only application preserves effects and components and adds only playback-required media");
        recipient.ReplayUndo(false);
        check(recipient.Bindings().Single().AssetId==oldBinding.AssetId&&JsonSerializer.Serialize(recipient.Playback("target-screen"))==oldPlayback&&recipient.Assets().Select(a=>a.Id).Order().SequenceEqual(oldAssets),
            "One undo restores wallpaper, playlist and prior media references together");
        check(recipient.ScreenProfile("target-screen")!.Fill==oldFill&&recipient.Bindings().Single().Fill==oldBinding.Fill&&recipient.WallpaperVolume("target-screen")==61&&recipient.GetSetting("videoSound:target-screen")=="1",
            "Undo restores both saved/runtime fill modes and the previous unmuted volume");

        long history=recipient.HistoryRevision;
        receiver.Apply(imported.Id,map,All);
        check(recipient.HistoryRevision==history+1&&recipient.ScreenProfile("target-screen")!.Effects.ToJson()==sourceEffects.ToJson()&&PlayingName()=="source video"&&recipient.Assets().Count==6,
            "Applying all four categories commits one coherent undoable change");
        recipient.ReplayUndo(false);
        check(recipient.GetSetting("widgets.v1")==oldWidgets&&recipient.ScreenProfile("target-screen")!.Effects.ToJson()==oldProfile&&recipient.Bindings().Single().AssetId==oldBinding.AssetId&&recipient.Assets().Select(a=>a.Id).Order().SequenceEqual(oldAssets),
            "One undo restores all four categories without losing unrelated media");
        recipient.ReplayUndo(true);
        check(PlayingName()=="source video"&&WidgetConfiguration.FromJson(recipient.GetSetting("widgets.v1")).Widgets.Single(w=>w.Kind==WidgetKind.Text).Visual.Text=="private-note",
            "Redo reapplies the full preset and its private component configuration");
        recipient.ReplayUndo(false);

        before=recipient.ConfigurationRevision();history=recipient.HistoryRevision;
        bool conflict=false;try { receiver.Apply(imported.Id,map,All,"stale-revision"); } catch(ConfigurationConflictException) { conflict=true; }
        check(conflict&&recipient.ConfigurationRevision()==before&&recipient.HistoryRevision==history,
            "A stale preset application is rejected before any database or history changes");

        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(recipientRoot,"library.db"),Pooling=false}.ToString())) {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="CREATE TRIGGER fail_preset_widgets BEFORE INSERT ON settings WHEN NEW.key='widgets.v1' BEGIN SELECT RAISE(ABORT,'injected preset failure'); END";command.ExecuteNonQuery();
            bool rolledBack=false;try {receiver.Apply(imported.Id,map,All);} catch(SqliteException) {rolledBack=true;}
            command.CommandText="DROP TRIGGER fail_preset_widgets";command.ExecuteNonQuery();
            check(rolledBack&&recipient.ConfigurationRevision()==before&&recipient.HistoryRevision==history,
                "A late SQLite failure rolls back media, effects, playback, components and history as one transaction");
        }

        await CheckInvalidPackages(root,receiver,recipient,exported,check);
        await CheckOldEffectSchema(root,exported,check);
        await CheckScreenSemantics(root,check);
        await CheckRenames(root,check);
    }

    private static async Task CheckRenames(string root,Action<bool,string> check)
    {
        string data=Path.Combine(root,"rename");
        using var store=new LibraryStore(data);Seed(store,"A","rename");
        var service=new PresetService(store,data);
        // Keep the v0.1.11.36 three-file format explicit: new local presets no
        // longer carry a duplicate ZIP, but legacy rename/recovery still must work.
        var saved=await SaveLegacyFixture(service,service.Capture("原始名称",["A"],All),data);
        string directory=Path.Combine(data,"presets",saved.Id),resourceRoot=Path.Combine(data,"preset-resources");
        static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        static Dictionary<string,string> Files(string path)=>Directory.GetFiles(path).ToDictionary(p=>Path.GetFileName(p),p=>Hash(p),StringComparer.Ordinal);
        static bool Same(Dictionary<string,string> first,Dictionary<string,string> second)=>first.Count==second.Count&&first.All(p=>second.TryGetValue(p.Key,out var hash)&&hash==p.Value);
        static void CopyPreset(string source,string target) {Directory.CreateDirectory(target);foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)));}
        string previous=Path.Combine(root,"rename-before");CopyPreset(directory,previous);
        var document=service.Read(saved.Id);var resourceHashes=Files(resourceRoot);
        string configuration=store.ConfigurationRevision();long history=store.HistoryRevision;
        var renamed=await service.RenameAsync(saved.Id,"  新名称 · 桌面  ");
        var reopened=new PresetService(store,data);
        var updated=reopened.Read(saved.Id);
        check(renamed.Id==saved.Id&&renamed.CreatedUtc==saved.CreatedUtc&&renamed.Name=="新名称 · 桌面"&&reopened.List().Single().Name==renamed.Name,
            "Renaming persists a trimmed name while retaining the preset identity and creation time across reopen");
        check(JsonSerializer.Serialize(updated with {Name=document.Name})==JsonSerializer.Serialize(document)&&Same(resourceHashes,Files(resourceRoot)),
            "Renaming preserves every playback, media, effect, widget and privacy field and all owned resource bytes");
        check(store.ConfigurationRevision()==configuration&&store.HistoryRevision==history,
            "Renaming a saved preset leaves the active configuration and undo history unchanged");
        using(var zip=ZipFile.OpenRead(Path.Combine(directory,"preset.zip"))) {
            using var reader=new StreamReader(zip.GetEntry("manifest.json")!.Open());
            check(reader.ReadToEnd()==File.ReadAllText(Path.Combine(directory,"manifest.json"))&&renamed.ZipBytes==new FileInfo(Path.Combine(directory,"preset.zip")).Length,
                "Rename commits matching local and ZIP manifests together with the updated archive size");
        }
        string export=Path.Combine(root,"renamed.zip");await reopened.ExportAsync(saved.Id,export);
        using(var recipient=new LibraryStore(Path.Combine(root,"rename-recipient"))) {
            var receiver=new PresetService(recipient,recipient.Root);var imported=await receiver.ImportAsync(export);
            var portable=receiver.Read(imported.Id);
            check(imported.Name==renamed.Name&&portable.Name==renamed.Name&&portable.CreatedUtc==saved.CreatedUtc&&portable.Widgets!.ToJson()==document.Widgets!.ToJson(),
                "Exporting and reimporting a renamed preset retains its new name, creation time and private component content");
            check(portable.Assets.All(a=>File.ReadAllBytes(a.Path).SequenceEqual(File.ReadAllBytes(updated.Assets.Single(b=>b.Id==a.Id).Path))),
                "A rewritten ZIP preserves all streamed wallpaper resource bytes");
        }
        var complete=Files(directory);
        await reopened.RenameAsync(saved.Id,renamed.Name);
        check(Same(complete,Files(directory)),"Renaming to the existing name is a byte-preserving no-op");
        using(var canceled=new CancellationTokenSource()) {
            canceled.Cancel();bool rejected=false;
            try {await reopened.RenameAsync(saved.Id,"取消的名称",canceled.Token);}catch(OperationCanceledException) {rejected=true;}
            check(rejected&&Same(complete,Files(directory)),"A canceled rename preserves the complete original preset");
        }
        bool invalid=true;
        foreach(string name in new[]{"","   ",new string('x',81),"bad\nname"}) {
            try {await reopened.RenameAsync(saved.Id,name);invalid=false;}catch(InvalidDataException) {}
        }
        foreach(string id in new[]{"../outside",saved.Id+"/child","not-an-id"}) {
            try {await reopened.RenameAsync(id,"合法名称");invalid=false;}catch(ArgumentException) {}
        }
        check(invalid&&Same(complete,Files(directory)),"Rename rejects invalid names and traversal identities without changing any files");
        bool locked=false;
        using(var file=new FileStream(Path.Combine(directory,"preset.zip"),FileMode.Open,FileAccess.Read,FileShare.None)) {
            try {await reopened.RenameAsync(saved.Id,"失败的名称");}catch(IOException) {locked=true;}
        }
        check(locked&&Same(complete,Files(directory))&&reopened.Read(saved.Id).Name==renamed.Name,
            "A failed ZIP read leaves the old preset usable and all three persistent files unchanged");
        check(!Directory.EnumerateDirectories(Path.Combine(data,"preset-staging")).Any(),"Canceled and failed renames leave no incomplete unjournaled staging directories");

        // Inspection intentionally skips only large media hashes, never structural or image validation.
        string media=updated.Assets[0].Path;byte[] original=File.ReadAllBytes(media),changed=original.ToArray();changed[^1]^=0x7f;File.WriteAllBytes(media,changed);
        bool fullReadRejected=false;try {reopened.Read(saved.Id);}catch(InvalidDataException) {fullReadRejected=true;}
        check(reopened.Inspect(saved.Id).Name==renamed.Name&&fullReadRejected,
            "Details inspection avoids rehashing large media while the applying Read path still rejects changed media bytes");
        File.WriteAllBytes(media,original);
        File.WriteAllBytes(media,original.Concat(new byte[]{0}).ToArray());
        bool sizeRejected=false;try {reopened.Inspect(saved.Id);}catch(InvalidDataException) {sizeRejected=true;}
        File.WriteAllBytes(media,original);
        check(sizeRejected,"Details inspection still rejects media whose size no longer matches the manifest");
        string embedded=Directory.GetFiles(resourceRoot,"*.bin")[0];byte[] embeddedBytes=File.ReadAllBytes(embedded);
        changed=embeddedBytes.ToArray();changed[^1]^=1;File.WriteAllBytes(embedded,changed);
        bool imageRejected=false;try {reopened.Inspect(saved.Id);}catch(InvalidDataException) {imageRejected=true;}
        File.WriteAllBytes(embedded,embeddedBytes);
        check(imageRejected,"Details inspection retains SHA verification for embedded images and effect resources");
        using(var canceled=new CancellationTokenSource()) {
            canceled.Cancel();bool rejected=false;try {reopened.Inspect(saved.Id,canceled.Token);}catch(OperationCanceledException) {rejected=true;}
            check(rejected,"Details inspection honors cancellation before reading the manifest");
        }

        string Stage() {string path=Path.Combine(data,"preset-staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        void Journal(string staging,string? id=null)=>File.WriteAllText(Path.Combine(staging,"rename.json"),JsonSerializer.Serialize(new {Version=1,Id=id??saved.Id}));
        string pending=Stage();CopyPreset(previous,Path.Combine(pending,"candidate"));Journal(pending);
        var recovered=new PresetService(store,data);
        check(recovered.Read(saved.Id).Name==renamed.Name&&!Directory.Exists(pending)&&Same(complete,Files(directory)),
            "Recovery before the first directory move preserves the current preset and discards only the staged candidate");
        pending=Stage();CopyPreset(previous,Path.Combine(pending,"candidate"));Journal(pending);Directory.Move(directory,Path.Combine(pending,"original"));
        recovered=new PresetService(store,data);
        check(recovered.Read(saved.Id).Name==renamed.Name&&!Directory.Exists(pending)&&Same(complete,Files(directory)),
            "Recovery after moving the original restores all three original files without installing an incomplete candidate");
        pending=Stage();CopyPreset(previous,Path.Combine(pending,"original"));Journal(pending);
        recovered=new PresetService(store,data);
        check(recovered.Read(saved.Id).Name==renamed.Name&&!Directory.Exists(pending)&&Same(complete,Files(directory)),
            "Recovery after a completed promotion retains the new preset instead of overwriting it with the backup");
        pending=Stage();CopyPreset(previous,Path.Combine(pending,"original"));Journal(pending);
        string manifestPath=Path.Combine(directory,"manifest.json");byte[] manifestBytes=File.ReadAllBytes(manifestPath);File.WriteAllText(manifestPath,"{}");
        bool protectedBackup=false;try {new PresetService(store,data);}catch(Exception error) when(error is InvalidDataException or JsonException) {protectedBackup=true;}
        check(protectedBackup&&Directory.Exists(Path.Combine(pending,"original"))&&File.Exists(Path.Combine(pending,"rename.json")),
            "Recovery preserves the backup and journal when the canonical preset cannot be validated");
        File.WriteAllBytes(manifestPath,manifestBytes);recovered=new PresetService(store,data);
        pending=Stage();CopyPreset(previous,Path.Combine(pending,"original"));Journal(pending,"../outside");
        bool rejectedJournal=false;try {new PresetService(store,data);}catch(InvalidDataException) {rejectedJournal=true;}
        check(rejectedJournal&&Directory.Exists(Path.Combine(pending,"original"))&&Same(complete,Files(directory)),
            "Recovery rejects a tampered traversal identity while preserving canonical and backup data");
        Journal(pending);recovered=new PresetService(store,data);
        check(recovered.List().Single().Id==saved.Id&&Same(resourceHashes,Files(resourceRoot)),
            "Repeated rename recovery leaves one stable preset identity and never deletes shared resources");
        pending=Stage();CopyPreset(previous,Path.Combine(pending,"candidate"));
        File.WriteAllText(Path.Combine(pending,"rename.pending"),"{\"Version\":");
        recovered=new PresetService(store,data);
        check(recovered.Read(saved.Id).Name==renamed.Name&&Same(complete,Files(directory)),
            "A crash during unpublished journal writing cannot block access to the untouched canonical preset");
        foreach(string file in Directory.GetFiles(Path.Combine(pending,"candidate")))File.Delete(file);
        Directory.Delete(Path.Combine(pending,"candidate"));File.Delete(Path.Combine(pending,"rename.pending"));Directory.Delete(pending);
        var padded=await recovered.SaveAsync(document with {Name="  已接受的空格名称  "});
        await recovered.RenameAsync(padded.Id,"规范名称");
        check(recovered.Read(padded.Id).Name=="规范名称",
            "Presets previously accepted with surrounding name whitespace can still be renamed to a normalized name");

        using(var concurrentStore=new LibraryStore(Path.Combine(root,"rename-concurrent"))) {
            string large=Path.Combine(concurrentStore.Root,"large.mp4");
            using(var file=File.Create(large))file.SetLength(32*1024*1024);
            concurrentStore.Upsert(new("large",large,"Large fixture",new FileInfo(large).Length,1,32,24,"h264","sdr","test"));
            var concurrent=new PresetService(concurrentStore,concurrentStore.Root);
            var entry=await SaveLegacyFixture(concurrent,concurrent.Capture("并发原始",["A"],None with {Library=true}),concurrentStore.Root);
            var rename=concurrent.RenameAsync(entry.Id,"并发新名");
            bool copying=false;
            for(int attempt=0;attempt<1000&&!rename.IsCompleted;attempt++) {
                if(Directory.EnumerateDirectories(Path.Combine(concurrentStore.Root,"preset-staging")).Any(s=>File.Exists(Path.Combine(s,"candidate","preset.zip")))) {copying=true;break;}
                await Task.Delay(1);
            }
            concurrent.Delete(entry.Id);
            bool renameFailed=false;try {await rename;}catch(IOException) {renameFailed=true;}
            check(copying&&renameFailed&&!Directory.Exists(Path.Combine(concurrentStore.Root,"presets",entry.Id))&&concurrent.List().Count==0,
                "Deleting during a large ZIP rewrite removes the whole preset and makes rename fail without resurrecting partial metadata");
            check(Directory.GetFiles(Path.Combine(concurrentStore.Root,"preset-resources")).Length==1&&!Directory.EnumerateDirectories(Path.Combine(concurrentStore.Root,"preset-staging")).Any(),
                "A concurrent deletion preserves shared media and cleans the abandoned rename candidate");
            entry=await SaveLegacyFixture(concurrent,concurrent.Capture("取消原始",["A"],None with {Library=true}),concurrentStore.Root);
            string current=Path.Combine(concurrentStore.Root,"presets",entry.Id);var beforeCancel=Files(current);
            using var cancellation=new CancellationTokenSource();
            rename=concurrent.RenameAsync(entry.Id,"取消新名",cancellation.Token);copying=false;
            for(int attempt=0;attempt<1000&&!rename.IsCompleted;attempt++) {
                if(Directory.EnumerateDirectories(Path.Combine(concurrentStore.Root,"preset-staging")).Any(s=>File.Exists(Path.Combine(s,"candidate","preset.zip")))) {copying=true;break;}
                await Task.Delay(1);
            }
            cancellation.Cancel();
            bool aborted=false;try {await rename;}catch(OperationCanceledException) {aborted=true;}
            check(copying&&aborted&&Same(beforeCancel,Files(current))&&concurrent.Read(entry.Id).Name=="取消原始",
                "Cancellation during large media streaming leaves the original archive, manifest and metadata byte-identical");
        }
    }

    internal static async Task<PresetSummary> SaveLegacyFixture(PresetService service,PresetDocument document,string dataRoot)
    {
        var saved=await service.SaveAsync(document);
        string directory=Path.Combine(dataRoot,"presets",saved.Id),archive=Path.Combine(directory,"preset.zip");
        await service.ExportAsync(saved.Id,archive);
        using(var zip=ZipFile.OpenRead(archive))
        {
            foreach(var entry in zip.Entries.Where(e=>e.FullName.StartsWith("resources/",StringComparison.Ordinal)))
                entry.ExtractToFile(Path.Combine(dataRoot,"preset-resources",Path.GetFileName(entry.FullName)),true);
        }
        File.Delete(Path.Combine(directory,"locations.json"));
        saved=saved with {ZipBytes=new FileInfo(archive).Length};
        File.WriteAllText(Path.Combine(directory,"info.json"),JsonSerializer.Serialize(saved));
        return saved;
    }

    private static void Seed(LibraryStore store,string screen,string prefix)
    {
        var media=Path.Combine(store.Root,"originals");Directory.CreateDirectory(media);
        foreach(var (suffix,extension,bytes) in new[] {("video",".mp4",Encoding.UTF8.GetBytes("fixture-video-"+prefix)),("image",".png",Convert.FromBase64String(PixelPng).Concat(Encoding.UTF8.GetBytes(prefix)).ToArray()),("unused",".mp4",Encoding.UTF8.GetBytes("unused-video-"+prefix))}) {
            string path=Path.Combine(media,suffix+extension);File.WriteAllBytes(path,bytes);
            store.Upsert(new(prefix+"-"+suffix,path,prefix+" "+suffix,bytes.Length,suffix=="image"?0:3,32,24,suffix=="image"?"png":"h264","sdr","test"));
        }
        var pixels=new byte[MaskPixels.Bytes];
        Array.Fill(pixels,(byte)255,0,pixels.Length/2);
        var mask=new EffectMask {Id=prefix+"-mask",Name="Half mask",Shape=5,Width=.72,Height=.61,Image=MaskPixels.Encode(pixels)};
        var fx=new VideoEffects {
            Brightness=prefix=="source"?.23:.07,
            Collection=new() {Added=EffectKind.Color|EffectKind.MaskLibrary},
            Masks=[mask],EffectMasks=new() {[EffectKind.Color]=mask.Id}
        };
        var fill=prefix=="target"?FillMode.Cover:FillMode.Contain;
        store.SaveProfile(screen,"",new(fill,fx));
        store.SaveBinding(new(screen,prefix+"-video",fill,true,42) {Effects=fx});
        store.SavePlayback(screen,new() {Mode=prefix=="source"?PlaybackMode.Sequential:PlaybackMode.Shuffle,AssetIds=[prefix+"-video",prefix+"-image"],Transition=PlaybackTransition.CrossFade,Trigger=PlaybackTrigger.Interval,IntervalSeconds=47});
        store.SaveWallpaperVolume(screen,prefix=="target"?61:37);
        if(prefix=="source")store.SaveWallpaperVolume(screen,0);
        store.SetSetting("widgets.v1",new WidgetConfiguration {
            Widgets=[new() {Id=prefix+"-text",Kind=WidgetKind.Text,Screen=screen,Enabled=false,X=.23,Y=.37,Layer=7,Visual=new() {Text="private-note",FontSize=39}},
                new() {Id=prefix+"-sticker",Kind=WidgetKind.Sticker,Screen=screen,Enabled=false,Visual=new() {Image=PixelPng}},
                new() {Id=prefix+"-search",Kind=WidgetKind.Search,Screen=screen,Enabled=false,Search=new() {Engine=SearchEngine.Custom,CustomUrl="https://private.example/search?q={query}"}},
                new() {Id=prefix+"-shortcuts",Kind=WidgetKind.Shortcuts,Screen=screen,Enabled=false,ShortcutId="command"}],
            Shortcuts=[new("command","private-command","cmd.exe /c echo private-command","common",0,Kind:ShortcutKind.Command),new("folder","private-folder",Path.Combine(store.Root,"private-folder"),"common",1,Kind:ShortcutKind.Folder)]
        }.ToJson());
    }

    private static bool IsWithin(string path,string parent)=>Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);

    private static async Task CheckScreenSemantics(string root,Action<bool,string> check)
    {
        using(var store=new LibraryStore(Path.Combine(root,"repeat-shortcuts"))) {
            var state=new WidgetConfiguration {
                Widgets=[new() {Id="a",Kind=WidgetKind.Shortcuts,Screen="A",ShortcutId="first"},
                    new() {Id="b",Kind=WidgetKind.Shortcuts,Screen="A",ShortcutId="second"}],
                Shortcuts=[new("first","Site","https://example.com/a","common",0),
                    new("second","Site","https://example.com/b","common",1)]
            };
            store.SetSetting("widgets.v1",state.ToJson());
            var service=new PresetService(store,store.Root);
            var document=service.Capture("Cards",["A"],None with {Widgets=true,IncludeWidgetPrivateData=true});
            var map=new Dictionary<string,string>{{"A","B"}};
            service.Apply(document,map);
            var first=WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
            service.Apply(document,map);
            var second=WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
            check(first.Groups.SequenceEqual(second.Groups)&&first.Shortcuts.SequenceEqual(second.Shortcuts),
                "Repeatedly applying a widget preset preserves the saved shortcut and group inventories");
            var local=second.Widgets.Where(w=>w.Screen=="A").Select(w=>w.ShortcutId).ToHashSet();
            var imported=second.Widgets.Where(w=>w.Screen=="B").Select(w=>w.ShortcutId).ToHashSet();
            check(imported.Count==2&&!imported.Overlaps(local)&&second.Shortcuts.Select(c=>c.ContentKey()).Distinct().Count()==2,
                "Same-name shortcuts with distinct targets survive and do not share editable IDs across screens");
        }
        using(var source=new LibraryStore(Path.Combine(root,"effects-source")))
        using(var target=new LibraryStore(Path.Combine(root,"effects-target"))) {
            Seed(source,"A","fx-source");Seed(target,"B","fx-target");
            var baseline=source.ScreenProfile("A")!;
            source.SaveEffectLayers("A","fx-source-video",baseline,new() {Overrides=EffectKind.Color,Effects=baseline.Effects with {Brightness=.46}});
            var targetBaseline=target.ScreenProfile("B")!;
            var originalOverride=new AssetEffects {Overrides=EffectKind.Color,Effects=targetBaseline.Effects with {Brightness=.11}};
            target.SaveEffectLayers("B","fx-target-video",targetBaseline,originalOverride);
            var skippedBaseline=new WallpaperProfile(FillMode.Cover,targetBaseline.Effects with {Glow=true,GlowStrength=.34});
            target.SaveProfile("C","",skippedBaseline);
            var skippedAppearance=target.EffectiveProfile("C","fx-target-video")!.Effects;
            target.SaveBinding(new("C","fx-target-video",FillMode.Cover,false,15) {Effects=skippedAppearance});
            var sourcePresets=new PresetService(source,source.Root);var targetPresets=new PresetService(target,target.Root);
            var capture=sourcePresets.Capture("当前可见画面",["A"],None with {Effects=true});
            check(capture.Assets.Length==0&&capture.ScreenProfiles["A"].Effects.Brightness==.46,
                "An effects-only preset captures the current wallpaper's effective material overrides without packaging wallpaper media");
            target.HistoryEnabled=true;
            targetPresets.Apply(capture,new Dictionary<string,string> { ["A"]="B" });
            check(target.EffectiveProfile("B","fx-target-video").Effects.Brightness==.46&&target.Bindings().Single(b=>b.ScreenId=="B").Effects.Brightness==.46,
                "An effects-only preset produces the exported appearance even when the recipient wallpaper had its own override");
            check(target.EffectiveProfile("C","fx-target-video").Effects.ToJson()==skippedAppearance.ToJson()&&target.Bindings().Single(b=>b.ScreenId=="C").Effects.ToJson()==skippedAppearance.ToJson(),
                "Applying effects to one display preserves the appearance of a skipped display playing the same material");
            target.ReplayUndo(false);
            check(target.ScreenProfile("B")!.Effects.ToJson()==targetBaseline.Effects.ToJson()&&target.AssetProfile("fx-target-video").Effects.ToJson()==originalOverride.Effects.ToJson()&&target.AssetProfile("fx-target-video").Overrides==originalOverride.Overrides,
                "Undo restores the recipient's prior display defaults and material-specific effects");
            check(target.ScreenProfile("C")!.Effects.ToJson()==skippedBaseline.Effects.ToJson()&&target.EffectiveProfile("C","fx-target-video").Effects.ToJson()==skippedAppearance.ToJson(),
                "Undo restores the skipped display's original effects layers as well as its unchanged appearance");
        }
        using(var store=new LibraryStore(Path.Combine(root,"group-screens"))) {
            var state=new WidgetConfiguration {
                LayoutGroups=[new() {Id="group-B",Name="B group",Screen="B"},new() {Id="empty-B",Name="Empty B group",Screen="B"}],
                Widgets=[new() {Id="member-B",Kind=WidgetKind.Text,Screen="A",LayoutGroupId="group-B",Enabled=false,Visual=new() {Text="Grouped on B"}},
                    new() {Id="standalone-A",Kind=WidgetKind.Clock,Screen="A",Enabled=false},
                    new() {Id="primary-A",Kind=WidgetKind.Search,Screen="",Enabled=false}]
            };
            store.SetSetting("widgets.v1",state.ToJson());
            var service=new PresetService(store,store.Root);
            var captureA=service.Capture("A components",["A"],None with {Widgets=true,IncludeWidgetPrivateData=true});
            check(captureA.Widgets!.Widgets.Length==2&&captureA.Widgets.Widgets.All(w=>w.Screen=="A")&&!captureA.Widgets.Widgets.Any(w=>w.Visual.Text=="Grouped on B"),
                "Component capture uses a layout group's display and resolves empty standalone displays to the selected primary display");
            var captureB=service.Capture("B components",["B"],None with {Widgets=true,IncludeWidgetPrivateData=true});
            check(captureB.Widgets!.Widgets.Any(w=>w.Visual.Text=="Grouped on B"&&w.Screen=="B")&&captureB.Widgets.LayoutGroups.Any(g=>g.Name=="B group"&&g.Screen=="B"),
                "A member whose own display differs from its group can be captured on the group's actual display");
            var saved=await service.SaveAsync(captureA);
            service.Apply(saved.Id,new Dictionary<string,string> { ["A"]="A" });
            var applied=WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
            check(applied.Widgets.Any(w=>w.Id=="member-B"&&w.LayoutGroupId=="group-B")&&applied.LayoutGroups.Any(g=>g.Id=="group-B")&&applied.LayoutGroups.Any(g=>g.Id=="empty-B"),
                "Applying a preset to A preserves both populated and empty layout groups on B");
            check(applied.Widgets.Length==3&&!applied.Widgets.Any(w=>w.Screen==""),
                "Applying primary-display components replaces existing empty-display instances without creating duplicates");
        }
        using(var source=new LibraryStore(Path.Combine(root,"undo-cap-source")))
        using(var target=new LibraryStore(Path.Combine(root,"undo-cap-target"))) {
            WidgetConfiguration LargeWidgets(byte marker) {
                var bytes=new byte[1_650_000];Convert.FromBase64String(PixelPng).CopyTo(bytes,0);bytes[^1]=marker;
                string image=Convert.ToBase64String(bytes);
                return new() {Widgets=Enumerable.Range(0,4).Select(i=>new WidgetInstance {Id="large-"+i,Kind=WidgetKind.Sticker,Screen="A",Enabled=false,Visual=new() {Image=image}}).ToArray()};
            }
            source.SetSetting("widgets.v1",LargeWidgets(1).ToJson());target.SetSetting("widgets.v1",LargeWidgets(2).ToJson());
            var sourceService=new PresetService(source,source.Root);var targetService=new PresetService(target,target.Root);
            var document=sourceService.Capture("Large legal components",["A"],None with {Widgets=true});
            string before=target.ConfigurationRevision();target.HistoryEnabled=true;long history=target.HistoryRevision;
            bool rejected=false;try {targetService.Apply(document,new Dictionary<string,string> { ["A"]="A" });}
            catch(InvalidOperationException error) {rejected=error.Message.Contains("撤销");}
            check(rejected&&target.ConfigurationRevision()==before&&target.HistoryRevision==history&&target.UndoLabel==null,
                "An individually legal component preset exceeding the combined undo budget is rejected atomically instead of silently losing undo");
        }
        using(var store=new LibraryStore(Path.Combine(root,"shared-material-guard"))) {
            Seed(store,"A","shared");
            var service=new PresetService(store,store.Root);
            var entry=await service.SaveAsync(service.Capture("Shared material",["A"],All));
            var map=new Dictionary<string,string> { ["A"]="A" };
            service.Apply(entry.Id,map,All);
            var document=service.Read(entry.Id);
            string asset=store.Bindings().Single(b=>b.ScreenId=="A").AssetId;
            string originalAsset=document.Bindings.Single().AssetId;
            var baseline=store.ScreenProfile("A")!;
            var current=new AssetEffects {Overrides=EffectKind.Color,Effects=baseline.Effects with {Brightness=.11}};
            store.SaveEffectLayers("A",asset,baseline,current);
            store.SaveBinding(store.Bindings().Single(b=>b.ScreenId=="A") with {Effects=current.Resolve(baseline.Effects)});
            store.SaveProfile("C","",new(FillMode.Cover,baseline.Effects with {Glow=true}));
            store.SaveBinding(new("C",asset,FillMode.Cover,false,24) {Effects=store.EffectiveProfile("C",asset).Effects});
            store.HistoryEnabled=true;string before=store.ConfigurationRevision();long history=store.HistoryRevision;
            var changedSpecific=new Dictionary<string,AssetEffects>(document.AssetEffects) { [originalAsset]=current with {Effects=current.Effects with {Brightness=.42}} };
            bool rejected=false;try {service.Apply(document with {AssetEffects=changedSpecific},map,All);}catch(InvalidOperationException) {rejected=true;}
            check(rejected&&store.ConfigurationRevision()==before&&store.HistoryRevision==history,
                "Playing-and-effects application refuses a global material override that would change a skipped display");
            string skipped=store.EffectiveProfile("C",asset).Effects.ToJson();
            var unchangedSpecific=new Dictionary<string,AssetEffects>(document.AssetEffects) { [originalAsset]=current };
            service.Apply(document with {AssetEffects=unchangedSpecific},map,All);
            check(store.EffectiveProfile("C",asset).Effects.ToJson()==skipped,
                "A shared material can be applied when the skipped display's effective appearance remains unchanged");
        }
    }

    private sealed record ArchiveItem(string Name,byte[] Bytes);

    private static async Task CheckOldEffectSchema(string root,string source,Action<bool,string> check)
    {
        string path=Path.Combine(root,"legacy-effects-34.zip");
        using(var original=ZipFile.OpenRead(source))
        using(var target=ZipFile.Open(path,ZipArchiveMode.Create))
            foreach(var entry in original.Entries)
            {
                using var input=entry.Open();using var output=target.CreateEntry(entry.FullName,CompressionLevel.NoCompression).Open();
                if(entry.FullName!="manifest.json") {input.CopyTo(output);continue;}
                var json=JsonNode.Parse(input)!;
                void OldEffects(JsonNode? node)
                {
                    if(node is JsonObject map)
                        foreach(var pair in map.ToArray())
                        {
                            if(pair.Key=="Effects"&&pair.Value is JsonObject effects&&effects["Version"]!=null)
                                effects["Version"]=34;
                            OldEffects(pair.Value);
                        }
                    else if(node is JsonArray list) foreach(var item in list) OldEffects(item);
                }
                OldEffects(json["Document"]);byte[] bytes=Encoding.UTF8.GetBytes(json.ToJsonString());output.Write(bytes);
            }
        using var store=new LibraryStore(Path.Combine(root,"legacy-effect-recipient"));
        var service=new PresetService(store,store.Root);var preview=await service.PreviewPackageAsync(path);
        var saved=await service.ImportAsync(path,new PresetImportOptions {ExpectedFingerprint=preview.Fingerprint});
        var document=service.Read(saved.Id);
        check(document.ScreenProfiles.Values.All(p=>p.Effects.Version==VideoEffects.Original.Version)&&
            document.AssetEffects.Values.All(p=>p.Effects.Version==VideoEffects.Original.Version)&&
            document.Bindings.All(b=>b.Effects.Version==VideoEffects.Original.Version),
            "Legacy JSON 34 preset effects migrate across screen profiles, asset overrides and bindings after resource restoration");
        var inspected=service.Inspect(saved.Id).ScreenProfiles["source-screen"].Effects;
        check(inspected.Masks.Single() is {Width:.72,Height:.61} mask&&inspected.EffectMasks[EffectKind.Color]==mask.Id,
            "Legacy preset inspection preserves mask dimensions and the effect's mask assignment");
        service.Apply(await service.PrepareApplyAsync(saved.Id,store.Assets()),new Dictionary<string,string>{{"source-screen","target-screen"}});
        check(store.Bindings().Count==1,"A real ZIP with legacy effect schemas remains applicable after migration");
    }

    private static async Task CheckInvalidPackages(string root,PresetService service,LibraryStore store,string source,Action<bool,string> check)
    {
        var original=new List<ArchiveItem>();
        using(var archive=ZipFile.OpenRead(source))
            foreach(var entry in archive.Entries) {using var stream=entry.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);original.Add(new(entry.FullName,bytes.ToArray()));}
        string resource=original.First(e=>e.Name.StartsWith("resources/",StringComparison.Ordinal)).Name;
        int index=0;
        async Task Reject(string name,Action<List<ArchiveItem>> change)
        {
            string path=Path.Combine(root,"rejected-"+(++index)+".zip");
            var entries=original.Select(e=>new ArchiveItem(e.Name,e.Bytes.ToArray())).ToList();change(entries);
            using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
                foreach(var item in entries) {using var stream=archive.CreateEntry(item.Name,CompressionLevel.Optimal).Open();stream.Write(item.Bytes);}
            var presets=service.List().Select(p=>p.Id).Order().ToArray();
            var files=Directory.EnumerateFiles(store.Root,"*",SearchOption.AllDirectories).Order().ToArray();
            string revision=store.ConfigurationRevision();long history=store.HistoryRevision;
            bool rejected=false;
            try {await service.ImportAsync(path);}
            catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or JsonException or OverflowException) {rejected=true;}
            check(rejected,"Preset ZIP rejects "+name);
            check(service.List().Select(p=>p.Id).Order().SequenceEqual(presets)&&store.ConfigurationRevision()==revision&&store.HistoryRevision==history,
                "Rejected "+name+" does not publish a preset or modify configuration/history");
            check(Directory.EnumerateFiles(store.Root,"*",SearchOption.AllDirectories).Order().SequenceEqual(files),
                "Rejected "+name+" cleans temporary extraction files");
        }
        static void Manifest(List<ArchiveItem> entries,Action<JsonNode> mutate)
        {
            int at=entries.FindIndex(e=>e.Name=="manifest.json");var json=JsonNode.Parse(entries[at].Bytes)!;mutate(json);
            entries[at]=entries[at] with {Bytes=Encoding.UTF8.GetBytes(json.ToJsonString())};
        }
        await Reject("parent-directory traversal",entries=>entries.Add(new("../escaped.txt","escape"u8.ToArray())));
        await Reject("backslash traversal",entries=>entries.Add(new("resources\\..\\..\\escaped.txt","escape"u8.ToArray())));
        await Reject("an absolute entry path",entries=>entries.Add(new("C:/escaped.txt","escape"u8.ToArray())));
        await Reject("duplicate manifest entries",entries=>entries.Add(entries.Single(e=>e.Name=="manifest.json")));
        await Reject("case-insensitive manifest duplicates",entries=>entries.Add(entries.Single(e=>e.Name=="manifest.json") with {Name="MANIFEST.JSON"}));
        await Reject("duplicate resources",entries=>entries.Add(entries.Single(e=>e.Name==resource)));
        await Reject("case-insensitive resource duplicates",entries=>entries.Add(entries.Single(e=>e.Name==resource) with {Name=resource.ToUpperInvariant()}));
        await Reject("a missing manifest",entries=>entries.RemoveAll(e=>e.Name=="manifest.json"));
        await Reject("a missing required resource",entries=>entries.RemoveAll(e=>e.Name==resource));
        await Reject("an undeclared resource",entries=>entries.Add(new("resources/unlisted.bin","unexpected"u8.ToArray())));
        await Reject("a corrupted resource hash",entries=> {int at=entries.FindIndex(e=>e.Name==resource);var bytes=entries[at].Bytes.ToArray();bytes[0]^=0xff;entries[at]=entries[at] with {Bytes=bytes};});
        await Reject("a declared resource size mismatch",entries=>Manifest(entries,json=>json["Resources"]![0]!["Bytes"]=json["Resources"]![0]!["Bytes"]!.GetValue<long>()+1));
        await Reject("a manifest checksum mismatch",entries=>Manifest(entries,json=>json["Resources"]![0]!["Sha256"]=new string('0',64)));
        await Reject("an unsupported package version",entries=>Manifest(entries,json=>json["Version"]=999));
        await Reject("an unsupported document version",entries=>Manifest(entries,json=>json["Document"]!["Version"]=999));
        await Reject("an external absolute media reference",entries=>Manifest(entries,json=>json["Document"]!["Assets"]![0]!["Path"]="C:\\private\\external.mp4"));
        await Reject("an impossible declared resource capacity",entries=>Manifest(entries,json=>json["Resources"]![0]!["Bytes"]=long.MaxValue));
        await Reject("duplicate manifest resource declarations",entries=>Manifest(entries,json=>json["Resources"]!.AsArray().Add(json["Resources"]![0]!.DeepClone())));
        await Reject("an oversized manifest",entries=> {
            int at=entries.FindIndex(e=>e.Name=="manifest.json");var bytes=new byte[PresetPackageLimits.MaxManifestBytes+1];
            Array.Fill(bytes,(byte)' ');entries[at].Bytes.CopyTo(bytes,0);entries[at]=entries[at] with {Bytes=bytes};
        });
        check(!File.Exists(Path.Combine(root,"escaped.txt"))&&!File.Exists(Path.Combine(store.Root,"escaped.txt")),
            "Archive path traversal never creates a file outside owned extraction resources");
    }
}
