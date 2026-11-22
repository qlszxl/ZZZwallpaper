using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private async Task CheckRefinement81(string output)
    {
        async Task Invoke(Button button) {((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await ReorderSettle();}
        string screen=SelectedScreen!.DeviceName;
        Navigate("wallpaper");await ReorderSettle();
        ReorderCheck(Math.Abs(Root.ActualWidth*interfaceScale-ShellViewport.ActualWidth)<2&&Math.Abs(Root.ActualHeight*interfaceScale-ShellViewport.ActualHeight)<2,"Root scales uniformly to the actual window viewport");
        SetHomeApplyScope(true);ReorderCheck(applyToAllScreens&&HomeApplyAllScope.IsChecked&&!HomeApplyCurrentScope.IsChecked&&HomeApplyLabel.Text.Contains("全部"),"Apply scope dropdown changes the main action");
        SetHomeApplyScope(false);ReorderCheck(!applyToAllScreens&&HomeApplyCurrentScope.IsChecked&&HomeApplyButtons.Children.Count==2,"Apply defaults to current screen with one scope arrow");

        var a=new MediaAsset("position81-a",Path.Combine(output,"position-fixture.png"),"位置 A",1,0,1600,500,"png","SDR","image");
        var b=a with {Id="position81-b",Name="位置 B"};
        homePreviewAsset=a;RefreshHomePosition();homePositionX!.Value=25;homePositionY!.Value=-15;homePositionScale!.Value=145;
        RefreshHomePreview(b,null);RefreshHomePosition();
        ReorderCheck(RuntimeSettingsReader.Position(store,screen,a.Id)==new WallpaperPosition(25,-15,145)&&CurrentHomePosition(screen)==new WallpaperPosition(),"Switching assets flushes pending edits to the old asset and centers the new one");
        SaveHomePosition(screen,new(-20,30,80));homePreviewAsset=a;RefreshHomePosition();
        ReorderCheck(homePositionX.Value==25&&homePositionScale.Value==145&&RuntimeSettingsReader.Position(store,screen,b.Id).Scale==80,"Each home preview restores its own position and scale");
        store.SaveBinding(new(screen,a.Id,FillMode.Cover,false,1));homePreviewAsset=b;
        var reply=await ExecuteRecordedAutomation(new("wallpaper_set_screen_options",ToolCatalog.Json(new {screenId=screen,expectedRevision=store.ConfigurationRevision(),scale=160})),CancellationToken.None);
        ReorderCheck(!reply.IsError&&RuntimeSettingsReader.Position(store,screen,a.Id).Scale==160&&RuntimeSettingsReader.Position(store,screen,b.Id).Scale==80,"MCP changes the bound asset without overwriting a different home preview");
        store.RemoveBindings(screen);

        store.Upsert(a);File.Copy(a.Path,modules.Library.Poster(a.Id),true);
        Navigate("library");ApplyLibraryRows([a],a.Id);await ReorderSettle(400);
        var selectedRow=LibraryList.Items.OfType<LibraryRow>().Single();selectedRow.SetApplied("已应用 · 屏幕 1");await ReorderSettle();
        var container=(GridViewItem)LibraryList.ContainerFromItem(selectedRow);
        IEnumerable<DependencyObject> Visuals(DependencyObject element) {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++){var child=VisualTreeHelper.GetChild(element,i);yield return child;foreach(var descendant in Visuals(child))yield return descendant;}
        }
        ReorderCheck(container.IsSelected&&Visuals(container).OfType<CheckBox>().Any(check=>check.Name=="LibrarySelectionToggle"&&check.IsChecked==true),"Selected media retains the bright selection checkbox");
        ReorderCheck(Visuals(container).OfType<TextBlock>().Any(text=>text.Text=="已应用 · 屏幕 1")&&LibraryApplyButton.IsEnabled,"Applied marker and selected-media application entry are visible");
        ReorderCheck(wallpapers!.Running.Count==0,"Media selection alone does not apply desktop wallpaper");
        await ReorderCapture(output,"library-selection-81");selectedRow.SetApplied("");

        Navigate("presets");await ReorderSettle();
        ReorderCheck(modules.Presets.List().Count==0&&SharingScroll.AllowDrop&&PresetDropSurfaceAvailable,"Empty preset page supports whole-page drop");
        string zip=Path.Combine(output,"drop-fixture.zip");
        var fixture=modules.Presets.Capture("Drop fixture",[screen],new() {Playing=false,Effects=true,Widgets=false});
        await modules.Presets.ExportAsync(fixture,zip);
        int before=modules.Presets.List().Count;await ImportDroppedPreset([zip]);await ReorderSettle();
        ReorderCheck(modules.Presets.List().Count==before+1&&!presetBusy,"Dropped ZIP uses the production import path and releases busy state");
        bool rejected=false;try {await ImportDroppedPreset([zip,zip]);}catch(ArgumentException){rejected=true;}
        ReorderCheck(rejected&&modules.Presets.List().Count==before+1,"Multiple dropped packages are rejected without a partial import");
        await ReorderCapture(output,"presets-81");

        var guideTask=ShowOnboarding();await ReorderSettle();ReorderCheck(onboardingDialog is {IsPrimaryButtonEnabled:false},"First guide starts at step one and can be closed");
        onboardingDialog!.Hide();await guideTask;ReorderCheck(store.GetSetting("onboarding.v1")=="seen"&&onboardingDialog==null,"Skipping guide records completion and releases its dialog");

        Navigate("display");LoadEffectDraft(true);
        effectSession.Collection=new EffectCollection {Added=effectDefinitions.Select(definition=>definition.Kind).Aggregate((left,right)=>left|right)};
        effectSession.AdoptOriginal(DraftEffects);RefreshEffectCollection();await ReorderSettle();
        var helpDescriptions=new Dictionary<string,string>();
        foreach(var definition in effectDefinitions) {
            OpenEffect(definition.Kind);
            foreach(var card in EffectsLogicalChildren(definition.Panel).OfType<ContentControl>().Where(card=>card.Style==HomeStyle("ParameterSectionCard")))SectionChrome.SetIsExpanded(card,true);
            await ReorderSettle(250);
            foreach(var button in EffectsLogicalChildren(definition.Panel).OfType<Button>()) {
                string name=AutomationProperties.GetName(button),text=AutomationProperties.GetHelpText(button);
                if(name.EndsWith("说明")&&text.Length>0)helpDescriptions.TryAdd(name,text);
            }
        }
        File.WriteAllText(Path.Combine(output,"effect-description-debug.json"),JsonSerializer.Serialize(new {count=helpDescriptions.Count,descriptions=helpDescriptions,labels=EffectsLogicalChildren(EffectsDetailSectionsHost).OfType<TextBlock>().Where(t=>ParameterHint.GetEnabled(t)).Select(t=>new {t.Text,t.IsLoaded,wrapped=parameterHintHosts.TryGetValue(t,out _)}).ToArray()},new JsonSerializerOptions {WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        await ReorderCapture(output,"effect-description-debug");
        ReorderCheck(helpDescriptions.Count>100,"Loaded effect settings have individual description entries");
        OpenEffect(EffectKind.Color);await ReorderSettle(800);await ReorderCapture(output,"effect-help-81");
        widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()),modules);
        try {
            var fixture=new WidgetConfiguration {Widgets=Enum.GetValues<WidgetKind>().Select(kind=>new WidgetInstance {Id="hint81-"+kind,Kind=kind,Enabled=false,Screen=screen}).ToArray()};
            widgets.Save(fixture);Navigate("widgets");
            foreach(var item in fixture.Widgets) {
                OpenWidget(item.Id);
                if(TimeWidgets.IsTime(item.Kind)) {
                    var details=WidgetEditorPanel.Children.OfType<ContentControl>().Single(c=>SectionChrome.GetTitle(c)=="外观细节");
                    var groups=EffectsLogicalChildren(details).OfType<Expander>().ToArray();
                    ReorderCheck(!SectionChrome.GetIsExpanded(details)&&groups.Length>=4&&groups.All(g=>!g.IsExpanded)&&
                        !WidgetEditorPanel.Children.OfType<Expander>().Any(),"Time detail groups share a collapsed main heading: "+item.Kind);
                }
                foreach(var disclosure in WidgetDisclosureElements().ToArray())SetDisclosureOpen(disclosure.Element,true);
                await ReorderSettle(250);
                foreach(var button in EffectsLogicalChildren(WidgetEditorPanel).OfType<Button>()) {
                    string name=AutomationProperties.GetName(button),text=AutomationProperties.GetHelpText(button);
                    if(name.EndsWith("说明")&&text.Length>0)helpDescriptions.TryAdd(name,text);
                }
            }
            OpenWidget("hint81-Performance");foreach(var disclosure in WidgetDisclosureElements().ToArray())SetDisclosureOpen(disclosure.Element,true);await ReorderSettle();
            var metric=EffectsLogicalChildren(WidgetEditorPanel).OfType<CheckBox>().First();bool? selected=metric.IsChecked;
            var metricHelp=((Grid)metric.Parent).Children.OfType<Button>().Single();
            ReorderCheck(metricHelp.Width==16,"Metric choices have independent compact help buttons beside their checkboxes");
            await Invoke(metricHelp);
            ReorderCheck(activeHelp is {IsOpen:true}&&metric.IsChecked==selected,"Reading metric details does not toggle the metric");
            var hide=await ExecuteRecordedAutomation(new("app_update_settings",ToolCatalog.Json(new {helpHints=false,expectedRevision=store.ConfigurationRevision()})),CancellationToken.None);
            ReorderCheck(!hide.IsError&&activeHelp==null&&helpButtons.All(reference=>!reference.TryGetTarget(out var button)||button.Visibility==Visibility.Collapsed),"Global help switch hides old and new icons and closes details through MCP");
            var late=HelpIcon("新说明","测试");ReorderCheck(late.Visibility==Visibility.Collapsed,"Newly created descriptions inherit the global disabled state");
            SetHelpHintsEnabled(true);await ReorderSettle();await ReorderCapture(output,"widget-help-81");
            ReorderCheck(widgets.ProcessId==null,"Description tests never launch desktop widgets");
        }finally {widgets.Dispose();widgets=null;}
        File.WriteAllText(Path.Combine(output,"description-catalog.json"),JsonSerializer.Serialize(helpDescriptions,new JsonSerializerOptions {WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        ReorderCheck(!TrayState().HasWallpaper,"Tray runtime state reflects isolated idle playback");
        Navigate("wallpaper");
    }
}
