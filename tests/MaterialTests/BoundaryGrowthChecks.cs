using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.ControlCenter;
using Microsoft.Data.Sqlite;

internal static class BoundaryGrowthChecks
{
    internal static async Task AutomationBounds(string root,Action<bool,string> check)
    {
        using var store=new LibraryStore(root);var ui=new MainWindow(store);
        async Task<string> Revision(string id)=>(await ui.Call("effect_get",new {screenId="left",assetId=id,scope="asset"})).Data.GetProperty("revision").GetString()!;
        for(int i=0;i<40;i++) {
            string id="asset-"+i;store.Upsert(new(id,Path.Combine(root,id+".mp4"),id,8,0,100,100,"test","SDR","Software"));
            await ui.Call("effect_clear",new {screenId="left",assetId=id,scope="asset",expectedRevision=await Revision(id)});
        }
        check(ui.ScopedUndoCount==32,"MCP per-target undo snapshots are bounded after editing 40 materials");
        check(ui.LastEffectRefresh is {Screens.Length:0,Assets:["asset-39"]},"Production MCP material edit refreshes only its material consumers");
        bool expired=false;
        try {await ui.Call("effect_undo",new {screenId="left",assetId="asset-0",scope="asset",expectedRevision=await Revision("asset-0")});}
        catch(OperationFault ex) when(ex.Code=="invalid_state") {expired=true;}
        check(expired,"Evicted MCP single-step undo fails explicitly");
        await ui.Call("effect_undo",new {screenId="left",assetId="asset-39",scope="asset",expectedRevision=await Revision("asset-39")});
        check(ui.ScopedUndoCount==31&&store.AssetProfile("asset-39").Overrides==0,"Recent MCP single-step undo remains functional");
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        Projection(check);
        Import(Path.Combine(root,"atomic-import"),check);
        Resources(Path.Combine(root,"resource-growth"),check);
        Files(Path.Combine(root,"retention"),check);
    }
    private static void Projection(Action<bool,string> check)
    {
        var effects=new VideoEffects {Collection=new() {Added=EffectCollection.All},AudioReactive=true,AudioLegacy=false,
            AudioGlow=true,RgbSplit=true,Glow=true,AudioRules=[new() {Parameter="Brightness",Strong=.1},new() {Parameter="RgbOffset"},new() {Parameter="GlowStrength",Enabled=false}]};
        string original=effects.ToJson();var projected=EffectProjection.Isolate(effects,EffectKind.AudioReactive);
        check(projected.GetCollection().Added==(EffectKind.AudioReactive|EffectKind.Color|EffectKind.RgbSplit),"Audio solo includes custom targets and excludes disabled rules and dormant legacy mappings");
        check(effects.ToJson()==original&&projected.AudioRules.SequenceEqual(effects.AudioRules),"Projection preserves saved parameters and rules");
        var removed=effects with {Collection=new() {Added=EffectKind.AudioReactive},RgbSplit=false};
        check(EffectProjection.Isolate(removed,EffectKind.AudioReactive).GetCollection().Added==EffectKind.AudioReactive,"Audio solo never re-adds a removed target");
        check(!EffectProjection.Isolate(effects with {RgbSplit=false},EffectKind.AudioReactive).ForRendering().RgbSplit,"Audio solo never enables a disabled target");
        var legacy=effects with {AudioLegacy=true,AudioRules=[]};
        check(EffectProjection.Isolate(legacy,EffectKind.AudioReactive).GetCollection().Added==EffectKind.AudioReactive,"A retired classic switch cannot add a visual target without an explicit rule");
        check(EffectProjection.Isolate(effects,EffectKind.Glow).GetCollection().Added==EffectKind.Glow,"Ordinary solo isolates only the requested effect");
        check(EffectChange.Screen("left").Affects("left","a")&&!EffectChange.Screen("left").Affects("right","a"),"Screen edits refresh only their screen");
        check(EffectChange.Asset("a").Affects("right","a")&&!EffectChange.Asset("a").Affects("left","b"),"Material edits refresh every use of that material, not other materials");
    }
    private static JsonElement History(LibraryStore store)=>JsonSerializer.SerializeToElement(store.HistoryDetails());
    private static VideoEffects Mask(int n)
    {
        var pixels=new byte[MaskPixels.Bytes];Array.Fill(pixels,(byte)(n%251));BitConverter.GetBytes(n).CopyTo(pixels,0);
        return new() {Collection=new() {Added=EffectKind.Glow|EffectKind.MaskLibrary},Glow=true,
            Masks=[new() {Id="test-mask",Name="test",Shape=5,Image=MaskPixels.Encode(pixels)}],EffectMasks=new() {[EffectKind.Glow]="test-mask"}};
    }
    private static SqliteConnection Open(string root)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(root,"library.db"),Pooling=false}.ToString());connection.Open();return connection;
    }
    private static long Scalar(SqliteConnection db,string sql)
    {
        using var command=db.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void Execute(SqliteConnection db,string sql)
    {
        using var command=db.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();
    }
    private static void Import(string root,Action<bool,string> check)
    {
        using var store=new LibraryStore(root);using var db=Open(root);
        var first=new WidgetConfiguration {Widgets=[new() {Id="before",Kind=WidgetKind.Clock}]};
        var next=first with {Widgets=[first.Widgets[0] with {X=.8}]};
        var baseline=new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
        store.SetSetting("widgets.v1",first.ToJson());store.SaveProfile("left","",baseline);
        store.HistoryEnabled=true;
        store.SaveProfile("left","",baseline with {Fill=FillMode.Contain});
        var imports=new ConfigurationImportService(store);var history=new HistoryService(store);
        var profile=new WallpaperProfile(FillMode.Cover,Mask(1000));
        long before=store.HistoryRevision;string revision=store.ConfigurationRevision();
        imports.Apply(next,"left",profile,revision);
        check(store.HistoryRevision==before+1&&History(store).GetProperty("undoCount").GetInt32()==2,"Shared import adds one atomic undo entry and retains previous history");
        check(store.GetSetting("widgets.v1")==next.ToJson()&&store.ScreenProfile("left")!.Effects.ToJson()==profile.Effects.ToJson(),"Import commits components and effects together");
        bool conflict=false;
        try {imports.Apply(first,"left",baseline,revision);}catch(ConfigurationConflictException) {conflict=true;}
        check(conflict&&store.HistoryRevision==before+1,"Stale import fails without changing configuration or history");
        var undone=history.Restore(false,store.HistoryRevision.ToString());
        check(store.GetSetting("widgets.v1")==first.ToJson()&&store.ScreenProfile("left")!.Fill==FillMode.Contain&&store.GetSetting("sharing.undo.v1")==null,"Normal undo restores the complete pre-import state");
        check(undone.Effects.Screens.SequenceEqual(new[]{"left"})&&undone.Effects.Assets.Length==0,"Import undo identifies only its affected screen");
        history.Restore(false);check(store.ScreenProfile("left")!.Fill==FillMode.Cover,"History before an import remains usable");
        history.Restore(true);history.Restore(true);store.MaintainStorage();
        check(store.ScreenProfile("left")!.Effects.Masks[0].Image==profile.Effects.Masks[0].Image,"Redo survives resource maintenance");
        imports.Restore(store.ConfigurationRevision());
        check(store.GetSetting("widgets.v1")==first.ToJson()&&store.ScreenProfile("left")!.Fill==FillMode.Contain&&store.GetSetting("sharing.undo.v1")==null,"Dedicated import restore uses the same atomic persistence path");
        history.Restore(false);check(store.GetSetting("widgets.v1")==next.ToJson()&&imports.ReadBackup()!=null,"Dedicated import restore can itself be undone");
        string unchanged=store.ConfigurationRevision();long historyRevision=store.HistoryRevision,resourceCount=Scalar(db,"SELECT COUNT(*) FROM effect_resource");
        Execute(db,"CREATE TRIGGER fail_shared BEFORE UPDATE ON screen_profile BEGIN SELECT RAISE(ABORT,'injected disk failure'); END");
        bool failed=false;
        try {imports.Apply(first,"left",profile with {Effects=Mask(1001)},unchanged);}catch(SqliteException) {failed=true;}
        Execute(db,"DROP TRIGGER fail_shared");
        check(failed&&store.ConfigurationRevision()==unchanged&&store.HistoryRevision==historyRevision,"Failure after component writes rolls back data and preserves undo history");
        check(Scalar(db,"SELECT COUNT(*) FROM effect_resource")==resourceCount,"Failed import rolls back newly inserted mask pixels");
        history.Restore(false);check(store.GetSetting("widgets.v1")==first.ToJson(),"Undo remains usable after a failed import");
        bool staleHistory=false;try {history.Restore(true,"stale");}catch(ConfigurationConflictException) {staleHistory=true;}
        check(staleHistory,"History service rejects stale revision before replay");
        string existingEffects=store.ScreenProfile("left")!.Effects.ToJson();
        var componentsOnly=new LumaWall.Contracts.Sharing.SharedConfiguration(VideoEffects.Original,next,false,[]);
        var widgetsOnly=imports.Apply(componentsOnly,"left",store.ConfigurationRevision());
        check(widgetsOnly.Profile==null&&store.ScreenProfile("left")!.Effects.ToJson()==existingEffects,"UI and MCP component-only imports preserve existing display effects");
        imports.Apply(componentsOnly with {HasEffects=true},"left",store.ConfigurationRevision());
        check(store.ScreenProfile("left")!.Effects.IsNeutral,"Explicitly empty shared effects restore the original image without requiring a material");
    }
    private static void Resources(string root,Action<bool,string> check)
    {
        using(var store=new LibraryStore(root)) {
            using var db=Open(root);store.HistoryEnabled=true;
            long firstPages=0,secondPages=0;
            for(int i=0;i<400;i++) {
                store.BreakUndoGesture();store.SaveProfile("left","",new(FillMode.Cover,Mask(i)));
                if(i%10==9)store.MaintainStorage();
                if(i==199)firstPages=Scalar(db,"PRAGMA page_count");
            }
            secondPages=Scalar(db,"PRAGMA page_count");
            check(History(store).GetProperty("undoCount").GetInt32()==100&&Scalar(db,"SELECT COUNT(*) FROM effect_resource")==101,"400 distinct masks retain only 100 undo steps plus the preceding live resource");
            check(secondPages<=firstPages+16,"SQLite reuses deleted pages instead of growing with every edit");
            string wal=Path.Combine(root,"library.db-wal");check(!File.Exists(wal)||new FileInfo(wal).Length==0,"Maintenance truncates the completed WAL");
            for(int i=0;i<50;i++)store.ReplayUndo(false);
            store.MaintainStorage();
            check(Scalar(db,"SELECT COUNT(*) FROM effect_resource")==101&&store.ScreenProfile("left")!.Effects.Masks[0].Image==Mask(349).Masks[0].Image,"Maintenance protects both undo and redo resource references");
            for(int i=0;i<50;i++)store.ReplayUndo(true);
            check(store.ScreenProfile("left")!.Effects.Masks[0].Image==Mask(399).Masks[0].Image,"All retained redos recover exact mask pixels");
            Console.WriteLine($"GROWTH masks=400 retained=101 pagesAfter200={firstPages} pagesAfter400={secondPages}");
        }
        using(var reopened=new LibraryStore(root)) {
            using var db=Open(root);
            check(Scalar(db,"SELECT COUNT(*) FROM effect_resource")==1&&reopened.ScreenProfile("left")!.Effects.Masks[0].Image==Mask(399).Masks[0].Image,"Restart reclaims expired session history resources while preserving the active mask");
        }
    }
    private static void Files(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        string Write(string folder,string name,int size=8,int ageDays=2) {
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,name);File.WriteAllBytes(path,new byte[size]);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-ageDays));return path;
        }
        string cache=Path.Combine(root,"pixels"),unrelated=Write(cache,"my-original.rgba");
        string pinned=Write(cache,new string('F',64)+".rgba",ageDays:30);
        for(int cycle=0;cycle<3;cycle++) {
            for(int i=0;i<300;i++)Write(cache,(cycle*300+i).ToString("X64")+".rgba");
            OwnedFileRetention.Prune(cache,n=>OwnedFileRetention.HexName(n,".rgba",64),128,128*1024*1024,TimeSpan.FromDays(7),[pinned]);
            check(Directory.GetFiles(cache,"*.rgba").Length==129&&File.Exists(pinned)&&File.Exists(unrelated),"Repeated cache churn stays at 128 owned files and preserves selected/unowned files");
        }
        OwnedFileRetention.Prune(cache,n=>OwnedFileRetention.HexName(n,".rgba",64),128,16,TimeSpan.FromDays(7),[pinned]);
        check(Directory.GetFiles(cache,"*.rgba").Length==3,"Cache byte budget also applies below the file-count limit");
        string recent=Write(cache,new string('E',64)+".rgba",ageDays:0);
        OwnedFileRetention.Prune(cache,n=>OwnedFileRetention.HexName(n,".rgba",64),0,0,TimeSpan.Zero,recentGrace:TimeSpan.FromMinutes(2));
        check(File.Exists(recent)&&File.Exists(unrelated)&&Directory.GetFiles(cache,"*.rgba").Length==2,"In-flight files receive a short grace period; old generated files are removed");
        string expired=Write(cache,Guid.NewGuid().ToString("N")+".tmp"),fresh=Write(cache,Guid.NewGuid().ToString("N")+".tmp",ageDays:0);
        OwnedFileRetention.Pixels(cache,".rgba",recent);
        check(!File.Exists(expired)&&File.Exists(fresh),"Abandoned pixel staging files expire without removing active writes");
        using(var store=new LibraryStore(Path.Combine(root,"library"))) {
            string original=Write(root,"source.mp4"),id=Guid.NewGuid().ToString("N");
            store.Upsert(new(id,original,"source",8,0,100,100,"test","SDR","Software"));
            Write(Path.GetDirectoryName(store.Poster(id))!,Path.GetFileName(store.Poster(id)));
            store.SetSetting("assetAudio:"+id,"true");store.SetSetting("poster.v4:"+id,"test");store.HistoryEnabled=true;store.Remove(id);store.MaintainStorage();
            check(File.Exists(store.Poster(id))&&store.GetSetting("assetAudio:"+id)!=null,"Removed material poster and metadata are retained while undo can restore them");
            store.ReplayUndo(false);check(store.Assets().Any(a=>a.Id==id)&&File.Exists(store.Poster(id)),"Undo restores a removed material with its poster");
            store.ReplayUndo(true);
            for(int i=0;i<101;i++) {store.BreakUndoGesture();store.SaveProfile("left","",new(FillMode.Cover,new() {Brightness=i/1000d}));}
            store.MaintainStorage();
            check(!File.Exists(store.Poster(id))&&store.GetSetting("assetAudio:"+id)==null&&store.GetSetting("poster.v4:"+id)==null&&File.Exists(original),"Expired removal history reclaims generated posters and metadata, preserving the source file");
            string preview=Path.Combine(store.Root,"effect-preview");
            string selected=Write(preview,"custom-"+Guid.NewGuid().ToString("N")+".png",ageDays:30);
            store.SetSetting("effectPreviewCustom",selected);
            for(int i=0;i<80;i++) {
                Write(preview,"custom-"+Guid.NewGuid().ToString("N")+".jpg");
                Write(preview,"mcp-poster-"+i.ToString("X64")+".bmp");
                Write(Path.Combine(preview,"cache"),Guid.NewGuid().ToString("N")+".bmp");
            }
            for(int i=0;i<150;i++)Write(Path.Combine(store.Root,"automation-artifacts"),Guid.NewGuid().ToString("N")+".json");
            store.MaintainPreviewFiles();
            check(File.Exists(selected)&&Directory.GetFiles(preview,"custom-*").Length==1,"Replacing preview images reclaims old copies and retains the selected image");
            check(Directory.GetFiles(preview,"mcp-poster-*").Length==64&&Directory.GetFiles(Path.Combine(preview,"cache")).Length==0,"Generated poster and abandoned renderer frame caches have retention limits");
            check(Directory.GetFiles(Path.Combine(store.Root,"automation-artifacts")).Length==128,"Internal automation output is bounded");
        }
        string outside=Path.Combine(root,"link-target"),protectedFile=Write(outside,Guid.NewGuid().ToString("N")+".bmp");
        string link=Path.Combine(root,"link");
        try {
            Directory.CreateSymbolicLink(link,outside);
            check(!OwnedFileRetention.SafeDirectory(link)&&OwnedFileRetention.Prune(link,_=>true,0,0,TimeSpan.Zero)==0&&File.Exists(protectedFile),"Retention refuses linked cache directories");
            string nested=Path.Combine(outside,"nested");Write(nested,Guid.NewGuid().ToString("N")+".bmp");
            check(!OwnedFileRetention.SafeDirectory(Path.Combine(link,"nested")),"Retention refuses linked ancestors as well as direct links");
        }catch(UnauthorizedAccessException) {Console.WriteLine("SKIP symlink fixture: OS does not permit creating symbolic links");}
    }
}
