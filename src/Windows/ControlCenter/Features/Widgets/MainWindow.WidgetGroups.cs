using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static string GroupFlowLabel(WidgetGroupFlow flow)=>flow switch {WidgetGroupFlow.Horizontal=>UiLanguage.Text("横向"),WidgetGroupFlow.Vertical=>UiLanguage.Text("纵向"),_=>UiLanguage.Text("网格")};
    private static string WidgetDisplayName(WidgetInstance widget,WidgetConfiguration state)=>widget.Kind==WidgetKind.Shortcuts?state.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId)?.Name??UiLanguage.Text("待设置卡片"):WidgetTitle(widget);
    private void OpenWidgetGroup(string id)
    {
        if(widgets?.State.LayoutGroups.Any(g=>g.Id==id)!=true)return;
        CommitSpectrumInput();if(selectedLayoutGroupId!=id)RememberNavigation();
        widgets.FlushPendingSave();CloseWidgetDrawer(false);selectedWidgetId=null;selectedLayoutGroupId=id;widgetDetailKey=null;RefreshWidgets();WidgetsScroll.ChangeView(null,0,null,true);
    }
    private void CreateWidgetGroup()=>WidgetEdit(()=> {
        if(widgets==null)return;var group=new WidgetLayoutGroup {Name=UiLanguage.Format($"组件组 {widgets.State.LayoutGroups.Length+1}"),Screen=(SelectedScreen??WidgetPlacement.Resolve(null)).DeviceName};
        RememberNavigation();
        CloseWidgetDrawer(false);selectedWidgetId=null;selectedLayoutGroupId=group.Id;widgetDetailKey=null;
        widgets.Save(widgets.State with {LayoutGroups=[..widgets.State.LayoutGroups,group]});WidgetsScroll.ChangeView(null,0,null,true);
    });
    private bool LiveWidgetGroup(string id,Func<WidgetLayoutGroup,WidgetLayoutGroup> update)
    {
        if(widgets==null||syncingWidgetEditor)return false;var current=widgets.State.LayoutGroups.FirstOrDefault(g=>g.Id==id);if(current==null)return false;
        try {var next=update(current);if(next==current)return true;applyingWidgetLive=true;widgets.UpdateLive(widgets.State with {LayoutGroups=widgets.State.LayoutGroups.Select(g=>g.Id==id?next:g).ToArray()});}
        catch(Exception ex) {ShowError(ex.Message,ex);return false;}
        finally {applyingWidgetLive=false;}
        syncWidgetEditor?.Invoke();UpdateWidgetSaveStatus();return true;
    }
    private void SetWidgetGroup(string id,string? groupId)=>WidgetEdit(()=> {
        if(widgets==null)return;var state=widgets.State;var item=state.Widgets.First(w=>w.Id==id);
        // Detaching retains the last visible location, instead of jumping back to
        // coordinates from before the component joined the group.
        var placed=WidgetPlacement.Effective(item,state);
        var changed=item with {LayoutGroupId=groupId,LayoutOrder=state.Widgets.Where(w=>w.LayoutGroupId==groupId).Select(w=>w.LayoutOrder).DefaultIfEmpty(-1).Max()+1,Screen=placed.Screen,X=Math.Clamp(placed.X,0,1),Y=Math.Clamp(placed.Y,0,1)};
        applyingWidgetLive=true;try {widgets.UpdateLive(state with {Widgets=state.Widgets.Select(w=>w.Id==id?changed:w).ToArray()});}finally {applyingWidgetLive=false;}
        if(selectedWidgetId==id)widgetDetailKey=null;RefreshWidgets();
    });
    private void ReorderWidget(string id,int direction)=>WidgetEdit(()=> {
        if(widgets==null)return;var item=widgets.State.Widgets.First(w=>w.Id==id);var members=WidgetStacking.GroupMembers(widgets.State,item.LayoutGroupId!).ToList();
        int from=members.FindIndex(w=>w.Id==id),to=from+direction;if(to<0||to>=members.Count)return;
        (members[from],members[to])=(members[to],members[from]);var order=members.Select((w,i)=>(w.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        widgets.UpdateLive(widgets.State with {Widgets=widgets.State.Widgets.Select(w=>order.TryGetValue(w.Id,out int index)?w with {LayoutOrder=index}:w).ToArray()});
    });
    private void DissolveWidgetGroup(string id)=>WidgetEdit(()=> {
        if(widgets==null)return;var state=widgets.State;
        var singles=state.Widgets.Select(w=> {if(w.LayoutGroupId!=id)return w;var placed=WidgetPlacement.Effective(w,state);return placed with {LayoutGroupId=null,LayoutOrder=0,X=Math.Clamp(placed.X,0,1),Y=Math.Clamp(placed.Y,0,1)};}).ToArray();
        widgets.Save(state with {LayoutGroups=state.LayoutGroups.Where(g=>g.Id!=id).ToArray(),Widgets=singles});
    });
    private static FrameworkElement WidgetAlignmentMenu(Action<bool,double> apply,Func<bool,double,bool> isAligned)
    {
        var row=new Grid {ColumnSpacing=8};
        string[] labels=[UiLanguage.Text("靠左"),UiLanguage.Text("水平居中"),UiLanguage.Text("靠右"),UiLanguage.Text("靠上"),UiLanguage.Text("垂直居中"),UiLanguage.Text("靠下")];
        var buttons=new List<Button>();
        void Refresh() {for(int n=0;n<6;n++)ChoiceSelection.SetIsSelected(buttons[n],isAligned(n<3,(n%3)/2d));}
        for(int i=0;i<6;i++) {
            int index=i;row.ColumnDefinitions.Add(new());
            var button=WidgetButton(labels[i],()=> {apply(index<3,(index%3)/2d);Refresh();});
            button.Padding=new Thickness(8,0,8,0);button.MinWidth=0;button.BorderThickness=new Thickness(3);Grid.SetColumn(button,i);row.Children.Add(button);buttons.Add(button);
        }
        row.Tag=(Action)Refresh;row.Loaded+=(_,_)=>Refresh();row.PointerEntered+=(_,_)=>Refresh();return row;
    }
}
