namespace LumaWall.Automation;

public static partial class ToolCatalog
{
    private static IEnumerable<OperationTool> FileTools()
    {
        var id=Text("Identifier from the corresponding read/preview tool");
        yield return Tool("media_inspect_steam_project","Inspect one explicitly authorized Wallpaper Engine project folder containing project.json; list its importable media, package entries and textures. Does not run scripts or follow Steam library discovery outside this project.",true,new(){{"path",Text("Authorized project directory",32767)},{"offset",Integer(0,10000)},{"limit",Integer(1,100)}},"path");
        yield return Tool("media_import_steam_asset","Import an asset returned by media_inspect_steam_project. Optional copyDirectory must already exist in an authorized directory. Package entries/textures require copyDirectory. Uses the normal validated extraction/importer and preserves originals.",false,new(){{"sourceId",id},{"copyDirectory",Text("Authorized existing output directory outside the project",32767)}},"sourceId");
        yield return Tool("media_preview_source_delete","Preview deletion of one imported, manually authorized source file, its affected screens/references and preset impact. Returns a five-minute plan. No file is deleted. Steam-managed originals remain managed in Steam.",true,new(){{"assetId",id}},"assetId");
        yield return Tool("media_delete_source","Execute the exact previewed source-file deletion. Requires explicit confirm=true, current configuration revision and an unchanged file stamp. Defaults to recycle bin; permanent=true is irreversible. Not undoable by application history. Invoke only after the user authorizes deletion of the previewed file.",false,new(){{"deletePlanId",id},{"confirm",Json(new {type="boolean",@const=true})},{"permanent",Boolean()}},"deletePlanId","confirm");
        yield return Tool("media_reveal_source","Open Explorer selecting the source file of one existing imported media asset. Never runs the media or accepts arbitrary shell commands.",false,new(){{"assetId",id}},"assetId");
        yield return Tool("app_quit","Flush configuration then exit this app normally, stopping its wallpapers, widgets and test audio. Returns before shutdown; reconnect requires launching the app again.",false,new());
    }
}
