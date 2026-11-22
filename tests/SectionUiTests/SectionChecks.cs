using System.Text.Json;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task CheckSectionDesign(string output,Action<bool,string> check)
    {
        async Task Settle(){Root.UpdateLayout();await Task.Delay(130);Root.UpdateLayout();}
        void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        var inventory=new Dictionary<string,string[]>();
        void Sections(FrameworkElement root,string context)
        {
            var cards=EffectsLogicalChildren(root).OfType<ContentControl>().Where(c=>c.Style==HomeStyle("UtilityCard")||c.Style==HomeStyle("EffectsSection")||c.Style==HomeStyle("ParameterSectionCard")).ToArray();
            check(cards.Length>0||context=="效果 / 隐藏任务栏",context+": parameter groups or parameter-free taskbar effect");
            foreach(var card in cards) {
                check(card.Style==HomeStyle("ParameterSectionCard"),context+": shared section frame "+card.Name);
                check(!string.IsNullOrWhiteSpace(SectionChrome.GetTitle(card)),context+": named group "+SectionChrome.GetTitle(card));
                if(card.IsLoaded&&card.ActualHeight>0)check(PinnedElements(card).OfType<SectionPattern>().Any(),context+": rendered texture for "+SectionChrome.GetTitle(card));
            }
            inventory[context]=cards.Select(c=>SectionChrome.GetTitle(c)).ToArray();
        }
        string screen=SelectedScreen!.DeviceName;effectTargetAssetId=null;
        store.SaveTargetEffects(screen,null,new(FillMode.Contain,new VideoEffects {Collection=new() {Added=effectDefinitions.Aggregate((EffectKind)0,(all,d)=>all|d.Kind)},Brightness=.02,
            AudioRules=[new() {Parameter="Brightness",Weak=0,Strong=.2,Enabled=false},new() {Parameter="GlowStrength",Weak=0,Strong=.8,Enabled=false}]}),VideoEffects.Original);
        audioOpenGroups.Add(EffectKind.Color);audioOpenGroups.Add(EffectKind.Glow);
        Navigate("display");LoadEffectDraft(true);SaveAppearance(Appearance with {Pattern=true});await Settle();
        foreach(var definition in effectDefinitions) {
            OpenEffect(definition.Kind);await Settle();Sections(definition.Panel,"效果 / "+definition.Name);
            check(EffectsBackActions.Children.Count==2&&ReferenceEquals(EffectsRemoveButton.Parent,EffectsBackActions),definition.Name+": remove action adjacent to return");
            if(definition.Kind==EffectKind.Blur)await PinnedCapture(output,"sections-blur");
            if(definition.Kind==EffectKind.Glitch) {
                glitchMode.SelectedIndex=1;ClusterSmallCountSlider.Value=5;await Settle();Sections(definition.Panel,"效果 / 故障集群");await PinnedCapture(output,"sections-glitch-cluster");
                check(EffectsLogicalChildren(definition.Panel).OfType<ContentControl>().Any(c=>SectionChrome.GetTitle(c)=="小故障 5"),"Dynamic glitch-layer titles update");
            }
        }
        OpenEffect(EffectKind.Color);await Settle();var back=(Button)EffectsBackActions.Children[0];
        check(Math.Abs(EffectsTargetButton.ActualHeight-back.ActualHeight)<.1&&Math.Abs(EffectsRemoveButton.ActualHeight-back.ActualHeight)<.1,"Effect header controls share height");
        check(Grid.GetColumn(EffectsTargetButton)<Grid.GetColumn(EffectsBackActions),"Target precedes return and remove");
        await PinnedCapture(output,"sections-effects-wide");
        var texture=PinnedElements(EffectsInstanceControls).OfType<SectionPattern>().Single();check(texture.Visibility==Visibility.Visible&&!texture.IsHitTestVisible,"Texture follows enabled preference without intercepting input");
        SaveAppearance(Appearance with {Pattern=false});await Settle();check(texture.Visibility==Visibility.Collapsed,"Brand-pattern switch hides section texture");SaveAppearance(Appearance with {Pattern=true});
        SetEffectExplanation(true);await Settle();check(effectExplanationOpen,"Preview disclosure still opens");SetEffectExplanation(false);
        EffectsTargetOpen_Click(EffectsTargetButton,new RoutedEventArgs());await Settle();check(!EffectsRemoveButton.IsEnabled&&!back.IsEnabled,"Target modal disables relocated remove and return");CloseEffectsTarget(true);await Settle();
        foreach(int width in new[]{1100,960}) {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width,1000));await Settle();
            foreach(var control in new FrameworkElement[]{EffectsTargetButton,back,EffectsRemoveButton}) {var p=control.TransformToVisual(Root).TransformPoint(new());check(p.X>=0&&p.X+control.ActualWidth<=Root.ActualWidth+.5,"Header fits width "+width+" / "+AutomationProperties.GetName(control));}
            await PinnedCapture(output,"sections-effects-"+width);
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,1000));
        var items=Enum.GetValues<WidgetKind>().Select(kind=>new WidgetInstance {Id="section-"+kind,Kind=kind,Screen=screen,Enabled=false,Spectrum=new() {SubSpectra=[new() {Enabled=false}]}}).ToArray();
        widgets!.Save(new WidgetConfiguration {Widgets=items,LayoutGroups=[new WidgetLayoutGroup {Id="section-group",Name="测试组件组",Screen=screen}]});
        Navigate("widgets");RefreshWidgets();await Settle();
        foreach(var widget in items) {
            OpenWidget(widget.Id);await Settle();Sections(WidgetEditorPanel,"组件 / "+WidgetTitle(widget.Kind));
            check(WidgetsHeaderActions.Children.Count==2&&AutomationProperties.GetName(WidgetsHeaderActions.Children[1])=="移除组件",widget.Kind+": header remove action");
            check(!EffectsLogicalChildren(WidgetEditorPanel).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="移除组件"),widget.Kind+": no duplicate footer remove");
            if(widget.Kind==WidgetKind.Spectrum) {
                var child=EffectsLogicalChildren(WidgetEditorPanel).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="子频谱 1");Invoke(child);await Settle();Sections(WidgetEditorPanel,"组件 / 子频谱");
                var common=EffectsLogicalChildren(WidgetEditorPanel).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="组件整体");Invoke(common);await Settle();Sections(WidgetEditorPanel,"组件 / 频谱整体");
            }
        }
        widgets.Save(widgets.State with {Widgets=widgets.State.Widgets.Select(w=>w.Kind is WidgetKind.Clock or WidgetKind.FocusTimer?w with {LayoutGroupId="section-group"}:w).ToArray()});
        OpenWidgetGroup("section-group");await Settle();Sections(WidgetEditorPanel,"组件组");
        OpenWidget("section-Performance");await Settle();
        var layout=EffectsLogicalChildren(WidgetEditorPanel).OfType<PerformanceLayoutPicker>().Single();
        var gridFlow=EffectsLogicalChildren(layout).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="指标排列方式：网格");Invoke(gridFlow);await Settle();
        var columns=EffectsLogicalChildren(layout).OfType<ScrubNumberInput>().Single();columns.BeginEdit();columns.Editor.Text="4";columns.CommitEdit();await Settle();
        check(widgets.State.Widgets.Single(w=>w.Id=="section-Performance").Performance.Columns==4,"Grid picker saves selected columns");
        var ordered=EffectsLogicalChildren(WidgetEditorPanel).OfType<OrderedTileList>().Single();
        var beforeOrder=widgets.State.Widgets.Single(w=>w.Id=="section-Performance").Performance.Selected(widgets.State.Widgets.Single(w=>w.Id=="section-Performance").Metrics);
        Invoke(EffectsLogicalChildren(ordered).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="后移选中项目"));await Settle();
        var afterOrder=widgets.State.Widgets.Single(w=>w.Id=="section-Performance").Performance.Selected(widgets.State.Widgets.Single(w=>w.Id=="section-Performance").Metrics);
        check(afterOrder.Length==beforeOrder.Length&&afterOrder[1]==beforeOrder[0],"Shared reorder changes only metric order");
        check(!EffectsLogicalChildren(WidgetEditorPanel).OfType<TextBlock>().Any(t=>t.Visibility==Visibility.Visible&&(t.Text.Contains("至少选择")||t.Text.Contains("GPU 为")||t.Text.Contains("左右拖动数值"))),"Permanent component explanations removed");
        var layoutTop=layout.TransformToVisual(WidgetsScroll).TransformPoint(new()).Y;WidgetsScroll.ChangeView(null,WidgetsScroll.VerticalOffset+layoutTop-70,null,true);await Settle();await PinnedCapture(output,"refined-performance-layout");
        OpenWidget("section-Search");await Settle();await PinnedCapture(output,"sections-search-wide");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960,1000));await Settle();await PinnedCapture(output,"sections-search-narrow");
        check(Grid.GetColumn((FrameworkElement)WidgetsHeaderActions.Children[0])!=Grid.GetColumn((FrameworkElement)WidgetsHeaderActions.Children[1]),"Widget return and remove stay in adjacent columns");
        check(!PinnedElements(WidgetsOverview).OfType<TextBlock>().Any(t=>t.Name=="WidgetStatus"),"Routine footer status is removed");
        Navigate("presets");RefreshSharingPage();await Settle();
        foreach(var button in new[]{(Button)WidgetsHeaderActions.Children[0],(Button)WidgetsHeaderActions.Children[1]}.Concat(settingsCategoryButtons.Values)) {
            if(!button.IsLoaded||button.ActualHeight==0)continue;
            var texts=EffectsLogicalChildren(button).OfType<TextBlock>().Where(t=>t.Visibility==Visibility.Visible&&t.ActualHeight>0).ToArray();
            var icons=EffectsLogicalChildren(button).OfType<Viewbox>().Where(v=>v.Visibility==Visibility.Visible&&v.ActualHeight>0).ToArray();
            foreach(var label in texts) {var lp=label.TransformToVisual(button).TransformPoint(new());check(Math.Abs(lp.Y+label.ActualHeight/2-button.ActualHeight/2)<1.5,"Header button label vertically centred: "+label.Text);foreach(var icon in icons){var ip=icon.TransformToVisual(button).TransformPoint(new());check(Math.Abs(lp.Y+label.ActualHeight/2-ip.Y-icon.ActualHeight/2)<1.5,"Button label and icon share centre: "+label.Text);}}
        }
        check(SharingPanel.Children[0]==presetLibraryHost&&!EffectsLogicalChildren(SharingPanel).OfType<CheckBox>().Any(),"My presets is first and its configuration editor is not inline");
        check(AutomationProperties.GetName(presetSave)=="保存当前配置","The preset page exposes its save-dialog action");
        await PinnedCapture(output,"sections-presets-narrow");AppWindow.Resize(new Windows.Graphics.SizeInt32(1440,1000));await Settle();await PinnedCapture(output,"sections-presets-wide");
        Invoke(presetSave);await Settle();
        for(int i=0;i<30&&(presetSaveDialog==null||presetEditor.ActualWidth==0);i++)await Settle();
        check(presetSaveDialog is {Content:ScrollViewer},"The save action opens the preset editor in a dialog");
        var options=new[]{presetPlaying,presetLibrary,presetEffects,presetWidgets};
        check(EffectsLogicalChildren(presetEditor).OfType<CheckBox>().Count()==5,"The preset dialog exposes four complete categories and one component privacy option");
        foreach(var option in options) {
            check(option.Parent==presetChoiceGrid||option.Parent is FrameworkElement parent&&parent.Parent==presetChoiceGrid,"Preset categories use shared responsive grids");
            check(option.ActualWidth>option.ActualHeight&&option.ActualHeight>=64,"Export choice is compact with a usable hit target");
            var label=((Grid)option.Content).Children.OfType<TextBlock>().Single();
            var offset=label.TransformToVisual(option).TransformPoint(new());
            check(Math.Abs(offset.Y+label.ActualHeight/2-option.ActualHeight/2)<1.5,"Export label vertically centred: "+label.Text);
            check(offset.Y>=0&&offset.Y+label.ActualHeight<=option.ActualHeight,"Wrapped export label stays inside tile: "+label.Text);
        }
        var first=options[0];bool before=first.IsChecked==true;((IToggleProvider)new CheckBoxAutomationPeer(first).GetPattern(PatternInterface.Toggle)).Toggle();await Settle();check(first.IsChecked!=before,"Rectangular sharing option remains toggleable");
        presetWidgets.IsChecked=false;await Settle();check(!presetPrivacy.IsEnabled,"Component privacy belongs to the component category");
        presetWidgets.IsChecked=true;await Settle();check(presetPrivacy.IsEnabled,"Component privacy becomes available with components");
        presetSaveDialog!.Hide();await Settle();
        for(int i=0;i<30&&presetSaveDialog!=null;i++)await Settle();
        check(presetSaveDialog==null&&presetEditor.Parent==null,"Closing the preset dialog detaches its editor");
        Navigate("widgets");OpenWidget("section-Search");await Settle();BeginHistoryGesture();
        var widgetIds=widgets.State.Widgets.Select(w=>w.Id).Order().ToArray();Invoke((Button)WidgetsHeaderActions.Children[1]);await Settle();
        check(widgets.State.Widgets.Select(w=>w.Id).Order().SequenceEqual(widgetIds.Where(id=>id!="section-Search")),"Relocated widget remove deletes only the selected instance");
        await RestoreHistory(false);await Settle();check(widgets.State.Widgets.Select(w=>w.Id).Order().SequenceEqual(widgetIds),"Widget removal is undoable");
        Navigate("display");effectTargetAssetId=null;LoadEffectDraft(true);OpenEffect(EffectKind.Color);await Settle();BeginHistoryGesture();
        var added=effectCollection.Added;Invoke(EffectsRemoveButton);await Settle();check(effectCollection.Added==(added&~EffectKind.Color),"Relocated effect remove deletes only selected effect");
        await RestoreHistory(false);await Settle();check(effectCollection.Added==added,"Effect removal is undoable");
        check(widgets.ProcessId==null,"All component fixtures stayed disabled");
        File.WriteAllText(Path.Combine(output,"section-inventory.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions {WriteIndented=true}));
    }
}
