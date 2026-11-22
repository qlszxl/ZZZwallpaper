using LumaWall.Contracts;
using LumaWall.Storage;
using LumaWall.Application.Presets;
using Microsoft.Data.Sqlite;

internal static class Refinement81Checks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        string legacy=Path.Combine(root,"position-81");Directory.CreateDirectory(legacy);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(legacy,"library.db"),Pooling=false}.ToString())) {
            db.Open();using var command=db.CreateCommand();command.CommandText="CREATE TABLE binding(screen TEXT PRIMARY KEY,asset TEXT NOT NULL,fill INTEGER NOT NULL,paused INTEGER NOT NULL,revision INTEGER NOT NULL); CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL); INSERT INTO binding VALUES('left','asset-a',0,0,1); INSERT INTO settings VALUES('wallpaper.position:left',$position)";
            command.Parameters.AddWithValue("$position",new WallpaperPosition(12,-24,130).ToJson());command.ExecuteNonQuery();
        }
        using(var store=new LibraryStore(legacy)) {
            check(store.ReadWallpaperPosition("left","asset-a")==new WallpaperPosition(12,-24,130),"Legacy screen placement migrates to its bound asset");
            check(store.ReadWallpaperPosition("left","asset-b")==new WallpaperPosition()&&store.ReadWallpaperPosition("right","asset-a")==new WallpaperPosition(),"Other assets and screens start independently centered");
            store.HistoryEnabled=true;
            store.SetSetting(LibraryStore.WallpaperPositionKey("left","asset-b"),new WallpaperPosition(-40,50,90).ToJson());
            check(store.ReadWallpaperPosition("left","asset-a").Scale==130&&store.ReadWallpaperPosition("left","asset-b").Scale==90,"Editing one material does not overwrite another material");
            check(store.UndoLabel=="调整壁纸位置","Material position joins existing undo history");
        }
        using(var store=new LibraryStore(legacy)) {
            check(store.ReadWallpaperPosition("left","asset-a").Scale==130&&store.ReadWallpaperPosition("left","asset-b").Scale==90,"Material placements survive reopening without repeated migration");
            CheckPresets(store,check);
            CheckShortcutReferences(store,check);
        }
    }

    private static void CheckPresets(LibraryStore store,Action<bool,string> check)
    {
        var presets=new PresetService(store,store.Root);
        check(presets.List().Count==0,"Fresh preset list is empty");
        var document=presets.Capture("清透柔光",["left"],new() {Playing=false,Effects=true,Widgets=false});
        var saved=presets.SaveAsync(document).GetAwaiter().GetResult();
        string zip=Path.Combine(store.Root,"user-preset.zip");
        presets.ExportAsync(saved.Id,zip).GetAwaiter().GetResult();
        var imported=presets.ImportAsync(zip).GetAwaiter().GetResult();
        for(int i=1;i<=4;i++) {
            string id="b810000000000000000000000000000"+i;
            string directory=Path.Combine(store.Root,"presets",id);
            Directory.CreateDirectory(directory);
            foreach(var path in Directory.EnumerateFiles(Path.Combine(store.Root,"presets",saved.Id)))
                File.Copy(path,Path.Combine(directory,Path.GetFileName(path)));
            string info=Path.Combine(directory,"info.json");
            File.WriteAllText(info,File.ReadAllText(info).Replace(saved.Id,id));
            File.WriteAllBytes(Path.Combine(store.Root,"builtin-preset-"+id+".installed"),[1]);
        }
        check(presets.List().Count==2,"Retired IDs are excluded even before cleanup");
        for(int i=0;i<2;i++) {
            presets=new PresetService(store,store.Root);
            check(presets.List().Select(p=>p.Id).ToHashSet().SetEquals([saved.Id,imported.Id])
                &&presets.Read(saved.Id).Name==document.Name&&presets.Read(imported.Id).Name==document.Name,
                "Saved and imported presets with a sample's name survive migration and reopening");
        }
        check(Directory.GetDirectories(Path.Combine(store.Root,"presets")).Length==2
            &&Directory.GetFiles(store.Root,"builtin-preset-*.installed").Length==0,"Former preset directories and markers are removed");
    }

    private static void CheckShortcutReferences(LibraryStore store,Action<bool,string> check)
    {
        var first=new WidgetInstance {Id="one",Kind=WidgetKind.Shortcuts,Screen="left",ShortcutId="shared"};
        var hidden=first with {Id="two",Screen="right",Enabled=false};
        var state=new WidgetConfiguration {
            Widgets=[first,hidden],
            Shortcuts=[new("shared","Site","https://example.com","common",0),
                new("old","Site","https://example.com","common",1)]
        };
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(store.Root,"library.db")}.ToString())) {
            db.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="INSERT INTO settings(key,value) VALUES('widgets.v1',$value)";
            cmd.Parameters.AddWithValue("$value",state.ToJson());cmd.ExecuteNonQuery();
        }
        WidgetConfiguration Read()=>WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
        store.ReconcileWidgetSettings();store.ReconcileWidgetSettings();
        state=Read();
        check(state.Shortcuts.Single().Id=="shared"&&state.Widgets.Length==2,"Upgrade removes orphaned IDs from the stored configuration");
        var presets=new PresetService(store,store.Root);
        var preset=presets.Capture("Shared",["left"],new() {Playing=false,Effects=false,Widgets=true,IncludeWidgetPrivateData=true});
        var map=new Dictionary<string,string> {["left"]="left"};
        presets.Apply(preset,map);var applied=Read();presets.Apply(preset,map);state=Read();
        check(applied.Shortcuts.SequenceEqual(state.Shortcuts)&&state.Shortcuts.Length==2
            &&state.Shortcuts.All(c=>state.Widgets.Any(w=>w.ShortcutId==c.Id)),
            "Reapplying a preset preserves a hidden card on another display and creates no unused copies");
        var changed=state.Widgets.Single(w=>w.Screen=="left") with {ShortcutId="shared"};
        store.SetSetting("widgets.v1",(state with {Widgets=[changed,hidden]}).ToJson());
        check(Read().Shortcuts.Single().Id=="shared","Switching cards removes the previous card's last reference");
        store.SetSetting("widgets.v1",(Read() with {Widgets=[hidden]}).ToJson());
        check(Read().Shortcuts.Single().Id=="shared","A hidden widget alone retains its card");
        store.HistoryEnabled=true;store.BreakUndoGesture();
        store.SetSetting("widgets.v1",(Read() with {Widgets=[]}).ToJson());
        check(Read().Shortcuts.Length==0,"Removing the final widget removes its stored card");
        store.ReplayUndo(false);
        check(Read().Widgets.Single().Id==hidden.Id&&Read().Shortcuts.Single().Id=="shared","Undo restores the widget and its card together");
        store.ReplayUndo(true);store.ReconcileWidgetSettings();
        check(Read().Shortcuts.Length==0,"Redo and reloading keep the picker empty");
    }
}
