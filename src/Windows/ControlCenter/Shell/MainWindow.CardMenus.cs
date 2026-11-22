using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static MenuFlyout CreateCardMenu()=>new() {MenuFlyoutPresenterStyle=HomeStyle("HomeMenuFlyoutPresenter")};
    private static MenuFlyoutItem CardMenuAction(string label,Action action)
    {
        var item=new MenuFlyoutItem {
            Text=label,Style=HomeStyle("HomeMenuFlyoutItem"),FontFamily=UiFonts.Body,
            FontWeight=Microsoft.UI.Text.FontWeights.Medium
        };
        item.Click+=(_,_)=>action();return item;
    }
    private static MenuFlyout CardRemovalMenu(string label,Action remove)
    {
        var menu=CreateCardMenu();var item=CardMenuAction(label,remove);
        // Each menu owns its icon geometry; sharing the resource instance
        // makes the second PathIcon fail with E_INVALIDARG during startup.
        item.Icon=new PathIcon {Data=CloneIconGeometry("UtilityIconTrash")};
        menu.Items.Add(item);return menu;
    }
    private void RemoveWidget(string id)=>WidgetEdit(()=> {
        if(widgets==null||!widgets.State.Widgets.Any(w=>w.Id==id))return;
        widgets.FlushPendingSave();
        // Delete only the desktop instance. A saved shortcut can be shared by
        // another widget and must remain available in the card library.
        widgets.Save(widgets.State with {Widgets=widgets.State.Widgets.Where(w=>w.Id!=id).ToArray()});
    });
}
