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
    private static void InvokeRefinementButton(Button button)=>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static FrameworkElement[] SectionElements(FrameworkElement root)=>new[]{root}.Concat(EffectsLogicalChildren(root)).ToArray();
    private static string SectionValue(FrameworkElement element)=>element switch {
        Slider slider=>"slider:"+slider.Value,
        NumberBox number=>"number:"+number.Value,
        ScrubNumberInput input=>"scrub:"+input.Value,
        ComboBox combo=>"combo:"+combo.SelectedIndex,
        ToggleSwitch toggle=>"toggle:"+toggle.IsOn,
        CheckBox check=>"check:"+check.IsChecked,
        TextBox text=>"text:"+text.Text,
        _=>""
    };
    private static bool SectionVisible(FrameworkElement element)
    {
        for(DependencyObject? node=element;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is UIElement {Visibility:Visibility.Collapsed})return false;
        return element.IsLoaded&&element.ActualHeight>0;
    }
    private async Task CheckSectionSet(FrameworkElement root,string label)
    {
        var cards=SectionElements(root).OfType<ContentControl>()
            .Where(card=>card.Style==HomeStyle("ParameterSectionCard")&&!SectionChrome.GetHasOwnHeader(card)).ToArray();
        refinementMeasurements.Add(new {sectionInventory=label,cards=cards.Select(card=>new {title=SectionChrome.GetTitle(card),visible=SectionVisible(card),enabled=card.IsEnabled,height=card.ActualHeight}).ToArray()});
        var records=cards.Where(card=>SectionVisible(card)&&card.IsEnabled).Select(card=>new {
            Card=card,Content=card.Content,Height=card.ActualHeight,
            Button=SectionElements((FrameworkElement)SectionChrome.GetHeader(card)!).OfType<Button>()
                .Single(button=>AutomationProperties.GetAutomationId(button)=="ParameterSectionToggle"),
            Values=card.Content is FrameworkElement content?SectionElements(content).Select(element=>(Element:element,Value:SectionValue(element))).Where(v=>v.Value.Length>0).ToArray():[]
        }).ToArray();
        if(records.Length==0) {
            refinementMeasurements.Add(new {section=label,cardCount=0});return;
        }
        string revision=store.ConfigurationRevision();long history=store.HistoryRevision;string? widgetRevision=widgets?.ConfigurationRevision;
        ReorderCheck(records.All(record=>SectionChrome.GetIsExpanded(record.Card)&&SectionChrome.GetBodyVisibility(record.Card)==Visibility.Visible),
            "Parameter groups initially expose their complete controls: "+label);
        foreach(var record in records.Reverse())InvokeRefinementButton(record.Button);
        await ReorderSettle(55);
        ReorderCheck(records.All(record=>!SectionChrome.GetIsExpanded(record.Card)&&SectionChrome.GetBodyVisibility(record.Card)==Visibility.Collapsed),
            "Every section header collapses its own body through its real button action: "+label);
        ReorderCheck(records.All(record=>ReferenceEquals(record.Card.Content,record.Content)&&record.Values.All(pair=>SectionValue(pair.Element)==pair.Value)),
            "Collapse preserves editor control instances and all existing values: "+label);
        var measured=records.Where(record=>record.Height>0).ToArray();
        ReorderCheck(measured.All(record=>record.Card.ActualHeight<=record.Height+1),
            "Collapsed group layout never increases its previous measured height: "+label);
        ReorderCheck(measured.Any(record=>record.Card.ActualHeight<record.Height-1),
            "Collapsing available groups reduces their actual layout height: "+label);
        ReorderCheck(records.All(record=>SectionChrome.GetHeaderMargin(record.Card).Bottom==0&&AutomationProperties.GetItemStatus(record.Button)=="已收起"),
            "Collapsed headers have no body gap and expose their state to accessibility: "+label);
        // UIA Invoke queues Click. Let a parent reopen before invoking a child
        // header that was unloaded when that parent's presenter collapsed.
        foreach(var record in records) {
            ReorderCheck(record.Button.IsEnabled&&record.Button.IsLoaded,
                "The reopened section header remains loaded and enabled: "+label+" / "+SectionChrome.GetTitle(record.Card));
            InvokeRefinementButton(record.Button);await ReorderSettle(20);
        }
        await ReorderSettle(55);
        ReorderCheck(records.All(record=>SectionChrome.GetIsExpanded(record.Card)&&record.Values.All(pair=>SectionValue(pair.Element)==pair.Value)),
            "Reopening groups restores all parameter controls without changing values: "+label);
        ReorderCheck(records.All(record=>SectionElements((FrameworkElement)SectionChrome.GetHeader(record.Card)!)
            .Where(element=>element.Name=="ParameterSectionChevron").All(element=>element.RenderTransform is RotateTransform {Angle:180})),
            "Reopened section arrows match the expanded state: "+label);
        ReorderCheck(store.ConfigurationRevision()==revision&&store.HistoryRevision==history&&widgets?.ConfigurationRevision==widgetRevision,
            "Opening and closing sections changes neither persisted configuration nor undo history: "+label);
        refinementMeasurements.Add(new {section=label,cardCount=cards.Length,measuredCount=measured.Length,
            heights=records.Select(record=>new {title=SectionChrome.GetTitle(record.Card),before=record.Height,after=record.Card.ActualHeight})});
    }
    internal async Task CheckCollapseSectionsUi(string output)
    {
        Navigate("display");LoadEffectDraft(true);
        effectSession.Collection=effectSession.Collection with {Added=effectDefinitions.Select(definition=>definition.Kind).Aggregate((left,right)=>left|right)};
        effectSession.AdoptOriginal(DraftEffects);RefreshEffectCollection();
        foreach(var definition in effectDefinitions) {
            OpenEffect(definition.Kind);SetEffectEnabled(definition,true);
            // Establish a real enabled draft before taking the no-change
            // baseline. A later draft refresh would undo a host-only override.
            // These writes are confined to the fixture database, with no
            // wallpaper controller or desktop process connected.
            FlushEffectChanges();await ReorderSettle(150);FlushEffectChanges();await ReorderSettle(55);
            ReorderCheck(wallpapers==null&&!store.HasPendingEffectWrite&&!effectSession.HasQueuedSaves,
                "The enabled effect fixture has finished its isolated configuration writes: "+definition.Kind);
            await CheckSectionSet(definition.Panel,"effect / "+definition.Kind);
        }
        await CheckSectionSet(EffectsInstanceControls,"shared effect controls");
        await ReorderCapture(output,"effect-sections-expanded");
        var effectCards=SectionElements(EffectsDetailPanel).OfType<ContentControl>().Where(card=>card.Style==HomeStyle("ParameterSectionCard")&&!SectionChrome.GetHasOwnHeader(card)).ToArray();
        foreach(var card in effectCards)SectionChrome.SetIsExpanded(card,false);
        await ReorderSettle(80);await ReorderCapture(output,"effect-sections-collapsed");
        foreach(var card in effectCards)SectionChrome.SetIsExpanded(card,true);

        var disabled=Enum.GetValues<WidgetKind>().Select(kind=>new WidgetInstance {Kind=kind,Enabled=false,
            Spectrum=new SpectrumOptions {SubSpectra=[new SpectrumOptions {Enabled=false}]}}).ToArray();
        var group=new WidgetLayoutGroup {Name="隔离折叠测试组"};
        var controller=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()),modules);
        try {
            controller.Save(new WidgetConfiguration {Widgets=disabled,LayoutGroups=[group]});
            widgets=controller;Navigate("widgets");
            foreach(var widget in disabled) {
                OpenWidget(widget.Id);await ReorderSettle(65);
                ReorderCheck(controller.ProcessId==null&&controller.State.Widgets.All(item=>!item.Enabled),"Disabled widget fixtures never start a desktop host: "+widget.Kind);
                await CheckSectionSet(WidgetEditorPanel,"widget / "+widget.Kind);
                if(widget.Kind is WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer)
                    ReorderCheck(SectionElements(WidgetEditorPanel).OfType<Expander>().All(expander=>expander.IsExpanded),"Time widget detail groups initially remain expanded: "+widget.Kind);
                if(widget.Kind==WidgetKind.Spectrum) {
                    foreach(string name in new[]{"子频谱 1","组件整体"}) {
                        var button=SectionElements(WidgetEditorPanel).OfType<Button>().Single(button=>AutomationProperties.GetName(button)==name);
                        InvokeRefinementButton(button);await ReorderSettle(65);
                        await CheckSectionSet(WidgetEditorPanel,"spectrum / "+name);
                    }
                }
            }
            OpenWidgetGroup(group.Id);await ReorderSettle(65);await CheckSectionSet(WidgetEditorPanel,"widget layout group");
            await ReorderCapture(output,"widget-group-expanded");
            ReorderCheck(controller.ProcessId==null,"All widget editor collapse checks finish without a desktop host");
        } finally {controller.Dispose();widgets=null;ResetWidgetNavigation();}
    }
}
