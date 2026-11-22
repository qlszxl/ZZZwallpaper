using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Automation;
using LumaWall.Contracts;

internal static class ContractChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var stream=typeof(ContractChecks).Assembly.GetManifestResourceStream("AutomationTests.LegacyToolNames.json")!;
        var legacy=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        string[] additions=["app_get_capabilities","ui_navigate","ui_select_option","wallpaper_set_volume","wallpaper_edit_playlist","media_remove_folder","effect_order","widget_duplicate","preset_list","preset_get","preset_save","preset_rename","preset_delete","preset_export","preset_import","preset_apply"];
        additions=[..additions,"media_inspect_steam_project","media_import_steam_asset","media_preview_source_delete","media_delete_source","media_reveal_source","app_quit"];
        additions=[..additions,"ui_library_get_filter","ui_library_set_filter","screen_set_features"];
        check(ToolCatalog.All.Count==99,"Exactly 99 public tools");
        ToolCatalog.Validate("media_list",ToolCatalog.Json(new {query="",kind="image",orientation="portrait",resolution="1080p",sort="size",folderId="all"}));
        ToolCatalog.Validate("ui_library_set_filter",ToolCatalog.Json(new {query=""}));
        reject(()=>ToolCatalog.Validate("media_list",ToolCatalog.Json(new {resolution="2K"})),"Unknown resolution rejected");
        ToolCatalog.Validate("diagnostics_export",ToolCatalog.Json(new {}));
        ToolCatalog.Validate("diagnostics_export",ToolCatalog.Json(new {format="bundle",includeLogs=false,includeEnvironment=false,includeConfiguration=true}));
        check(true,"Diagnostic summary compatibility and explicit configuration-only bundle accepted");
        reject(()=>ToolCatalog.Validate("diagnostics_export",ToolCatalog.Json(new {includeConfiguration=true})),"Summary export cannot silently collect configuration");
        reject(()=>ToolCatalog.Validate("diagnostics_export",ToolCatalog.Json(new {format="bundle",includeLogs=false,includeEnvironment=false,includeConfiguration=false})),"Empty diagnostic bundle rejected");
        check(ToolCatalog.All.Select(t=>t.Name).ToHashSet().SetEquals(legacy.Values.Append("wallpaper_get_screen_options").Concat(additions)),"Existing names preserved with explicit coverage additions");
        foreach(var old in legacy.Keys) {
            try {ToolCatalog.Validate(old,ToolCatalog.Json(new {}));throw new Exception("Legacy accepted: "+old);}
            catch(OperationFault e) {check(e.Code=="unknown_tool","Legacy name rejected: "+old);}
        }
        JsonNode Example(JsonElement schema) {
            if(schema.TryGetProperty("enum",out var choices))return JsonNode.Parse(choices[0].GetRawText())!;
            var type=schema.GetProperty("type");string? name=type.ValueKind==JsonValueKind.Array?type[0].GetString():type.GetString();
            return name switch {"string"=>JsonValue.Create("fixture")!,"boolean"=>JsonValue.Create(false)!,"integer" or "number"=>JsonNode.Parse(schema.TryGetProperty("minimum",out var n)?n.GetRawText():"0")!,"array"=>new JsonArray(Example(schema.GetProperty("items"))),"object"=>Required(schema),_=>throw new Exception(name)};
        }
        JsonObject Required(JsonElement schema) {
            var result=new JsonObject();if(schema.TryGetProperty("required",out var required))foreach(var key in required.EnumerateArray())result[key.GetString()!]=Example(schema.GetProperty("properties").GetProperty(key.GetString()!));return result;
        }
        foreach(var tool in ToolCatalog.All) {
            var args=Required(tool.InputSchema);
            switch(tool.Name) {
                case "media_delete_source":args["confirm"]=true;break;
                case "wallpaper_edit_playlist":args["name"]="fixture";break;
                case "media_import_batch":args["paths"]=new JsonArray("C:/fixture.png");break;
                case "config_preview_import":args["code"]="fixture";break;
                case "wallpaper_edit_queue":args["assetIds"]=new JsonArray("fixture");break;
                case "wallpaper_set_screen_options":args["fill"]="Cover";break;
                case "screen_set_features":args["effectsEnabled"]=false;break;
                case "widget_edit_group":args["screenId"]="fixture";break;
                case "widget_set_lock":args["locked"]=false;break;
                case "widget_set_image":args["clear"]=true;break;
            }
            ToolCatalog.Validate(tool.Name,JsonSerializer.SerializeToElement(args));
            check(true,"Required arguments form valid request: "+tool.Name);
            if(ToolCatalog.UsesConfigurationRevision(tool.Name)) {args.Remove("expectedRevision");reject(()=>ToolCatalog.Validate(tool.Name,JsonSerializer.SerializeToElement(args)),"Write requires version: "+tool.Name);}
            var error=OperationErrors.FromException(new OperationFault("resource_not_found","缺少资源","arguments.assetId"));
            ToolCatalog.ValidateValue(error.Data,tool.OutputSchema,"result");
            check(tool.OutputSchema.GetProperty("properties").EnumerateObject().Count()>14,"Typed output metadata: "+tool.Name);
        }
        void Bad(string tool,string json,string code,string? field=null) {
            try {ToolCatalog.Validate(tool,JsonSerializer.Deserialize<JsonElement>(json));throw new Exception("Unexpected accepted request");}
            catch(OperationFault e) {check(e.Code==code&&(field==null||e.Field==field),"Stable validation code: "+tool+" / "+code);}
        }
        Bad("app_get_state","{\"extra\":true}","unknown_field","arguments.extra");
        Bad("media_list","{\"limit\":101}","out_of_range","arguments.limit");
        Bad("effect_get","{\"screenId\":\"s\",\"scope\":\"asset\"}","missing_required_field","arguments.assetId");
        Bad("media_import_batch","{\"expectedRevision\":\"r\",\"paths\":[\"p\"],\"folder\":\"f\"}","invalid_combination");
        Bad("media_import_batch","{\"expectedRevision\":\"r\"}","invalid_combination");
        Bad("wallpaper_edit_queue","{\"screenId\":\"s\",\"expectedRevision\":\"r\",\"action\":\"folder\"}","missing_required_field","arguments.folderId");
        Bad("wallpaper_edit_queue","{\"screenId\":\"s\",\"expectedRevision\":\"r\",\"action\":\"clear\",\"assetIds\":[\"a\"]}","invalid_combination");
        Bad("wallpaper_set_screen_options","{\"screenId\":\"s\",\"expectedRevision\":\"r\"}","invalid_combination");
        Bad("widget_add","{\"screenId\":\"s\",\"expectedRevision\":\"r\",\"kind\":\"Shortcuts\"}","missing_required_field","arguments.name");
        var create=ToolCatalog.All.Single(t=>t.Name=="widget_add").InputSchema.GetProperty("properties").GetProperty("clock").GetProperty("properties").GetProperty("ShowSeconds");
        var patch=ToolCatalog.All.Single(t=>t.Name=="widget_update").InputSchema.GetProperty("properties").GetProperty("clock").GetProperty("properties").GetProperty("ShowSeconds");
        check(!create.GetProperty("default").GetBoolean()&&!patch.TryGetProperty("default",out _),"Creation and patch clock defaults are distinct");
        var widget=WidgetPatch.Apply(new() {Kind=WidgetKind.Clock,Clock=new() {ShowSeconds=true}},ToolCatalog.Json(new {clock=new {Use24Hour=false}}));
        check(widget.Clock.ShowSeconds&&!widget.Clock.Use24Hour,"Omitted clock field stays unchanged");
        var conflict=OperationErrors.FromException(OperationFault.Conflict("old","new","app_get_state"));
        check(!conflict.Data.GetProperty("retryable").GetBoolean()&&conflict.Data.GetProperty("details").GetProperty("currentRevision").GetString()=="new","Version conflict requires reread and exposes current revision");
        var unconfirmed=OperationErrors.Normalize(new(true,ToolCatalog.Json(new {saved=true,renderConfirmed=false})));
        check(unconfirmed.Data.GetProperty("errorCode").GetString()=="render_not_confirmed"&&unconfirmed.Data.GetProperty("saved").GetBoolean(),"Partial render failure retains committed state and normalized error");
    }
}
