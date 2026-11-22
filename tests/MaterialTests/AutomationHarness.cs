using LumaWall.Automation;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

// Executes the production effect handler against an isolated real database,
// without WinUI, the normal MCP pipe, playback or any personal configuration.
public sealed partial class MainWindow
{
    private readonly LibraryStore store;
    private readonly TestModules modules;
    private readonly TestWallpapers wallpapers=new();
    private TestScreen? SelectedScreen=>OperationScreen("left");
    private string? effectTargetAssetId=>null;
    public MainWindow(LibraryStore data) {store=data;modules=new(new EffectsService(data),new LibraryService(data));}
    internal int ScopedUndoCount=>scopedEffectUndo.Count;
    internal EffectChange? LastEffectRefresh=>wallpapers.LastChange;
    private static string Arg(System.Text.Json.JsonElement args,string name)=>args.GetProperty(name).GetString()!;
    private TestScreen OperationScreen(string id)=>new(id);
    private void FlushEffectChanges() { }
    private void EndEffectEditing() { }
    private void LoadEffectDraft(bool force) { }
    public Task<OperationReply> Call(string name,object args) {var json=ToolCatalog.Json(args);ToolCatalog.Validate(name,json);return AutomationEffects(new(name,json),default);}
    private sealed record TestScreen(string DeviceName) {public System.Drawing.Rectangle Bounds=>new(0,0,1920,1080);}
    private sealed class TestWallpapers {public Dictionary<string,TestPlaying> Running {get;}=new();public EffectChange? LastChange;public void RefreshLiveEffects(EffectChange? change=null) {LastChange=change;}}
    private sealed record TestPlaying(MediaAsset Asset) {public TestPlayer Player {get;}=new();}
    private sealed class TestPlayer {public Task<bool> WaitForConfiguration(CancellationToken token)=>Task.FromResult(true);}
    private sealed record TestModules(EffectsService Effects,LibraryService Library);
}
