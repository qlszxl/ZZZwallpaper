using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void BuildWidgetEditor(WidgetInstance widget,WidgetConfiguration state)
    {
        string id=widget.Id;widgetLayerInputs.Clear();
        var heading=new StackPanel {Spacing=6};var title=UtilityText(WidgetTitle(widget),"UtilityHeading");heading.Children.Add(title);
        widgetSaveStatus=UtilityText(widgets?.SaveStatus??"");widgetSaveStatus.Visibility=Visibility.Collapsed;WidgetEditorPanel.Children.Add(heading);
        var enabled=WidgetToggle(UiLanguage.Text("显示到桌面"),widget.Enabled);var basic=new StackPanel {Spacing=14};basic.Children.Add(WidgetToggleRow(UiLanguage.Text("显示到桌面"),enabled));
        var locked=WidgetToggle(UiLanguage.Text("锁定组件"),widget.Locked);basic.Children.Add(WidgetToggleRow(UiLanguage.Text("锁定组件"),locked));ToolTipService.SetToolTip(locked,UiLanguage.Text("锁定位置和右键菜单"));
        var interactionLocked=WidgetToggle(UiLanguage.Text("交互锁定"),widget.InteractionLocked);basic.Children.Add(WidgetToggleRow(UiLanguage.Text("交互锁定"),interactionLocked));ToolTipService.SetToolTip(interactionLocked,UiLanguage.Text("锁定组件功能，动画和计时继续"));
        var topmost=WidgetToggle(UiLanguage.Text("置于顶层"),widget.AlwaysOnTop);basic.Children.Add(WidgetToggleRow(UiLanguage.Text("置于顶层"),topmost));
        basic.Children.Add(WidgetLayerRow(widget));
        WidgetEditorPanel.Children.Add(UtilityCard(basic,UiLanguage.Text("组件状态")));
        if(widget.Kind==WidgetKind.Shortcuts)WidgetEditorPanel.Children.Add(BuildWidgetCardFields(widget,state));

        var membership=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        membership.Items.Add(new ComboBoxItem {Content=UiLanguage.Text("独立组件"),Tag=""});foreach(var group in state.LayoutGroups)membership.Items.Add(new ComboBoxItem {Content=group.Name,Tag=group.Id});
        membership.SelectedIndex=widget.LayoutGroupId==null?0:1+Array.FindIndex(state.LayoutGroups,g=>g.Id==widget.LayoutGroupId);
        WidgetEditorPanel.Children.Add(UtilityCard(WidgetField(UiLanguage.Text("所属组件组"),membership),UiLanguage.Text("所属组件组")));
        membership.SelectionChanged+=(_,_)=> {if(!syncingWidgetEditor&&membership.SelectedItem is ComboBoxItem {Tag:string groupId})SetWidgetGroup(id,groupId.Length==0?null:groupId);};

        var position=new StackPanel {Spacing=16};
        position.Children.Add(UtilityText(UiLanguage.Text("位置"),"UtilityHeading"));
        Action refreshAlignment=()=>{};
        if(widget.LayoutGroupId==null)
            position.Children.Add(WidgetPositionAlignment(id,false,(horizontal,value)=>LiveWidget(id,w=>horizontal?w with {X=value}:w with {Y=value}),out refreshAlignment));
        var screens=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};position.Children.Add(WidgetField(UiLanguage.Text("显示器"),screens));
        var x=new WidgetPositionInput(UiLanguage.Text("水平 X"));var y=new WidgetPositionInput(UiLanguage.Text("垂直 Y"));
        var coordinates=new Grid {ColumnSpacing=16,RowSpacing=12};coordinates.Children.Add(WidgetField(UiLanguage.Text("水平 X"),x));coordinates.Children.Add(WidgetField(UiLanguage.Text("垂直 Y"),y));UtilityAdaptiveGrid(coordinates,2,480);position.Children.Add(coordinates);
        if(widget.LayoutGroupId is { } parent) {
            var grouped=new StackPanel {Spacing=12};grouped.Children.Add(UtilityText(UiLanguage.Text("位置由组件组控制")));grouped.Children.Add(WidgetButton(UiLanguage.Text("调整组件组"),()=>OpenWidgetGroup(parent),"Widgets"));WidgetEditorPanel.Children.Add(UtilityCard(grouped,UiLanguage.Text("位置与对齐")));
        } else WidgetEditorPanel.Children.Add(UtilityCard(position));

        Action? syncOptions=widget.Kind==WidgetKind.Search?BuildSearchSettings(widget):widget.Kind is WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask?BuildVisualWidgetSettings(widget):null;
        if(TimeWidgets.IsTime(widget.Kind))syncOptions=BuildTimeWidgetSettings(widget);
        if(FeaturePacks.GridFor(widget) is {} grid)syncOptions=BuildPackageGridSettings(widget,grid);
        var metricChecks=new Dictionary<PerformanceMetrics,CheckBox>();
        if(widget.Kind==WidgetKind.Performance) {
            var metricsPanel=new StackPanel {Spacing=14};metricsPanel.Children.Add(UtilityText(UiLanguage.Text("显示指标"),"UtilityHeading"));
            
            var categories=new Dictionary<string,Grid>();
            foreach(var metric in Enum.GetValues<PerformanceMetrics>().OrderBy(m=>Array.IndexOf(new[]{"CPU","GPU",UiLanguage.Text("内存"),UiLanguage.Text("磁盘"),UiLanguage.Text("网络"),UiLanguage.Text("系统")},PerformanceFields.Category(m)))) {
                string category=PerformanceFields.Category(metric);
                if(!categories.TryGetValue(category,out var choices)){metricsPanel.Children.Add(UtilityText(category,"UtilityLabel"));choices=ChoiceCardGrid(maximumColumns:4,bounded:true);categories[category]=choices;metricsPanel.Children.Add(choices);}
                var check=SelectionCard(PerformanceFields.Label(metric),(widget.Metrics&metric)!=0,source=>MetricCardIcon(metric,source));
                metricChecks.Add(metric,check);
                var tile=new Grid();
                tile.Children.Add(check);
                AddCardHelp(tile,PerformanceFields.Label(metric));
                choices.Children.Add(tile);
                void ChangeMetric() {
                    if(syncingWidgetEditor||widgets==null)return;
                    var current=widgets.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return;
                    var next=check.IsChecked==true?current.Metrics|metric:current.Metrics&~metric;
                    if(next==0) {syncingWidgetEditor=true;check.IsChecked=true;syncingWidgetEditor=false;Inform(UiLanguage.Text("至少保留一项指标。"));return;}
                    
                    LiveWidget(id,w=>w with {Metrics=next});
                }
                check.Checked+=(_,_)=>ChangeMetric();check.Unchecked+=(_,_)=>ChangeMetric();
            }
            
            WidgetEditorPanel.Children.Add(UtilityCard(metricsPanel));
            syncOptions=BuildPerformanceSettings(widget);
        }
        var appearance=new StackPanel {Spacing=16};appearance.Children.Add(UtilityText(widget.Kind==WidgetKind.Spectrum?UiLanguage.Text("整体外观"):UiLanguage.Text("外观"),"UtilityHeading"));
        var iconOnly=WidgetToggle(UiLanguage.Text("仅显示图标"),widget.IconOnly);if(widget.Kind==WidgetKind.Shortcuts)appearance.Children.Add(WidgetToggleRow(UiLanguage.Text("仅显示图标"),iconOnly));
        var iconButton=WidgetButton(UiLanguage.Text("更换图标…"),()=>{});iconButton.Click+=(_,_)=>ShowWidgetIcons(id,iconButton);iconButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var iconRow=new Grid {ColumnSpacing=14};iconRow.ColumnDefinitions.Add(new() {Width=new GridLength(24)});iconRow.ColumnDefinitions.Add(new());
        var iconPreview=new ContentControl {Width=24,Height=24};iconRow.Children.Add(iconPreview);var iconText=UtilityText(UiLanguage.Text("更换图标…"),"UtilityLabel");iconText.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(iconText,1);iconRow.Children.Add(iconText);iconButton.Content=iconRow;if(widget.Kind is WidgetKind.Search or WidgetKind.Shortcuts)appearance.Children.Add(iconButton);
        var opacity=new Slider {Style=HomeStyle("HomeSlider"),Minimum=35,Maximum=100,StepFrequency=1,Value=widget.Opacity*100};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(opacity,UiLanguage.Text("组件不透明度"));
        var valueHeading=new Grid {ColumnSpacing=16};valueHeading.ColumnDefinitions.Add(new());valueHeading.ColumnDefinitions.Add(new() {Width=GridLength.Auto});valueHeading.Children.Add(UtilityText(UiLanguage.Text("不透明度"),"UtilityLabel"));var opacityValue=new EffectValueInput {Target=opacity,Unit="%",Precision=0,Width=148};Grid.SetColumn(opacityValue,1);valueHeading.Children.Add(opacityValue);var opacityField=new StackPanel {Spacing=ParameterUi.LabelGap};opacityField.Children.Add(valueHeading);opacityField.Children.Add(opacity);appearance.Children.Add(opacityField);
        var interval=new Slider {Style=HomeStyle("HomeSlider"),Minimum=100,Maximum=60000,StepFrequency=10};
        if(widget.Kind==WidgetKind.Performance)appearance.Children.Add(NumericSliderRow(UiLanguage.Text("刷新间隔"),interval,UiLanguage.Text("毫秒"),0));WidgetEditorPanel.Children.Add(UtilityCard(appearance));
        DecorateParameterSections(WidgetEditorPanel,WidgetTitle(widget)+UiLanguage.Text("设置"));

        string? screenKey=null,iconKey="uninitialized";
        syncWidgetEditor=()=> {
            if(widgets==null)return;var current=widgets.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return;
            syncingWidgetEditor=true;
            try {
                var list=Screen.AllScreens;string topology=string.Join(";",list.Select(s=>s.DeviceName+s.WorkingArea.ToString()));
                if(topology!=screenKey) {screenKey=topology;screens.ItemsSource=list.Select(s=>new ScreenRow(s)).ToArray();}
                var target=WidgetPlacement.Resolve(current.Screen);var rows=(ScreenRow[])screens.ItemsSource;
                screens.SelectedItem=rows.FirstOrDefault(s=>s.Screen.DeviceName==target.DeviceName);
                var bounds=WidgetPlacement.Measure(current,target);x.SetBounds(bounds.MaxX,Math.Round(current.X*bounds.MaxX));y.SetBounds(bounds.MaxY,Math.Round(current.Y*bounds.MaxY));
                enabled.IsOn=current.Enabled;topmost.IsOn=current.AlwaysOnTop;locked.IsOn=current.Locked;interactionLocked.IsOn=current.InteractionLocked;iconOnly.IsOn=current.IconOnly;opacity.Value=current.Opacity*100;
                refreshAlignment();
                syncOptions?.Invoke();SyncWidgetLayers();
                foreach(var pair in metricChecks)pair.Value.IsChecked=(current.Metrics&pair.Key)!=0;
                interval.Value=current.Performance.IntervalMilliseconds(current.RefreshSeconds);
                var card=widgets.State.Shortcuts.FirstOrDefault(c=>c.Id==current.ShortcutId);title.Text=card?.Name??WidgetTitle(current.Kind);
                string? icon=current.Icon??card?.Icon;if(icon!=iconKey) {iconKey=icon;iconPreview.Content=WidgetImage(icon,WidgetGlyph(current.Kind),iconButton,24);}
            } finally {syncingWidgetEditor=false;}
        };
        syncWidgetEditor();
        topmost.Toggled+=(_,_)=>LiveWidget(id,w=>w with {AlwaysOnTop=topmost.IsOn});
        enabled.Toggled+=(_,_)=>LiveWidget(id,w=>w with {Enabled=enabled.IsOn});
        locked.Toggled+=(_,_)=>LiveWidget(id,w=>w with {Locked=locked.IsOn});
        interactionLocked.Toggled+=(_,_)=>LiveWidget(id,w=>w with {InteractionLocked=interactionLocked.IsOn});
        screens.SelectionChanged+=(_,_)=> {if(!syncingWidgetEditor&&screens.SelectedItem is ScreenRow row)LiveWidget(id,w=>w with {Screen=row.Screen.DeviceName});};
        void SetPosition(bool horizontal,double value) {
            LiveWidget(id,w=> {var bounds=WidgetPlacement.Measure(w,WidgetPlacement.Resolve(w.Screen));double extent=horizontal?bounds.MaxX:bounds.MaxY;double normalized=extent<=0?0:Math.Clamp(value/extent,0,1);return horizontal?w with {X=normalized}:w with {Y=normalized};});
        }
        x.ValueChanged+=value=>SetPosition(true,value);y.ValueChanged+=value=>SetPosition(false,value);
        x.InteractionCompleted+=()=>widgets?.FlushPendingSave();y.InteractionCompleted+=()=>widgets?.FlushPendingSave();
        opacity.ValueChanged+=(_,_)=>LiveWidget(id,w=>w with {Opacity=opacity.Value/100});
        iconOnly.Toggled+=(_,_)=>LiveWidget(id,w=>w with {IconOnly=iconOnly.IsOn});
        interval.ValueChanged+=(_,_)=> {if(!syncingWidgetEditor)LiveWidget(id,w=>w with {Performance=w.Performance with {RefreshMilliseconds=(int)Math.Round(interval.Value)}});};
        if(widget.Kind==WidgetKind.Spectrum) {
            // Keep shared widget controls together; only the selected spectrum
            // layer is mounted below the layer selector.
            var common=new StackPanel {Spacing=16};
            foreach(var item in WidgetEditorPanel.Children.Skip(1).ToArray()) {WidgetEditorPanel.Children.Remove(item);common.Children.Add(item);}
            var syncCommon=syncWidgetEditor;var syncSpectrum=BuildSpectrumSettings(widget,common);
            syncWidgetEditor=()=> {
                syncCommon?.Invoke();bool wasSyncing=syncingWidgetEditor;syncingWidgetEditor=true;
                try {syncSpectrum();}finally {syncingWidgetEditor=wasSyncing;}
            };
            syncWidgetEditor();
        }
    }
}

