using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed class EffectDisclosureState
    {
        public bool Preview,Controls;
        public readonly Dictionary<object,bool> Sections=new();
    }
    private readonly Dictionary<EffectKind,EffectDisclosureState> effectDisclosureStates=new();
    private static object EffectDisclosureKey(FrameworkElement element)=>element.Tag is string key&&key.StartsWith("effect-fold:",StringComparison.Ordinal)?key:element;
    private IEnumerable<FrameworkElement> EffectDisclosures(EffectKind kind)=>EffectsLogicalChildren(effectDefinitions.First(d=>d.Kind==kind).Panel).Where(e=>
        e is Expander||e is ContentControl card&&card.Style==HomeStyle("ParameterSectionCard")&&!SectionChrome.GetHasOwnHeader(card));
    private static bool DisclosureOpen(FrameworkElement element)=>element is Expander expander?expander.IsExpanded:SectionChrome.GetIsExpanded(element);
    private static void SetDisclosureOpen(FrameworkElement element,bool open)
    {
        if(element is Expander expander)expander.IsExpanded=open;else SectionChrome.SetIsExpanded(element,open);
    }
    private void CaptureEffectSectionStates(EffectKind kind)
    {
        if(navigationPage!="display")return;
        if(!effectDisclosureStates.TryGetValue(kind,out var state))effectDisclosureStates[kind]=state=new();
        foreach(var element in EffectDisclosures(kind))state.Sections[EffectDisclosureKey(element)]=DisclosureOpen(element);
    }
    private void RestoreEffectSectionStates(EffectKind kind)
    {
        effectDisclosureStates.TryGetValue(kind,out var state);
        foreach(var element in EffectDisclosures(kind)) {
            SetDisclosureOpen(element,state?.Sections.GetValueOrDefault(EffectDisclosureKey(element))??false);
        }
    }
    private void CaptureEffectDisclosures()
    {
        if(navigationPage!="display"||displayedEffectKind is not { } kind)return;
        CaptureEffectSectionStates(kind);
        var state=effectDisclosureStates[kind];state.Preview=effectExplanationOpen;state.Controls=SectionChrome.GetIsExpanded(EffectsInstanceControls);
    }
    private void RestoreEffectDisclosures()
    {
        EffectDisclosureState? state=null;
        if(selectedEffectKind is { } kind) {RestoreEffectSectionStates(kind);effectDisclosureStates.TryGetValue(kind,out state);}
        SectionChrome.SetIsExpanded(EffectsInstanceControls,state?.Controls??false);
        SetEffectExplanation(state?.Preview??false);
    }
    private void ResetEffectDisclosureSession()
    {
        if(!effectsPageReady)return;
        effectDisclosureStates.Clear();audioRuleToExpand=null;
        foreach(var definition in effectDefinitions)foreach(var element in EffectDisclosures(definition.Kind))SetDisclosureOpen(element,false);
        SectionChrome.SetIsExpanded(EffectsInstanceControls,false);SetEffectExplanation(false);displayedEffectKind=null;
    }
}
