using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Application.Presets;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using LumaWall.Storage;

internal static class PresetResourceLocationChecks
{
    private static readonly PresetSelection Selection = new() {Playing=true,Library=true,Effects=false,Widgets=false};
    internal static void Run(string root,Action<bool,string> check) => RunAsync(Path.Combine(root,"preset-resource-location"),check).GetAwaiter().GetResult();

    private static async Task RunAsync(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        string originals=Path.Combine(root,"original-media");Directory.CreateDirectory(originals);
        string video=Path.Combine(originals,"original.mp4");
        await using(var file=new FileStream(video,FileMode.CreateNew,FileAccess.Write)) { file.SetLength(32*1024*1024);await file.WriteAsync("original bytes"u8.ToArray()); }
        var media=new MediaAsset("original-id",video,"原视频",new FileInfo(video).Length,10,1920,1080,"h264","sdr","test");
        using var store=new LibraryStore(Path.Combine(root,"source"));
        store.Upsert(media);store.SaveBinding(new("A",media.Id,FillMode.Contain,false,0));
        var service=new PresetService(store,store.Root);var capture=service.Capture("引用原文件",["A"],Selection);
        string revision=store.ConfigurationRevision();long history=store.HistoryRevision;
        var saved=await service.SaveAsync(capture);
        string local=Path.Combine(store.Root,"presets",saved.Id);
        check(!File.Exists(Path.Combine(local,"preset.zip"))&&File.Exists(Path.Combine(local,"locations.json"))&&saved.ZipBytes==0,
            "Local saves retain a small resource-location manifest without creating a duplicate ZIP");
        check(!Directory.GetFiles(store.Root,"*.mp4",SearchOption.AllDirectories).Any()&&service.Read(saved.Id).Assets.Single().Path==video,
            "Saving a local preset never copies its existing video into the data directory");
        check(store.ConfigurationRevision()==revision&&store.HistoryRevision==history,"Saving referenced media leaves the library and history unchanged");
        using(var locked=new FileStream(video,FileMode.Open,FileAccess.Read,FileShare.None))
            await service.RenameAsync(saved.Id,"轻量改名");
        check(service.List().Single().Name=="轻量改名"&&Directory.GetFiles(local).Length==3,"Renaming a referenced preset touches only three small metadata files even while its video is locked");
        var reopened=new PresetService(store,store.Root);
        var prepared=await reopened.PrepareApplyAsync(saved.Id,store.Assets());
        reopened.Apply(prepared,new Dictionary<string,string>{{"A","A"}});
        check(store.Assets().Count()==1&&store.Bindings().Single().AssetId==media.Id&&store.Assets().Single().Path==video,
            "Applying a locally saved preset reuses the original library identity and path without a second media row");
        string zip=Path.Combine(root,"portable.zip");await reopened.ExportAsync(saved.Id,zip);
        using(var archive=ZipFile.OpenRead(zip))
        {
            check(archive.Entries.Count==2&&archive.Entries.Single(e=>e.FullName!="manifest.json").Length==media.Bytes,
                "On-demand export still produces a complete portable ZIP with real video bytes");
            using var reader=new StreamReader(archive.GetEntry("manifest.json")!.Open());
            check(!reader.ReadToEnd().Contains(originals,StringComparison.OrdinalIgnoreCase),"On-demand export excludes trusted local absolute paths from its portable manifest");
        }
        using var target=new LibraryStore(Path.Combine(root,"target"));
        var receiver=new PresetService(target,target.Root);
        var preview=await receiver.PreviewPackageAsync(zip);
        check(preview.Name=="轻量改名"&&preview.MediaCount==1&&preview.MediaBytes==media.Bytes&&preview.PackageBytes==new FileInfo(zip).Length&&preview.Fingerprint.Length==64,
            "ZIP preview reports media count and bytes using only bounded metadata");
        string selected=Path.Combine(root,"selected-media");Directory.CreateDirectory(selected);
        string unrelated=Path.Combine(selected,"my-file.txt");File.WriteAllText(unrelated,"keep");
        var imported=await receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=selected,ExpectedFingerprint=preview.Fingerprint});
        var importedDocument=receiver.Read(imported.Id);string copied=importedDocument.Assets.Single().Path;
        check(copied.StartsWith(Path.Combine(selected,PresetService.ImportedMediaFolderName,imported.Id)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(copied),
            "Imported video is written only beneath the explicitly selected media directory's owned preset folder");
        check(!Directory.GetFiles(target.Root,"*.mp4",SearchOption.AllDirectories).Any()&&!Directory.GetFiles(target.Root,"*.zip",SearchOption.AllDirectories).Any(),
            "External import leaves no video or retained ZIP copy on the application data drive");
        check(Hash(copied)==Hash(video)&&File.ReadAllText(unrelated)=="keep","Chosen-directory import retains exact bytes and does not alter neighboring user files");
        var unappliedDuplicate=await receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=selected,ExpectedFingerprint=preview.Fingerprint});
        check(receiver.Read(unappliedDuplicate.Id).Assets.Single().Path==copied&&Directory.GetDirectories(Path.Combine(selected,PresetService.ImportedMediaFolderName)).Length==1,
            "Importing the same ZIP again before either preset is applied reuses the verified saved-preset video");
        receiver.Delete(unappliedDuplicate.Id);
        receiver=new PresetService(target,target.Root);
        prepared=await receiver.PrepareApplyAsync(imported.Id,target.Assets());receiver.Apply(prepared,new Dictionary<string,string>{{"A","B"}});
        check(target.Assets().Single().Path==copied&&target.Bindings().Single().ScreenId=="B","External resource locations remain usable after reopening and apply without a second copy");
        string appliedId=target.Assets().Single().Id;
        var second=await receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=selected,ExistingAssets=target.Assets().ToArray(),ExpectedFingerprint=preview.Fingerprint});
        check(receiver.Read(second.Id).Assets.Single().Path==copied&&Directory.GetDirectories(Path.Combine(selected,PresetService.ImportedMediaFolderName)).Length==1,
            "A repeated import verifies and reuses identical existing library media instead of extracting another video");
        prepared=await receiver.PrepareApplyAsync(second.Id,target.Assets());receiver.Apply(prepared,new Dictionary<string,string>{{"A","B"}});
        check(target.Assets().Count()==1&&target.Assets().Single().Id==appliedId,"Repeated apply retains the already-existing library identity");
        await receiver.RenameAsync(second.Id,"导入后改名");string roundtrip=Path.Combine(root,"roundtrip.zip");await receiver.ExportAsync(second.Id,roundtrip);
        check((await receiver.PreviewPackageAsync(roundtrip)).Name=="导入后改名","A metadata-only rename also updates the next exported ZIP name");
        receiver.Delete(imported.Id);receiver.Delete(second.Id);
        check(File.Exists(copied)&&target.Assets().Single().Path==copied&&File.ReadAllText(unrelated)=="keep",
            "Deleting local preset metadata preserves media used by the library and never deletes user-selected directories");

        var legacy=await PresetPackageChecks.SaveLegacyFixture(service,capture with {Name="旧格式"},store.Root);
        prepared=await service.PrepareApplyAsync(legacy.Id,store.Assets());
        check(prepared.Assets.Single().Path==video,"Old ZIP-backed presets also resolve duplicate copied media back to the existing original by verified content hash");
        service.Apply(prepared,new Dictionary<string,string>{{"A","A"}});
        check(store.Assets().Count()==1&&store.Bindings().Single().AssetId==media.Id,"Applying a legacy preset after hash resolution creates no duplicate library entry");
        string legacyCopy=service.Read(legacy.Id).Assets.Single().Path;
        File.Delete(legacyCopy);
        prepared=await service.PrepareApplyAsync(legacy.Id,store.Assets());
        check(prepared.Assets.Single().Path==video,"Preparing an old preset can recover from its missing copied file by resolving the library's same-hash original first");
        string moved=Path.Combine(originals,"moved.mp4");File.Move(video,moved);store.Upsert(media with {Path=moved});
        prepared=await reopened.PrepareApplyAsync(saved.Id,store.Assets());
        check(prepared.Assets.Single().Path==moved,"Preparing a referenced preset can find an identical media file that was moved and re-associated in the library");
        File.Move(moved,video);store.Upsert(media);

        await CheckFailureBoundaries(root,receiver,target,zip,preview,selected,check);
        await CheckRecovery(root,check);
        prepared=await reopened.PrepareApplyAsync(saved.Id,store.Assets());
        using(var edit=new FileStream(video,FileMode.Open,FileAccess.Write)) {edit.Position=20;edit.WriteByte(55);}
        File.SetLastWriteTimeUtc(video,DateTime.UtcNow.AddSeconds(2));
        string before=store.ConfigurationRevision();bool stale=false;
        try {reopened.Apply(prepared,new Dictionary<string,string>{{"A","A"}});}catch(InvalidDataException) {stale=true;}
        check(stale&&store.ConfigurationRevision()==before,"A media change after async preparation is rejected before applying any database changes");
        bool changed=false;try {reopened.Read(saved.Id);}catch(InvalidDataException) {changed=true;}
        check(changed,"A saved local reference rejects changed original media rather than silently adopting different bytes");
        service.Delete(saved.Id);
        check(File.Exists(video),"Deleting a locally referenced preset never deletes its user's original file");
    }

    private static async Task CheckFailureBoundaries(string root,PresetService receiver,LibraryStore store,string zip,PresetImportPreview preview,string selected,Action<bool,string> check)
    {
        int count=receiver.List().Count;string revision=store.ConfigurationRevision();
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool canceled=false;try {await receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=selected},cancel.Token);}catch(OperationCanceledException) {canceled=true;}
            check(canceled&&receiver.List().Count==count,"Canceled import leaves no visible local preset");
        }
        bool mismatch=false;try {await receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=selected,ExpectedFingerprint=new string('0',64)});}catch(InvalidDataException) {mismatch=true;}
        check(mismatch&&receiver.List().Count==count,"Import refuses a package that no longer matches the approved preview fingerprint");
        string bad=Path.Combine(root,"bad-payload.zip");
        using(var source=ZipFile.OpenRead(zip))
        using(var destination=ZipFile.Open(bad,ZipArchiveMode.Create))
            foreach(var entry in source.Entries)
            {
                using var input=entry.Open();using var output=destination.CreateEntry(entry.FullName,CompressionLevel.NoCompression).Open();
                if(entry.FullName=="manifest.json") input.CopyTo(output);
                else {int first=input.ReadByte();output.WriteByte((byte)(first^1));input.CopyTo(output);}
            }
        bool corrupt=false;try {await receiver.ImportAsync(bad,new PresetImportOptions {MediaDirectory=selected});}catch(InvalidDataException) {corrupt=true;}
        check(corrupt&&receiver.List().Count==count&&!Directory.GetDirectories(Path.Combine(selected,PresetService.ImportedMediaFolderName),".import-*").Any(),
            "A corrupted streamed video rolls back its selected-drive temporary bundle and publishes no partial preset");
        check(!Directory.EnumerateDirectories(Path.Combine(store.Root,"preset-staging")).Any()&&store.ConfigurationRevision()==revision,
            "Failed and canceled import clean application staging and never modify the library");

        string cancelDirectory=Path.Combine(root,"cancel-media");Directory.CreateDirectory(cancelDirectory);
        using(var cancel=new CancellationTokenSource())
        {
            var import=receiver.ImportAsync(zip,new PresetImportOptions {MediaDirectory=cancelDirectory},cancel.Token);
            bool started=false;
            for(int i=0;i<5000&&!import.IsCompleted;i++)
            {
                string parent=Path.Combine(cancelDirectory,PresetService.ImportedMediaFolderName);
                if(Directory.Exists(parent)&&Directory.EnumerateFiles(parent,"*.mp4",SearchOption.AllDirectories).Any()) {started=true;cancel.Cancel();break;}
                await Task.Delay(1);
            }
            bool canceled=false;try {await import;}catch(OperationCanceledException) {canceled=true;}
            check(started&&canceled&&receiver.List().Count==count,"Cancellation while a real video is streaming aborts before publishing metadata");
            string owned=Path.Combine(cancelDirectory,PresetService.ImportedMediaFolderName);
            check(!Directory.Exists(owned)||!Directory.EnumerateFileSystemEntries(owned).Any(),"Mid-stream cancellation removes only its owned bundle on the selected drive");
        }
    }

    private static async Task CheckRecovery(string root,Action<bool,string> check)
    {
        using var store=new LibraryStore(Path.Combine(root,"recovery-store"));_=new PresetService(store,store.Root);
        string parent=Path.Combine(root,"recovery-media",PresetService.ImportedMediaFolderName);Directory.CreateDirectory(parent);
        string id=Guid.NewGuid().ToString("N"),name=new string('a',64)+".mp4",bundle=Path.Combine(parent,".import-"+id);
        Directory.CreateDirectory(bundle);File.WriteAllText(Path.Combine(bundle,"owner.json"),JsonSerializer.Serialize(new {Version=1,Id=id}));
        File.WriteAllText(Path.Combine(bundle,name),"partial");
        string staging=Path.Combine(store.Root,"preset-staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging,"import.json"),JsonSerializer.Serialize(new {Version=1,Id=id,MediaParent=parent,Names=new[]{name}}));
        string unrelated=Path.Combine(parent,"keep.txt");File.WriteAllText(unrelated,"keep");
        _=new PresetService(store,store.Root);
        check(!Directory.Exists(bundle)&&!Directory.Exists(staging)&&File.ReadAllText(unrelated)=="keep",
            "Restart recovery removes a journaled interrupted external bundle while preserving unrelated files");

        id=Guid.NewGuid().ToString("N");bundle=Path.Combine(parent,".import-"+id);Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle,"owner.json"),JsonSerializer.Serialize(new {Version=1,Id=id}));
        File.WriteAllText(Path.Combine(bundle,"unknown.txt"),"user");staging=Path.Combine(store.Root,"preset-staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging,"import.json"),JsonSerializer.Serialize(new {Version=1,Id=id,MediaParent=parent,Names=new[]{name}}));
        bool protectedUnknown=false;try {_=new PresetService(store,store.Root);}catch(IOException) {protectedUnknown=true;}
        check(protectedUnknown&&File.ReadAllText(Path.Combine(bundle,"unknown.txt"))=="user"&&File.Exists(Path.Combine(staging,"import.json")),
            "Recovery refuses to delete an external bundle containing unknown files and retains its recovery receipt");
        File.Delete(Path.Combine(bundle,"unknown.txt"));_=new PresetService(store,store.Root);
        await Task.CompletedTask;
    }

    private static string Hash(string path) {using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
}
