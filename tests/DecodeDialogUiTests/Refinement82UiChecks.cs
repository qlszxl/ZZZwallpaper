using System.Text.Json;
using System.Runtime.InteropServices;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    [DllImport("user32.dll",EntryPoint="PostMessageW")] private static extern bool Test82PostMessage(nint hwnd,uint message,nint w,nint l);
    private delegate bool Test82EnumCallback(nint hwnd,nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint hwnd,Test82EnumCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,System.Text.StringBuilder name,int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from,nint to,ref Test82Point point,uint count);
    [StructLayout(LayoutKind.Sequential)] private struct Test82Point {public int X,Y;}
    private async Task CheckRefinement82(string output)
    {
        async Task Invoke(Button button) {((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await ReorderSettle(100);}
        IEnumerable<DependencyObject> Visuals(DependencyObject element) {for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++){var child=VisualTreeHelper.GetChild(element,i);yield return child;foreach(var descendant in Visuals(child))yield return descendant;}}
        Navigate("display");LoadEffectDraft(true);
        effectSession.Collection=new EffectCollection {Added=effectDefinitions.Select(definition=>definition.Kind).Aggregate((left,right)=>left|right)};
        effectSession.AdoptOriginal(DraftEffects);RefreshEffectCollection();await ReorderSettle();
        var accessFailures=new List<string>();
        foreach(var definition in effectDefinitions.Where(item=>item.Kind!=EffectKind.MaskLibrary)) {
            OpenEffect(definition.Kind);SetEffectEnabled(definition,false);RefreshEffectCollection();await ReorderSettle(80);
            var cards=EffectsLogicalChildren(definition.Panel).OfType<ContentControl>().Where(card=>card.Style==HomeStyle("ParameterSectionCard")).ToArray();
            foreach(var card in cards)SectionChrome.SetIsExpanded(card,true);
            await ReorderSettle(60);
            var headers=EffectsLogicalChildren(definition.Panel).OfType<Button>().Where(button=>AutomationProperties.GetAutomationId(button)=="ParameterSectionToggle").ToArray();
            if(headers.All(button=>button.IsEnabled))ReorderCheck(true,"Disabled effect keeps every drawer available: "+definition.Name);
            else accessFailures.Add("Disabled headers: "+definition.Name);
            var inputs=EffectsLogicalChildren(definition.Panel).OfType<Control>().Where(control=>control is Slider or ComboBox or RadioButtons or ToggleSwitch or TextBox).ToArray();
            var activeInputs=inputs.Where(control=>control.IsLoaded).ToArray();
            if(activeInputs.Any(control=>control.IsEnabled))File.WriteAllText(Path.Combine(output,"enabled-inputs-82.json"),JsonSerializer.Serialize(activeInputs.Where(control=>control.IsEnabled).Select(control=>new {type=control.GetType().Name,name=AutomationProperties.GetName(control),parent=control.Parent?.GetType().Name,control.Visibility}),new JsonSerializerOptions {WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
            if(activeInputs.All(control=>!control.IsEnabled))ReorderCheck(true,"Disabled effect exposes read-only parameter controls: "+definition.Name);
            else accessFailures.Add("Editable input: "+definition.Name);
            var heading=headers.FirstOrDefault();if(heading is {IsEnabled:true}) {await Invoke(heading);await Invoke(heading);}
            SetEffectEnabled(definition,true);RefreshEffectCollection();await ReorderSettle(40);
        }
        File.WriteAllText(Path.Combine(output,"access-failures-82.json"),JsonSerializer.Serialize(accessFailures));
        ReorderCheck(accessFailures.Count==0,"Every disabled effect passes drawer and input checks: "+string.Join("; ",accessFailures));
        OpenEffect(EffectKind.Transform);var transform=effectDefinitions.Single(item=>item.Kind==EffectKind.Transform);
        var slider=EffectsLogicalChildren(transform.Panel).OfType<Slider>().First();
        SetEffectEnabled(transform,false);RefreshEffectCollection();slider.IsEnabled=false;
        SetEffectEnabled(transform,true);RefreshEffectCollection();ReorderCheck(!slider.IsEnabled,"Enabling an effect retains a field's own disabled condition");slider.IsEnabled=true;
        OpenEffect(EffectKind.RgbSplit);var rgb=effectDefinitions.Single(item=>item.Kind==EffectKind.RgbSplit);
        SetEffectEnabled(rgb,false);RefreshEffectCollection();await ReorderSettle(600);
        var rgbLabels=EffectsLogicalChildren(rgb.Panel).OfType<TextBlock>().ToHashSet();
        File.WriteAllText(Path.Combine(output,"rgb-labels-82.json"),JsonSerializer.Serialize(rgbLabels.Select(label=>new {label.Text,label.IsLoaded,label.Visibility,wrapped=parameterHintHosts.TryGetValue(label,out _)}),new JsonSerializerOptions {WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        var sectionHelp=EffectsLogicalChildren(rgb.Panel).OfType<Button>().Single(button=>AutomationProperties.GetName(button)=="RGB 色散说明");
        static bool OutsideButton(Button help) {
            for(var node=VisualTreeHelper.GetParent(help);node!=null;node=VisualTreeHelper.GetParent(node))
                if(node is ButtonBase)return false;
            return true;
        }
        ReorderCheck(sectionHelp.ActualWidth==16&&OutsideButton(sectionHelp),"Section help has its own compact hit target outside the disclosure button");
        void CheckTrailingHint(Button hint) {
            var canvas=(Canvas)hint.Parent;var group=(Grid)canvas.Parent;
            var name=AutomationProperties.GetName(hint);
            var label=Visuals(group).OfType<TextBlock>().First(text=>text.Text+"说明"==name&&text.ActualWidth>0);
            File.AppendAllText(Path.Combine(output,"hint-positions.txt"),name+" "+(label.Parent as FrameworkElement)?.Tag+" "+Canvas.GetLeft(hint)+","+Canvas.GetTop(hint)+"\n");
            var edge=label.TransformToVisual(group).TransformPoint(new Point(label.ActualWidth,label.ActualHeight/2));
            var icon=hint.TransformToVisual(group).TransformPoint(new Point(0,hint.ActualHeight/2));
            ReorderCheck(icon.X-edge.X is >=3 and <=9 && Math.Abs(icon.Y-edge.Y)<2,"Help follows its visible title on the same line: "+label.Text);
        }
        CheckTrailingHint(sectionHelp);
        bool before=SectionChrome.GetIsExpanded(EffectsLogicalChildren(rgb.Panel).OfType<ContentControl>().First(card=>card.Style==HomeStyle("ParameterSectionCard")));
        await Invoke(sectionHelp);ReorderCheck(activeHelp is {IsOpen:true}&&!EffectIsEnabled(rgb),"Help remains available while the effect is disabled");DismissHelp();
        ReorderCheck(before==SectionChrome.GetIsExpanded(EffectsLogicalChildren(rgb.Panel).OfType<ContentControl>().First(card=>card.Style==HomeStyle("ParameterSectionCard"))),"Reading header help does not toggle its drawer");
        await ReorderCapture(output,"disabled-effect-82");
        EffectsBack();await ReorderSettle();
        var cardHelp=EffectsLogicalChildren(EffectsListPanel).OfType<Button>().First(button=>AutomationProperties.GetName(button).EndsWith("说明"));
        await ReorderCapture(output,"overview-hints");CheckTrailingHint(cardHelp);await Invoke(cardHelp);ReorderCheck(selectedEffectKind==null&&activeHelp is {IsOpen:true},"Reading effect-card help does not open the editor");DismissHelp();

        Navigate("packs");await ReorderSettle(300);
        ReorderCheck(ExpansionScroll.Visibility==Visibility.Visible&&ExpansionPanel.IsLoaded&&Title.Contains("扩展包"),"Expansion settings route opens from the settings workspace");
        if(Environment.GetEnvironmentVariable("ZZZWALLPAPER_EXPANSION_FIXTURE") is {Length:>0} zip)
        {
            bool Installed()=>ExpansionCatalog.Packages.Any(p=>p.Descriptor.Id=="warehouse-particles");
            var layer=effectDefinitions.Single(d=>d.Kind==EffectKind.Particles);
            IEnumerable<ComboBox> PicturePickers()=>EffectsLogicalChildren(layer.Panel).OfType<ComboBox>()
                .Where(c=>c.Items.OfType<ComboBoxItem>().Any(item=>item.Content as string=="自定义图片"));
            bool HasPictures(ComboBox c)=>c.Items.OfType<ComboBoxItem>().Any(item=>item.Content as string=="菲林");
            bool HasWarehouse()=>EffectsLogicalChildren(WidgetDrawerContent).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="搜索物资");
            Navigate("widgets");ShowWidgetAdd(WidgetsAddButton,null);
            ReorderCheck(!Installed() && !HasWarehouse() && PicturePickers().Count()==2 && !PicturePickers().Any(HasPictures),"Clean install exposes custom particles and hides both expansion catalogs");
            CloseWidgetDrawer(false);
            string broken=Path.Combine(output,"invalid-expansion.zip");
            using(var archive=System.IO.Compression.ZipFile.Open(broken,System.IO.Compression.ZipArchiveMode.Create))archive.CreateEntry("unknown");
            bool rejected=false;try {await ImportExpansion(broken);}catch(InvalidDataException) {rejected=true;}
            ReorderCheck(rejected&&!Installed(),"An invalid pack leaves the installation empty");
            await ImportExpansion(zip);await ReorderSettle();
            ShowWidgetAdd(WidgetsAddButton,null);
            ReorderCheck(Installed()&&HasWarehouse()&&PicturePickers().All(HasPictures),"The settings import unlocks the warehouse and both particle layers in the same session");
            CloseWidgetDrawer(false);
            ReorderCheck(FeaturePacks.FindWidget(WidgetConfiguration.LegacyMaterialsWidget)!=null,"The package registers its component implementation");
            ReorderCheck(ParticleArtwork.Prepare().Length==64&&ParticleArtwork.All.Where(a=>a.File!=null).All(a=>File.Exists(ParticleArtwork.ParticlePath(a.File!))),"The native atlas and all seven picker previews load from the installed pack");
            await ImportExpansion(zip);
            ReorderCheck(PicturePickers().All(c=>c.Items.OfType<ComboBoxItem>().Count(item=>item.Content as string=="菲林")==1),"Importing the same pack twice does not duplicate styles");
            Navigate("packs");await ReorderSettle(450);
            var baseTile=expansionCards.Children.OfType<Border>().Single(b=>AutomationProperties.GetName(b)==ExpansionCatalog.Base.Name);
            ReorderCheck(!Visuals(baseTile).OfType<Button>().Any(),"Base pack is bundled and permanent");
            var tile=expansionCards.Children.OfType<Border>().Single(b=>AutomationProperties.GetName(b)==ExpansionCatalog.Packages.Single().Descriptor.Name);
            ReorderCheck(Math.Abs(tile.ActualWidth-tile.ActualHeight)<2&&expansionCount.Text.EndsWith("2"),"Installed expansion uses a square preset-style tile");
            await ReorderCapture(output,"installed-expansion");
            var remove=EffectsLogicalChildren(tile).OfType<Button>().Single();
            await Invoke(remove);var prompt=DecodeDialog();prompt.Hide();await ReorderSettle(300);
            ReorderCheck(Installed(),"Cancelling deletion keeps the installed pack");
            await Invoke(remove);prompt=DecodeDialog();
            await Invoke(Visuals(prompt).OfType<Button>().Single(b=>b.Name=="PrimaryButton"));
            for(int i=0;i<30&&expansionBusy;i++)await ReorderSettle(100);
            ReorderCheck(!Installed()&&expansionCards.Children.Count==1&&AutomationProperties.GetName(expansionCards.Children[0])==ExpansionCatalog.Base.Name&&
                !PicturePickers().Any(HasPictures)&&!EffectsLogicalChildren(layer.Panel).OfType<CheckBox>().Any(b=>AutomationProperties.GetName(b).EndsWith("菲林")),
                "Deleting an expansion removes its tile and its fixed and random particle choices");
            Navigate("widgets");ShowWidgetAdd(WidgetsAddButton,null);
            ReorderCheck(!HasWarehouse(),"Deleting the expansion removes its widget from the add menu");CloseWidgetDrawer(false);
            await ImportExpansion(zip);
            ReorderCheck(PicturePickers().All(HasPictures)&&expansionCards.Children.Count==2,"Reimport restores the deleted expansion");

        }
        Navigate("performance");await ReorderSettle(600);RefreshSettingsHints();await ReorderSettle();await ReorderCapture(output,"performance-help-82");
        Navigate("appearance");await ReorderSettle(600);RefreshSettingsHints();await ReorderSettle();await ReorderCapture(output,"appearance-help-82");
        Navigate("settings");await ReorderSettle(600);RefreshSettingsHints();await ReorderSettle();
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        foreach(string route in new[]{"performance","appearance","settings"}) {Navigate(route);await ReorderSettle(180);RefreshSettingsHints();await ReorderSettle(80);}
        string[] settingsNames=["开机自动启动说明","延时启动说明","自动适应说明","FPS 上限说明","MCP说明","开启说明","新手引导说明","品牌底纹说明","界面动效说明"];
        var settingsButtons=EffectsLogicalChildren(SettingsWorkspace).OfType<Button>().Where(button=>AutomationProperties.GetName(button).EndsWith("说明")).ToArray();
        ReorderCheck(!EffectsLogicalChildren(SettingsCategoryGrid).OfType<Button>().Any(button=>AutomationProperties.GetName(button).EndsWith("说明")),"All four settings navigation buttons omit help");
        ReorderCheck(!settingsButtons.Any(button=>AutomationProperties.GetName(button) is "荧光黄绿说明" or "恢复默认外观说明"),"Appearance swatches and reset omit help");
        ReorderCheck(settingsButtons.Where(button=>AutomationProperties.GetName(button) is "品牌底纹说明" or "界面动效说明").All(button=>AutomationProperties.GetHelpText(button).Contains("开销")),"Appearance help describes rendering cost");
        ReorderCheck(settingsButtons.All(OutsideButton),"All settings help buttons sit outside action buttons");
        var diagnosticActions=EffectsLogicalChildren(SettingsDiagnosticsHost).OfType<Button>().Where(button=>button.Content is string title&&(title=="打开日志文件夹"||title=="导出诊断包")).ToArray();
        ReorderCheck(diagnosticActions.Length==2&&diagnosticActions.All(b=>b.ActualWidth>0)&&Math.Abs(diagnosticActions[0].TransformToVisual(SettingsDiagnosticsHost).TransformPoint(new Point()).Y-diagnosticActions[1].TransformToVisual(SettingsDiagnosticsHost).TransformPoint(new Point()).Y)<1,"Both diagnostic actions occupy the same row");
        foreach(var action in diagnosticActions) {
            var hint=settingsButtons.Single(button=>AutomationProperties.GetName(button)==action.Content+"说明");
            var edge=action.TransformToVisual(SettingsDiagnosticsHost).TransformPoint(new Point(action.ActualWidth,0));
            var start=hint.TransformToVisual(SettingsDiagnosticsHost).TransformPoint(new Point());
            ReorderCheck(start.X>=edge.X&&start.X-edge.X<8,"Diagnostic help is just outside its button: "+action.Content);
        }
        SettingsScroll.ChangeView(null,SettingsScroll.ScrollableHeight,null,true);
        await ReorderSettle();await ReorderCapture(output,"diagnostic-actions");
        SettingsScroll.ChangeView(null,0,null,true);await ReorderSettle();
        ReorderCheck(settingsButtons.GroupBy(button=>AutomationProperties.GetName(button)).All(group=>group.Count()==1),"Repeated settings navigation and collection never duplicate description icons");
        File.WriteAllText(Path.Combine(output,"settings-help-82.json"),JsonSerializer.Serialize(settingsButtons.Select(button=>AutomationProperties.GetName(button)),new JsonSerializerOptions {Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        await ReorderCapture(output,"settings-help-82");
        ReorderCheck(settingsNames.All(name=>settingsButtons.Any(button=>AutomationProperties.GetName(button)==name)),"General settings expose inline explanations for their controls and actions: "+string.Join(",",settingsNames.Where(name=>!settingsButtons.Any(button=>AutomationProperties.GetName(button)==name))));
        var guideHelp=settingsButtons.Single(button=>AutomationProperties.GetName(button)=="新手引导说明");await Invoke(guideHelp);
        ReorderCheck(onboardingDialog==null&&activeHelp is {IsOpen:true},"Reading settings action help does not execute that action");DismissHelp();
        await ReorderCapture(output,"settings-help-82");
        var guideTask=ShowOnboarding();await ReorderSettle();var guide=onboardingDialog!;
        var previous=Visuals(guide).OfType<Button>().Single(button=>button.Name=="PrimaryButton");var next=Visuals(guide).OfType<Button>().Single(button=>button.Name=="SecondaryButton");
        ReorderCheck(previous.Content.ToString()=="上一步"&&next.Content.ToString()=="下一步"&&!previous.IsEnabled&&previous.TransformToVisual(null).TransformPoint(new Point()).X<next.TransformToVisual(null).TransformPoint(new Point()).X,"Guide places Previous on the left and Next on the right");
        await Invoke(next);ReorderCheck(guide.IsPrimaryButtonEnabled,"Next advances the guide and enables Previous");await Invoke(previous);ReorderCheck(!guide.IsPrimaryButtonEnabled,"Previous returns to step one");guide.Hide();await guideTask;
        SetHelpHintsEnabled(false);ReorderCheck(settingsButtons.All(button=>button.Visibility==Visibility.Collapsed),"The global switch also hides settings explanations");SetHelpHintsEnabled(true);

        Navigate("library");await ReorderSettle(500);var row=LibraryList.Items.OfType<LibraryRow>().First();LibraryList.SelectedItem=row;await ReorderSettle();
        var item=(GridViewItem)LibraryList.ContainerFromItem(row);
        ReorderCheck(libraryResultCount.Parent==null&&Visuals(item).OfType<TextBlock>().All(text=>!text.Text.Contains("已选中")),"Library hides the result count and selected wording");
        int presses=0;item.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>presses++),true);
        var point=item.TransformToVisual(ShellViewport).TransformPoint(new Point(item.ActualWidth*.4,item.ActualHeight*.5));
        int x=(int)(point.X*Root.XamlRoot.RasterizationScale),y=(int)(point.Y*Root.XamlRoot.RasterizationScale);nint coordinates=(nint)((y<<16)|(x&65535));var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Test82PostMessage(hwnd,0x200,0,coordinates);Test82PostMessage(hwnd,0x201,1,coordinates);await ReorderSettle(80);Test82PostMessage(hwnd,0x202,0,coordinates);await ReorderSettle(300);
        if(presses==0) {
            nint input=0;EnumChildWindows(hwnd,(child,_)=> {var name=new System.Text.StringBuilder(256);GetClassName(child,name,256);GetWindowThreadProcessId(child,out uint pid);if(pid==(uint)Environment.ProcessId&&name.ToString().Contains("InputSite"))input=child;return true;},0);
            if(input!=0) {var at=new Test82Point {X=x,Y=y};MapWindowPoints(hwnd,input,ref at,1);var local=(nint)((at.Y<<16)|(at.X&65535));Test82PostMessage(input,0x200,0,local);Test82PostMessage(input,0x201,1,local);await ReorderSettle(80);Test82PostMessage(input,0x202,0,local);await ReorderSettle(300);}
        }
        File.WriteAllText(Path.Combine(output,"library-pointer-82.json"),JsonSerializer.Serialize(new {presses,nativePointerVerified=presses>0,selected=item.IsSelected,limitation=presses>0?"":"Targeted messages to the off-screen HWND and its input child do not produce WinUI pointer events on this host. No global pointer input or activation was used; the production tap transition is checked below."}));
        if(presses>0)ReorderCheck(!item.IsSelected&&LibraryList.SelectedItems.Count==0,"Clicking the selected material again clears selection through native pointer routing");
        var material=(LibraryGridViewItem)item;LibraryList.SelectedItem=row;await ReorderSettle();
        material.BeginSelectionTap(false,item);ReorderCheck(!material.CompleteSelectionTap(item)&&material.IsSelected,"Modified clicks retain native range and extended selection");
        var selectionBox=Visuals(item).OfType<CheckBox>().Single(check=>check.Name=="LibrarySelectionToggle");
        material.BeginSelectionTap(true,selectionBox);ReorderCheck(!material.CompleteSelectionTap(selectionBox)&&material.IsSelected,"The independent selection checkbox retains its own click handling");
        material.BeginSelectionTap(true,item);ReorderCheck(material.CompleteSelectionTap(item),"A repeated plain material tap is handled by the production transition");await ReorderSettle();
        ReorderCheck(!material.IsSelected&&LibraryList.SelectedItems.Count==0&&!selectionBox.IsChecked.GetValueOrDefault(),"Repeated material tap updates container, selection collection and checkbox together");
        await ReorderCapture(output,"library-deselected-82");ReorderCheck(wallpapers!.Running.Count==0,"Selection and read-only tests never start desktop playback");
        Navigate("wallpaper");
    }
}
