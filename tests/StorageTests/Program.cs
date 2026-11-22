using LumaWall.Contracts;
using LumaWall.Storage;
using Microsoft.Data.Sqlite;

int checks=0;
void Check(bool ok,string name) {if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
string root=Path.GetFullPath(Path.Combine("artifacts","storage-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
using(var old=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(root,"library.db"),Pooling=false}.ToString())) {
    old.Open();using var cmd=old.CreateCommand();cmd.CommandText="CREATE TABLE binding(screen TEXT PRIMARY KEY,asset TEXT NOT NULL,fill INTEGER NOT NULL,paused INTEGER NOT NULL,revision INTEGER NOT NULL); INSERT INTO binding VALUES('left','sample',1,1,7); PRAGMA user_version=1";cmd.ExecuteNonQuery();
}
var fx=new VideoEffects {Collection=new() {Added=EffectKind.Color|EffectKind.Scanlines|EffectKind.RgbSplit|EffectKind.Softening|EffectKind.Vignette|EffectKind.Glow|EffectKind.Noise},Scanlines=true,RgbSplit=true,Softening=true,Contrast=1.05,Saturation=.95,Vignette=true,Glow=true,Brightness=.12,Noise=true,NoiseStrength=.008};
using(var store=new LibraryStore(root)) {
    var migrated=store.Bindings().Single();Check(migrated.Effects.IsNeutral&&migrated.UserPaused&&migrated.Fill==FillMode.Contain&&migrated.Revision==7,"v1 database upgrades without losing existing binding or pause");
    Check(store.GetSetting("fpsLimit") == null && store.GetSetting("pause.HighResourceLoad") == null && store.GetSetting("audioAutoAdapt") == null,
        "Upgrading a library with no saved preferences preserves its implicit defaults");
    store.SaveProfile("left","sample",new(FillMode.Cover,fx));
    Check(store.Bindings().Single().Effects.ToJson()==fx.ToJson(),"Display profile persistence updates saved binding effects without claiming renderer application");
    Check(store.Profile("right","sample")!.Effects.IsNeutral&&store.Profile("left","new-video")!.Effects.ToJson()==fx.ToJson(),"Display defaults are shared across materials and isolated across screens");
    store.SaveBinding(migrated with {Effects=fx,Fill=FillMode.Cover,Revision=8},true);
    Check(store.Bindings().Single().Effects.ToJson()==fx.ToJson()&&store.Profile("left","sample")!.Effects.ToJson()==fx.ToJson(),"apply commits binding and saved profile together");
    store.SaveProfile("left","sample",new(FillMode.Contain,VideoEffects.Original));
    store.SaveBinding(store.Bindings().Single() with {UserPaused=false});
    Check(store.Profile("left","sample")!.Effects.IsNeutral&&store.Bindings().Single().Effects.IsNeutral,"Pause retains the current saved display effects");
    try {store.SaveBinding(migrated with {Effects=fx with {Brightness=double.NaN}},true);throw new Exception("invalid parameters accepted");}catch(ArgumentException) {}
    Check(store.Bindings().Single().Effects.IsNeutral,"invalid update rolls back instead of destroying prior effects");
}
using(var store=new LibraryStore(root)) {
    Check(store.Bindings().Single().Effects.IsNeutral&&store.Profile("left","sample")!.Effects.IsNeutral,"Reopen preserves saved binding and display defaults");
    store.Remove("sample");Check(store.Bindings().Count==0&&store.AssetProfile("sample").Overrides==0&&store.ScreenProfile("left")!=null,"Removing a material clears its binding/overrides and preserves display defaults");
}
using(var old=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(root,"library.db"),Pooling=false}.ToString())) {
    old.Open();using var cmd=old.CreateCommand();cmd.CommandText="INSERT INTO binding VALUES('legacy','sample',0,1,9,'{\"Version\":1,\"ScreenTexture\":true,\"TextureStrength\":0.6,\"Glow\":true,\"GlowStrength\":0.4}')";cmd.ExecuteNonQuery();
}
using(var store=new LibraryStore(root)) {
    var legacy=store.Bindings().Single();Check(legacy.UserPaused&&legacy.Revision==9&&legacy.Effects.Version==VideoEffects.Original.Version&&legacy.Effects.Scanlines&&legacy.Effects.GlowStrength==.2,"existing v1 effect JSON migrates without changing screen, revision or pause");
    store.SaveBinding(legacy,true);Check(store.Profile("legacy","sample")!.Effects.ToJson()==legacy.Effects.ToJson(),"Migrated display scheme round trips without further drift");
}
using(var store=new LibraryStore(Path.Combine(root,"module-boundaries"))) {
    var settings = new SettingsService(store);
    Check(settings.FpsLimit() == 30 && settings.PausePolicy().HasFlag(PauseReason.HighResourceLoad) && settings.Get("audioAutoAdapt") == "false",
        "A new library caps playback at 30 FPS, pauses on high load and disables adaptive audio");
    settings.Set("fpsLimit", "0");
    settings.Set("pause.HighResourceLoad", "false");
    settings.Set("audioAutoAdapt", "true");
    using (var reopened = new LibraryStore(store.Root)) {
        var saved = new SettingsService(reopened);
        Check(saved.FpsLimit() == 0 && !saved.PausePolicy().HasFlag(PauseReason.HighResourceLoad) && saved.Get("audioAutoAdapt") == "true",
            "Reopening a library preserves explicit overrides of all three installation defaults");
    }
    store.SaveWallpaperVolume("left",42);store.HistoryEnabled=true;
    store.SaveWallpaperVolume("left",0);
    Check(store.WallpaperVolume("left")==42&&store.GetSetting("videoSound:left")=="0","Muting preserves the prior audible volume after the storage extraction");
    store.ReplayUndo(false);
    Check(store.WallpaperVolume("left")==42&&store.GetSetting("videoSound:left")=="1","Volume undo restores sound state atomically");
    store.SetSetting("playback:left","{\"Mode\":0,\"AssetIds\":[\"a\",\"b\"]}");
    var playback=store.Playback("left");
    Check(playback.Version==2&&playback.Mode==PlaybackMode.Automatic&&playback.AssetIds.SequenceEqual(["a","b"]),"Legacy playback migration retains queue order and adopts the current default mode");
    store.SavePlayback("left",playback with {Mode=PlaybackMode.SingleLoop});
    Check(store.Playback("left").Mode==PlaybackMode.SingleLoop,"Explicit current-version playback preferences survive the migration boundary");
}
using(var store=new LibraryStore(Path.Combine(root,"background-effects"))) {
    var original=new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
    store.SaveProfile("left","",original);store.HistoryEnabled=true;
    var effects=new EffectsService(store);
    long before=store.HistoryRevision;
    var pending=store.QueueEffectWrite(effects.Prepare("left",null,original with {Effects=original.Effects with {Brightness=.4}},original.Effects));
    var saved=store.ScreenProfile("left")!;
    Check(pending.IsCompletedSuccessfully&&saved.Effects.Brightness==.4&&store.HistoryRevision==before+1,"The next storage read waits for a background effect commit and captures one undo record");
    store.CompleteEffectWrite(pending);
    Check(store.HistoryRevision==before+1,"Repeated completion does not duplicate the background write history");
    new HistoryService(store).Restore(false);
    Check(store.ScreenProfile("left")!.Effects.Brightness==0,"Background effect commits remain undoable through the extracted history service");
    using var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(store.Root,"library.db"),Pooling=false}.ToString());db.Open();
    using var command=db.CreateCommand();
    command.CommandText="CREATE TRIGGER fail_background BEFORE UPDATE ON screen_profile BEGIN SELECT RAISE(ABORT,'injected write failure'); END";command.ExecuteNonQuery();
    before=store.HistoryRevision;
    store.QueueEffectWrite(effects.Prepare("left",null,original with {Effects=original.Effects with {Brightness=.2}},original.Effects));
    bool failed=false;try {store.CompleteEffectWrite();}catch(SqliteException) {failed=true;}
    command.CommandText="DROP TRIGGER fail_background";command.ExecuteNonQuery();
    Check(failed&&store.ScreenProfile("left")!.Effects.Brightness==0&&store.HistoryRevision==before,"Background transaction failure leaves committed settings and undo history unchanged");
}
using(var store=new LibraryStore(Path.Combine(root,"nonblocking-effect-gestures"))) {
    var original=new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
    store.SaveProfile("left","",original);store.HistoryEnabled=true;
    var effects=new EffectsService(store);
    long firstGesture=store.CurrentUndoGesture;store.BreakUndoGesture();
    long secondGesture=store.CurrentUndoGesture;store.BreakUndoGesture();
    Task pending;
    using(var blocker=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(store.Root,"library.db"),Pooling=false}.ToString())) {
        blocker.Open();using(var transaction=blocker.BeginTransaction()) {
            pending=store.QueueEffectWrite(effects.Prepare("left",null,original with {Effects=original.Effects with {Brightness=.2}},original.Effects),firstGesture);
            Check(store.HasPendingEffectWrite&&!store.TryCompleteEffectWrite(),"Readiness polling returns immediately while another connection holds the writer lock");
            Check(store.HistoryRevision==0,"Polling an unfinished write cannot publish partial undo history");
            transaction.Rollback();
        }
    }
    Check(pending.Wait(TimeSpan.FromSeconds(10))&&store.TryCompleteEffectWrite()&&!store.HasPendingEffectWrite,"Completed background commits can be finalized without another wait");
    var next=store.QueueEffectWrite(effects.Prepare("left",null,original with {Effects=original.Effects with {Brightness=.4}},original.Effects),secondGesture);
    store.CompleteEffectWrite(next);
    next=store.QueueEffectWrite(effects.Prepare("left",null,original with {Effects=original.Effects with {Brightness=.45}},original.Effects),secondGesture);
    store.CompleteEffectWrite(next);
    new HistoryService(store).Restore(false);
    Check(store.ScreenProfile("left")!.Effects.Brightness==.2,"Delayed saves from one gesture merge while the previous gesture stays independently undoable");
    new HistoryService(store).Restore(false);
    Check(store.ScreenProfile("left")!.Effects.Brightness==0,"A second undo restores the state before both delayed gestures");
}
using(var store=new LibraryStore(Path.Combine(root,"retry-ordered-material-deltas"))) {
    var original=VideoEffects.Original;
    store.Upsert(new("material",Path.Combine(store.Root,"material.mp4"),"material",0,1,1920,1080,"h264","sdr","test"));
    store.SaveProfile("left","",new(FillMode.Cover,original));store.HistoryEnabled=true;
    var effects=new EffectsService(store);
    long firstGesture=store.CurrentUndoGesture;store.BreakUndoGesture();
    long secondGesture=store.CurrentUndoGesture;store.BreakUndoGesture();
    var first=original with {Brightness=.2,Collection=original.GetCollection() with {Added=EffectKind.Color|EffectKind.Noise}};
    var second=first with {Noise=true,NoiseStrength=.008};
    Check(AssetEffects.ChangedKinds(first,second)==EffectKind.Noise,"The later material delta changes a different effect kind than the failed brightness gesture");
    using var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=Path.Combine(store.Root,"library.db"),Pooling=false}.ToString());db.Open();
    using var command=db.CreateCommand();command.CommandText="CREATE TRIGGER fail_queued BEFORE UPDATE ON screen_profile BEGIN SELECT RAISE(ABORT,'injected queued failure'); END";command.ExecuteNonQuery();
    store.QueueEffectWrite(effects.Prepare("left","material",new(FillMode.Cover,first),original),firstGesture);
    bool failed=false;try {store.CompleteEffectWrite();}catch(SqliteException) {failed=true;}
    Check(failed&&store.HistoryRevision==0,"A failed first material gesture leaves no partial history for a later queued delta");
    command.CommandText="DROP TRIGGER fail_queued";command.ExecuteNonQuery();
    store.QueueEffectWrite(effects.Prepare("left","material",new(FillMode.Cover,first),original),firstGesture);store.CompleteEffectWrite();
    store.QueueEffectWrite(effects.Prepare("left","material",new(FillMode.Cover,second),first),secondGesture);store.CompleteEffectWrite();
    var saved=store.EffectiveProfile("left","material").Effects;
    Check(saved.Brightness==.2&&saved.Noise&&saved.NoiseStrength==.008,"Retrying the failed gesture before later deltas preserves both effect kinds");
    new HistoryService(store).Restore(false);saved=store.EffectiveProfile("left","material").Effects;
    Check(saved.Brightness==.2&&!saved.Noise,"Undo after an ordered retry restores only the later gesture");
    new HistoryService(store).Restore(false);
    Check(store.EffectiveProfile("left","material").Effects.Brightness==0,"The retried first gesture remains separately undoable");
}
EffectOrderChecks.Run(root,Check);
PresetPackageChecks.Run(root,Check);
PresetResourceLocationChecks.Run(root,Check);
UnifiedDataMigrationChecks.Run(root,Check);
LibrarySourceFileChecks.Run(root,Check);
SettingsSnapshotChecks.Run(root,Check);
Console.WriteLine($"{checks} storage checks passed.");
