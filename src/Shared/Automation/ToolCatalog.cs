using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

namespace LumaWall.Automation;

public sealed record OperationTool(string Name,string Description,JsonElement InputSchema,bool ReadOnly,JsonElement OutputSchema,bool Destructive,bool Idempotent)
{
    public bool ReadOnlyHint=>ReadOnly&&Name is not ("media_preview_poster" or "config_export" or "config_preview_import" or "ui_capture_desktop" or "ui_capture_window" or "diagnostics_export" or "diagnostics_get_render_stats" or "diagnostics_benchmark_decode");
}

// This catalog is also validated inside the application. The bridge is not a trust boundary.
public static partial class ToolCatalog
{
    public const string EnabledSetting="automation.mcp.enabled";
    public static bool IsEnabled(string? setting)=>setting=="true";
    public static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
    public static JsonElement Object(Dictionary<string,JsonElement> properties,params string[] required)=>Json(new {type="object",properties,required,additionalProperties=false});
    private static JsonElement Text(string description,int max=128)=>Json(new {type="string",description,minLength=1,maxLength=max});
    private static JsonElement Number(double min,double max)=>Json(new {type="number",minimum=min,maximum=max});
    private static JsonElement Integer(int min,int max)=>Json(new {type="integer",minimum=min,maximum=max});
    private static JsonElement Choice(params string[] values)=>Json(new {type="string",@enum=values});
    private static JsonElement Boolean()=>Json(new {type="boolean"});
    private static readonly JsonElement Screen=Text("Exact screen ID returned by app_get_state. Never infer a display number.");
    private static readonly JsonElement Revision=Text("Revision returned by effect_get. Re-read after a conflict.",64);
    private static OperationTool Tool(string name,string description,bool readOnly,Dictionary<string,JsonElement> properties,params string[] required) {
        if(!readOnly)properties.TryAdd("idempotencyKey",Text("Unique retry key within this application instance; reuse only with identical arguments.",128));
        if(UsesConfigurationRevision(name)) {
            properties.TryAdd("expectedRevision",Text("Exact app_get_state.configurationRevision (also widget_get.revision). Re-read on conflict.",64));
            required=[..required.Where(r=>r!="expectedRevision"),"expectedRevision"];
        }
        var schema=AddConditions(name,Object(properties,required));
        return new(name,description,schema,readOnly,ResultSchema(name),!readOnly&&name is not ("media_import" or "media_import_batch" or "widget_add" or "history_create_checkpoint"),
            name is "wallpaper_set_paused" or "wallpaper_set_sound" or "wallpaper_set_fps_limit" or "wallpaper_set_decoder_mode" or "widget_set_lock" or "audio_stop_test" or "task_cancel_job" or "config_cancel_import" or "history_delete_checkpoint");
    }
    private static Dictionary<string,JsonElement> WidgetArguments(bool create)
    {
        var fields=new Dictionary<string,JsonElement> {
            ["screenId"]=Screen,["x"]=Number(0,1),["y"]=Number(0,1),["widthDip"]=Integer(24,16384),["heightDip"]=Integer(24,16384),
            ["opacity"]=Number(.35,1),["enabled"]=Boolean(),["alwaysOnTop"]=Boolean(),["layer"]=Integer(-999,999),["text"]=Text("Plain display text for Text widgets",2000),
            ["name"]=Text("Website shortcut display name; required when adding Shortcuts",40),
            ["url"]=Text("HTTP/HTTPS website address; required when adding Shortcuts. Stored only, never opened by this call.",4096),
            ["iconOnly"]=Boolean(),["metrics"]=Integer(1,(int)PerformanceFields.All),["timerAction"]=Choice("start","pause","reset","skip")
        };
        foreach(var group in WidgetPatch.Schemas)fields[char.ToLowerInvariant(group.Key[0])+group.Key[1..]]=WidgetPatch.InputSchema(group.Key,create);
        if(create)fields["kind"]=Text("Built-in widget kind or installed package/widget ID",129);else fields["widgetId"]=Text("ID from app_get_state");
        return fields;
    }
    public static readonly IReadOnlyList<OperationTool> All=[
        ..ExtendedTools(),
        ..CoverageTools(),
        ..FileTools(),
        Tool("app_get_state","Read application, screens, wallpaper playback and component states. No filesystem access.",true,new()),
        Tool("media_list","List imported assets with optional metadata filters; does not change UI filters. Resolution uses the shorter edge. Use returned asset IDs for effects and wallpaper.",true,LibraryFilterArguments(true)),
        Tool("media_import","Import one existing local image/video file into the media library. Stores a reference and thumbnail; never copies, executes, downloads or applies the source. Returns an asset ID. Supported images: JPG/JPEG/PNG/BMP/WebP/TIFF (first frame for multi-frame images).",false,new(){{"path",Text("Absolute local file path supplied by the user; no URLs, UNC paths or commands.",32767)}},"path"),
        Tool("effect_set_scope","Enable a complete material override from current effective values, or restore display inheritance. Requires explicit assetId. scope=screen removes the material override; it does not overwrite the display preset.",false,new(){{"screenId",Screen},{"assetId",Text("ID from media_list/media_import")},{"expectedRevision",Revision},{"effect",Choice(EffectPatch.Groups.Keys.ToArray())},{"scope",Choice("screen","asset")}},"screenId","assetId","expectedRevision","effect","scope"),
        Tool("effect_get","Read effects for the selected target before editing. scope=screen works even with an empty media library; asset returns only the explicit material's saved layer (empty collection when unset); effective returns display defaults merged with that asset. Playback merges both layers, with material groups overriding matching display groups. Internal fractions are 0–1. Returned overrides identifies each group source.",true,new(){{"screenId",Screen},{"assetId",Text("ID from media_list/media_import")},{"scope",Choice("screen","asset","effective")}},"screenId"),
        Tool("effect_clear","Clear all effects on the specified target. scope=screen clears display defaults without changing material overrides and needs no media. scope=asset requires assetId and explicitly disables all material visual/audio effects, including inherited display effects. Original media, fill and other targets are preserved. Call effect_get first.",false,new(){{"screenId",Screen},{"assetId",Text("Required for scope=asset")},{"scope",Choice("screen","asset")},{"expectedRevision",Revision}},"screenId","scope","expectedRevision"),
        Tool("effect_copy_screen_to_asset","Copy all visual/audio effects from screenId to assetId, replacing its material overrides. Keeps the source display configuration. Source is the UI's top display selector; obtain its exact ID from app_get_state.selectedScreenId. Taskbar and fill stay display-scoped. Read effect_get with scope=asset and the same IDs first.",false,new(){{"screenId",Screen},{"assetId",Text("Destination material ID from media_list")},{"expectedRevision",Revision}},"screenId","assetId","expectedRevision"),
        Tool("audio_set_rule","Create or replace one audio parameter rule. Read effect_get for audioParameters, bounds, units, own rules and revision. Does not enable the audio effect or the target effect. Disabled material rules suppress inherited rules.",false,new(){{"screenId",Screen},{"assetId",Text("ID from media_list")},{"scope",Choice("screen","asset")},{"expectedRevision",Revision},{"rule",EffectPatch.ParameterSchema.GetProperty("properties").GetProperty("AudioRules").GetProperty("items")}},"screenId","scope","expectedRevision","rule"),
        Tool("audio_remove_rule","Remove one rule from the chosen scope. Removing a material rule restores display inheritance. Set Enabled=false to suppress an inherited rule instead. Supports effect_undo.",false,new(){{"screenId",Screen},{"assetId",Text("ID from media_list")},{"scope",Choice("screen","asset")},{"expectedRevision",Revision},{"parameter",Text("Exact Key from audioParameters",64)}},"screenId","scope","expectedRevision","parameter"),
        Tool("effect_set","Add/enable/disable one UI effect and optionally set its strength and parameters. Applies and automatically saves like the UI. scope=asset requires assetId and affects all screens showing that material; scope=screen (default) edits display defaults even with no media. Same effect is overridden, never stacked. Never edits code.",false,new(){{"screenId",Screen},{"assetId",Text("Explicit asset ID required for scope=asset; omit for display editing")},{"scope",Choice("screen","asset")},{"maskId",Text("Existing mask ID returned by effect_get; use none to clear the mask",64)},{"expectedRevision",Revision},{"effect",Choice(EffectPatch.Groups.Keys.ToArray())},{"enabled",Boolean()},{"strength",Number(0,1)},{"parameters",EffectPatch.ParameterSchema}},"screenId","expectedRevision","effect","enabled"),
        Tool("history_undo","Undo the most recent supported configuration edit (effects, widgets, playback queue/options or media reference removal), including UI edits. Session-local shared history. Read app_get_state.history first; do not undo unrelated user edits without their request. Does not delete source files.",false,new(){{"expectedRevision",Text("Exact app_get_state.history.revision",32)}},"expectedRevision"),
        Tool("history_redo","Redo the most recently undone configuration edit. Read app_get_state.history first. Any new recorded edit clears redo.",false,new(){{"expectedRevision",Text("Exact app_get_state.history.revision",32)}},"expectedRevision"),
        Tool("effect_undo","Undo the last MCP effect edit, clear or copy on the same target, only if its state has not subsequently changed. Use scope=asset for material edits and copies, screen for display edits.",false,new(){{"screenId",Screen},{"assetId",Text("Same asset ID used for the edit")},{"scope",Choice("screen","asset")},{"expectedRevision",Revision}},"screenId","expectedRevision"),
        Tool("wallpaper_apply","Apply an imported image/video using display defaults plus material overrides.",false,new(){{"screenId",Screen},{"assetId",Text("ID from media_list")}},"screenId","assetId"),
        Tool("wallpaper_set_paused","Set the wallpaper's user pause state. Environment pause reasons remain effective.",false,new(){{"screenId",Screen},{"paused",Boolean()}},"screenId","paused"),
        Tool("wallpaper_set_sound","Enable or mute wallpaper video sound for one screen.",false,new(){{"screenId",Screen},{"enabled",Boolean()}},"screenId","enabled"),
        Tool("wallpaper_step","Move to next or previous video in the existing sequential/shuffle queue.",false,new(){{"screenId",Screen},{"direction",Choice("next","previous")}},"screenId","direction"),
        Tool("wallpaper_set_playback","Change existing playback mode, transition and trigger. Preserves the existing queue.",false,new(){{"screenId",Screen},{"mode",Choice(Enum.GetNames<PlaybackMode>())},{"transition",Choice(Enum.GetNames<PlaybackTransition>())},{"trigger",Choice(Enum.GetNames<PlaybackTrigger>())},{"intervalSeconds",Integer(10,86400)}},"screenId","mode"),
        Tool("wallpaper_set_fps_limit","Set global selected video FPS: 0 follows source, 1–240 caps output. Selects before hardware readback/conversion where possible; required reference frames still decode. Does not change audio speed.",false,new(){{"fps",Integer(0,240)}},"fps"),
        Tool("wallpaper_set_decoder_mode","Choose global Auto, Hardware preferred, or Software decoding. Auto starts with hardware and may fall back to software under sustained lag. Changes apply during playback or on resume.",false,new(){{"mode",Choice("Auto","Hardware","Software")}},"mode"),
        Tool("diagnostics_benchmark_decode","Benchmark an imported video using hardware and software at source FPS and the requested/current cap. Includes decode/readback/conversion, excludes rendering/effects/audio. Uses temporary workers, adds load, takes roughly 6–20 seconds; allow up to 180 seconds. Does not change decoder settings. Speed=media seconds/wall seconds; CPU uses total machine capacity. Results include GPU utilization (busiest engine for this worker PID), sampled RAM working set/private commit, dedicated/shared GPU memory averages and peaks. Byte fields use bytes; null means unavailable. Other application processes are excluded.",true,new(){{"assetId",Text("Video ID from media_list")},{"fps",Integer(0,240)}},"assetId"),
        Tool("media_preview_poster","Extract a source video frame into an app-owned preview file without changing the saved cover. seconds is source time, direction -1/0/1 means previous/at-or-after/next. Returns actual PTS seconds for precise follow-up steps and saving. At endpoints returns nearest available frame. Preview path is local to this PC and is replaced by the next preview for this asset.",true,new(){{"assetId",Text("Video ID from media_list")},{"seconds",Number(0,10000000)},{"direction",Integer(-1,1)}},"assetId","seconds"),
        Tool("media_set_poster","Save a manually selected source frame as cover. Manual choice survives scans and metadata refresh; use media_reset_poster to restore automatic selection. Use media_preview_poster first and pass its actual Seconds.",false,new(){{"assetId",Text("Video ID from media_list")},{"seconds",Number(0,10000000)}},"assetId","seconds"),
        Tool("media_reset_poster","Regenerate an imported video's cover using brightness, sharpness and nearby frame selection; clears its manual cover choice.",false,new(){{"assetId",Text("Video ID from media_list")}},"assetId"),
        Tool("widget_update","Edit an ungrouped unlocked widget's position, size, layer, visibility and kind-specific options. Website shortcuts accept name/url; commands and folders cannot be edited. Read widget_get first. x/y are 0–1 fractions of available travel; widthDip/heightDip are logical pixels. Only supplied fields change. A timerAction-only update can operate a position-locked or grouped FocusTimer, but interactionLocked rejects it. FocusTimer supports timerAction=start/pause/reset/skip; focus contains durations and AutoStart, while clock and countdown expose their own options. Changing focus durations resets the timer.",false,WidgetArguments(false),"widgetId"),
        Tool("widget_add","Add Search, Shortcuts (website only; name and url required), Performance, Spectrum, Text, Sticker (built-in shapes) or Mask, Clock, Countdown, FocusTimer or an installed package widget. Supports position, dimensions and kind-specific options. For top/bottom black bars use two Mask widgets with x=0, y=0/1, widthDip=screen widthDip, visual={FillColor:'#000000',FillOpacity:1,Radius:0,Padding:0,ClickThrough:true}, opacity=1. Never opens a URL or runs commands.",false,WidgetArguments(true),"screenId","kind"),
        Tool("widget_get","Read a component's complete editable options, website target and kind-specific schemas before editing. Image content and command/folder targets are omitted.",true,new(){{"widgetId",Text("ID from app_get_state")}},"widgetId"),
        Tool("widget_remove","Remove one unlocked ungrouped desktop component. Removes its shortcut card when no remaining widget uses it. Original media files are kept.",false,new(){{"widgetId",Text("ID from app_get_state")}},"widgetId"),
        Tool("diagnostics_sample_performance","Sample CPU, GPU, working-set/private memory, dedicated/shared GPU memory and I/O of this application's processes for two seconds. Processes include active media workers. gpu reports aggregate usage: sum contributions to each physical engine, then select the busiest engine; never sum per-process GPU percentages. Memory fields use bytes. Null metrics mean unavailable. Does not inspect other applications.",true,new())
    ];
    public static OperationTool Validate(string name,JsonElement arguments)
    {
        var tool=All.FirstOrDefault(t=>t.Name==name)??throw new OperationFault("unknown_tool","不支持的操作；仅允许当前工具目录中的名称。","name");
        ValidateValue(arguments,tool.InputSchema,"arguments");return tool;
    }
}

public static class EffectPatch
{
    public static readonly IReadOnlyDictionary<string,string[]> Groups=ShareProjection.EffectFields
        .Where(p=>(p.Key&AssetEffects.Allowed)!=0).ToDictionary(p=>p.Key.ToString(),p=>p.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries)
            .Where(f=>f!=p.Key.ToString()||p.Key is EffectKind.ZoomTunnel or EffectKind.Particles or EffectKind.WaterWave or EffectKind.EdgeOutline or EffectKind.Pixelate or EffectKind.SunPrism or EffectKind.LightFlare or EffectKind.Transform).ToArray(),StringComparer.Ordinal);
    public static readonly JsonElement ParameterSchema=LoadSchema();
    private static JsonElement LoadSchema()
    {
        var assembly=typeof(EffectPatch).Assembly;
        using var stream=assembly.GetManifestResourceStream("LumaWall.Automation.EffectsSchema.json")
            ??throw new InvalidOperationException("缺少内置效果参数定义，请使用完整构建。");
        using var document=JsonDocument.Parse(stream);
        // Do not call ToolCatalog here: its static initialization consumes this
        // schema, and either class may be accessed first by a client.
        var properties=Groups.Values.SelectMany(v=>v).Distinct().ToDictionary(name=>name,name=>ExpandSchema(document.RootElement.GetProperty("properties").GetProperty(name),document.RootElement));
        return JsonSerializer.SerializeToElement(new {type="object",properties,required=Array.Empty<string>(),additionalProperties=false});
    }
    private static JsonElement ExpandSchema(JsonElement schema,JsonElement root)
    {
        if(schema.TryGetProperty("$ref",out var reference))schema=root.GetProperty("$defs").GetProperty(reference.GetString()!.Split('/')[^1]);
        var node=JsonNode.Parse(schema.GetRawText())!.AsObject();
        if(schema.TryGetProperty("properties",out var properties))foreach(var property in properties.EnumerateObject())node["properties"]![property.Name]=JsonNode.Parse(ExpandSchema(property.Value,root).GetRawText());
        return JsonSerializer.SerializeToElement(node);
    }
    private static void MergeObject(JsonObject target,JsonObject incoming)
    {
        foreach(var p in incoming) {
            if(p.Value is JsonObject child&&target[p.Key] is JsonObject existing)MergeObject(existing,child);
            else target[p.Key]=p.Value?.DeepClone();
        }
    }
    public static bool Enabled(VideoEffects effects,string group)
    {
        if(group=="Color")return effects.GetCollection().ColorEnabled;
        var node=JsonSerializer.SerializeToElement(effects).GetProperty(group);
        return node.ValueKind==JsonValueKind.Object?node.GetProperty("Enabled").GetBoolean():node.GetBoolean();
    }
    public static VideoEffects Apply(VideoEffects current,string group,bool enabled,double? strength,JsonElement parameters,int screenWidth,int screenHeight)
    {
        if(!Groups.TryGetValue(group,out var fields))throw new ArgumentException("不支持的效果");
        ToolCatalog.ValidateValue(parameters,ParameterSchema,"parameters");
        var node=JsonSerializer.SerializeToNode(current)!.AsObject();
        foreach(var p in parameters.EnumerateObject()) {
            if(!fields.Contains(p.Name,StringComparer.Ordinal))throw new ArgumentException(p.Name+" 不属于所选效果");
            if(p.Name=="RgbOffset"&&p.Value.GetDouble()*Math.Max(1,screenHeight)/1080d>screenWidth*.1)throw new ArgumentException("RGB 偏移不得超过所选屏幕宽度的 10%");
            if(p.Value.ValueKind==JsonValueKind.Object&&node[p.Name] is JsonObject nested)MergeObject(nested,JsonNode.Parse(p.Value.GetRawText())!.AsObject());
            else node[p.Name]=JsonNode.Parse(p.Value.GetRawText());
        }
        var kind=Enum.Parse<EffectKind>(group);var collection=current.GetCollection() with {Added=current.GetCollection().Added|kind};
        if(strength.HasValue)collection=collection.WithStrength(kind,strength.Value);
        if(kind==EffectKind.Color)collection=collection with {ColorEnabled=enabled};
        else if(node[group] is JsonObject advanced)advanced["Enabled"]=enabled;
        else node[group]=JsonValue.Create(enabled);
        if(kind==EffectKind.Noise&&enabled&&!parameters.TryGetProperty("NoiseStrength",out _)&&current.NoiseStrength==0)node["NoiseStrength"]=.005;
        node["Collection"]=JsonSerializer.SerializeToNode(collection);
        var result=node.Deserialize<VideoEffects>()!.Validate();
        if(parameters.TryGetProperty("AudioRules",out _))foreach(var rule in result.AudioRules)
            if(rule.Parameter=="RgbOffset"&&Math.Max(rule.Weak,rule.Strong)*screenHeight/1080d>screenWidth*.1)throw new ArgumentException("RGB 偏移超出当前显示器范围。");
        return result;
    }
}
