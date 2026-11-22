using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<ContentDialog> OpenPresetDetailsUi(string id)
    {
        for(int i=0;i<120&&(presetBusy||!presetCards.Children.OfType<Border>().Any(card=>card.Tag as string==id));i++)await ReorderSettle(50);
        var card=presetCards.Children.OfType<Border>().Single(card=>card.Tag as string==id);
        InvokePresetAction(PresetVisuals(card).OfType<Button>().Single(button=>AutomationProperties.GetName(button).StartsWith("详情：",StringComparison.Ordinal)));
        for(int i=0;i<160&&(presetDetailsDialog==null||presetDetailsName?.ActualWidth is not >0);i++)await ReorderSettle(50);
        await ReorderSettle();
        await PresetNameValue(presetDetailsName??throw new InvalidOperationException("Details name editor is unavailable."),"details dialog opened or reopened");
        return presetDetailsDialog??throw new InvalidOperationException("The card's Details action did not open a dialog.");
    }

    private async Task ClosePresetDetailsUi(ContentDialog dialog)
    {
        InvokePresetAction(PresetDialogAction(dialog,"关闭"));
        for(int i=0;i<160&&(presetDetailsDialog!=null||presetBusy);i++)await ReorderSettle(50);
        await ReorderSettle();
        ReorderCheck(presetDetailsDialog==null&&presetDetailsName==null&&dialog.Content==null&&!presetBusy,"Closing preset details detaches the dialog editor and ends its busy state");
    }

    private static StackPanel PresetDetailsBody(ContentDialog dialog)=>(StackPanel)((ScrollViewer)dialog.Content).Content;
    private static Dictionary<string,Expander> PresetDetailsCategories(ContentDialog dialog)=>PresetDetailsBody(dialog).Children.OfType<Expander>()
        .ToDictionary(AutomationProperties.GetName,StringComparer.Ordinal);
    private static bool HasPresetDetail(DependencyObject root,string label,string value)=>PresetVisuals(root).OfType<TextBlock>()
        .Any(text=>AutomationProperties.GetName(text)==label+"："+value);

    private async Task TogglePresetDetailUi(Expander section,bool expanded)
    {
        var header=PresetVisuals(section).OfType<ToggleButton>().First(button=>button.Name=="ExpanderHeader");
        if(section.IsExpanded!=expanded)
            ((IToggleProvider)new ToggleButtonAutomationPeer(header).GetPattern(PatternInterface.Toggle)).Toggle();
        await ReorderSettle(100);
        ReorderCheck(section.IsExpanded==expanded&&header.IsChecked==expanded,"Invoking the details header through UI Automation changes the matching section state");
        var chevron=PresetVisuals(header).OfType<Microsoft.UI.Xaml.Shapes.Path>().Single(path=>path.Name=="ExpandCollapseChevron");
        ReorderCheck(chevron.RenderTransform is RotateTransform rotate&&Math.Abs(rotate.Angle-(expanded?180:0))<.1,
            "The details chevron matches its expanded state");
    }

    private async Task CheckPresetDetailsStableLayout(string output,PresetDocument source)
    {
        var original=source.Widgets!.Widgets[0];
        var widgets=Enumerable.Range(0,10).Select(i=>original with {
            Id="layout-widget-"+i,Visual=original.Visual with {Text="长文本："+new string('测',180)+" "+i}
        }).ToArray();
        var saved=await modules.Presets.SaveAsync(source with {Name="展开布局检查",Widgets=source.Widgets with {Widgets=widgets}});
        RefreshPresetCards();
        var dialog=await OpenPresetDetailsUi(saved.Id);
        foreach(var size in new[]{new Windows.Graphics.SizeInt32(1440,1100),new Windows.Graphics.SizeInt32(960,760)}) {
            ReorderResize(size.Width,size.Height);await ReorderSettle();
            var categories=PresetDetailsCategories(dialog);
            foreach(var category in categories.Values)category.IsExpanded=false;
            await ReorderSettle();
            var scroll=(ScrollViewer)dialog.Content;scroll.ChangeView(null,0,null,true);await ReorderSettle(100);
            var surface=PresetDialogVisuals(dialog).First(element=>element.Name=="BackgroundElement"&&element.ActualWidth>0);
            var before=surface.TransformToVisual(Root).TransformPoint(new());
            double width=surface.ActualWidth,height=surface.ActualHeight,bodyWidth=PresetDetailsBody(dialog).ActualWidth;
            ReorderCheck(scroll.ScrollableHeight<1,"Collapsed preset categories fit inside the reserved viewport at "+size.Width);
            await CapturePresetDialog(output,"presets-details-collapsed-"+size.Width,dialog);
            for(int repeat=0;repeat<2;repeat++) {
                await TogglePresetDetailUi(categories["组件"],true);
                var nested=((StackPanel)categories["组件"].Content).Children.OfType<Expander>().ToArray();
                ReorderCheck(nested.Length==10,"The expanded category retains all ten saved component sections");
                await TogglePresetDetailUi(nested[0],true);
                ReorderCheck(scroll.ScrollableHeight>0,"Long nested preset details scroll within the fixed viewport");
                var after=surface.TransformToVisual(Root).TransformPoint(new());
                ReorderCheck(Math.Abs(surface.ActualWidth-width)<.5&&Math.Abs(surface.ActualHeight-height)<.5&&
                    Math.Abs(after.X-before.X)<.5&&Math.Abs(after.Y-before.Y)<.5&&Math.Abs(PresetDetailsBody(dialog).ActualWidth-bodyWidth)<.5,
                    "Expanding nested content preserves the dialog bounds and content width at "+size.Width);
                ReorderCheck(scroll.ScrollableWidth<.5&&surface.ActualWidth<=Root.ActualWidth&&surface.ActualHeight<=Root.ActualHeight,
                    "Long text wraps without horizontal scrolling or clipping the details dialog");
                scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await ReorderSettle(100);
                ReorderCheck(scroll.VerticalOffset>0,"The last component remains reachable using the details scrollbar");
                await CapturePresetDialog(output,"presets-details-expanded-"+size.Width+"-"+repeat,dialog);
                await TogglePresetDetailUi(categories["组件"],false);
                await ReorderSettle();
                ReorderCheck(Math.Abs(surface.ActualWidth-width)<.5&&Math.Abs(surface.ActualHeight-height)<.5,
                    "Collapsing a long category does not resize the dialog");
                ReorderCheck(scroll.VerticalOffset<=scroll.ScrollableHeight+.5,"Collapsing content clamps the scroll position without leaving an empty viewport");
            }
        }
        await ClosePresetDetailsUi(dialog);modules.Presets.Delete(saved.Id);RefreshPresetCards();
        ReorderResize(1440,1100);await ReorderSettle();
    }

    private async Task CheckPresetDetailsUi(string output,string subsetId,string screen)
    {
        string currentRevision=store.ConfigurationRevision();long currentHistory=store.HistoryRevision;
        var subset=modules.Presets.List().Single(entry=>entry.Id==subsetId);
        var dialog=await OpenPresetDetailsUi(subsetId);
        ReorderCheck(dialog.Title as string=="预设详情"&&dialog.PrimaryButtonText=="保存名称"&&dialog.CloseButtonText=="关闭"&&!dialog.IsPrimaryButtonEnabled,
            "Details opens with the saved name and disables saving an unchanged name");
        await CheckPresetNameFocusCycle(dialog,presetDetailsName!,"详情输入测试","details dialog first open");
        var categories=PresetDetailsCategories(dialog);
        ReorderCheck(categories.Keys.Order().SequenceEqual(new[]{"正在播放","素材库","画面效果","组件"}.Order())&&categories["画面效果"].IsExpanded,
            "Details contains exactly the four preset categories and opens the first included category");
        foreach(string absent in new[]{"正在播放","素材库"}) {
            categories[absent].IsExpanded=true;await ReorderSettle(80);
            ReorderCheck(PresetVisuals(categories[absent]).OfType<TextBlock>().Any(text=>text.Text=="未包含"),"Details explicitly marks the excluded category instead of presenting current settings: "+absent);
        }
        categories["组件"].IsExpanded=true;await ReorderSettle(100);
        ReorderCheck(HasPresetDetail(categories["组件"],"隐私内容","未包含")&&!PresetVisuals(dialog).OfType<TextBlock>().Any(text=>text.Text.Contains("UI private note")),
            "Details preserves the saved package's privacy exclusion and does not read current private component text");
        await InputPresetName(presetDetailsName!,"   ","rename whitespace validation");await ReorderSettle(80);
        ReorderCheck(!dialog.IsPrimaryButtonEnabled&&PresetDetailsBody(dialog).Children.OfType<TextBlock>().Any(text=>text.Visibility==Visibility.Visible&&text.Text.Contains("1–80")),
            "A whitespace-only rename shows validation and cannot be confirmed");
        await InputPresetName(presetDetailsName!,"非法\u0001名称","rename control-character validation");await ReorderSettle(80);
        ReorderCheck(!dialog.IsPrimaryButtonEnabled&&presetDetailsName!.MaxLength==80,"Control characters cannot be saved and the name editor enforces its maximum length");
        await InputPresetName(presetDetailsName!,"  "+subset.Name+"  ","rename unchanged-name validation");await ReorderSettle(80);
        ReorderCheck(!dialog.IsPrimaryButtonEnabled&&!PresetDetailsBody(dialog).Children.OfType<TextBlock>().Any(text=>text.Visibility==Visibility.Visible&&text.Text.Contains("1–80")),
            "Whitespace around an unchanged valid name neither enables save nor displays an error");
        await InputPresetName(presetDetailsName!,"取消的改名","cancel rename after UIA input");await ReorderSettle(80);
        ReorderCheck(dialog.IsPrimaryButtonEnabled,"A valid changed name enables confirmation");
        await ClosePresetDetailsUi(dialog);
        ReorderCheck(modules.Presets.Inspect(subsetId).Name==subset.Name&&modules.Presets.List().Count==1,"Closing details cancels the edited name and creates no duplicate preset");

        byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6dSAAAAAASUVORK5CYII=");
        string imagePath=Path.Combine(output,"details-wallpaper.png");File.WriteAllBytes(imagePath,png);
        const string assetId="details-image",wallpaperName="详情测试壁纸";
        var effects=VideoEffects.Original with {Brightness=.14,Collection=new() {Added=EffectKind.Color}};
        var complete=new PresetDocument {
            Name="四类设置详情",Selection=new() {Playing=true,Library=true,Effects=true,Widgets=true,IncludeWidgetPrivateData=true},
            Screens=[screen],Assets=[new(assetId,imagePath,wallpaperName,png.Length,0,1,1,"png","sdr","fixture")],
            Bindings=[new(screen,assetId,FillMode.Contain,false,0) {Effects=effects}],
            Playback=new() {[screen]=new() {Mode=PlaybackMode.Sequential,Trigger=PlaybackTrigger.Interval,IntervalSeconds=47,Transition=PlaybackTransition.CrossFade,AssetIds=[assetId],CustomPlaylists=[]}},
            Volumes=new() {[screen]=37},SoundEnabled=new() {[screen]=false},ScreenProfiles=new() {[screen]=new(FillMode.Contain,effects)},
            Widgets=new() {Widgets=[new() {Id="details-text",Screen=screen,Kind=WidgetKind.Text,Enabled=false,X=.23,Y=.37,Width=.22,Height=.12,Opacity=.63,Layer=7,Locked=true,Visual=new() {Text="详情测试文本"}}]}
        };
        var full=await modules.Presets.SaveAsync(complete);RefreshPresetCards();
        dialog=await OpenPresetDetailsUi(full.Id);categories=PresetDetailsCategories(dialog);
        ReorderCheck(categories["正在播放"].IsExpanded&&HasPresetDetail(categories["正在播放"],"壁纸",wallpaperName)&&
            HasPresetDetail(categories["正在播放"],"填充","适应")&&HasPresetDetail(categories["正在播放"],"播放状态","播放中")&&
            HasPresetDetail(categories["正在播放"],"声音","关闭")&&HasPresetDetail(categories["正在播放"],"音量","37%")&&
            HasPresetDetail(categories["正在播放"],"播放模式","顺序播放")&&HasPresetDetail(categories["正在播放"],"切换条件","47 秒"),
            "Playing details show the saved wallpaper, fill, playback mode, interval and muted remembered volume");
        await CapturePresetDialog(output,"presets-details-playing",dialog);
        categories["素材库"].IsExpanded=true;await ReorderSettle(120);
        var library=PresetVisuals(categories["素材库"]).OfType<ListView>().Single();
        ReorderCheck(HasPresetDetail(categories["素材库"],"素材数量","1")&&library.Items.Count==1&&library.Items[0].ToString()!.Contains(wallpaperName+"  ·  图片  ·  1 × 1"),
            "Library details list the bundled wallpaper with its actual kind and dimensions");
        categories["画面效果"].IsExpanded=true;await ReorderSettle(150);
        ReorderCheck(PresetVisuals(categories["画面效果"]).OfType<ComboBox>().Single().Items.Count==1&&HasPresetDetail(categories["画面效果"],"亮度","14%"),
            "Effect details expose the saved target and its nondefault brightness in display units");
        categories["组件"].IsExpanded=true;await ReorderSettle(120);
        var widgetSection=((StackPanel)categories["组件"].Content).Children.OfType<Expander>().Single();
        widgetSection.IsExpanded=true;await ReorderSettle(120);
        ReorderCheck(HasPresetDetail(categories["组件"],"隐私内容","包含")&&HasPresetDetail(categories["组件"],"组件数量","1")&&HasPresetDetail(widgetSection,"类型","文本")&&
            HasPresetDetail(widgetSection,"显示","关闭")&&HasPresetDetail(widgetSection,"位置","23% / 37%")&&
            HasPresetDetail(widgetSection,"尺寸","22% × 12%")&&HasPresetDetail(widgetSection,"透明度","63%")&&
            HasPresetDetail(widgetSection,"层级","7")&&HasPresetDetail(widgetSection,"锁定","是")&&HasPresetDetail(widgetSection,"文本","详情测试文本"),
            "Component details include privacy, actual position, dimensions, opacity, layer, lock and saved text");
        foreach(int width in new[]{1440,960}) {
            ReorderResize(width,1100);await ReorderSettle();
            var surface=PresetDialogVisuals(dialog).First(element=>element.Name=="BackgroundElement"&&element.ActualWidth>0);
            ReorderCheck(surface.ActualWidth<=Root.ActualWidth&&surface.ActualHeight<=Root.ActualHeight,"The expanded details dialog fits the available window at "+width);
            var scroll=(ScrollViewer)dialog.Content;scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await ReorderSettle(100);
            await CapturePresetDialog(output,"presets-details-components-"+width,dialog);
        }
        await InputPresetName(presetDetailsName!,"  已改名的预设  ","confirm rename with UIA input");await ReorderSettle(80);
        ReorderCheck(dialog.IsPrimaryButtonEnabled,"A trimmed valid new preset name enables save");
        InvokePresetAction(PresetDialogAction(dialog,"保存名称"));
        for(int i=0;i<160&&(presetDetailsDialog!=null||presetBusy||!presetCards.Children.OfType<Border>().Any(card=>card.Tag as string==full.Id&&AutomationProperties.GetName(card)=="已改名的预设"));i++)await ReorderSettle(50);
        ReorderCheck(presetDetailsDialog==null&&presetDetailsName==null&&!presetBusy&&modules.Presets.List().Count==2&&modules.Presets.Inspect(full.Id).Name=="已改名的预设",
            "Confirming rename updates the existing preset identity without creating another preset");
        var renamed=presetCards.Children.OfType<Border>().Single(card=>card.Tag as string==full.Id);
        ReorderCheck(AutomationProperties.GetName(renamed)=="已改名的预设"&&PresetVisuals(renamed).OfType<TextBlock>().Any(text=>text.Text=="已改名的预设"&&ToolTipService.GetToolTip(text) as string=="已改名的预设")&&
            PresetVisuals(renamed).OfType<Button>().All(button=>AutomationProperties.GetName(button).EndsWith("：已改名的预设",StringComparison.Ordinal)),
            "Rename refreshes the card title, full-name tooltip and every accessible action target");
        dialog=await OpenPresetDetailsUi(full.Id);
        ReorderCheck(presetDetailsName!.Text=="已改名的预设"&&!dialog.IsPrimaryButtonEnabled,"Reopening details shows the committed name and has no pending edit");
        await InputPresetName(presetDetailsName,"重开详情后再次编辑","details reopened after successful rename");
        await ClosePresetDetailsUi(dialog);
        ReorderCheck(modules.Presets.Inspect(full.Id).Name=="已改名的预设","Cancelling UIA input in reopened details preserves the committed name");
        await CheckPresetDetailsStableLayout(output,complete);
        ReorderCheck(store.ConfigurationRevision()==currentRevision&&store.HistoryRevision==currentHistory,"Viewing details and renaming presets leave the active configuration and history untouched");
        modules.Presets.Delete(full.Id);RefreshPresetCards();
        for(int i=0;i<120&&presetCards.Children.Count!=1;i++)await ReorderSettle(50);
        ReorderCheckIsolation();
    }
}
