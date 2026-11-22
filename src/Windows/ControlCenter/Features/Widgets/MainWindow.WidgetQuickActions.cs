using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private MenuFlyout WidgetCardMenu(string id)
    {
        var menu=CreateWidgetMenu();
        var visibility=CardMenuAction(UiLanguage.Text("显示组件"),()=>WidgetEdit(()=> {
            var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);
            if(current!=null)widgets!.SetEnabled(id,!current.Enabled);
        }));
        var locking=CardMenuAction(UiLanguage.Text("锁定组件"),()=>WidgetEdit(()=> {
            if(widgets==null)return;var state=widgets.State;var current=state.Widgets.FirstOrDefault(w=>w.Id==id);
            if(current==null||state.LayoutGroups.Any(g=>g.Id==current.LayoutGroupId&&g.Locked))return;
            widgets.Save(state with {Widgets=state.Widgets.Select(w=>w.Id==id?w with {Locked=!current.Locked}:w).ToArray()});
        }));
        var interaction=CardMenuAction(UiLanguage.Text("锁定交互"),()=>WidgetEdit(()=> {
            if(widgets==null)return;var state=widgets.State;var current=state.Widgets.FirstOrDefault(w=>w.Id==id);
            if(current==null)return;
            widgets.Save(state with {Widgets=state.Widgets.Select(w=>w.Id==id?w with {InteractionLocked=!current.InteractionLocked}:w).ToArray()});
        }));
        var remove=CardMenuAction(UiLanguage.Text("删除组件"),()=>RemoveWidget(id));
        remove.Icon=new PathIcon {Data=CloneIconGeometry("UtilityIconTrash")};
        menu.Items.Add(visibility);menu.Items.Add(locking);menu.Items.Add(interaction);menu.Items.Add(new MenuFlyoutSeparator());menu.Items.Add(remove);
        void Refresh() {
            var state=widgets?.State;var current=state?.Widgets.FirstOrDefault(w=>w.Id==id);
            visibility.Text=current?.Enabled==true?UiLanguage.Text("隐藏组件"):UiLanguage.Text("显示组件");
            locking.Text=current!=null&&state!.IsEditingLocked(current)?UiLanguage.Text("解锁组件"):UiLanguage.Text("锁定组件");
            interaction.Text=current?.InteractionLocked==true?UiLanguage.Text("解锁交互"):UiLanguage.Text("锁定交互");interaction.IsEnabled=current!=null;
            visibility.IsEnabled=remove.IsEnabled=current!=null;
            locking.IsEnabled=current!=null&&!state!.LayoutGroups.Any(g=>g.Id==current.LayoutGroupId&&g.Locked);
        }
        menu.Opening+=(_,_)=>Refresh();Refresh();return menu;
    }
    private MenuFlyout WidgetGroupCardMenu(string id)
    {
        var menu=CreateWidgetMenu();
        var visibility=CardMenuAction(UiLanguage.Text("显示组内全部组件"),()=>WidgetEdit(()=> {
            if(widgets==null)return;var state=widgets.State;
            if(!state.LayoutGroups.Any(g=>g.Id==id)||!state.Widgets.Any(w=>w.LayoutGroupId==id))return;
            bool visible=state.Widgets.Any(w=>w.LayoutGroupId==id&&w.Enabled);
            widgets.Save(state with {Widgets=state.Widgets.Select(w=>w.LayoutGroupId==id?w with {Enabled=!visible}:w).ToArray()});
        }));
        var locking=CardMenuAction(UiLanguage.Text("锁定组件组"),()=>WidgetEdit(()=> {
            if(widgets==null)return;var state=widgets.State;var current=state.LayoutGroups.FirstOrDefault(g=>g.Id==id);if(current==null)return;
            widgets.Save(state with {LayoutGroups=state.LayoutGroups.Select(g=>g.Id==id?g with {Locked=!current.Locked}:g).ToArray()});
        }));
        var dissolve=CardMenuAction(UiLanguage.Text("解散组件组（保留组件）"),()=> {
            if(widgets?.State.LayoutGroups.Any(g=>g.Id==id)==true)DissolveWidgetGroup(id);
        });
        dissolve.Icon=new PathIcon {Data=CloneIconGeometry("UtilityIconTrash")};
        menu.Items.Add(visibility);menu.Items.Add(locking);menu.Items.Add(new MenuFlyoutSeparator());menu.Items.Add(dissolve);
        void Refresh() {
            var state=widgets?.State;var current=state?.LayoutGroups.FirstOrDefault(g=>g.Id==id);
            bool hasMembers=current!=null&&state!.Widgets.Any(w=>w.LayoutGroupId==id);
            bool visible=hasMembers&&state!.Widgets.Any(w=>w.LayoutGroupId==id&&w.Enabled);
            visibility.Text=visible?UiLanguage.Text("隐藏组内全部组件"):UiLanguage.Text("显示组内全部组件");
            locking.Text=current?.Locked==true?UiLanguage.Text("解锁组件组"):UiLanguage.Text("锁定组件组");
            visibility.IsEnabled=hasMembers;locking.IsEnabled=dissolve.IsEnabled=current!=null;
        }
        menu.Opening+=(_,_)=>Refresh();Refresh();return menu;
    }
    private Grid WidgetCardFooter(WidgetInstance widget)
    {
        var row=new Grid {ColumnSpacing=10,MinHeight=36};row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(UiLanguage.Text("层级"),"UtilityLabel");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
        var input=WidgetLayerInput(widget);input.Width=76;input.AttachDragHandle(label);Grid.SetColumn(input,1);row.Children.Add(input);return row;
    }
}
