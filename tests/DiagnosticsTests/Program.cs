using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using LumaWall.Diagnostics;

if(args.Length>0&&args[0]=="--stderr") {
    Console.Error.WriteLine("diagnostic stderr marker 中文");
    Console.Error.Write(new string('x',30000));return;
}
if(args.Length>0&&args[0]=="--fault") {
    AppLog.Initialize("FaultTest",inherit:true);
    throw new InvalidOperationException("unhandled test marker",new IOException("inner test marker"));
}
int passed=0;
string ReadLive(string path) {
    using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    using var reader=new StreamReader(input);return reader.ReadToEnd();
}
void Check(bool value,string description) {if(!value)throw new Exception(description);Console.WriteLine("PASS "+description);passed++;}
string root=Path.GetFullPath(Path.Combine("artifacts","test-results","diagnostics-"+Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
string basic=Path.Combine(root,"basic");
using(var log=new RollingFileLog(basic,"Tests","1.2.3","test-session")) {
    Exception failure;
    try {throw new InvalidOperationException("中文 error",new IOException("inner marker"));}catch(Exception ex) {failure=ex;}
    log.Write("Error","Test","测试: "+Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),failure);
    using(var entry=JsonDocument.Parse(ReadLive(log.CurrentFile!).Trim())) {
        var value=entry.RootElement;
        Check(value.GetProperty("exception").GetString()!.Contains("inner marker")&&value.GetProperty("exception").GetString()!.Contains("Program.cs"),"exception chain and stack retained");
        Check(value.GetProperty("message").GetString()!.Contains("%USERPROFILE%"),"user profile redacted");
        Check(value.GetProperty("session").GetString()=="test-session"&&value.GetProperty("processId").GetInt32()>0&&value.GetProperty("version").GetString()=="1.2.3","session/version/process metadata retained");
    }
    Parallel.For(0,60,i=>log.Write("Information","Parallel","record "+i));
    var lines=ReadLive(log.CurrentFile!).Split('\n',StringSplitOptions.RemoveEmptyEntries);
    Check(lines.Length==61&&lines.All(line=>JsonDocument.Parse(line).RootElement.TryGetProperty("time",out _)),"concurrent writes remain complete JSON records");
    File.WriteAllText(Path.Combine(basic,"settings.db"),"private database");
    File.WriteAllText(Path.Combine(basic,"unrelated.txt"),"private text");
    string zip=Path.Combine(root,"bundle.zip");
    DiagnosticBundle.Create(basic,zip,new {test=true},new(true,false,false));
    using(var archive=ZipFile.OpenRead(zip)) {
        Check(archive.Entries.Count==3&&!archive.Entries.Any(e=>e.Name is "settings.db" or "unrelated.txt"),"export includes only known logs and diagnostic metadata");
        using var reader=new StreamReader(archive.Entries.Single(e=>e.FullName.StartsWith("logs/")).Open());
        Check(reader.ReadToEnd().Contains("inner marker"),"live open log can be exported");
    }
    byte[] original=File.ReadAllBytes(zip);
    try {DiagnosticBundle.Create(Path.Combine(root,"missing"),zip,new {},new(true,false,false));}catch(DirectoryNotFoundException) { }
    Check(original.SequenceEqual(File.ReadAllBytes(zip))&&!Directory.GetFiles(root,".zzzwallpaper-*.tmp").Any(),"failed export preserves existing archive and cleans temporary file");
}
var defaults=new DiagnosticExportOptions();
Check(defaults.IncludeLogs&&defaults.IncludeEnvironment&&!defaults.IncludeConfiguration,"Diagnostic defaults include logs/environment and leave configuration opt-in");
var appearance=LumaWall.Contracts.AppearanceOptions.FromJson(null);
Check(appearance.Pattern&&appearance.Motion,"New profiles enable brand pattern and motion");
var oldAppearance=LumaWall.Contracts.AppearanceOptions.FromJson("{\"Pattern\":false,\"Motion\":false}");
Check(!oldAppearance.Pattern&&!oldAppearance.Motion,"Existing explicit appearance choices are preserved");
var redacted=DiagnosticConfiguration.Redact(new {Scale=1.25,Enabled=true,Path="C:/private/picture.png",Text="private note",Ids=new[]{"secret-id","secret-id"},Nested=new {Command="private command"}});
Check(!redacted.GetRawText().Contains("private")&&!redacted.GetRawText().Contains("secret-id")&&redacted.GetProperty("Scale").GetDouble()==1.25&&redacted.GetProperty("Enabled").GetBoolean(),"Configuration keeps parameters while removing all free text and paths");
Check(redacted.GetProperty("Ids")[0].GetString()==redacted.GetProperty("Ids")[1].GetString(),"Redacted identifiers preserve relationships inside one snapshot");
var nested=DiagnosticConfiguration.Redact(new {Values=new[]{1,2,3},Items=new[]{new {Name="private nested text",Enabled=true}},Empty=new[]{""}});
Check(nested.GetProperty("Values")[2].GetInt32()==3&&nested.GetProperty("Items")[0].GetProperty("Enabled").GetBoolean()
    &&!nested.GetRawText().Contains("private nested text"),"Nested configuration objects and numeric arrays remain valid after redaction");
for(int mask=1;mask<8;mask++) {
    var options=new DiagnosticExportOptions((mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
    string bundle=Path.Combine(root,$"selection-{mask}.zip");
    DiagnosticBundle.Create(basic,bundle,new {test=true},options,new {os="environment-marker"},redacted);
    using var archive=ZipFile.OpenRead(bundle);
    Check(archive.Entries.Any(e=>e.FullName.StartsWith("logs/"))==options.IncludeLogs
        &&(archive.GetEntry("system-info.json")!=null)==options.IncludeEnvironment
        &&(archive.GetEntry("configuration.json")!=null)==options.IncludeConfiguration,
        $"Selection {mask} writes only selected categories even when unselected snapshots are supplied");
}
string protectedBundle=Path.Combine(root,"protected.zip");File.WriteAllText(protectedBundle,"keep-existing");
try {DiagnosticBundle.Create(basic,protectedBundle,new {},new(false,false,false));throw new Exception("Empty selection accepted");}catch(ArgumentException) { }
Check(File.ReadAllText(protectedBundle)=="keep-existing","Empty selection cannot replace an existing destination");
using(var cancel=new CancellationTokenSource()) {
    cancel.Cancel();try {DiagnosticBundle.Create(basic,protectedBundle,new {},new(true,false,false),token:cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException) { }
    Check(File.ReadAllText(protectedBundle)=="keep-existing","Cancelled export preserves the destination");
}
string environmentOnly=Path.Combine(root,"environment-only.zip");
DiagnosticBundle.Create(Path.Combine(root,"missing"),environmentOnly,new {},new(false,true,false),new {os="test"});
Check(File.Exists(environmentOnly),"Environment-only export works when the log directory is unavailable");
string rotation=Path.Combine(root,"rotation");Directory.CreateDirectory(rotation);
File.WriteAllText(Path.Combine(rotation,"keep.txt"),"keep");
string expired=Path.Combine(rotation,"zzzwallpaper-expired.jsonl");File.WriteAllText(expired,"expired");File.SetLastWriteTimeUtc(expired,DateTime.UtcNow.AddDays(-20));
using(var log=new RollingFileLog(rotation,"Rotation","1","session",2048,3)) {
    Check(!File.Exists(expired),"expired logs removed on startup");
    for(int i=0;i<35;i++)log.Write("Error","Rotation",i+new string('x',800));
    var files=new DirectoryInfo(rotation).GetFiles("*.jsonl");
    Check(files.Length==3&&files.All(f=>f.Length<=2048),"file count and size bounded after rotation");
    log.Write("Error","Large",new string('中',100000));
    Check(new FileInfo(log.CurrentFile!).Length<=2048&&ReadLive(log.CurrentFile!).Contains("Oversized"),"oversized entries stay within file limit");
    Check(File.Exists(Path.Combine(rotation,"keep.txt")),"retention leaves unrelated files untouched");
}
string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"file instead of directory");
string tied=Path.Combine(root,"tied");Directory.CreateDirectory(tied);
for(int i=0;i<6;i++) {
    string path=Path.Combine(tied,$"zzzwallpaper-future-{i}.jsonl");File.WriteAllText(path,"old");
    File.SetLastWriteTimeUtc(path,DateTime.UtcNow.Date.AddDays(1));
}
using(var log=new RollingFileLog(tied,"Ties","1","session",2048,3)) {
    Check(Directory.GetFiles(tied,"*.jsonl").Length==3&&File.Exists(log.CurrentFile),"retention reserves the live file slot even with tied future timestamps");
}
using(var log=new RollingFileLog(Path.Combine(root,"single"),"Single","1","session",1024,1)) {
    bool bounded=true;for(int i=0;i<40;i++) {log.Write("Error","Rotation",i+new string('x',800));bounded&=Directory.GetFiles(log.DirectoryPath,"*.jsonl").Length==1;}
    Check(bounded,"one-file retention remains bounded on every rotation");
}
using(var log=new RollingFileLog(blocked,"Blocked","1","session")) {
    log.Write("Error","Blocked","must not throw");
    Check(log.LastError!=null,"unwritable directory does not throw into application");
}
string rate=Path.Combine(root,"rate");
string rateFile;
using(var log=new RollingFileLog(rate,"Rate","1","session")) {
    for(int i=0;i<2000;i++)log.Write("Warning","Flood","same repeated error");
    rateFile=log.CurrentFile!;
}
Check(File.ReadAllLines(rateFile).Length<2000&&File.ReadAllText(rateFile).Contains("Repeated log entries"),"error floods rate limited with suppression summary");

AppLog.Initialize("Tests",root);
OperationTrace.Initialize(AppLog.DirectoryPath,()=>null);
var firstOperation=OperationTrace.Begin("display","test-unflushed");
OperationTrace.Record(firstOperation,"renderer_configuration","acknowledged","revision=7");
var otherOperation=OperationTrace.Begin("widgets","unrelated");
var recentOperation=OperationTrace.ReadRecent(firstOperation.Id);
Check(recentOperation.Length==2&&recentOperation.All(e=>e.GetProperty("operationId").GetString()==firstOperation.Id),"unflushed stage records are immediately available by exact operation ID");
Check(recentOperation[1].GetProperty("data").GetProperty("Stage").GetString()=="renderer_configuration","recent logs preserve renderer stage association");
Check(OperationTrace.ReadRecent(firstOperation.Id,DateTimeOffset.Now.AddSeconds(1)).Length==0,"recent trace timestamp filter applies before limiting");
OperationTrace.Shutdown();
ProcessStartInfo Child(string mode) {
    var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
    if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(mode);AppLog.ConfigureChild(start);return start;
}
using(var child=Process.Start(Child("--stderr"))!) {
    var drain=AppLog.CaptureStandardErrorAsync(child,"ChildTest");
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));await drain;
    string content=ReadLive(AppLog.CurrentFile!);
    Check(content.Contains("diagnostic stderr marker")&&content.Contains("xxxx"),"child stderr including long newline-free output captured without blocking");
}
using(var child=Process.Start(Child("--fault"))!) {
    var drain=AppLog.CaptureStandardErrorAsync(child,"FaultTest");
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));await drain;
    Check(child.ExitCode!=0,"unhandled exception still terminates child");
    string fault=Directory.GetFiles(AppLog.DirectoryPath!,"*FaultTest*.jsonl").Single();
    string content=File.ReadAllText(fault);
    Check(content.Contains("unhandled test marker")&&content.Contains("inner test marker")&&content.Contains(AppLog.Session),"unhandled child exception logged with inherited directory and session");
}
Console.WriteLine($"{passed} diagnostic checks passed. Evidence: {root}");
