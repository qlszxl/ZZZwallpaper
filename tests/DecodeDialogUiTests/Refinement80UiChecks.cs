using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using LumaWall.Automation;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private async Task CheckRefinement80(string output)
    {
        Navigate("wallpaper");RefreshHomePosition();await ReorderSettle();
        homePositionFlyout!.ShowAt(HomePositionButton);await ReorderSettle();
        ReorderCheck(homePositionX!.ActualWidth>150&&homePositionY!.ActualWidth>150&&homePositionScale!.ActualWidth>150,"Position popup exposes all three sliders together");
        var popup=(ContentControl)homePositionFlyout.Content;
        ReorderCheck(EffectsLogicalChildren(popup).OfType<EffectValueInput>().Count()==3,"Position popup exposes three direct numeric inputs");
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(popup);
        var pixels=await bitmap.GetPixelsAsync();using(var target=File.Create(Path.Combine(output,"position-popup.png"))){using var stream=target.AsRandomAccessStream();var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();}
        homePositionX!.Value=17;homePositionY!.Value=-23;homePositionScale!.Value=140;EndHistoryGesture();
        var screen=SelectedScreen!.DeviceName;
        ReorderCheck(RuntimeSettingsReader.Position(store,screen,homePreviewAsset?.Id)==new WallpaperPosition(17,-23,140)&&FillPicker.SelectedIndex==2,"Scale and offsets persist together and select Custom fill");
        homePositionFlyout.Hide();await RestoreHistory(false);
        var reply=await ExecuteRecordedAutomation(new("wallpaper_set_screen_options",ToolCatalog.Json(new {screenId=screen,expectedRevision=store.ConfigurationRevision(),scale=125})),CancellationToken.None);
        ReorderCheck(!reply.IsError&&reply.Data.GetProperty("scale").GetInt32()==125,"MCP scale update follows the production save path");
        Navigate("library");await ReorderSettle();
        var items=LibraryFilterGrid.Children.OfType<FrameworkElement>().ToArray();
        ReorderCheck(items.Length==8&&items.All(item=>Grid.GetRow(item)==0)&&ReferenceEquals(LibraryView.LibraryToolbarScroll.Content,LibraryFilterGrid),"Library source, search, filters and sort share one scrolling row");
        ReorderCheck(LibraryView.LibraryToolbarScroll.ScrollableWidth<=1,"Library toolbar fits the normal desktop window width without a horizontal scrollbar");
        foreach(var id in new[]{"LibraryFilterToggle","LibraryFilterReset"}) {
            var button=items.OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)==id);
            ReorderCheck(button.ActualHeight>=48&&button.ActualWidth>=64&&button.Padding.Top==0&&button.Padding.Bottom==0,"Library action retains full-height text space: "+id);
        }
        await ReorderCapture(output,"library-toolbar");
        widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()),modules);
        try {
            var fixture=new WidgetConfiguration {Widgets=Enum.GetValues<WidgetKind>().Select(kind=>new WidgetInstance {Id="fixture-"+kind,Kind=kind,Enabled=false,Screen=screen}).ToArray()};
            widgets.Save(fixture);Navigate("widgets");
            foreach(var item in fixture.Widgets) {
                OpenWidget(item.Id);await ReorderSettle(30);
                var sections=WidgetDisclosureElements().ToArray();
                File.WriteAllText(Path.Combine(output,"drawers-"+item.Kind+".json"),JsonSerializer.Serialize(sections.Select(s=>new{s.Key,open=DisclosureOpen(s.Element),type=s.Element.GetType().Name}),new JsonSerializerOptions{WriteIndented=true}));
                ReorderCheck(sections.Length>0&&sections.All(s=>!DisclosureOpen(s.Element)),"New widget editor starts collapsed: "+item.Kind);
                // Spectrum places common state behind its own layer selector.
                if(item.Kind!=WidgetKind.Spectrum)ReorderCheck(EffectsLogicalChildren(WidgetEditorPanel).OfType<ToggleSwitch>().Any(t=>AutomationProperties.GetName(t)=="置于顶层"),"Topmost is available in component state: "+item.Kind);
            }
            string id="fixture-Performance";OpenWidget(id);await ReorderSettle();
            var first=WidgetDisclosureElements().First();SetDisclosureOpen(first.Element,true);CaptureWidgetDisclosures();
            OpenWidget("fixture-Text");OpenWidget(id);
            ReorderCheck(DisclosureOpen(WidgetDisclosureElements().First().Element),"Widget expansion survives switching components within the page");
            var top=EffectsLogicalChildren(WidgetEditorPanel).OfType<ToggleSwitch>().Single(t=>AutomationProperties.GetName(t)=="置于顶层");top.IsOn=true;widgets.FlushPendingSave();
            ReorderCheck(widgets.State.Widgets.Single(w=>w.Id==id).AlwaysOnTop,"Topmost toggle saves independently of visibility");
            Navigate("library");Navigate("widgets");OpenWidget(id);
            ReorderCheck(WidgetDisclosureElements().All(s=>!DisclosureOpen(s.Element)),"Leaving Widgets resets all drawers on reentry");
            var metrics=WidgetDisclosureElements().Single(s=>SectionChrome.GetTitle(s.Element)=="显示指标");SetDisclosureOpen(metrics.Element,true);
            ReorderCheck(EffectsLogicalChildren(metrics.Element).OfType<CheckBox>().Count()==27,"All 27 metrics use existing selection cards");
            await ReorderSettle();await ReorderCapture(output,"widget-metrics");
            ReorderCheck(widgets.ProcessId==null,"Disabled fixture widgets never launch a desktop host");
        }finally{widgets.Dispose();widgets=null;}
    }
}
