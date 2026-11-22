namespace LumaWall.Contracts;

public static class WidgetStacking
{
    public static IEnumerable<WidgetInstance> GroupMembers(WidgetConfiguration state,string groupId)=>
        state.Widgets.Where(w=>w.LayoutGroupId==groupId).OrderBy(w=>w.LayoutOrder).ThenBy(w=>w.Id,StringComparer.Ordinal);
    // Mirrors the overview: groups first, then independent widgets. A group's
    // member order is also its tie-break order; changing layer never reorders UI.
    public static IEnumerable<WidgetInstance> ListOrder(WidgetConfiguration state)
    {
        foreach(var group in state.LayoutGroups)
            foreach(var widget in GroupMembers(state,group.Id))yield return widget;
        foreach(var widget in state.Widgets.Where(w=>w.LayoutGroupId==null))yield return widget;
    }
    public static IEnumerable<WidgetInstance> FrontToBack(WidgetConfiguration state)=>ListOrder(state).Where(w=>w.Enabled).OrderByDescending(w=>w.AlwaysOnTop).ThenByDescending(w=>w.Layer);
}
