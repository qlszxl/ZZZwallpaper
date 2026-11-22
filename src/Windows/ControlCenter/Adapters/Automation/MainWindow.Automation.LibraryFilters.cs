using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static LibraryFilter AutomationLibraryFilter(JsonElement args,LibraryFilter current)=>new LibraryFilter(
        Optional(args,"query")??current.Query,Optional(args,"kind")??current.Kind,Optional(args,"orientation")??current.Orientation,
        Optional(args,"resolution")??current.Resolution,Optional(args,"sort")??current.Sort).Validate();
    private string? AutomationLibraryFolder(JsonElement args,string? current)
    {
        string? id=Optional(args,"folderId")??current;
        if(id=="all"||id==null)return null;
        if(!modules.Library.Folders().Any(f=>f.Id==id))throw new OperationFault("resource_not_found","素材来源不存在","arguments.folderId");
        return id;
    }
    private object AutomationLibraryFilterState()=>new {query=libraryFilter.Query,kind=libraryFilter.Kind,orientation=libraryFilter.Orientation,
        resolution=libraryFilter.Resolution,sort=libraryFilter.Sort,folderId=SelectedLibraryFolder?.Id??"all"};
}
