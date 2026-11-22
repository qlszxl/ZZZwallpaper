using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaWall.Automation;

public static partial class ToolCatalog
{
    public static bool UsesConfigurationRevision(string name)=>name is
        "app_update_settings" or "screen_set_features" or "app_restart" or "history_create_checkpoint" or "history_restore_checkpoint" or "config_commit_import" or
        "media_import" or "media_import_batch" or "media_remove" or "media_relink" or "media_refresh" or "media_set_poster" or "media_reset_poster" or
        "wallpaper_apply" or "wallpaper_cancel" or "wallpaper_set_paused" or "wallpaper_set_sound" or "wallpaper_step" or "wallpaper_set_playback" or
        "wallpaper_edit_queue" or "wallpaper_set_screen_options" or "wallpaper_set_fps_limit" or "wallpaper_set_decoder_mode" or
        "widget_add" or "widget_update" or "widget_remove" or "widget_set_lock" or "widget_edit_group" or "widget_set_image" or "widget_reset_appearance" or "widget_remove_shortcut" or
        "audio_set_capture_device" or "effect_edit_mask" or "wallpaper_set_volume" or "wallpaper_edit_playlist" or "media_remove_folder" or "widget_duplicate" or "preset_save" or "preset_import" or "preset_apply" or "preset_rename" or "preset_delete" or "media_import_steam_asset" or "media_delete_source" or "app_quit";

    private static JsonElement AddConditions(string name,JsonElement schema)
    {
        var node=JsonNode.Parse(schema.GetRawText())!.AsObject();var rules=new JsonArray();
        JsonObject Required(params string[] keys)=>new() {["required"]=JsonSerializer.SerializeToNode(keys)};
        JsonObject Match(string field,params string[] values)=>new() {["properties"]=new JsonObject {[field]=new JsonObject {["enum"]=JsonSerializer.SerializeToNode(values)}},["required"]=new JsonArray(field)};
        JsonObject Forbid(params string[] keys)=>new() {["not"]=new JsonObject {["anyOf"]=new JsonArray(keys.Select(k=>(JsonNode)Required(k)).ToArray())}};
        void When(string field,string[] values,JsonObject then)=>rules.Add(new JsonObject {["if"]=Match(field,values),["then"]=then});
        void Need(string field,string[] values,params string[] keys)=>When(field,values,Required(keys));
        void Exclude(string field,string[] values,params string[] keys)=>When(field,values,Forbid(keys));
        void Either(string first,string second)=>rules.Add(new JsonObject {["oneOf"]=new JsonArray(Required(first),Required(second))});
        if(name is "effect_order" or "effect_get" or "effect_set" or "effect_clear" or "effect_undo" or "audio_set_rule" or "audio_remove_rule")Need("scope",["asset","effective"],"assetId");
        if(name=="media_import_batch") {Either("paths","folder");rules.Add(new JsonObject {["if"]=Required("recursive"),["then"]=Required("folder")});}
        if(name=="config_preview_import")Either("code","imageAssetId");
        if(name=="diagnostics_export") {
            rules.Add(new JsonObject {["if"]=Match("format","bundle"),["else"]=Forbid("includeLogs","includeEnvironment","includeConfiguration")});
            rules.Add(new JsonObject {
                ["if"]=new JsonObject {["required"]=new JsonArray("includeLogs","includeEnvironment"),["properties"]=new JsonObject {
                    ["includeLogs"]=new JsonObject {["const"]=false},["includeEnvironment"]=new JsonObject {["const"]=false}}},
                ["then"]=new JsonObject {["required"]=new JsonArray("includeConfiguration"),["properties"]=new JsonObject {["includeConfiguration"]=new JsonObject {["const"]=true}}}
            });
        }
        if(name=="wallpaper_edit_queue") {
            Need("action",["set","add","remove","reorder"],"assetIds");Need("action",["folder"],"folderId");
            Exclude("action",["set","add","remove","reorder","clear"],"folderId");Exclude("action",["folder","clear"],"assetIds");
        }
        if(name=="wallpaper_edit_playlist") {Need("action",["create","rename"],"name");Need("action",["rename","select","delete"],"playlistId");Exclude("action",["create"],"playlistId");Exclude("action",["select","delete"],"name");}
        if(name=="wallpaper_set_screen_options")rules.Add(new JsonObject {["anyOf"]=new JsonArray(Required("fill"),Required("hideTaskbar"),Required("taskbarEnabled"),Required("taskbarStyle"),Required("taskbarVisual"),Required("positionX"),Required("positionY"),Required("scale"))});
        if(name=="widget_set_lock")rules.Add(new JsonObject {["anyOf"]=new JsonArray(Required("locked"),Required("interactionLocked"))});
        if(name=="screen_set_features")rules.Add(new JsonObject {["anyOf"]=new JsonArray(Required("effectsEnabled"),Required("widgetsEnabled"))});
        if(name=="widget_add") {Need("kind",["Shortcuts"],"name","url");rules.Add(new JsonObject {["if"]=Match("kind","Shortcuts"),["else"]=Forbid("name","url")});}
        if(name=="widget_edit_group") {
            Need("action",["create"],"screenId");Exclude("action",["create"],"groupId");
            Need("action",["update","members","ungroup","lock"],"groupId");Need("action",["members"],"widgetIds");Need("action",["lock"],"locked");
            Exclude("action",["create","update","members","ungroup"],"locked");Exclude("action",["update","ungroup","lock"],"widgetIds");
        }
        if(name=="effect_edit_mask") {Need("action",["update","delete"],"maskId");Exclude("action",["create"],"maskId");Exclude("action",["delete"],"parameters","imageAssetId");}
        if(name=="widget_set_image")rules.Add(new JsonObject {["oneOf"]=new JsonArray(
            new JsonObject {["required"]=new JsonArray("assetId"),["not"]=new JsonObject {["properties"]=new JsonObject {["clear"]=new JsonObject {["const"]=true}},["required"]=new JsonArray("clear")}},
            new JsonObject {["required"]=new JsonArray("clear"),["properties"]=new JsonObject {["clear"]=new JsonObject {["const"]=true}},["not"]=Required("assetId")})});
        if(name=="ui_perform_action") {Need("action",["text"],"text");Need("action",["key"],"key");Need("action",["drag"],"x","y","toX","toY");Need("action",["scroll"],"delta");}
        if(rules.Count>0)node["allOf"]=rules;
        return JsonSerializer.SerializeToElement(node);
    }

    // Open for additive result fields; every declared field still has a stable type.
    // Common fields also cover errors, idempotency replays and asynchronous operation replies.
    private static JsonElement ResultSchema(string name)
    {
        var fields=new Dictionary<string,JsonElement>();
        JsonElement Kind(string types)=>Json(new {type=types.Split('|')});
        foreach(var spec in ("error:string errorCode:string field:string|null retryable:boolean recovery:string|null details:object|null operationId:string instanceId:string configurationRevision:string|null savedRevision:string|null saved:boolean status:string renderConfirmed:boolean|null rendererRevisions:array|null widgetRequestedRevision:string|null widgetAppliedRevision:string|null "+(name switch {
            "app_get_state"=>"version:string releaseChannel:string agentGuideVersion:integer mcpEnabled:boolean history:object automationPolicy:object selectedScreenId:string|null effectTarget:object capabilities:object screens:array widgets:array audioAutoAdapt:boolean fpsLimit:integer decoderMode:string",
            "app_get_settings"=>"skin:string motion:boolean pattern:boolean fullscreenPause:boolean pauseOnLock:boolean pauseOnDisplayOff:boolean pauseOnSuspend:boolean autoStart:boolean startupDelayEnabled:boolean startupDelaySeconds:integer helpHints:boolean screenFeatures:array schema:object",
            "screen_set_features"=>"screenId:string effectsEnabled:boolean widgetsEnabled:boolean",
            "app_update_settings"=>"systemSideEffect:string|null", "app_flush_configuration"=>"walCheckpointComplete:boolean", "app_restart"=>"oldInstanceId:string reconnectRequired:boolean",
            "media_list"=>"total:integer offset:integer items:array", "media_get_info"=>"assetId:string available:boolean reason:string probe:object fingerprint:object",
            "media_import"=>"assetId:string name:string kind:string", "media_import_batch"=>"jobId:string cancelPolicy:string total:integer processed:integer completedItemsRetained:boolean",
            "media_remove"=>"removed:array errors:array affectedScreens:array affectedQueues:array originalFilesPreserved:boolean",
            "media_relink" or "media_refresh"=>"assetId:string available:boolean", "media_list_folders"=>"items:array",
            "media_preview_poster"=>"assetId:string previewPath:string frame:object", "media_set_poster" or "media_reset_poster"=>"Mode:string Seconds:number|null",
            "wallpaper_apply"=>"applied:boolean screenId:string assetId:string", "wallpaper_cancel"=>"screenId:string removed:boolean wallpaper:null systemBackgroundRestored:boolean cleanupComplete:boolean",
            "wallpaper_set_paused"=>"userPaused:boolean effectivePaused:boolean", "wallpaper_set_sound"=>"enabled:boolean", "wallpaper_step"=>"assetId:string|null",
            "wallpaper_set_playback"=>"settings:object", "wallpaper_edit_queue"=>"screenId:string queue:object currentAssetId:string|null",
            "wallpaper_get_screen_options"=>"screenId:string online:boolean fill:string hideTaskbar:boolean taskbarEnabled:boolean taskbarStyle:string taskbarVisual:object customPosition:boolean positionX:integer positionY:integer scale:integer", "wallpaper_set_screen_options"=>"screenId:string fill:string hideTaskbar:boolean taskbarEnabled:boolean taskbarStyle:string taskbarVisual:object customPosition:boolean positionX:integer positionY:integer scale:integer",
            "wallpaper_set_fps_limit"=>"fps:integer", "wallpaper_set_decoder_mode"=>"mode:string",
            "effect_get"=>"screenId:string assetId:string|null scope:string revision:string fill:string parameters:object scanCardImages:object enabled:object overrides:object audioParameters:array audioRules:array ownAudioRules:array collection:object editableGroups:object parameterSchema:object masks:array effectMasks:object appliedScreens:array",
            "effect_set" or "effect_set_scope" or "effect_clear" or "effect_copy_screen_to_asset" or "effect_undo" or "audio_set_rule" or "audio_remove_rule"=>"screenId:string assetId:string|null scope:string revision:string",
            "effect_list_masks"=>"scope:string masks:array references:object", "effect_edit_mask"=>"maskId:string|null storedMaskIds:array references:object",
            "widget_get"=>"widgetId:string revision:string widgetRevision:string appliedWidgetRevision:string|null snapshotStillCurrent:boolean renderTelemetry:object|null kind:string screenId:string locked:boolean interactionLocked:boolean defaultGroups:array visual:object clock:object countdown:object focus:object gridOptions:object|null parameterSchemas:object website:object|null",
            "widget_add" or "widget_update"=>"widgetId:string", "widget_remove"=>"widgetId:string removed:boolean", "widget_set_lock" or "widget_set_image" or "widget_reset_appearance"=>"widgetId:string locked:boolean interactionLocked:boolean image:object|null",
            "widget_edit_group"=>"groupId:string group:object|null members:array", "widget_list_shortcuts"=>"items:array groups:array", "widget_remove_shortcut"=>"shortcutId:string removedWidgetIds:array",
            "audio_get_state"=>"sampledAt:string|null active:boolean requestedDevice:string telemetry:object|null measurement:string", "audio_list_devices"=>"defaultDeviceId:string items:array", "audio_set_capture_device"=>"screenId:string requestedDevice:string requestedEndpoint:string systemDefaultChanged:boolean",
            "audio_play_test"=>"deviceId:string loop:boolean volume:number", "audio_get_test_state"=>"state:string positionSeconds:number|null durationSeconds:number|null deviceId:string|null", "audio_stop_test"=>"stopped:boolean",
            "history_undo" or "history_redo"=>"message:string revision:string", "history_create_checkpoint"=>"checkpointId:string scope:string expires:string timerPolicy:string excluded:array",
            "history_restore_checkpoint"=>"warnings:array historyCleared:boolean", "history_delete_checkpoint"=>"removed:boolean",
            "config_export"=>"path:string code:string|null qrPath:string|null parts:integer|null excludedCommandAndFolderEntries:integer", "config_preview_import"=>"importPlanId:string expectedRevision:string screenId:string replaceAllWidgets:boolean oldWidgetCount:integer newWidgetCount:integer replaceDisplayEffects:boolean assetOverridesPreserved:boolean warnings:array removedWidgetIds:array",
            "config_commit_import"=>"importPlanId:string widgetIds:array", "config_cancel_import"=>"cancelled:boolean",
            "task_get_operation"=>"tool:string startedAt:string finishedAt:string|null isError:boolean reply:object", "task_get_job" or "task_cancel_job"=>"jobId:string startedAt:string finishedAt:string|null total:integer processed:integer offset:integer items:array completedItemsRetained:boolean",
            "ui_get_tree"=>"treeRevision:string page:string coordinateSpace:string total:integer offset:integer controls:array scope:string truncated:boolean snapshotLifetimeMs:integer", "ui_perform_action"=>"controlId:string inputMethod:string eventCount:integer requiresReadback:boolean",
            "ui_capture_desktop" or "ui_capture_window"=>"path:string width:integer height:integer capturedAt:string",
            "diagnostics_benchmark_decode"=>"AssetId:string FpsLimit:integer SourceStamp:string SampledAt:string Results:array Recommendation:string", "diagnostics_sample_performance"=>"sampledAt:string intervalSeconds:integer processes:array gpu:object|null note:string",
            "diagnostics_get_render_stats"=>"screenId:string sampledAt:string|null stats:object|null decoder:string requestedRevision:integer appliedRevision:integer measurement:string unavailable:array", "diagnostics_get_logs"=>"scope:string items:array applicationLogs:array boundedTail:boolean", "diagnostics_export"=>"path:string createdAt:string format:string includes:object",
            _=>CoverageResultSchema(name)
        })).Split(' ',StringSplitOptions.RemoveEmptyEntries)) {var pair=spec.Split(':');fields[pair[0]]=Kind(pair[1]);}
        // Job records can carry a null error when no failure occurred.
        fields["error"]=Kind("string|null");
        return Json(new {type="object",properties=fields,additionalProperties=true});
    }
}
