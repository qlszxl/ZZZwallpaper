using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Control? widgetDrawerFocus;
    private int widgetDrawerEpoch;
    private bool widgetDrawerClosing;
    private double widgetDrawerWidth=1040,widgetDrawerHeight=720;
    private void WidgetsAdd_Click(object sender,RoutedEventArgs e)=>ShowWidgetAdd((Control)sender,null);
    private void ShowWidgetAdd(Control origin,string? groupId)
    {
        OpenWidgetDrawer(groupId==null?UiLanguage.Text("添加组件"):UiLanguage.Text("添加组内组件"),origin);
        
        if(groupId==null) {
            var addGroup=WidgetButton(UiLanguage.Text("新建组件组"),CreateWidgetGroup,"Widgets");WidgetDrawerContent.Children.Add(addGroup);
        } else if(widgets!=null) {
            var existing=widgets.State.Widgets.Where(w=>w.LayoutGroupId==null&&ExpansionCatalog.WidgetAvailable(w)).ToArray();
            if(existing.Length>0) {
                WidgetDrawerContent.Children.Add(UtilityText(UiLanguage.Text("加入已有组件"),"UtilityLabel"));
                var existingChoices=ModalChoicesGrid();WidgetDrawerContent.Children.Add(existingChoices);
                foreach(var widget in existing)existingChoices.Children.Add(WidgetButton(WidgetDisplayName(widget,widgets.State),()=> {SetWidgetGroup(widget.Id,groupId);CloseWidgetDrawer(true);},WidgetGlyph(widget.Kind)));
                WidgetDrawerContent.Children.Add(UtilityText(UiLanguage.Text("新建组件"),"UtilityLabel"));
            }
        }
        var choices=ModalChoicesGrid();WidgetDrawerContent.Children.Add(choices);
        foreach(var item in FeaturePacks.Widgets)choices.Children.Add(WidgetButton(item.Entry.Title,()=>AddWidget(WidgetKind.Extension,groupId,item.Key),"Widgets"));
        foreach(var kind in ExpansionCatalog.Widgets.Keys) {
            if(!ExpansionCatalog.WidgetAvailable(kind))continue;
            var button=WidgetButton(WidgetTitle(kind),()=>AddWidget(kind,groupId));button.MinHeight=112;button.Padding=new Thickness(18,16,18,16);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var row=new Grid {ColumnSpacing=16};row.ColumnDefinitions.Add(new() {Width=new GridLength(28)});row.ColumnDefinitions.Add(new());row.Children.Add(UtilityIcon(WidgetGlyph(kind),button,28));
            var title=UtilityText(WidgetTitle(kind),"UtilityHeading");title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);row.Children.Add(title);button.Content=row;
            button.Padding=new Thickness(18,16,18,16);var card=new Grid();card.Children.Add(button);AddCardHelp(card,WidgetTitle(kind));choices.Children.Add(card);
        }
    }
    private void OpenWidgetDrawer(string title,Control origin,double width=1040,double height=720)
    {
        CloseWidgetDrawer(false);widgetDrawerCleanupPending=false;widgetDrawerEpoch++;widgetDrawerFocus=origin;WidgetDrawerTitle.Text=title;WidgetDrawerContent.Children.Clear();
        WidgetDrawerSubtitle.Visibility=ColorDriveBackdrop.Visibility=Visibility.Collapsed;
        WidgetDrawerLayout.RowSpacing=16;WidgetDrawerScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        widgetDrawerWidth=width;widgetDrawerHeight=height;
        WidgetDrawerOverlay.Visibility=Visibility.Visible;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=false;
        WidgetDrawerScroll.ChangeView(null,0,null,true);LayoutCenteredModals();LayoutWidgetCards();WidgetDrawerCloseButton.Focus(FocusState.Programmatic);
    }
    private void CloseWidgetDrawer(bool restoreFocus)
    {
        if(widgetDrawerClosing||WidgetDrawerOverlay==null||WidgetDrawerOverlay.Visibility!=Visibility.Visible)return;
        widgetDrawerClosing=true;
        try {
            foreach(var picker in WidgetDrawerContent.Children.OfType<DiscColorPicker>().ToArray())picker.Commit();
            widgetDrawerEpoch++;WidgetDrawerOverlay.Visibility=Visibility.Collapsed;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=true;
            widgets?.FlushPendingSave();
            if(navigationPage=="widgets")widgetDrawerCleanupPending=true;
            else WidgetDrawerContent.Children.Clear();
            if(restoreFocus&&widgetDrawerFocus?.IsLoaded==true)widgetDrawerFocus.Focus(FocusState.Programmatic);widgetDrawerFocus=null;
        } finally {widgetDrawerClosing=false;ScheduleWidgetRefresh();}
    }
    private void WidgetDrawerClose_Click(object sender,RoutedEventArgs e)=>CloseWidgetDrawer(true);
    private void WidgetDrawer_KeyDown(object sender,KeyRoutedEventArgs e) {if(e.Key==Windows.System.VirtualKey.Escape) {CloseWidgetDrawer(true);e.Handled=true;}}
    private void ShowWidgetIcons(string id,Button origin)
    {
        var widget=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(widget==null)return;
        var card=widgets!.State.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId);OpenWidgetDrawer(UiLanguage.Text("选择图标"),origin);
        
        int epoch=widgetDrawerEpoch;
        WidgetDrawerContent.Children.Add(WidgetIconPicker(widget.Icon,WidgetGlyph(widget.Kind),value=>epoch==widgetDrawerEpoch&&selectedWidgetId==id&&LiveWidget(id,w=>w with {Icon=value}),card?.Icon));
    }
}
