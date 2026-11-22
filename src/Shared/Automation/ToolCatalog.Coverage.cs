using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.Automation;

public static partial class ToolCatalog
{
    private static IEnumerable<OperationTool> CoverageTools()
    {
        var id=Text("Existing ID returned by a read tool");
        yield return Tool("ui_library_get_filter","Read the current library search, filters and folder scope. Session-only UI state, independent of media_list filters.",true,new());
        yield return Tool("ui_library_set_filter","Patch library search and filters; folderId=all selects all sources. Unspecified fields retain current values. Does not import, apply, delete, navigate, or change undo history.",false,LibraryFilterArguments(false));
        yield return Tool("app_get_capabilities","Enumerate feature routes and explicit manual/security boundaries. A listed tool is supported, not evidence that a particular call has passed testing.",true,new());
        yield return Tool("ui_navigate","Open a page and optional screen, effect or widget detail. foreground=true also shows/activates this app; read foreground back before physical input. Uses normal navigation and saves pending edits.",false,new(){{"page",Choice("wallpaper","library","display","widgets","presets","settings","performance","appearance","packs","history")},{"screenId",Screen},{"assetId",id},{"effect",Choice(EffectPatch.Groups.Keys.ToArray())},{"widgetId",id},{"foreground",Boolean()}},"page");
        yield return Tool("ui_select_option","Select a dropdown option on the current effects/widget editor, using its tree revision and option index. Uses the same SelectionChanged handler as the UI.",false,new(){{"controlId",id},{"treeRevision",Text("Exact ui_get_tree revision",64)},{"index",Integer(0,10000)}},"controlId","treeRevision","index");
        yield return Tool("wallpaper_set_volume","Set this screen's wallpaper volume (0-100). Preserves its separate mute switch.",false,new(){{"screenId",Screen},{"volume",Integer(0,100)}},"screenId","volume");
        yield return Tool("wallpaper_edit_playlist","Create, rename, select or delete a named playback list. Edit its contents with wallpaper_edit_queue. Source files remain untouched.",false,new(){{"screenId",Screen},{"action",Choice("create","rename","select","delete")},{"playlistId",id},{"name",Text("Unique playlist name",80)}},"screenId","action");
        yield return Tool("media_remove_folder","Remove a saved folder source, preserving original files and media references.",false,new(){{"folderId",id}},"folderId");
        yield return Tool("effect_order","Set the visual stack order in an explicit screen/asset scope. Supply every added visual effect once, first rendered to last rendered. Audio and auxiliary functions have no layer.",false,new(){{"screenId",Screen},{"assetId",id},{"scope",Choice("screen","asset")},{"expectedRevision",Revision},{"effects",Json(new {type="array",items=Choice(EffectOrder.Default.Select(k=>k.ToString()).ToArray()),maxItems=20,uniqueItems=true})}},"screenId","scope","expectedRevision","effects");
        yield return Tool("widget_duplicate","Duplicate an unlocked widget on its screen. Keeps options and shortcut reference, leaves the group and assigns a fresh ID.",false,new(){{"widgetId",id}},"widgetId");
        yield return Tool("preset_list","List the same saved presets shown in the preset library.",true,new());
        yield return Tool("preset_get","Read preset categories, screens, counts and mapping information without private text, commands or embedded images.",true,new(){{"presetId",id}},"presetId");
        yield return Tool("preset_save","Save current screens as a named preset with the requested categories. Private widget data is excluded unless explicitly requested. Does not apply anything.",false,new(){{"name",Text("Preset name",80)},{"playing",Boolean()},{"library",Boolean()},{"effects",Boolean()},{"widgets",Boolean()},{"includePrivateData",Boolean()}},"name");
        yield return Tool("preset_rename","Rename a saved preset.",false,new(){{"presetId",id},{"name",Text("Preset name",80)}},"presetId","name");
        yield return Tool("preset_delete","Delete only the saved preset; preserves original media and current configuration.",false,new(){{"presetId",id}},"presetId");
        yield return Tool("preset_export","Export a saved preset ZIP into the app's artifact directory. May include media referenced by this preset.",false,new(){{"presetId",id}},"presetId");
        yield return Tool("preset_import","Import a preset ZIP from a manually authorized directory. Extracts media to an app-owned preset media directory; does not apply the preset or grant permissions.",false,new(){{"path",Text("ZIP in a manually authorized directory",32767)}},"path");
        yield return Tool("preset_apply","Apply a saved preset with explicit source-to-local screen mappings; omitted sources are skipped. Uses preset validation and shared undo history. Locked widgets must be explicitly unlocked first.",false,new(){{"presetId",id},{"screens",Json(new {type="array",items=Object(new(){{"source",Screen},{"target",Screen}},"source","target"),minItems=1,maxItems=32})}},"presetId","screens");
    }
    private static string CoverageResultSchema(string name)=>name switch {
        "ui_library_get_filter" or "ui_library_set_filter"=>"query:string kind:string orientation:string resolution:string sort:string folderId:string",
        "app_get_capabilities"=>"features:array boundaries:array toolCount:integer",
        "ui_navigate"=>"page:string screenId:string|null foreground:boolean", "ui_select_option"=>"controlId:string selectedIndex:integer requiresReadback:boolean",
        "wallpaper_set_volume"=>"screenId:string volume:integer enabled:boolean",
        "wallpaper_edit_playlist"=>"screenId:string settings:object", "media_remove_folder"=>"folderId:string removed:boolean",
        "effect_order"=>"screenId:string assetId:string|null scope:string revision:string", "widget_duplicate"=>"widgetId:string",
        "preset_list"=>"items:array", "preset_get"=>"preset:object",
        "preset_save" or "preset_import" or "preset_rename"=>"preset:object", "preset_delete"=>"removed:boolean", "preset_export"=>"path:string",
        "preset_apply"=>"result:object warnings:string",
        "media_inspect_steam_project"=>"project:object total:integer offset:integer items:array issues:array",
        "media_import_steam_asset"=>"assetId:string name:string kind:string",
        "media_preview_source_delete"=>"deletePlanId:string expectedRevision:string path:string Bytes:integer expiresAt:string assetIds:array affectedScreens:array presets:array unreadablePresets:integer undoable:boolean",
        "media_delete_source"=>"deleted:boolean permanent:boolean result:object undoable:boolean",
        "media_reveal_source"=>"assetId:string opened:boolean", "app_quit"=>"",
        _=>throw new InvalidOperationException("Missing output contract: "+name)
    };
}
