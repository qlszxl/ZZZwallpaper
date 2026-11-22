using LumaWall.Contracts;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private string? libraryAppliedSignature;
    private void RefreshLibraryApplied(bool force=false)
    {
        if(navigationPage!="library"&&!force)return;
        var applied=wallpapers?.Running.Values.Select(value=>(value.Asset.Id,value.Binding.ScreenId)).OrderBy(value=>value.ScreenId).ToArray()??[];
        string signature=string.Join(";",applied.Select(value=>value.ScreenId+"="+value.Id));
        if(!force&&signature==libraryAppliedSignature)return;libraryAppliedSignature=signature;
        var labels=applied.GroupBy(value=>value.Id).ToDictionary(group=>group.Key,group=>UiLanguage.Text("已应用 · ")+string.Join("、",group.Select(value=>UiLanguage.Text("屏幕 ")+value.ScreenId.Replace(@"\\.\DISPLAY",""))));
        foreach(var row in LibraryList.Items.OfType<LibraryRow>())row.SetApplied(labels.GetValueOrDefault(row.Asset.Id)??"");
    }
}
