using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void BuildWidgetGroupEditor(WidgetLayoutGroup initial,WidgetConfiguration state)
    {
        string id=initial.Id;widgetLayerInputs.Clear();
        var heading=new StackPanel {Spacing=6};var title=UtilityText(initial.Name,"UtilityHeading");heading.Children.Add(title);
        widgetSaveStatus=UtilityText(widgets!.SaveStatus);widgetSaveStatus.Visibility=Visibility.Collapsed;WidgetEditorPanel.Children.Add(heading);
        var name=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=initial.Name,MaxLength=40};
        var nameHint=UtilityText("");nameHint.Visibility=Visibility.Collapsed;
        var naming=new StackPanel {Spacing=12};naming.Children.Add(WidgetField(UiLanguage.Text("组件组名称"),name));naming.Children.Add(nameHint);WidgetEditorPanel.Children.Add(UtilityCard(naming,UiLanguage.Text("组件组信息")));
        var locked=WidgetToggle(UiLanguage.Text("锁定整组"),initial.Locked);naming.Children.Add(WidgetToggleRow(UiLanguage.Text("锁定整组"),locked));ToolTipService.SetToolTip(locked,UiLanguage.Text("锁定整组位置和右键菜单"));

        var position=new StackPanel {Spacing=16};position.Children.Add(UtilityText(UiLanguage.Text("整组位置"),"UtilityHeading"));
        position.Children.Add(WidgetPositionAlignment(id,true,(horizontal,value)=>LiveWidgetGroup(id,g=>horizontal?g with {X=value}:g with {Y=value}),out var refreshAlignment));
        var screens=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};position.Children.Add(WidgetField(UiLanguage.Text("显示器"),screens));
        var x=new WidgetPositionInput(UiLanguage.Text("组件组水平 X"));var y=new WidgetPositionInput(UiLanguage.Text("组件组垂直 Y"));
        var coordinates=new Grid {ColumnSpacing=16,RowSpacing=12};coordinates.Children.Add(WidgetField(UiLanguage.Text("水平 X"),x));coordinates.Children.Add(WidgetField(UiLanguage.Text("垂直 Y"),y));UtilityAdaptiveGrid(coordinates,2,480);position.Children.Add(coordinates);
        WidgetEditorPanel.Children.Add(UtilityCard(position));

        var arrangement=new StackPanel {Spacing=16};arrangement.Children.Add(UtilityText(UiLanguage.Text("组内排列"),"UtilityHeading"));
        var layout=new LayoutGridPicker();arrangement.Children.Add(layout);
        layout.Changed+=(flow,columns)=> {if(!syncingWidgetEditor)LiveWidgetGroup(id,g=>g with {Flow=flow,Columns=columns});};
        arrangement.Children.Add(UtilityText(UiLanguage.Text("组内对齐"),"UtilityLabel"));
        var alignment=WidgetAlignmentMenu((horizontal,value)=>LiveWidgetGroup(id,g=>horizontal?g with {HorizontalAlignment=(WidgetAlignment)(int)(value*2)}:g with {VerticalAlignment=(WidgetAlignment)(int)(value*2)}),(horizontal,value)=> {var group=widgets!.State.LayoutGroups.First(g=>g.Id==id);return (int)(horizontal?group.HorizontalAlignment:group.VerticalAlignment)==(int)(value*2);});arrangement.Children.Add(alignment);
        var gap=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=80,StepFrequency=1,Value=initial.Gap};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(gap,UiLanguage.Text("组内间距"));
        var gapHeading=new Grid();gapHeading.ColumnDefinitions.Add(new());gapHeading.ColumnDefinitions.Add(new() {Width=GridLength.Auto});gapHeading.Children.Add(UtilityText(UiLanguage.Text("组件间距"),"UtilityLabel"));
        var gapValue=new EffectValueInput {Target=gap,Unit="DIP",Precision=0,Width=148};Grid.SetColumn(gapValue,1);gapHeading.Children.Add(gapValue);var gapField=new StackPanel {Spacing=ParameterUi.LabelGap};gapField.Children.Add(gapHeading);gapField.Children.Add(gap);arrangement.Children.Add(gapField);
        var arrangementHint=UtilityText("");arrangementHint.Visibility=Visibility.Collapsed;arrangement.Children.Add(arrangementHint);WidgetEditorPanel.Children.Add(UtilityCard(arrangement));

        var members=new StackPanel {Spacing=12};var memberHeading=new Grid {ColumnSpacing=16};memberHeading.ColumnDefinitions.Add(new());memberHeading.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var countText=UtilityText(UiLanguage.Text("组内组件"),"UtilityHeading");memberHeading.Children.Add(countText);var add=WidgetButton(UiLanguage.Text("添加组件"),()=>{},"Add");add.Click+=(_,_)=>ShowWidgetAdd(add,id);Grid.SetColumn(add,1);memberHeading.Children.Add(add);members.Children.Add(memberHeading);
        var memberList=new OrderedTileList();members.Children.Add(memberList);var memberDetails=new StackPanel {Spacing=12};members.Children.Add(memberDetails);WidgetEditorPanel.Children.Add(UtilityCard(members));
        var dissolve=WidgetButton(UiLanguage.Text("解散组件组"),()=>DissolveWidgetGroup(id),"Trash");dissolve.HorizontalAlignment=HorizontalAlignment.Left;WidgetEditorPanel.Children.Add(dissolve);
        DecorateParameterSections(WidgetEditorPanel,UiLanguage.Text("组件组设置"));

        void ShowSelectedMember() {
            memberDetails.Children.Clear();memberDetails.ContextFlyout=null;widgetLayerInputs.Clear();
            var item=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==memberList.SelectedId);if(item==null)return;
            memberDetails.ContextFlyout=WidgetCardMenu(item.Id);
            var actions=new Grid {ColumnSpacing=12};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new());
            var edit=WidgetButton(UiLanguage.Text("编辑组件"),()=>OpenWidget(item.Id));var detach=WidgetButton(UiLanguage.Text("移出组件组"),()=>SetWidgetGroup(item.Id,null));
            actions.Children.Add(edit);Grid.SetColumn(detach,1);actions.Children.Add(detach);memberDetails.Children.Add(actions);memberDetails.Children.Add(WidgetCardFooter(item));
        }
        memberList.Selected+=_=>ShowSelectedMember();memberList.Invoked+=OpenWidget;
        memberList.Reordered+=(source,target)=>WidgetEdit(()=> {
            if(widgets==null)return;var list=WidgetStacking.GroupMembers(widgets.State,id).ToList();
            int from=list.FindIndex(w=>w.Id==source),to=list.FindIndex(w=>w.Id==target);if(from<0||to<0)return;
            var moved=list[from];list.RemoveAt(from);list.Insert(to,moved);var order=list.Select((w,i)=>(w.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
            widgets.UpdateLive(widgets.State with {Widgets=widgets.State.Widgets.Select(w=>order.TryGetValue(w.Id,out int index)?w with {LayoutOrder=index}:w).ToArray()});
        });
        string? topologyKey=null,memberKey=null;
        syncWidgetEditor=()=> {
            if(widgets==null)return;var current=widgets.State.LayoutGroups.FirstOrDefault(g=>g.Id==id);if(current==null)return;syncingWidgetEditor=true;
            try {
                title.Text=current.Name;locked.IsOn=current.Locked;
                refreshAlignment();
                string topology=string.Join(";",Screen.AllScreens.Select(s=>s.DeviceName+s.WorkingArea));
                if(topology!=topologyKey) {topologyKey=topology;screens.ItemsSource=Screen.AllScreens.Select(s=>new ScreenRow(s)).ToArray();}
                screens.SelectedItem=((ScreenRow[])screens.ItemsSource).FirstOrDefault(s=>s.Screen.DeviceName==WidgetPlacement.Resolve(current.Screen).DeviceName);
                var available=ExpansionCatalog.AvailableWidgets(widgets.State);
                var bounds=WidgetPlacement.MeasureGroup(available,current);x.SetBounds(bounds.MaxX,Math.Round(current.X*bounds.MaxX));y.SetBounds(bounds.MaxY,Math.Round(current.Y*bounds.MaxY));
                var area=WidgetPlacement.Resolve(current.Screen).WorkingArea;
                bool insufficient=bounds.Width>area.Width||bounds.Height>area.Height;
                arrangementHint.Text=insufficient?UiLanguage.Text("屏幕空间不足，请减少成员或尺寸。"):"";arrangementHint.Visibility=insufficient?Visibility.Visible:Visibility.Collapsed;
                var list=WidgetStacking.GroupMembers(available,id).ToArray();
                gap.Value=current.Gap;layout.Update(current.Flow,current.Columns,list.Length);
                if(alignment.Tag is Action refreshGroupAlignment)refreshGroupAlignment();
                string key=current.Locked+";"+string.Join(";",list.Select(w=>w.Id+":"+w.Enabled+":"+w.Locked+":"+WidgetDisplayName(w,widgets.State)));
                if(key!=memberKey) {
                    memberKey=key;countText.Text=UiLanguage.Format($"组内组件 · {list.Length}");
                    memberList.Update(list.Select(w=>new OrderedTileList.Item(w.Id,WidgetDisplayName(w,widgets.State))));
                    foreach(var memberButton in EffectsLogicalChildren(memberList).OfType<Button>())
                        if(memberButton.Tag is string memberId&&list.Any(w=>w.Id==memberId))memberButton.ContextFlyout=WidgetCardMenu(memberId);
                    ShowSelectedMember();
                }
                SyncWidgetLayers();
            } finally {syncingWidgetEditor=false;}
        };
        syncWidgetEditor();
        locked.Toggled+=(_,_)=>LiveWidgetGroup(id,g=>g with {Locked=locked.IsOn});
        DoubleClickEdit.OnAccepted(name,()=> {if(syncingWidgetEditor)return;string label=name.Text.Trim();if(label.Length==0) {nameHint.Text=UiLanguage.Text("名称不能为空");nameHint.Visibility=Visibility.Visible;return;}if(LiveWidgetGroup(id,g=>g with {Name=label})) {nameHint.Text="";nameHint.Visibility=Visibility.Collapsed;}});
        screens.SelectionChanged+=(_,_)=> {if(!syncingWidgetEditor&&screens.SelectedItem is ScreenRow row)LiveWidgetGroup(id,g=>g with {Screen=row.Screen.DeviceName});};
        void SetPosition(bool horizontal,double value)=>LiveWidgetGroup(id,g=> {var bounds=WidgetPlacement.MeasureGroup(ExpansionCatalog.AvailableWidgets(widgets!.State),g);double extent=horizontal?bounds.MaxX:bounds.MaxY;double normalized=extent<=0?0:Math.Clamp(value/extent,0,1);return horizontal?g with {X=normalized}:g with {Y=normalized};});
        x.ValueChanged+=value=>SetPosition(true,value);y.ValueChanged+=value=>SetPosition(false,value);x.InteractionCompleted+=()=>widgets?.FlushPendingSave();y.InteractionCompleted+=()=>widgets?.FlushPendingSave();
        gap.ValueChanged+=(_,_)=>LiveWidgetGroup(id,g=>g with {Gap=gap.Value});
    }
}

