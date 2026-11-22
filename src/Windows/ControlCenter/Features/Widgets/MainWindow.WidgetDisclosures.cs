using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private readonly Dictionary<string,Dictionary<string,bool>> widgetDisclosures=new();
    private string? activeWidgetDisclosure;
    private IEnumerable<(string Key,FrameworkElement Element)> WidgetDisclosureElements()
    {
        var counts=new Dictionary<string,int>();
        foreach(var element in EffectsLogicalChildren(WidgetEditorPanel)) {
            if(element is not Expander&&!(element is ContentControl card&&card.Style==HomeStyle("ParameterSectionCard")&&!SectionChrome.GetHasOwnHeader(card)))continue;
            string title=SectionChrome.GetScope(element)+"/"+SectionChrome.GetTitle(element);
            if(element is Expander expander)title+="/"+(expander.Header is TextBlock text?text.Text:expander.Header?.ToString());
            int index=counts.GetValueOrDefault(title);counts[title]=index+1;yield return(title+"/"+index,element);
        }
    }
    private void CaptureWidgetDisclosures()
    {
        if(navigationPage!="widgets"||activeWidgetDisclosure==null)return;
        if(!widgetDisclosures.TryGetValue(activeWidgetDisclosure,out var state))widgetDisclosures[activeWidgetDisclosure]=state=new();
        foreach(var item in WidgetDisclosureElements())state[item.Key]=DisclosureOpen(item.Element);
    }
    private void RestoreWidgetDisclosures(string id)
    {
        activeWidgetDisclosure=id;widgetDisclosures.TryGetValue(id,out var state);
        foreach(var item in WidgetDisclosureElements())SetDisclosureOpen(item.Element,state?.GetValueOrDefault(item.Key)??false);
    }
    private void ResetWidgetDisclosureSession()
    {
        widgetDisclosures.Clear();activeWidgetDisclosure=null;
        foreach(var item in WidgetDisclosureElements())SetDisclosureOpen(item.Element,false);
    }
}
