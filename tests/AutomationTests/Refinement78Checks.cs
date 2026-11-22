using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.ControlCenter;
using LumaWall.Storage;

internal static class Refinement78Checks
{
    internal static async Task Run(Action<bool,string> check,Action<Action,string> reject)
    {
        JsonElement J(string json)=>JsonDocument.Parse(json).RootElement.Clone();
        check(new StartupDelayOptions().EffectiveSeconds==0&&new StartupDelayOptions(true,45).Validate().EffectiveSeconds==45,"Startup defaults immediate; enabled delay uses seconds");
        reject(()=>new StartupDelayOptions(true,0).Validate(),"Zero delay rejected when configuring delay");
        reject(()=>new StartupDelayOptions(true,3601).Validate(),"Startup delay bounded");
        check(new PerformanceWidgetOptions().IntervalMilliseconds(5)==5000,"Legacy seconds preserved");
        check(new PerformanceWidgetOptions {RefreshMilliseconds=250}.IntervalMilliseconds(5)==250,"Explicit milliseconds override legacy seconds");
        reject(()=>new PerformanceWidgetOptions {RefreshMilliseconds=99}.Validate(),"Sampler cannot request sub-100ms intervals");
        var patched=WidgetPatch.Apply(new WidgetInstance {Kind=WidgetKind.Performance},J("{\"performance\":{\"RefreshMilliseconds\":150}}"));
        check(patched.Performance.RefreshMilliseconds==150,"MCP accepts millisecond performance refresh");
        reject(()=>WidgetPatch.Apply(patched,J("{\"performance\":{\"RefreshMilliseconds\":99}}")),"MCP enforces performance refresh bounds");
        var center=WallpaperPreviewLayout.Calculate(1000,700,32.0/9,16.0/9,false,new());
        var right=WallpaperPreviewLayout.Calculate(1000,700,32.0/9,16.0/9,false,new(100,0));
        var left=WallpaperPreviewLayout.Calculate(1000,700,32.0/9,16.0/9,false,new(-100,0));
        check(Math.Abs(center.Screen.Width/center.Screen.Height-16.0/9)<1e-6&&center.Screen.X>0&&center.Screen.Y>0,"Preview uses monitor aspect and leaves dimmable exterior");
        check(right.Content.X==right.Screen.X&&left.Content.X+left.Content.Width==left.Screen.X+left.Screen.Width,"Cover extremes reach source edges without exposing empty space");
        var contain=WallpaperPreviewLayout.Calculate(1000,700,.5,16.0/9,true,new(100,100));
        check(Math.Abs(contain.Content.X+contain.Content.Width-contain.Screen.X-contain.Screen.Width)<1e-6,"Contain position moves within letterbox");
        var portrait=WallpaperPreviewLayout.Calculate(700,1000,16.0/9,9.0/16,false,new(0,0));
        check(Math.Abs(portrait.Screen.Width/portrait.Screen.Height-9.0/16)<1e-6,"Portrait monitor aspect preserved");
        check(WallpaperPreviewLayout.Calculate(0,100,1,1,false,new())==default,"Unmeasured preview is harmless");
        check(WallpaperPosition.FromJson(new WallpaperPosition(-37,82).ToJson())==new WallpaperPosition(-37,82),"Wallpaper position round trips");
        reject(()=>new WallpaperPosition(101,0).Validate(),"Position bounds enforced");
        var effects=VideoEffects.Original with {Collection=new EffectCollection {Added=EffectKind.HideTaskbar,HideTaskbarEnabled=true,HideTaskbar=1,TaskbarStyle=TaskbarStyle.Frosted}};
        check(effects.Validate().TaskbarAppearance==TaskbarStyle.Frosted&&!effects.HidesTaskbar,"Frosted does not hide taskbar or change work-area behavior");
        check((effects with {Collection=effects.GetCollection() with {HideTaskbarEnabled=false}}).TaskbarAppearance==null,"Taskbar switch disables all styles");
        reject(()=>(effects with {Collection=effects.GetCollection() with {TaskbarStyle=(TaskbarStyle)9}}).Validate(),"Unknown taskbar style rejected");
        foreach(string json in new[]{"{\"screenId\":\"screen\",\"expectedRevision\":\"r\",\"effectsEnabled\":false}","{\"screenId\":\"screen\",\"expectedRevision\":\"r\",\"widgetsEnabled\":true}"})ToolCatalog.Validate("screen_set_features",J(json));
        reject(()=>ToolCatalog.Validate("screen_set_features",J("{\"screenId\":\"screen\",\"expectedRevision\":\"r\"}")),"Master switch requires a target feature");
        ToolCatalog.Validate("wallpaper_set_screen_options",J("{\"screenId\":\"screen\",\"expectedRevision\":\"r\",\"positionX\":-20,\"positionY\":30,\"taskbarStyle\":\"Transparent\"}"));
        ToolCatalog.Validate("app_update_settings",J("{\"expectedRevision\":\"r\",\"startupDelayEnabled\":true,\"startupDelaySeconds\":45}"));
        check(true,"MCP exposes startup delay, taskbar style, position and screen master switches");
        string root=Path.Combine(Path.GetTempPath(),"zzzwallpaper-refinement-"+Guid.NewGuid().ToString("N"));
        using(var store=new LibraryStore(root)) {
            store.SaveProfile("one","",new(FillMode.Contain,effects));
            store.SetSetting("widgets.v1",new WidgetConfiguration {Widgets=[new() {Kind=WidgetKind.Text,Screen="one",Enabled=true},new() {Kind=WidgetKind.Text,Screen="one",Enabled=false}]}.ToJson());
            var profile=JsonSerializer.Serialize(store.ScreenProfile("one"));string? widgets=store.GetSetting("widgets.v1");store.HistoryEnabled=true;
            using(store.BeginUndoBatch("Master fixture")) {store.SetSetting("screen.effects.enabled:one","false");store.SetSetting("screen.widgets.enabled:one","false");}
            check(JsonSerializer.Serialize(store.ScreenProfile("one"))==profile&&store.GetSetting("widgets.v1")==widgets&&store.GetSetting("screen.effects.enabled:two")==null,"Master switches preserve individual effects/widgets and other screens");
            store.ReplayUndo(false);
            check(store.GetSetting("screen.effects.enabled:one")==null&&store.GetSetting("screen.widgets.enabled:one")==null,"One undo restores both master switches");
            store.BreakUndoGesture();store.SetSetting("wallpaper.position:one",new WallpaperPosition(25,-60).ToJson());store.ReplayUndo(false);
            check(store.GetSetting("wallpaper.position:one")==null,"Position participates in undo");
            using(store.BeginUndoBatch("Delay fixture")) {store.SetSetting("startup.delay.enabled","true");store.SetSetting("startup.delay.seconds","45");}
            store.ReplayUndo(false);check(store.GetSetting("startup.delay.enabled")==null&&store.GetSetting("startup.delay.seconds")==null,"Startup delay fields undo together");
        }
        using var release=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
        var threads=new List<int>();bool sta=true;
        using(var launcher=new DesktopTargetLauncher(target=> {lock(threads)threads.Add(Environment.CurrentManagedThreadId);sta&=Thread.CurrentThread.GetApartmentState()==ApartmentState.STA;if(target=="block") {entered.Set();release.Wait(TimeSpan.FromSeconds(10));}if(target=="fail")throw new IOException("fixture");})) {
            var first=launcher.Open("block");check(entered.Wait(TimeSpan.FromSeconds(3)),"Shell worker starts without UI dispatcher");
            try {
                var pending=Enumerable.Range(0,8).Select(i=>launcher.Open("queued"+i)).ToArray();
                try {launcher.Open("overflow");throw new Exception("Unbounded shell queue");}catch(InvalidOperationException){check(true,"Shell queue bounded to eight pending actions");}
                release.Set();await Task.WhenAll(pending.Append(first)).WaitAsync(TimeSpan.FromSeconds(5));
                check(sta&&threads.Distinct().Count()==1,"All shell actions use one STA worker");
                try {await launcher.Open("fail");throw new Exception("Missing shell failure");}catch(IOException){check(true,"Shell errors propagate without duplicate browser fallback");}
                await launcher.Open("after-failure");check(true,"Worker recovers after one failed launch");
            } finally {release.Set();}
        }
    }
}
