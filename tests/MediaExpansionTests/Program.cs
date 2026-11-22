using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using LumaWall.Contracts;
using LumaWall.Storage;
using LumaWall.WidgetHost;
using Microsoft.Data.Sqlite;
using C=System.Windows.Controls;

internal static class Program
{
    private const string WidgetKey=WidgetConfiguration.LegacyMaterialsWidget;
    private static void Check(bool yes,string message){
        if(!yes)throw new InvalidOperationException(message);
        Console.WriteLine("PASS "+message);
    }
    [STAThread] private static void Main(string[] args){
        var root=Path.GetFullPath(args[0]);var zip=Path.GetFullPath(args[1]);
        var runtime=Path.GetFullPath(args[2]);
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ZZZWALLPAPER_FEATURE_PACK_ROOT",Path.Combine(root,"features"));
        Environment.SetEnvironmentVariable("ZZZWALLPAPER_BASE_DIRECTORY",Path.Combine(runtime,"ExpansionPacks","base"));
        AppContext.SetData("LumaWall.ExpansionPackRoot",Path.Combine(root,"legacy"));
        if(args.Length==4){Probe();return;}
        Check(!ExpansionCatalog.WidgetAvailable(WidgetKind.Warehouse)&&FeaturePacks.FindWidget(WidgetKey)==null
            &&ParticleArtwork.All.Count==8&&ExpansionCatalog.Packages.Count==0,"Clean install has no optional widget or particle registrations");
        var contracts=typeof(WidgetInstance).Assembly;
        var baseWidgets=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(FeaturePacks.BaseDirectory,"ZZZwallpaper.BaseWidgets.dll"));
        Check(baseWidgets.GetType("LumaWall.WidgetHost.WarehouseWidgetContent")==null
            &&typeof(WidgetContent).Assembly.GetType("LumaWall.WidgetHost.WarehouseWidgetContent")==null
            &&contracts.GetType("LumaWall.Contracts.WarehouseCatalog")==null
            &&!contracts.GetManifestResourceNames().Any(s=>s.Contains("Warehouse",StringComparison.OrdinalIgnoreCase))
            &&!Directory.EnumerateFiles(runtime,"*Warehouse*",SearchOption.AllDirectories).Any(),"Implementation, catalog, resources and native renderer are absent from the base");
        var clock=new WidgetInstance{Id="clock",Kind=WidgetKind.Clock,Enabled=false,X=.27,Y=.54,Layer=4,LayoutGroupId="group"};
        var oldItem=new WidgetInstance{Id="old",Kind=WidgetKind.Warehouse,Enabled=false,X=.2,Y=.8};
        var layout=new WidgetConfiguration{Widgets=[clock,oldItem,oldItem with{Id="grouped",LayoutGroupId="group",Enabled=true}],LayoutGroups=[new(){Id="group"}]};
        using var store=new LibraryStore(Path.Combine(root,"db"));
        var old=JsonNode.Parse(layout.ToJson())!;
        foreach(var w in old["Widgets"]!.AsArray())w!["Warehouse"]=new JsonObject{["Rows"]=5,["Width"]=800};
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(store.Root,"library.db")}.ToString())){
            db.Open();
            using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR REPLACE INTO settings(key,value) VALUES ($k,$v)";
            var key=cmd.Parameters.Add("$k",SqliteType.Text);var value=cmd.Parameters.Add("$v",SqliteType.Text);
            key.Value="widgets.v1";value.Value=old.ToJsonString();cmd.ExecuteNonQuery();
            key.Value="sharing.undo.v1";value.Value=new JsonObject{["Version"]=1,["Widgets"]=old.DeepClone()}.ToJsonString();cmd.ExecuteNonQuery();
        }
        store.ReconcileWidgetSettings();
        var ordinary=layout with{Widgets=[clock]};
        Check(store.GetSetting("widgets.v1")==ordinary.ToJson()&&!store.GetSetting("sharing.undo.v1")!.Contains("Warehouse"),
            "Upgrade erases saved, disabled and grouped material instances and stale parameters from the DB and import backup");
        ExpansionCatalog.Import(zip);
        var registered=FeaturePacks.FindWidget(WidgetKey)!;
        Check(registered.Entry.Grid!.DefaultRows==3&&ParticleArtwork.All.Count==15&&store.GetSetting("widgets.v1")==ordinary.ToJson(),"Import registers the component and seven particle styles without recreating instances");
        var migrated=WidgetConfiguration.FromJson(old.ToJsonString()).ReconcileMaterials();
        Check(migrated.Widgets[1].PackageWidgetId==WidgetKey&&migrated.Widgets[1].GridOptions is {Rows:5,Width:800}
            &&migrated.Widgets[2].LayoutGroupId=="group","Legacy layouts migrate with their dimensions when an installed pack supplies the component");
        var newItem=oldItem with{Id="added",Kind=WidgetKind.Extension,PackageWidgetId=WidgetKey,GridOptions=new(4,750)};
        var state=ordinary with{Widgets=[clock,newItem]};
        store.HistoryEnabled=true;store.SetSetting("widgets.v1",state.ToJson());
        store.BreakUndoGesture();store.SetSetting("widgets.v1",(state with{Widgets=[clock with{X=.61},newItem with{GridOptions=new(5,900)}]}).ToJson());
        var probe=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(Path.GetFileNameWithoutExtension(probe.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))probe.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(var arg in new[]{root,zip,runtime,"probe"})probe.ArgumentList.Add(arg);
        using(var child=Process.Start(probe)!){var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();child.WaitForExit();Console.Write(output.Result);Console.Write(error.Result);Check(child.ExitCode==0,"Pack-backed widget runs in a separate host process");}
        var atlasKey=ParticleArtwork.Prepare();
        var atlas=Path.Combine(Path.GetTempPath(),"ZZZwallpaper-particles",atlasKey+".rgba");
        Check(File.Exists(atlas),"Pack-backed particle pixels are prepared for the native renderer");
        ExpansionCatalog.Packages.Single().Remove();store.ReconcileWidgetSettings();
        Check(!Directory.Exists(registered.Directory)&&!File.Exists(atlas)&&ParticleArtwork.All.Count==8&&!ExpansionCatalog.Packages.Any(),"Removing the pack deletes its code, media, generated files and registrations");
        var clean=WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
        Check(clean.Widgets.Length==1&&clean.Widgets[0].X==.61&&clean.LayoutGroups[0].Id=="group"&&!clean.ToJson().Contains("GridOptions"),"Removal erases only the material instances and settings");
        ExpansionCatalog.Import(zip);store.ReplayUndo(false);
        Check(store.GetSetting("widgets.v1")==ordinary.ToJson(),"Reimport and undo preserve the clock edit history without resurrecting the removed instances");
        ExpansionCatalog.Packages.Single().Remove();store.ReconcileWidgetSettings();
    }
    private static void Probe(){
        var item=new WidgetInstance{Kind=WidgetKind.Extension,PackageWidgetId=WidgetKey};
        var context=new WidgetContentContext(()=>item,()=>null,()=>false,new(),new C.ContextMenu(),(_,_,_)=>{},()=>{},()=>{},_=>{},_=>{});
        using var widget=PackageWidgetFactory.Create(context,new("",0,0,false));
        var assembly=widget.GetType().Assembly;
        Check(widget.Element!=null&&assembly.GetName().Name=="ZZZwallpaper.WarehouseWidgets","New package builds its real WPF component");
        Check((int)assembly.GetType("LumaWall.Contracts.WarehouseCatalog",true)!.GetProperty("EnabledCount")!.GetValue(null)! == 62,"All 62 active material catalog entries resolve");
        var media=assembly.GetType("LumaWall.WidgetHost.WarehouseWidgetMedia",true)!;
        var prepared=(Task)media.GetMethod("PrepareAsync")!.Invoke(null,[128])!;
        prepared.GetAwaiter().GetResult();
        var result=prepared.GetType().GetProperty("Result")!.GetValue(prepared);
        using var renderer=(IDisposable)Activator.CreateInstance(media,[result])!;
        Check((int)media.GetProperty("TileSize")!.GetValue(renderer)! == 128,"Optional native DLL decodes the packaged 128-pixel material atlas");
    }
}
