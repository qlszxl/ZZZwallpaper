using ZZZwallpaper.InstallerPreview;

if(args is ["--cleanup-wait-fixture"]) {await Console.In.ReadToEndAsync();return;}
if (args is ["--process-fixture", var mode]) { await LifecycleChecks.Child(mode); return; }
if (args.Length is < 1 or > 2) throw new ArgumentException("Pass an explicit isolated fixture directory.");
var fixture = Path.GetFullPath(args[0]);
var local = Path.Combine(fixture, "Local");
var temporary = Path.Combine(fixture, "Temp");
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
if (args.Length == 2 && args[1] == "--linked-fixture")
{
    var result = PersonalDataCleanup.RemoveKnownData(local, temporary);
    Check(result.FailedItems > 0, "Linked data directories report incomplete cleanup");
    Check(File.Exists(Path.Combine(fixture, "Outside", "0123456789abcdef0123456789abcdef.bmp")), "Junction target content is preserved");
    Console.WriteLine($"Installer cleanup checks passed: {checks}"); return;
}
if (Directory.Exists(fixture)) throw new InvalidOperationException("Use a new fixture directory.");
void Write(string relative) { var file = Path.Combine(fixture, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "isolated test fixture"); }
var owned = new[] {
    "Local/LumaWall/library.db", "Local/LumaWall/library.db-wal", "Local/LumaWall/library.db-shm", "Local/LumaWall/library.db-journal",
    "Local/LumaWall/desktop-diagnostics.json", "Local/LumaWall/posters/0123456789abcdef0123456789abcdef.bmp",
    "Local/LumaWall/effect-preview/cache/0123456789abcdef0123456789abcdef.bmp",
    "Local/LumaWall/effect-preview/custom-0123456789abcdef0123456789abcdef.png",
    "Local/LumaWall/logs/zzzwallpaper-test.jsonl", "Local/LumaWall/diagnostics/menu-render.json",
    "Temp/ZZZwallpaper-masks/" + new string('a', 64) + ".mask", "Temp/ZZZwallpaper/logs/zzzwallpaper-test.jsonl"
};
var retained = new[] {
    "Local/LumaWall/original.mp4", "Local/LumaWall/posters/original.mp4", "Local/LumaWall/posters/my-photo.bmp",
    "Local/LumaWall/export.json", "Local/LumaWall/logs/notes.txt", "Local/OtherApp/library.db",
    "Temp/ZZZwallpaper-masks/user-file.txt", "Local/LumaWall.Modular/library.db", "Local/ZZZwallpaper-data-migrations/original/library.db"
};
foreach (var path in owned.Concat(retained)) Write(path);
var removed = PersonalDataCleanup.RemoveKnownData(local, temporary);
Check(removed.DeletedFiles == owned.Length && removed.FailedItems == 0, "Only known application data is removed");
Check(owned.All(path => !File.Exists(Path.Combine(fixture, path))), "Database, generated caches, diagnostics and logs are removed");
Check(retained.All(path => File.Exists(Path.Combine(fixture, path))), "Original media, exports, unknown files and other applications are preserved");
var again = PersonalDataCleanup.RemoveKnownData(local, temporary);
Check(again.DeletedFiles == 0 && again.FailedItems == 0, "Repeated cleanup is harmless");
Write("Local/LumaWall/library.db");
var locked = Path.Combine(local, "LumaWall", "library.db");
File.SetAttributes(locked, FileAttributes.ReadOnly);
try
{
    var partial = PersonalDataCleanup.RemoveKnownData(local, temporary);
    Check(partial.FailedItems == 1 && File.Exists(locked), "Permission failure is reported without pretending data was removed");
}
finally { File.SetAttributes(locked, FileAttributes.Normal); }
Check(PersonalDataCleanup.RemoveKnownData(Path.Combine(fixture, "Missing"), temporary).DeletedFiles == 0, "Missing profile does not create or remove other files");
await LifecycleChecks.Run(Path.Combine(fixture,"Processes"),Check);
var statusPath=Path.Combine(fixture,"status.ini");
File.WriteAllText(statusPath,"[State]\r\nbasisPoints=4321\r\nstage=files\r\nfile=资源/中文=name.dll\r\ncompleted=25\r\ntotal=100\r\n",new System.Text.UTF8Encoding(true));
var update=InstallationProgress.FromStatus(InstallationProgress.ReadStatus(statusPath))!;
Check(Math.Abs(update.Percent-25)<.0001&&update.File=="资源/中文=name.dll"&&update.CompletedFiles==25&&update.TotalFiles==100,"Fractional progress and UTF-8 filenames survive status transport");
Check(InstallationProgress.FromStatus(new(){{"completed","100"},{"total","100"},{"stage","finishing"}}) is {Percent:100,Stage:"正在完成安装"},"All payload files report 100 percent while finalization remains a separate stage");
Check(InstallationProgress.FromStatus(new())==null,"Missing status does not fabricate progress");
var removing=Path.Combine(fixture,"AsyncRemoval");Directory.CreateDirectory(removing);
var app=Path.Combine(removing,"ZZZwallpaper.dll");File.WriteAllText(app,"fixture");
var done=Path.Combine(fixture,"done.marker");
var delayed=UninstallCompletion.WaitAsync(removing,done,TimeSpan.FromSeconds(5));
await Task.Delay(350);Check(!delayed.IsCompleted,"Uninstaller handoff does not treat entry-process exit as completion");
var engineRecord=Path.Combine(removing,"unins000.dat");File.WriteAllText(engineRecord,"fixture");
File.Delete(app);File.WriteAllText(done,"done");await Task.Delay(350);
Check(!delayed.IsCompleted,"A completion marker does not hide an uninstall engine that is still removing its files");
File.Delete(engineRecord);
Check(await delayed,"Actual removal and completion marker finish the uninstall flow");
File.Delete(done);Check(await UninstallCompletion.WaitAsync(removing,done,TimeSpan.FromSeconds(3)),"Older engine without completion marker is recognized after stable file removal");
File.WriteAllText(app,"fixture");
Check(!await UninstallCompletion.WaitAsync(removing,done,TimeSpan.FromMilliseconds(500)),"Remaining application files never report uninstall success");
File.Delete(app);
int cleanCalls=0;
var cleanResult=await UninstallFinalizer.FinishAsync(removing,done,false,()=> {cleanCalls++;return new(4,0);},timeout:TimeSpan.FromSeconds(3));
Check(cleanCalls==1&&cleanResult?.DeletedFiles==4,"Legacy engine without marker still proceeds to personal-data cleanup");
cleanCalls=0;await UninstallFinalizer.FinishAsync(removing,done,true,()=> {cleanCalls++;return new(0,0);},timeout:TimeSpan.FromSeconds(3));
Check(cleanCalls==0,"Keep-personal-data mode never invokes cleanup");
cleanCalls=0;var retryResult=await UninstallFinalizer.CleanAsync(()=>++cleanCalls<3?new(1,1):new(2,0));
Check(cleanCalls==3&&retryResult.FailedItems==0&&retryResult.DeletedFiles==4,"Transient personal-data locks retry before success is reported");
cleanCalls=0;retryResult=await UninstallFinalizer.CleanAsync(()=> {cleanCalls++;return new(0,1);});
Check(cleanCalls==5&&retryResult.FailedItems==1,"Persistent data failure remains incomplete and allows cleanup-only retry");
File.WriteAllText(app,"fixture");cleanCalls=0;bool refused=false;
try {await UninstallFinalizer.FinishAsync(removing,done,false,()=> {cleanCalls++;return new(0,0);},timeout:TimeSpan.FromMilliseconds(300));}catch(InstallerException) {refused=true;}
Check(refused&&cleanCalls==0,"Unfinished program removal never pretends configuration cleanup ran");
await TempCleanupChecks.Run(Path.Combine(fixture,"TempCleanup"),Check);
await UpgradeChecks.Run(Path.Combine(fixture,"Upgrade"),Check);
Console.WriteLine($"Installer cleanup checks passed: {checks}");
