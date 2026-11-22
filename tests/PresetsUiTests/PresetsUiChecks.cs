using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<object> presetGridSnapshots=[];
    private sealed record PresetGridCard(Border Card,double X,double Y,int Row,int Column);
    private static IEnumerable<FrameworkElement> PresetVisuals(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in PresetVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }

    private ContentDialog? PresetDialog()=>presetSaveDialog;

    private IEnumerable<FrameworkElement> PresetDialogVisuals(ContentDialog dialog)=>PresetVisuals(dialog)
        .Concat(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(p=>PresetVisuals(p.Child))).Distinct();

    private static void InvokePresetAction(Button button)=>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private async Task PresetNameValue(TextBox box,string stage)
    {
        var value=await ReadPresetNameUia(box,null,stage);
        ReorderCheck(box.IsLoaded&&box.IsEnabled&&!box.IsReadOnly&&value.Supported&&!value.IsReadOnly,"The loaded preset name field and its native UIA ValuePattern are editable: "+stage);
    }

    private async Task InputPresetName(TextBox box,string text,string stage)
    {
        var value=await ReadPresetNameUia(box,text,stage);
        ReorderCheck(box.IsLoaded&&box.IsEnabled&&!box.IsReadOnly&&value.Supported&&!value.IsReadOnly,"The preset name remains editable through native UIA input: "+stage);
        ReorderCheck(value.Value==text&&box.Text==text,"Native UIA ValuePattern accepts the requested name without bypassing read-only state: "+stage);
    }

    private async Task CheckPresetNameFocusCycle(ContentDialog dialog,TextBox box,string text,string stage)
    {
        await PresetNameValue(box,stage+" / first entry");
        int gained=0,lost=0;
        void Gained(object sender,RoutedEventArgs args)=>gained++;
        void Lost(object sender,RoutedEventArgs args)=>lost++;
        box.GotFocus+=Gained;box.LostFocus+=Lost;
        try {
            ReorderCheck(box.Focus(FocusState.Programmatic),"The name field accepts local dialog focus: "+stage);
            await ReorderSettle(80);ReorderCheckIsolation();
            await InputPresetName(box,text+"（首次输入）",stage+" / first input");
            ReorderCheck(PresetDialogAction(dialog,dialog.CloseButtonText).Focus(FocusState.Programmatic),"Focus can move to another control inside the isolated dialog: "+stage);
            await ReorderSettle(80);ReorderCheckIsolation();
            ReorderCheck(lost>0&&!box.IsReadOnly,"The name field receives LostFocus without becoming read-only: "+stage);
            ReorderCheck(box.Focus(FocusState.Programmatic),"Focus can return to the name field without a double click: "+stage);
            await ReorderSettle(80);ReorderCheckIsolation();
            ReorderCheck(gained>0,"The name field receives focus again inside the same dialog: "+stage);
            await InputPresetName(box,text,stage+" / after focus returns");
        } finally {box.GotFocus-=Gained;box.LostFocus-=Lost;}
    }

    private async Task CheckPresetNameReadOnlyControl()
    {
        var control=new TextBox {Style=HomeStyle("UtilityTextBox"),Text="只读对照"};
        presetEditor.Children.Add(control);
        try {
            await ReorderSettle(80);
            var value=await ReadPresetNameUia(control,null,"unmodified UtilityTextBox negative control");
            ReorderCheck(control.IsLoaded&&control.IsReadOnly&&value.Supported&&value.IsReadOnly,"The native UIA probe reports read-only ValuePattern on the unmodified UtilityTextBox control");
        } finally {presetEditor.Children.Remove(control);}
    }

    private Button PresetDialogAction(ContentDialog dialog,string text)=>PresetDialogVisuals(dialog).OfType<Button>()
        .Single(button=>button.Visibility==Visibility.Visible&&button.ActualWidth>0&&
            (button.Content as string==text||AutomationProperties.GetName(button)==text));

    private async Task CapturePresetDialog(string output,string name,ContentDialog dialog)
    {
        var surface=PresetDialogVisuals(dialog).FirstOrDefault(element=>element.Name=="BackgroundElement"&&element.ActualWidth>0)??dialog;
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(surface);
        using var stream=File.Create(Path.Combine(output,name+".png"));
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private PresetGridCard[] CheckPresetGrid(int windowWidth,int expectedCount)
    {
        var cards=presetCards.Children.OfType<Border>().Select(card=> {
            var at=card.TransformToVisual(presetCards).TransformPoint(new());
            return new PresetGridCard(card,at.X,at.Y,Grid.GetRow(card),Grid.GetColumn(card));
        }).ToArray();
        string context=$"{windowWidth}px / {expectedCount} presets";
        var gridAt=presetCards.TransformToVisual(SharingScroll).TransformPoint(new());
        File.AppendAllText(Path.Combine(EffectReorderRegressionProgram.Output,"grid-measurements.jsonl"),JsonSerializer.Serialize(new {
            windowWidth,expectedCount,gridWidth=presetCards.ActualWidth,gridX=gridAt.X,scrollWidth=SharingScroll.ActualWidth,viewportWidth=SharingScroll.ViewportWidth,
            libraryWidth=presetLibraryHost.ActualWidth,panelWidth=SharingPanel.ActualWidth,parentWidth=(presetCards.Parent as FrameworkElement)?.ActualWidth,
            cards=cards.Select(item=>new {item.X,item.Y,item.Row,item.Column,width=item.Card.ActualWidth,height=item.Card.ActualHeight}).ToArray()
        })+Environment.NewLine);
        ReorderCheck(gridAt.X>=-.5&&gridAt.X+presetCards.ActualWidth<=SharingScroll.ActualWidth+.5,$"The complete preset grid remains inside the scroll viewport after resizing: {context}, gridX={gridAt.X}, gridWidth={presetCards.ActualWidth}, scrollWidth={SharingScroll.ActualWidth}, viewport={SharingScroll.ViewportWidth}");
        ReorderCheck(cards.Length==expectedCount&&presetListCount.Text==$"预设 · {expectedCount}","The Presets heading counts every square card: "+context);
        ReorderCheck(cards.Select(item=>item.Card.Tag as string).Distinct().Count()==expectedCount&&cards.All(item=>item.Card.Tag is string id&&Guid.TryParseExact(id,"N",out _)),"Each rendered card retains its own persistent preset identity: "+context);
        ReorderCheck(cards.All(item=>item.Card.ActualWidth>=200&&Math.Abs(item.Card.ActualWidth-item.Card.ActualHeight)<1),"Every preset card has a usable square outer frame: "+context);
        ReorderCheck(cards.All(item=>Math.Abs(item.Card.ActualWidth-cards[0].Card.ActualWidth)<=1&&item.X>=-.5&&item.X+item.Card.ActualWidth<=presetCards.ActualWidth+.5),"All preset cards share one size within pixel rounding and stay inside the available width: "+context);
        double slotWidth=(presetCards.ActualWidth-(presetCards.ColumnDefinitions.Count-1)*presetCards.ColumnSpacing)/presetCards.ColumnDefinitions.Count;
        ReorderCheck(cards.All(item=>Math.Abs(item.Card.ActualWidth-slotWidth)<=1),"Card sizes follow the available equal grid slots independently of item count: "+context);
        ReorderCheck(cards.All(a=>cards.All(b=>ReferenceEquals(a,b)||a.X+a.Card.ActualWidth<=b.X+.5||b.X+b.Card.ActualWidth<=a.X+.5||a.Y+a.Card.ActualHeight<=b.Y+.5||b.Y+b.Card.ActualHeight<=a.Y+.5)),"Square cards never overlap across columns or wrapped rows: "+context);
        var buttonMeasurements=new List<object>();
        foreach(var item in cards) {
            var buttons=PresetVisuals(item.Card).OfType<Button>().ToArray();
            string name=AutomationProperties.GetName(item.Card);
            ReorderCheck(buttons.Select(AutomationProperties.GetName).Order().SequenceEqual(new[]{"应用","详情","导出 ZIP","删除"}.Select(action=>action+"："+name).Order()),"Each square card exposes apply, details, ZIP export and delete actions with a distinct accessible target: "+context+" / "+item.Card.Tag);
            var logo=PresetVisuals(item.Card).OfType<Image>().Single(image=>image.Source is BitmapImage bitmap&&bitmap.UriSource?.AbsoluteUri=="ms-appx:///Assets/Branding/icon.png");
            ReorderCheck(logo.Source is BitmapImage loaded&&loaded.PixelWidth>0&&loaded.PixelHeight>0&&logo.ActualWidth>0&&logo.ActualHeight>0&&logo.ActualWidth<=120.5&&logo.ActualHeight<=104.5,"Each card renders the existing software logo within its cover: "+context+" / "+item.Card.Tag);
            string[] orderedActions=["应用","详情","导出 ZIP","删除"];
            ReorderCheck(orderedActions.Select((action,index)=>(button:buttons.Single(button=>AutomationProperties.GetName(button)==action+"："+name),index))
                .All(item=>Grid.GetRow(item.button)==item.index/2&&Grid.GetColumn(item.button)==item.index%2),"The four card actions form the requested usable two-by-two layout: "+context+" / "+item.Card.Tag);
            bool fit=true;
            foreach(var button in buttons) {
                var at=button.TransformToVisual(item.Card).TransformPoint(new());
                var label=PresetVisuals(button).OfType<TextBlock>().Single(text=>AutomationProperties.GetName(button)==text.Text+"："+name);
                var labelAt=label.TransformToVisual(button).TransformPoint(new());
                fit&=button.ActualHeight>=40&&at.X>=0&&at.Y>=0&&at.X+button.ActualWidth<=item.Card.ActualWidth+.5&&at.Y+button.ActualHeight<=item.Card.ActualHeight+.5&&
                    labelAt.X>=-.5&&labelAt.Y>=-.5&&labelAt.X+label.ActualWidth<=button.ActualWidth+.5&&labelAt.Y+label.ActualHeight<=button.ActualHeight+.5;
                buttonMeasurements.Add(new {id=item.Card.Tag,label=label.Text,x=at.X,y=at.Y,width=button.ActualWidth,height=button.ActualHeight,labelX=labelAt.X,labelY=labelAt.Y,labelWidth=label.ActualWidth,labelHeight=label.ActualHeight});
            }
            ReorderCheck(fit,"All four footer labels fit fully inside usable buttons and the square frame: "+context+" / "+item.Card.Tag);
            var title=PresetVisuals(item.Card).OfType<TextBlock>().Single(text=>text.Text==name);
            ReorderCheck(title.TextWrapping==TextWrapping.NoWrap&&title.TextTrimming==TextTrimming.CharacterEllipsis&&ToolTipService.GetToolTip(title) as string==name,"Preset titles stay on one line and retain the complete name in a tooltip: "+context+" / "+item.Card.Tag);
        }
        int columns=cards.Max(item=>item.Column)+1,rows=cards.Max(item=>item.Row)+1;
        if(expectedCount>1) {
            ReorderCheck((windowWidth<800?columns==1:columns>1)&&rows>1&&cards.Select(item=>Math.Round(item.X,1)).Distinct().Count()==columns&&cards.Select(item=>Math.Round(item.Y,1)).Distinct().Count()==rows,"The preset fixture uses the expected responsive column count and wraps onto multiple real rows: "+context);
            var first=cards.Where(item=>item.Row==0).OrderBy(item=>item.Column).ToArray();
            var last=cards.Where(item=>item.Row==rows-1).OrderBy(item=>item.Column).ToArray();
            if(columns>1)ReorderCheck(last.Length<first.Length&&last[0].Column==0&&Math.Abs(last[0].X-first[0].X)<.5&&last.All(item=>Math.Abs(item.Card.ActualWidth-first[0].Card.ActualWidth)<=1)&&
                last[^1].X+last[^1].Card.ActualWidth<presetCards.ActualWidth-first[0].Card.ActualWidth/2,"The incomplete final row starts at the left and leaves empty slots instead of stretching its cards: "+context);
        } else ReorderCheck(cards[0].Card.ActualWidth<presetCards.ActualWidth*.6&&cards[0].Column==0&&cards[0].Row==0,"A single preset occupies one grid slot instead of filling the row: "+context);
        presetGridSnapshots.Add(new {windowWidth,expectedCount,gridWidth=presetCards.ActualWidth,gridHeight=presetCards.ActualHeight,columns,rows,
            cards=cards.Select(item=>new {id=item.Card.Tag,name=AutomationProperties.GetName(item.Card),item.Row,item.Column,item.X,item.Y,width=item.Card.ActualWidth,height=item.Card.ActualHeight}).ToArray(),buttons=buttonMeasurements});
        return cards;
    }

    internal async void CheckPresetsUi(string output)
    {
        try {
            PrepareEffectReorderFixture();await ReorderSettle();ReorderCheckIsolation();
            string screen=SelectedScreen!.DeviceName;
            store.SaveProfile(screen,"",new(FillMode.Contain,VideoEffects.Original with {Brightness=.14}));
            store.SetSetting("widgets.v1",new WidgetConfiguration {Widgets=[new() {Id="preset-ui-text",Kind=WidgetKind.Text,Screen=screen,Enabled=false,Visual=new() {Text="UI private note"}}]}.ToJson());
            Navigate("presets");RefreshSharingPage();await ReorderSettle();
            ReorderCheck(SharingPanel.Visibility==Visibility.Visible&&navigationPage=="presets","The dedicated preset route displays the preset page");
            ReorderCheck(EffectsGlobalNavigation.Children.IndexOf(PresetsNav)+1==EffectsGlobalNavigation.Children.IndexOf(SettingsNav),"Presets appears immediately above Settings in the left navigation");
            ReorderCheck(AutomationProperties.GetName(PresetsNav)=="预设","The new navigation entry has the accessible Presets label");
            ReorderCheck(SharingPanel.Children[0]==presetLibraryHost&&presetListCount.Text=="预设 · 0","Presets is the first page section and its heading has no My prefix");
            ReorderCheck(PresetDialog()==null&&presetEditor.Parent==null&&!PresetVisuals(SharingPanel).Any(element=>element is CheckBox or TextBox),"The preset page has no inline configuration editor or category choices");
            var actions=PresetVisuals(SharingPanel).OfType<Button>().ToArray();
            ReorderCheck(AutomationProperties.GetName(presetSave)=="保存当前配置"&&actions.Contains(presetSave),"The page exposes the Save current configuration action");
            ReorderCheck(actions.Any(b=>AutomationProperties.GetName(b).Contains("导入")&&AutomationProperties.GetName(b).Contains("ZIP",StringComparison.OrdinalIgnoreCase)),"The page exposes ZIP import");
            ReorderCheck(!PresetVisuals(SharingPanel).OfType<TextBlock>().Any(t=>t.Text.Contains("二维码")||t.Text.Contains("粘贴分享")||t.Text.Contains("保存当前配置，或")||t.Text.Contains("选择预设后")||t.Text.Contains("当前壁纸、")||t.Text.Contains("文字、名称、")),"The preset page omits the old sharing explanations and category descriptions");
            foreach(int width in new[]{1440,1100,960}) {
                ReorderResize(width,1100);await ReorderSettle();
                var heading=presetListCount.TransformToVisual(presetHeader).TransformPoint(new());
                var button=presetSave.TransformToVisual(presetHeader).TransformPoint(new());
                bool aligned=presetHeader.ActualWidth<520?button.Y>=heading.Y+presetListCount.ActualHeight:
                    button.X>=heading.X+presetListCount.ActualWidth&&Math.Abs(button.Y+presetSave.ActualHeight/2-heading.Y-presetListCount.ActualHeight/2)<2;
                ReorderCheck(aligned&&Math.Abs(button.X+presetSave.ActualWidth-presetHeader.ActualWidth)<1,"The Save current configuration button stays right-aligned in the responsive Presets header at "+width);
                ReorderCheck(presetSave.ActualHeight>=40&&presetListCount.TransformToVisual(SharingScroll).TransformPoint(new()).Y<70,"The heading and usable save action stay at the top of the preset page at "+width);
                await ReorderCapture(output,"presets-empty-"+width);
            }

            InvokePresetAction(presetSave);
            for(int i=0;i<40&&(PresetDialog()==null||presetEditor.ActualWidth==0);i++)await ReorderSettle(50);
            var dialog=PresetDialog()??throw new InvalidOperationException("Save action did not open a ContentDialog.");
            await ReorderSettle();
            ReorderCheck(dialog.Title as string=="保存当前配置"&&dialog.Content is ScrollViewer scroller&&ReferenceEquals(scroller.Content,presetEditor),"Saving opens a modal editor with the requested title");
            await CheckPresetNameReadOnlyControl();
            await CheckPresetNameFocusCycle(dialog,presetName,"首次编辑的名称","save dialog first open");
            var choices=new[]{presetPlaying,presetLibrary,presetEffects,presetWidgets};
            ReorderCheck(choices.Select(AutomationProperties.GetName).Order().SequenceEqual(new[]{"正在播放","素材库","画面效果","组件"}.Order())&&PresetVisuals(presetEditor).OfType<CheckBox>().Count()==5,"The save dialog contains exactly four complete categories and one component privacy option");
            ReorderCheck(presetPrivacy.IsChecked!=true&&presetWidgets.IsChecked==true&&presetPrivacy.IsEnabled,"Component privacy is initially excluded and nested under the selected component category");
            ReorderCheck(dialog.PrimaryButtonText=="保存"&&dialog.SecondaryButtonText=="导出 ZIP"&&dialog.CloseButtonText=="取消","The save dialog exposes save, ZIP export and cancel actions");
            ReorderCheck(!PresetVisuals(presetEditor).OfType<TextBlock>().Any(t=>t.Text.Contains("当前壁纸、")||t.Text.Contains("素材库中的")||t.Text.Contains("画面效果、")||t.Text.Contains("组件、分组")||t.Text.Contains("文字、名称、")),"The dialog retains concise labels without category explanations");
            presetWidgets.IsChecked=false;await ReorderSettle();
            ReorderCheck(!presetPrivacy.IsEnabled||presetPrivacy.Visibility==Visibility.Collapsed,"The component privacy option is unavailable when components are excluded");
            presetWidgets.IsChecked=true;await ReorderSettle();
            ReorderCheck(presetPrivacy.IsEnabled&&presetPrivacy.Visibility==Visibility.Visible,"Selecting components restores its privacy option");
            foreach(int width in new[]{1440,960}) {
                ReorderResize(width,1100);await ReorderSettle();
                foreach(var choice in choices) {
                    var at=choice.TransformToVisual(presetEditor).TransformPoint(new());
                    ReorderCheck(choice.ActualWidth>=120&&choice.ActualHeight>=44&&at.X>=-.5&&at.X+choice.ActualWidth<=presetEditor.ActualWidth+.5,"Every category fits the save dialog with a usable hit target at "+width+": "+AutomationProperties.GetName(choice));
                }
                ReorderCheck(choices.All(choice=>Math.Abs(choice.ActualHeight-presetPlaying.ActualHeight)<1)&&Math.Abs(presetEffects.TransformToVisual(presetEditor).TransformPoint(new()).Y-presetWidgets.TransformToVisual(presetEditor).TransformPoint(new()).Y)<1,"All four category cards keep equal heights and the second row stays top-aligned above component privacy at "+width);
                var dialogSurface=PresetDialogVisuals(dialog).First(element=>element.Name=="BackgroundElement"&&element.ActualWidth>0);
                ReorderCheck(dialogSurface.ActualWidth<=Root.ActualWidth&&dialogSurface.ActualHeight<=Root.ActualHeight,"The save dialog fits the window at "+width);
                await CapturePresetDialog(output,"presets-save-dialog-"+width,dialog);
                ReorderCheckIsolation();
            }
            await InputPresetName(presetName,"取消后不应保存","cancel save");
            InvokePresetAction(PresetDialogAction(dialog,"取消"));
            for(int i=0;i<40&&PresetDialog()!=null;i++)await ReorderSettle(50);
            ReorderCheck(PresetDialog()==null&&presetEditor.Parent==null&&modules.Presets.List().Count==0,"Cancel closes and detaches the editor without creating a preset");
            ReorderCheck(!PresetVisuals(SharingPanel).Any(element=>element is CheckBox or TextBox),"Closing the dialog does not leave an inline editor on the page");

            InvokePresetAction(presetSave);
            for(int i=0;i<40&&PresetDialog()==null;i++)await ReorderSettle(50);
            dialog=PresetDialog()??throw new InvalidOperationException("Save dialog could not be reopened after cancellation.");await ReorderSettle();
            await CheckPresetNameFocusCycle(dialog,presetName,"离屏保存的预设","save dialog reopened after cancellation");
            foreach(var choice in choices)choice.IsChecked=false;await ReorderSettle();
            ReorderCheck(!dialog.IsPrimaryButtonEnabled&&!dialog.IsSecondaryButtonEnabled&&presetSave.IsEnabled,"An empty selection disables save and export without disabling the page's reopen action");
            presetEffects.IsChecked=true;presetWidgets.IsChecked=true;presetPrivacy.IsChecked=false;await InputPresetName(presetName,"离屏保存的预设","confirm save with UIA input");await ReorderSettle();
            ReorderCheck(dialog.IsPrimaryButtonEnabled&&dialog.IsSecondaryButtonEnabled,"Selecting complete categories enables save and export");
            InvokePresetAction(PresetDialogAction(dialog,"保存"));
            for(int i=0;i<160&&(PresetDialog()!=null||presetBusy||!PresetVisuals(presetCards).OfType<TextBlock>().Any(t=>t.Text=="离屏保存的预设"));i++)await ReorderSettle(50);
            ReorderCheck(PresetDialog()==null&&presetEditor.Parent==null&&!presetBusy&&PresetVisuals(presetCards).OfType<TextBlock>().Any(t=>t.Text=="离屏保存的预设"),"Saving closes the dialog and adds the named square preset card");
            var saved=modules.Presets.List().Single();var content=modules.Presets.Read(saved.Id);
            ReorderCheck(content.Selection.Effects&&content.Selection.Widgets&&!content.Selection.Playing&&!content.Selection.Library&&!content.Widgets!.ToJson().Contains("UI private note"),"The saved package reflects the dialog categories and excludes component private text");
            ReorderCheck(PresetVisuals(presetCards).OfType<Button>().Any(b=>AutomationProperties.GetName(b).Contains("应用"))&&PresetVisuals(presetCards).OfType<Button>().Any(b=>AutomationProperties.GetName(b).Contains("导出")),"Saved presets retain apply and ZIP export actions");
            ReorderCheck(!PresetVisuals(presetCards).OfType<TextBlock>().Any(t=>t.Text.Contains("素材：")||t.Text.Contains("组件：")||t.Text.Contains("已保存于")||t.Text.Contains("创建时间")),"Saved cards omit statistics and explanatory metadata");
            InvokePresetAction(presetSave);
            for(int i=0;i<40&&(PresetDialog()==null||!presetName.IsLoaded);i++)await ReorderSettle(50);
            dialog=PresetDialog()??throw new InvalidOperationException("Save dialog could not be reopened after saving.");await ReorderSettle();
            await InputPresetName(presetName,"保存后再次编辑","save dialog reopened after successful save");
            InvokePresetAction(PresetDialogAction(dialog,"取消"));
            for(int i=0;i<40&&PresetDialog()!=null;i++)await ReorderSettle(50);
            ReorderCheck(modules.Presets.List().Single().Name=="离屏保存的预设","Editing a reopened save dialog and cancelling leaves the UIA-created preset unchanged");
            await CheckPresetDetailsUi(output,saved.Id,screen);
            await CheckPresetImportLocationUi(output);
            foreach(int width in new[]{1440,1100,960}) {
                ReorderResize(width,1100);await ReorderSettle();
                await ReorderCapture(output,"presets-single-"+width);
                CheckPresetGrid(width,1);
            }
            string[] fixtureNames=["夜间桌面","画面效果与组件","素材库组合","正在播放的壁纸","浅色工作桌面","长名称布局测试：夜间桌面与组件完整配置以及窗口尺寸调整检查"];
            foreach(string name in fixtureNames)await modules.Presets.SaveAsync(content with {Name=name});
            RefreshPresetCards();
            for(int i=0;i<120&&presetCards.Children.Count!=7;i++)await ReorderSettle(50);
            foreach(int width in new[]{1440,1100,960,660,1440}) {
                ReorderResize(width,1100);SharingScroll.ChangeView(null,0,null,true);await ReorderSettle();
                await ReorderCapture(output,"presets-grid-"+width);
                CheckPresetGrid(width,7);
                if(SharingScroll.ScrollableHeight>1) {SharingScroll.ChangeView(null,SharingScroll.ScrollableHeight,null,true);await ReorderSettle();await ReorderCapture(output,"presets-grid-bottom-"+width);}
            }
            foreach(var entry in modules.Presets.List().Take(2))modules.Presets.Delete(entry.Id);
            RefreshPresetCards();for(int i=0;i<120&&presetCards.Children.Count!=5;i++)await ReorderSettle(50);
            foreach(int width in new[]{1440,960}) {
                ReorderResize(width,1100);SharingScroll.ChangeView(null,0,null,true);await ReorderSettle();
                await ReorderCapture(output,"presets-five-"+width);CheckPresetGrid(width,5);
            }
            foreach(var entry in modules.Presets.List())modules.Presets.Delete(entry.Id);
            RefreshPresetCards();for(int i=0;i<120&&presetCards.Children.Count!=0;i++)await ReorderSettle(50);
            await ReorderSettle();
            ReorderCheck(presetCards.Children.Count==0&&presetListCount.Text=="预设 · 0"&&presetSave.IsEnabled,"Deleting every isolated fixture preset returns to the empty page without stale cards or disabling save");
            await ReorderCapture(output,"presets-deleted-empty");
            File.WriteAllText(Path.Combine(output,"grid-layout.json"),JsonSerializer.Serialize(presetGridSnapshots,new JsonSerializerOptions {WriteIndented=true}));
            Navigate("settings");await ReorderSettle();
            ReorderCheck(!settingsCategoryButtons.Keys.Any(k=>k is "sharing" or "presets"),"Settings no longer includes configuration sharing or presets as a category");
            Navigate("sharing");await ReorderSettle();
            ReorderCheck(navigationPage=="presets"&&SharingPanel.Visibility==Visibility.Visible,"The legacy sharing route resolves to the new Presets page");
            ReorderCheckIsolation();
            ReorderCheck(App.ReorderIsolation is {ForegroundActivated:false},"No observation during the preset fixture recorded foreground activation");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks,
                scope="Real off-screen WinUI preset square-card grids, software logos, four actions, details, save/rename via UIA ValuePattern, loaded/read-only checks, local focus-loss/return and dialog reopen regression, validation/cancellation and privacy controls; no global keyboard injection or file-picker launch. ZIP round trips and rename/export consistency are covered by StorageTests.",
                foregroundActivated=App.ReorderIsolation!.ForegroundActivated,productionDataAccessed=false,desktopHostsStarted=false},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception error) {
            File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Environment.ExitCode=1;
        } finally {
            effectSession.StopSchedule();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            App.ReorderIsolation?.Observe("before preset fixture close");App.ReorderIsolation?.WriteReport();
            if(App.ReorderIsolation is {ForegroundActivated:true}) {
                File.Delete(Path.Combine(output,"results.json"));
                if(!File.Exists(Path.Combine(output,"failure.txt")))
                    File.WriteAllText(Path.Combine(output,"failure.txt"),"The preset fixture recorded foreground activation; see activation-guard.json.");
                Environment.ExitCode=1;
            }
            Close();store.Dispose();App.ChildProcesses.Dispose();Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }
}
