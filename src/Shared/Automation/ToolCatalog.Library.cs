using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.Automation;

public static partial class ToolCatalog
{
    private static Dictionary<string,JsonElement> LibraryFilterArguments(bool paged)
    {
        var result=new Dictionary<string,JsonElement> {
            {"query",Json(new {type="string",description="Literal name or filename search; empty clears search",maxLength=200})},{"kind",Choice(LibraryFilter.Kinds)},
            {"orientation",Choice(LibraryFilter.Orientations)},{"resolution",Choice(LibraryFilter.Resolutions)},
            {"sort",Choice(LibraryFilter.Sorts)},{"folderId",Text("ID from media_list_folders, or all",128)}
        };
        if(paged) {result.Add("offset",Integer(0,1000000));result.Add("limit",Integer(1,100));}
        return result;
    }
}
