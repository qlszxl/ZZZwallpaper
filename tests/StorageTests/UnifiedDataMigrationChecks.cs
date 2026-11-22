using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Application.Presets;
using LumaWall.Contracts;
using LumaWall.Storage;
using Microsoft.Data.Sqlite;

internal static class UnifiedDataMigrationChecks
{
    private const string Marker = "identity-migration.v1.json";
    private const string Recovery = "ZZZwallpaper-data-migrations";

    internal static void Run(string root, Action<bool,string> check)
    {
        root=Path.Combine(root,"identity-migration"); Directory.CreateDirectory(root);
        BasicRoots(root,check);
        CompleteMigration(root,check);
        InterruptedMigration(root,check);
        RejectedInputs(root,check);
    }

    private static string Fixture(string root,string name)
    {
        string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;
    }

    private static void BasicRoots(string root,Action<bool,string> check)
    {
        string empty=Fixture(root,"empty"), canonical=Path.Combine(empty,"LumaWall");
        check(UnifiedDataMigration.PrepareDefaultRoot(empty)==canonical,"A new default profile uses the original LumaWall data directory");
        check(!Directory.Exists(Path.Combine(empty,"LumaWall.Modular"))&&!Directory.Exists(Path.Combine(empty,Recovery)),"Fresh startup creates no separate modular profile or recovery snapshot");

        string original=Fixture(root,"original-only"), originalRoot=Path.Combine(original,"LumaWall");
        Seed(originalRoot,"original");string originalDatabase=Digest(Path.Combine(originalRoot,"library.db"));
        check(UnifiedDataMigration.PrepareDefaultRoot(original)==originalRoot&&Owner(originalRoot)=="original","An original-only profile remains the active configuration");
        check(Digest(Path.Combine(originalRoot,"library.db"))==originalDatabase&&!Directory.Exists(Path.Combine(original,Recovery)),"An original-only startup preserves its database without an unnecessary migration snapshot");
        Seed(Path.Combine(original,"LumaWall.Modular"),"late-old-modular");
        check(UnifiedDataMigration.PrepareDefaultRoot(original)==originalRoot&&Owner(originalRoot)=="original"&&!Directory.Exists(Path.Combine(original,Recovery)),"A modular profile created after the first unified launch cannot replace current canonical data");

        string modular=Fixture(root,"modular-only"), modularRoot=Path.Combine(modular,"LumaWall.Modular");
        Seed(modularRoot,"modular");string active=UnifiedDataMigration.PrepareDefaultRoot(modular);
        check(Owner(active)=="modular"&&Owner(modularRoot)=="modular","A modular-only profile becomes canonical while its source is preserved");
        string batch=Batch(modular,active);
        check(Directory.Exists(Path.Combine(batch,"modular-snapshot"))&&!Directory.Exists(Path.Combine(batch,"original-snapshot")),"A modular-only migration retains one complete source snapshot");
    }

    private static void CompleteMigration(string root,Action<bool,string> check)
    {
        string local=Fixture(root,"both-profiles"), original=Path.Combine(local,"LumaWall"), modular=Path.Combine(local,"LumaWall.Modular");
        Seed(original,"original");Seed(modular,"modular");
        File.WriteAllText(Path.Combine(original,"original-note.txt"),"original personal note");
        File.WriteAllText(Path.Combine(modular,"modular-note.txt"),"modular personal note");
        Directory.CreateDirectory(Path.Combine(modular,"unknown-user-files"));
        File.WriteAllBytes(Path.Combine(modular,"unknown-user-files","keep.bin"),[0,255,32,64]);
        string external=Path.Combine(local,"LumaWall.Modular-other","outside.mp4");Directory.CreateDirectory(Path.GetDirectoryName(external)!);File.WriteAllBytes(external,[9,8,7]);
        string archiveId;
        using(var store=new LibraryStore(modular))
        {
            store.Upsert(new("external",external,"External",3,1,100,100,"h264","sdr","test"));
            store.SaveFolder(new("owned-folder",Path.Combine(modular,"wallpapers"),false));
            store.SetSetting("effectPreviewCustom",Path.Combine(modular,"wallpapers","modular.mp4"));
            store.SetSetting("migration.paths",JsonSerializer.Serialize(new { nested=new[]{Path.Combine(modular,"unknown-user-files","keep.bin"),external}, slash=modular.Replace('\\','/')+"/wallpapers/modular.mp4", embedded=JsonSerializer.Serialize(new { path=Path.Combine(modular,"wallpapers","modular.mp4") }) }));
            store.SetSetting("literal-prose","A note mentioning "+modular+" remains a note.");
            var service=new PresetService(store,modular);
            var saved=PresetPackageChecks.SaveLegacyFixture(service,service.Capture("迁移后保留的预设",["screen"],new() {Library=true}),modular).GetAwaiter().GetResult();
            archiveId=saved.Id;service.Apply(saved.Id,new Dictionary<string,string>{{"screen","screen"}});
        }
        string presetZip=Path.Combine(modular,"presets",archiveId,"preset.zip"), presetDigest=Digest(presetZip);
        File.WriteAllText(Path.Combine(modular,"unknown-user-files","paths.json"),JsonSerializer.Serialize(new { owned=Path.Combine(modular,"wallpapers","modular.mp4"),outside=external }));
        SqliteConnection.ClearAllPools();
        // Keep a committed change only in the source WAL. The migration must use
        // SQLite's backup API rather than copy library.db alone.
        using var wal=Open(Path.Combine(modular,"library.db"));
        Execute(wal,"PRAGMA wal_autocheckpoint=0; INSERT INTO settings VALUES('committed-wal','preserved') ON CONFLICT(key) DO UPDATE SET value='preserved';");
        check(File.Exists(Path.Combine(modular,"library.db-wal"))&&new FileInfo(Path.Combine(modular,"library.db-wal")).Length>0,"The migration fixture contains committed source WAL changes");
        string active=UnifiedDataMigration.PrepareDefaultRoot(local), batch=Batch(local,active);
        check(active==original&&Owner(active)=="modular","When both profiles exist, the modular configuration becomes the one canonical active profile");
        check(Value(active,"committed-wal")=="preserved"&&Value(Path.Combine(batch,"modular-snapshot"),"committed-wal")=="preserved","Both the active database and recovery snapshot include committed WAL changes");
        check(Owner(Path.Combine(batch,"original-snapshot"))=="original"&&Owner(Path.Combine(batch,"original-directory"))=="original"&&Owner(modular)=="modular","Both original recovery copies and the original modular directory remain readable");
        check(File.ReadAllText(Path.Combine(batch,"original-snapshot","original-note.txt"))=="original personal note"&&File.ReadAllText(Path.Combine(batch,"modular-snapshot","modular-note.txt"))=="modular personal note","Both recovery snapshots retain unknown personal files");
        check(File.ReadAllBytes(Path.Combine(active,"unknown-user-files","keep.bin")).SequenceEqual(new byte[]{0,255,32,64}),"Migration copies unknown binary user files exactly");
        check(VerifyManifest(Path.Combine(batch,"original-snapshot"),Path.Combine(batch,"original-files.json"))&&VerifyManifest(Path.Combine(batch,"modular-snapshot"),Path.Combine(batch,"modular-files.json"))&&VerifyManifest(active,Path.Combine(batch,"candidate-files.json")),"Every recovery and committed-candidate manifest matches all recorded file hashes and sizes");
        check(!File.Exists(Path.Combine(local,Recovery,"pending.json")),"A completed migration removes its pending switch journal");
        using(var store=new LibraryStore(active))
        {
            check(store.Assets().Where(a=>a.Id!="external"&&!a.Path.Equals(external,StringComparison.OrdinalIgnoreCase)).All(a=>a.Path.StartsWith(active+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(a.Path)),"Internal wallpaper and imported-preset media references relocate to existing canonical files");
            check(store.Assets().Single(a=>a.Id=="external").Path==external,"An external path sharing the old directory-name prefix remains unchanged");
            check(store.Folders().Single().Path==Path.Combine(active,"wallpapers")&&store.GetSetting("effectPreviewCustom")==Path.Combine(active,"wallpapers","modular.mp4"),"Folder and preview-image paths relocate with the active profile");
            var json=JsonNode.Parse(store.GetSetting("migration.paths")!)!;
            check(json["nested"]![0]!.GetValue<string>()==Path.Combine(active,"unknown-user-files","keep.bin")&&json["nested"]![1]!.GetValue<string>()==external&&json["slash"]!.GetValue<string>()==active.Replace('\\','/')+"/wallpapers/modular.mp4"&&JsonNode.Parse(json["embedded"]!.GetValue<string>())!["path"]!.GetValue<string>()==Path.Combine(active,"wallpapers","modular.mp4"),"Nested, slash-separated and JSON-encoded internal paths relocate without changing external paths");
            check(store.GetSetting("literal-prose")=="A note mentioning "+modular+" remains a note.","Ordinary prose containing an old path remains unchanged");
            var service=new PresetService(store,active);
            check(service.List().Single().Id==archiveId&&service.Read(archiveId).Name=="迁移后保留的预设"&&Digest(Path.Combine(active,"presets",archiveId,"preset.zip"))==presetDigest,"Saved ZIP presets remain byte-identical, listed and readable after migration");
            service.Apply(archiveId,new Dictionary<string,string>{{"screen","screen"}});
            check(store.Bindings().Single().AssetId.StartsWith("preset-",StringComparison.Ordinal),"A migrated local preset can still apply successfully");
            store.SetSetting("owner","changed-after-migration");
        }
        var metadata=JsonNode.Parse(File.ReadAllText(Path.Combine(active,"unknown-user-files","paths.json")))!;
        check(metadata["owned"]!.GetValue<string>()==Path.Combine(active,"wallpapers","modular.mp4")&&metadata["outside"]!.GetValue<string>()==external,"Standalone JSON metadata uses relocated internal paths and unchanged external paths");
        int batches=Directory.GetDirectories(Path.Combine(local,Recovery)).Length;
        check(UnifiedDataMigration.PrepareDefaultRoot(local)==active&&Owner(active)=="changed-after-migration"&&Directory.GetDirectories(Path.Combine(local,Recovery)).Length==batches,"Repeated startup preserves new canonical edits and does not reimport the old modular profile");
    }

    private static void InterruptedMigration(string root,Action<bool,string> check)
    {
        string committed=Fixture(root,"committed-before-journal-cleanup"), canonical=Path.Combine(committed,"LumaWall");Seed(canonical,"committed-new-data");
        var state=Pending(committed);WriteMarker(canonical,state.Id);
        check(UnifiedDataMigration.PrepareDefaultRoot(committed)==canonical&&Owner(canonical)=="committed-new-data"&&!File.Exists(state.Pending),"Recovery after the directory switch preserves committed data and clears only the matching journal");

        string interrupted=Fixture(root,"interrupted-after-original-move");state=Pending(interrupted);
        Seed(Path.Combine(state.Batch,"original-directory"),"recover-original");Seed(Path.Combine(state.Batch,"candidate"),"never-committed");
        canonical=UnifiedDataMigration.PrepareDefaultRoot(interrupted);
        check(Owner(canonical)=="recover-original"&&Owner(Path.Combine(state.Batch,"candidate"))=="never-committed"&&!File.Exists(state.Pending)&&File.Exists(Path.Combine(state.Batch,"interrupted.json")),"Recovery restores the moved original and preserves an uncommitted candidate for inspection");

        string beforeMove=Fixture(root,"interrupted-before-original-move");state=Pending(beforeMove);canonical=Path.Combine(beforeMove,"LumaWall");Seed(canonical,"unchanged-original");
        check(UnifiedDataMigration.PrepareDefaultRoot(beforeMove)==canonical&&Owner(canonical)=="unchanged-original"&&File.Exists(Path.Combine(state.Batch,"interrupted.json")),"An interrupted journal before any directory move preserves the existing original");

        string conflicting=Fixture(root,"interrupted-with-new-data");state=Pending(conflicting);canonical=Path.Combine(conflicting,"LumaWall");Seed(canonical,"new-active-data");Seed(Path.Combine(state.Batch,"original-directory"),"recovery-original");
        check(Reject(()=>UnifiedDataMigration.PrepareDefaultRoot(conflicting))&&Owner(canonical)=="new-active-data"&&Owner(Path.Combine(state.Batch,"original-directory"))=="recovery-original"&&File.Exists(state.Pending),"Recovery refuses to overwrite a newly created canonical profile when the moved original also exists");
    }

    private static void RejectedInputs(string root,Action<bool,string> check)
    {
        foreach(string kind in new[]{"traversal","unsupported-version","oversized","missing-batch"})
        {
            string local=Fixture(root,"invalid-journal-"+kind), canonical=Path.Combine(local,"LumaWall");Seed(canonical,"untouched");
            string recovery=Fixture(local,Recovery), pending=Path.Combine(recovery,"pending.json");
            File.WriteAllText(pending,kind switch {
                "traversal"=>JsonSerializer.Serialize(new {Version=1,RecoveryId="../outside"}),
                "unsupported-version"=>JsonSerializer.Serialize(new {Version=2,RecoveryId=Guid.NewGuid().ToString("N")}),
                "oversized"=>new string(' ',4097),
                _=>JsonSerializer.Serialize(new {Version=1,RecoveryId=Guid.NewGuid().ToString("N")})});
            check(Reject(()=>UnifiedDataMigration.PrepareDefaultRoot(local))&&Owner(canonical)=="untouched"&&File.Exists(pending),"Invalid migration journal fails without touching active data: "+kind);
        }
        string corrupt=Fixture(root,"corrupt-source"), original=Path.Combine(corrupt,"LumaWall"), modular=Fixture(corrupt,"LumaWall.Modular");Seed(original,"original-survives");File.WriteAllText(Path.Combine(modular,"library.db"),"not a SQLite database");
        check(Reject(()=>UnifiedDataMigration.PrepareDefaultRoot(corrupt))&&Owner(original)=="original-survives"&&!File.Exists(Path.Combine(original,Marker)),"An invalid modular database fails before replacing the original profile");

        string locked=Fixture(root,"locked-source-file");original=Path.Combine(locked,"LumaWall");modular=Path.Combine(locked,"LumaWall.Modular");Seed(original,"original-survives");Seed(modular,"modular-survives");
        string lockedPath=Path.Combine(modular,"user-file.bin");File.WriteAllText(lockedPath,"must be preserved");
        using(var file=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.None))
            check(Reject(()=>UnifiedDataMigration.PrepareDefaultRoot(locked))&&Owner(original)=="original-survives"&&!File.Exists(Path.Combine(original,Marker)),"An unreadable personal file fails before replacing either profile");
        check(Owner(UnifiedDataMigration.PrepareDefaultRoot(locked))=="modular-survives"&&File.ReadAllText(Path.Combine(locked,"LumaWall","user-file.bin"))=="must be preserved","A snapshot failure is retryable after the source file becomes available");

        string invalidMarker=Fixture(root,"invalid-active-marker");original=Path.Combine(invalidMarker,"LumaWall");Seed(original,"original-survives");File.WriteAllText(Path.Combine(original,Marker),JsonSerializer.Serialize(new {Version=99,RecoveryId=Guid.NewGuid().ToString("N"),Policy="unknown"}));
        check(Reject(()=>UnifiedDataMigration.PrepareDefaultRoot(invalidMarker))&&Owner(original)=="original-survives","An unsupported completion marker does not silently overwrite the active profile");
    }

    private static void Seed(string root,string owner)
    {
        Directory.CreateDirectory(Path.Combine(root,"wallpapers"));string media=Path.Combine(root,"wallpapers",owner+".mp4");File.WriteAllBytes(media,[0,0,1,2,3,4,5]);
        using(var store=new LibraryStore(root))
        {
            store.SetSetting("owner",owner);
            store.Upsert(new(owner,media,owner,7,1,100,100,"h264","sdr","test"));
            store.SaveBinding(new("screen",owner,FillMode.Cover,false,1));
            store.SaveProfile("screen",owner,new(FillMode.Cover,VideoEffects.Original));
            store.SetSetting("widgets.v1",new WidgetConfiguration().ToJson());
        }
        SqliteConnection.ClearAllPools();
    }

    private static string Batch(string local,string active)
    {
        var marker=JsonNode.Parse(File.ReadAllText(Path.Combine(active,Marker)))!;
        return Path.Combine(local,Recovery,marker["RecoveryId"]!.GetValue<string>());
    }
    private static (string Id,string Batch,string Pending) Pending(string local)
    {
        string recovery=Fixture(local,Recovery), id=Guid.NewGuid().ToString("N"), batch=Fixture(recovery,id), pending=Path.Combine(recovery,"pending.json");
        File.WriteAllText(pending,JsonSerializer.Serialize(new {Version=1,RecoveryId=id}));return(id,batch,pending);
    }
    private static void WriteMarker(string root,string id)=>File.WriteAllText(Path.Combine(root,Marker),JsonSerializer.Serialize(new {Version=1,RecoveryId=id,Policy="test recovery"}));
    private static SqliteConnection Open(string database)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=database,Pooling=false}.ToString());connection.Open();return connection;
    }
    private static string? Value(string root,string key)
    {
        using var connection=Open(Path.Combine(root,"library.db"));using var command=connection.CreateCommand();command.CommandText="SELECT value FROM settings WHERE key=$key";command.Parameters.AddWithValue("$key",key);return command.ExecuteScalar() as string;
    }
    private static string? Owner(string root)=>Value(root,"owner");
    private static void Execute(SqliteConnection connection,string sql) {using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
    private static bool Reject(Action action) {try{action();return false;}catch(Exception ex) when(ex is IOException or InvalidDataException or SqliteException or JsonException or UnauthorizedAccessException){return true;}}
    private static string Digest(string path) {using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}
    private static bool VerifyManifest(string directory,string manifest)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(manifest));
        return json.RootElement.EnumerateArray().All(item=> {
            string file=Path.Combine(directory,item.GetProperty("Path").GetString()!);
            return File.Exists(file)&&new FileInfo(file).Length==item.GetProperty("Bytes").GetInt64()&&Digest(file)==item.GetProperty("Sha256").GetString();
        });
    }
}
