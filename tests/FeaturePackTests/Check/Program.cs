using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using LumaWall.Contracts;
using LumaWall.WidgetHost;
using C=System.Windows.Controls;
internal static class Program {
    private static void Assert(bool yes,string message){if(!yes)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);}
    [STAThread] static void Main(string[] a){
        var dir=a[0];var runtime=a[1];var previous=a[2];
        // Simulate a separately launched host with the exact bundled/updated modules.
        if(a[3]=="upgraded"){
            Assert(FeaturePacks.BaseDirectory.Contains("versions")&&ExpansionCatalog.Base.Version=="1.1.1","Restart resolves independently upgraded base");
            var search=new WidgetInstance {Kind=WidgetKind.Search};
            using var widget=Create(search);Assert(widget.SearchInput!=null,"Updated base creates its search widget");
            var baseEffect=new VideoEffects {PackageEffects=[new("base/invert")]};
            Assert(baseEffect.ToWire().StartsWith("38 "),"Base update can register a new GPU effect");
            using var added=Create(new(){Id="frombase",Kind=WidgetKind.Extension,PackageWidgetId="base/meter"});
            Assert(added.Element is C.TextBlock {Text:"Meter frombase"},"Base update can register a new WPF component");
            return;
        }
        var oldContext=new AssemblyLoadContext("previous",true);
        var old=oldContext.LoadFromAssemblyPath(Path.Combine(previous,"Contracts.dll"));
        var oldFx=old.GetType("LumaWall.Contracts.VideoEffects")!;
        var oldWidgets=old.GetType("LumaWall.Contracts.WidgetConfiguration")!;
        var current=new VideoEffects {Glow=true,GlowStrength=.43,Blur=true,BlurRadius=12,Brightness=.13,Transform=new(){Enabled=true},
            Collection=new(){Added=EffectKind.Glow|EffectKind.Blur|EffectKind.Transform|EffectKind.Color}};
        var legacyFx=JsonNode.Parse(current.ToJson())!.AsObject();legacyFx.Remove("PackageEffects");
        var oldConfig=oldFx.GetMethod("FromJson")!.Invoke(null,[legacyFx.ToJsonString()]);
        string oldJson=(string)oldFx.GetMethod("ToJson")!.Invoke(oldConfig,null)!;
        var restored=VideoEffects.FromJson(oldJson);
        Assert((string)oldFx.GetMethod("ToWire")!.Invoke(oldConfig,null)! ==restored.ToWire(),"Previous effects keep parameter values, order and rendering wire");
        File.WriteAllText(Path.Combine(dir,"legacy.wire"),restored.ToWire());
        var layout=new WidgetConfiguration {LayoutGroups=[new(){Id="g",Name="A",X=.31,Y=.11}],Widgets=[new(){Id="clock",Kind=WidgetKind.Clock,LayoutGroupId="g",X=.14,Y=.72,Layer=3,AlwaysOnTop=true,Opacity=.67}]};
        var oldLayout=oldWidgets.GetMethod("FromJson")!.Invoke(null,[layout.ToJson()]);
        string oldLayoutJson=(string)oldWidgets.GetMethod("ToJson")!.Invoke(oldLayout,null)!;
        var recovered=WidgetConfiguration.FromJson(oldLayoutJson);
        Assert(recovered.ToJson()==layout.ToJson(),"Previous widget IDs, group, position, layer and appearance survive");
        using(var search=Create(new(){Kind=WidgetKind.Search}))Assert(search.SearchInput!=null,"Bundled implementation builds original search content");
        File.WriteAllText(Path.Combine(dir,"baseline.wire"),VideoEffects.Original.ToWire());
        ExpansionCatalog.Import(Path.Combine(dir,"addon.zip"));
        var saved=new VideoEffects {PackageEffects=[new("feature-fixture/invert")]};
        var wire=saved.ToWire();Assert(wire.StartsWith("38 ")&&wire.Contains("invert.cso"),"Independent package resolves shader across save and rendering");
        File.WriteAllText(Path.Combine(dir,"addon.wire"),wire);
        var extension=new WidgetInstance {Kind=WidgetKind.Extension,Id="widget1",PackageWidgetId="feature-fixture/meter"};
        using(var widget=Create(extension))Assert(widget.Element is C.TextBlock {Text:"Meter widget1"},"Independent widget DLL creates real WPF content");
        var widgetJson=(new WidgetConfiguration {Widgets=[extension]}).ToJson();
        var enabled=FeaturePacks.Effects.Single().Directory;
        File.Copy(Path.Combine(dir,"addon.zip"),Path.Combine(dir,"tampered.zip"),true);
        using(var zip=ZipFile.Open(Path.Combine(dir,"tampered.zip"),ZipArchiveMode.Update)){
            var f=zip.GetEntry("invert.cso")!;f.Delete();using var stream=zip.CreateEntry("invert.cso").Open();stream.WriteByte(0);
        }
        var rejected=false;try{ExpansionCatalog.Import(Path.Combine(dir,"tampered.zip"));}catch(InvalidDataException){rejected=true;}
        Assert(rejected&&enabled==FeaturePacks.Effects.Single().Directory,"Corrupt update leaves active generation untouched");
        ExpansionCatalog.Packages.Single(p=>p.Descriptor.Id=="feature-fixture").Remove();
        Assert(saved.ToWire()==VideoEffects.Original.ToWire()&&WidgetConfiguration.FromJson(widgetJson).Widgets[0].PackageWidgetId==extension.PackageWidgetId,"Removal suspends extension and preserves saved IDs");
        ExpansionCatalog.Import(Path.Combine(dir,"addon.zip"));
        Assert(saved.ToWire()==wire,"Reimport restores saved effect");
        var pinned=FeaturePacks.BaseDirectory;
        ExpansionCatalog.Import(Path.Combine(dir,"base-upgrade.zip"));
        Assert(FeaturePacks.InstalledBaseVersion=="1.1.1"&&FeaturePacks.BaseDirectory==pinned&&!ExpansionCatalog.Packages.Any(p=>p.Descriptor.Id=="base"),"Mandatory base upgrades atomically and stays pinned in running process");
    }
    static WidgetContent Create(WidgetInstance item){
        var context=new WidgetContentContext(()=>item,()=>null,()=>false,new(),new C.ContextMenu(),(_,_,_)=>{},()=>{},()=>{},_=>{},_=>{});
        return PackageWidgetFactory.Create(context,new("",0,0,false));
    }
}
