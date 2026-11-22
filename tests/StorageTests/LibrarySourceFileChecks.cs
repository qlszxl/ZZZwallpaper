using System.Text.Json;
using LumaWall.Application;
using LumaWall.Application.Presets;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using LumaWall.Contracts.Steam;
using LumaWall.Storage;
using Microsoft.Data.Sqlite;

internal static class LibrarySourceFileChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        root = Path.Combine(root, "source-file-removal"); Directory.CreateDirectory(root);
        string Make(string relative)
        {
            string file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "isolated test media"); return file;
        }
        static MediaAsset Asset(string id,string path) => new(id,path,id,new FileInfo(path).Length,1,16,16,"test","sdr","test");
        static void Sql(LibraryStore store,string text)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(store.Root,"library.db"),Pooling=false}.ToString());
            db.Open();using var command=db.CreateCommand();command.CommandText=text;command.ExecuteNonQuery();
        }
        bool Throws(Action action) {try {action();return false;}catch(Exception error) when(error is IOException or InvalidDataException or InvalidOperationException or SqliteException or JsonException){return true;}}
        string file = Make("failure.mp4");
        using (var store = new LibraryStore(Path.Combine(root,"failure-db")))
        {
            store.Upsert(Asset("a",file)); store.SaveBinding(new("A","a",FillMode.Cover,false,0));
            store.SavePlayback("A",new() {AssetIds=["a"]}); store.HistoryEnabled=true;
            store.SaveWallpaperVolume("A",42);string revision=store.ConfigurationRevision();long history=store.HistoryRevision;
            check(Throws(()=>store.DeleteSourceFile(file,()=>throw new IOException("test denied"))),"A denied source deletion surfaces an error");
            check(File.Exists(file)&&store.ConfigurationRevision()==revision&&store.HistoryRevision==history,"Denied deletion rolls back library, binding, queues and history");
            check(Throws(()=>store.DeleteSourceFile(file,()=>{}))&&store.ConfigurationRevision()==revision,"A no-op shell result cannot remove the library reference");
            bool called=false;
            Sql(store,"CREATE TRIGGER fail_remove BEFORE DELETE ON media BEGIN SELECT RAISE(ABORT,'test SQL failure'); END");
            check(Throws(()=>store.DeleteSourceFile(file,()=>{called=true;File.Delete(file);}))&&!called&&File.Exists(file),"SQL failure is detected before any irreversible file operation");
            Sql(store,"DROP TRIGGER fail_remove");
            store.SetSetting("playback:A","{\"AssetIds\":42}");
            check(Throws(()=>store.DeleteSourceFile(file,()=>{called=true;File.Delete(file);}))&&!called&&File.Exists(file),"Malformed queues reject deletion before touching a file");
        }
        file=Make("success.mp4");string other=Make("unrelated.mp4");
        using(var store=new LibraryStore(Path.Combine(root,"success-db")))
        {
            store.Upsert(Asset("a",file));store.Upsert(Asset("other",other));
            Sql(store,"INSERT INTO media SELECT 'alias',upper(path),name,bytes,duration,width,height,codec,color,decoder FROM media WHERE id='a'");
            store.SaveBinding(new("A","a",FillMode.Cover,false,0));store.SaveBinding(new("B","alias",FillMode.Contain,true,0));
            store.SaveBinding(new("C","other",FillMode.Cover,false,0));
            store.SavePlayback("A",new() {AssetIds=["a","other","alias"],CustomPlaylists=[new(){Id="list",Name="kept",AssetIds=["alias","other"]}],PlaylistId="list"});
            store.HistoryEnabled=true;
            store.SavePlayback("A",store.Playback("A") with {IntervalSeconds=777});
            store.BreakUndoGesture();store.SaveProfile("unrelated-screen","",new(FillMode.Contain,VideoEffects.Original with {Brightness=.3}));
            store.BreakUndoGesture();store.SaveEffectLayers("C","other",new(FillMode.Cover,VideoEffects.Original),
                new AssetEffects {Overrides=EffectKind.Color,Effects=VideoEffects.Original with {Brightness=.25}});
            string? lastLabel=store.UndoLabel;
            var service=new PresetService(store,store.Root);
            var saved=service.SaveAsync(service.Capture("local source reference",["A"],new PresetSelection {Playing=true,Library=true,Effects=false,Widgets=false})).GetAwaiter().GetResult();
            check(service.FindFileReferences(file).Presets.Single().Id==saved.Id,"Preset impact lookup finds local original-file references without copying media");
            var result=store.DeleteSourceFile(file,()=>File.Delete(file));
            check(!File.Exists(file)&&File.Exists(other)&&result.AssetIds.Order().SequenceEqual(new[]{"a","alias"}),"A successful file removal deletes exactly the confirmed file and all same-path rows");
            check(store.Assets().Single().Id=="other"&&store.Bindings().Single().ScreenId=="C","Unrelated assets and playing screens survive deletion");
            var playback=store.Playback("A");
            check(playback.AssetIds.SequenceEqual(["other"])&&playback.CustomPlaylists!.Single().AssetIds.SequenceEqual(["other"])&&playback.IntervalSeconds==777,"Main and custom queues lose invalid references while preserving other settings");
            check(service.List().Single().Id==saved.Id&&service.FindFileReferences(file).Presets.Length==1,"Deleting a source leaves local preset metadata available for repair");
            check(store.UndoLabel==lastLabel,"Source deletion preserves unrelated material edit history");
            store.ReplayUndo(false);
            check(store.AssetProfile("other").Effects.Brightness==0&&!File.Exists(file),"Undo of an unrelated material edit neither restores nor claims to restore a deleted source");
            store.ReplayUndo(false);
            check(store.ScreenProfile("unrelated-screen")==null&&store.UndoLabel==null,"Unrelated screen undo remains intact and deleted-file queue undo is invalidated");
        }
        foreach (var (relative,marker,allow) in new[] {
            ("steamapps/workshop/content/431960/123/video.mp4",false,false),
            ("wallpaper_engine/projects/myprojects/custom/video.mp4",false,false),
            ("custom-project/video.mp4",true,false),
            ("copied/video.mp4",true,true)
        })
        {
            file=Make(relative);
            using var store=new LibraryStore(Path.Combine(root,Guid.NewGuid().ToString("N")));
            store.Upsert(Asset("a",file));
            if(marker)store.SetSetting(SteamSourceIdentity.SettingKey("a"),JsonSerializer.Serialize(
                new SteamWallpaperSource("s",Path.GetDirectoryName(file)!,"123","video",allow?Path.Combine(root,"original-steam.mp4"):file,file,allow,null)));
            bool called=false;
            bool rejected=Throws(()=>store.DeleteSourceFile(file,()=>{called=true;File.Delete(file);}));
            check(allow?!rejected&&called&&!File.Exists(file):rejected&&!called&&File.Exists(file),
                allow?"Copied Steam media outside the managed source is deletable":"Steam originals are protected before the file callback: "+relative);
        }
        file=Make("changed.mp4");var snapshot=LibrarySourceFiles.Inspect(file);
        File.AppendAllText(file,"changed");
        check(Throws(()=>LibrarySourceFiles.RequireUnchanged(snapshot))&&File.Exists(file),"A changed source is rejected before destructive operations");
        check(Throws(()=>LibrarySourceFiles.Normalize("relative.mp4"))&&Throws(()=>LibrarySourceFiles.Normalize(Path.GetPathRoot(file)!))&&
            Throws(()=>LibrarySourceFiles.Normalize(file+":stream")),"Relative, root and alternate-stream paths cannot be source deletion targets");
    }
}
