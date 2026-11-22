using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private async Task CheckRefinement78(string output)
    {
        var screen=SelectedScreen!.DeviceName;
        wallpapers=new WallpaperController(store,action=>DispatcherQueue.TryEnqueue(()=>action()),modules);
        async Task<OperationReply> Call(string tool,object args) {
            var request=new OperationRequest(tool,ToolCatalog.Json(args));
            var reply=await ExecuteRecordedAutomation(request,CancellationToken.None);
            ReorderCheck(!reply.IsError,"MCP execution: "+tool);return reply;
        }
        await Call("app_update_settings",new {expectedRevision=store.ConfigurationRevision(),autoStart=true,startupDelayEnabled=true,startupDelaySeconds=37});
        ReorderCheck(StartupRegistration.Enabled&&StartupRegistration.Delay==37,"Startup UI/MCP uses shared validated delay with isolated registry fixture");
        await Call("wallpaper_set_screen_options",new {screenId=screen,expectedRevision=store.ConfigurationRevision(),fill="Cover",taskbarEnabled=true,taskbarStyle="Frosted",positionX=25,positionY=-35});
        var options=await Call("wallpaper_get_screen_options",new {screenId=screen});
        ReorderCheck(options.Data.GetProperty("positionX").GetInt32()==25&&options.Data.GetProperty("positionY").GetInt32()==-35&&options.Data.GetProperty("taskbarStyle").GetString()=="Frosted","MCP reads back taskbar style and both position axes");
        string profileBefore=JsonSerializer.Serialize(modules.Effects.ScreenProfile(screen));
        await Call("screen_set_features",new {screenId=screen,expectedRevision=store.ConfigurationRevision(),effectsEnabled=false,widgetsEnabled=false});
        ReorderCheck(!RuntimeSettingsReader.EffectsEnabled(store,screen)&&!RuntimeSettingsReader.WidgetsEnabled(store,screen),"Screen masters persist disabled state");
        ReorderCheck(JsonSerializer.Serialize(modules.Effects.ScreenProfile(screen))==profileBefore,"Master action does not alter individual effect data");
        await RestoreHistory(false);
        ReorderCheck(RuntimeSettingsReader.EffectsEnabled(store,screen)&&RuntimeSettingsReader.WidgetsEnabled(store,screen),"UI undo restores both master flags in one step");
        var kind=taskbarStylePicker.Items.Cast<ComboBoxItem>().Select(item=>item.Content?.ToString()).ToArray();
        ReorderCheck(kind.SequenceEqual(new[]{"隐藏","透明","磨砂"}),"Taskbar style remains an existing themed drop-down");
        foreach(var host in new[]{EffectTargetHelpHost,EffectOrderHelpHost,DesktopMenuHelpHost})
            ReorderCheck(host.Content is Button {Width:16,Height:16},"Shared help icon uses the compact 16 DIP presentation");
        // A synthetic poster stays in the isolated window; no desktop player is started.
        string poster=Path.Combine(output,"position-fixture.png");
        using(var bitmap=new System.Drawing.Bitmap(1600,500)) {
            using var draw=System.Drawing.Graphics.FromImage(bitmap);
            for(int i=0;i<8;i++)using(var brush=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255,40+i*25,50,220-i*20)))draw.FillRectangle(brush,i*200,0,200,500);
            bitmap.Save(poster,System.Drawing.Imaging.ImageFormat.Png);
        }
        Navigate("wallpaper");homePreviewAsset=new("position-fixture",poster,"位置预览",1,0,1600,500,"png","SDR","image");
        PosterImage.Source=new BitmapImage(new Uri(poster));EmptyPreview.Visibility=Visibility.Collapsed;
        RefreshHomePosition();await ReorderSettle();LayoutHomePreview();
        ReorderCheck(Math.Abs(HomeMonitorFrame.Width/HomeMonitorFrame.Height-SelectedScreen.Bounds.Width/(double)SelectedScreen.Bounds.Height)<.001,"Live preview frame matches selected monitor aspect");
        ReorderCheck(HomePreviewDimLeft.Width>0&&HomePreviewDimTop.Height>0,"Preview keeps a dim exterior around the target screen");
        homePositionX!.Value=-60;homePositionY!.Value=70;EndHistoryGesture();
        ReorderCheck(pendingHomePosition==null&&RuntimeSettingsReader.Position(store,screen,"position-fixture")==new WallpaperPosition(-60,70),"Slider release flushes both axes before undo gesture closes");
        await ReorderSettle();await ReorderCapture(output,"home-position");
        await RestoreHistory(false);
        ReorderCheck(RuntimeSettingsReader.Position(store,screen,"position-fixture")==new WallpaperPosition(),"Undo restores the preceding material position without leaving home");

        // Traverse hundreds of loaded controls as pointer feedback would, without
        // synthesizing mouse/keyboard input or changing foreground activation.
        var previous=HomeSettingsCard.Content;var hostPanel=new StackPanel();HomeSettingsCard.Content=hostPanel;
        var controls=Enumerable.Range(0,32).Select(i=>new Button {Content="反馈 "+i,Style=HomeStyle("UtilityButton"),Height=36}).ToArray();
        foreach(var control in controls)hostPanel.Children.Add(control);
        await ReorderSettle();
        var coordinatorProperty=(DependencyProperty)typeof(InteractionFocus).GetField("CoordinatorProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var coordinator=(InteractionFocus.Coordinator)Root.GetValue(coordinatorProperty);
        var hovered=coordinator.GetType().GetField("hovered",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var cache=(IDictionary)coordinator.GetType().GetField("feedback",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(coordinator)!;
        int maxCache=0;var durations=new List<double>();
        for(int i=0;i<320;i++) {
            var watch=Stopwatch.StartNew();hovered.SetValue(coordinator,controls[i%controls.Length]);coordinator.Refresh();watch.Stop();durations.Add(watch.Elapsed.TotalMilliseconds);maxCache=Math.Max(maxCache,cache.Count);
            if(i%16==15)await ReorderSettle(10);
        }
        ReorderCheck(maxCache<=8,"Hover feedback stays bounded across 320 control transitions");
        hovered.SetValue(coordinator,null);coordinator.SetActive(false);await ReorderSettle();
        bool allHidden=cache.Values.Cast<object>().All(item=>!(bool)item.GetType().GetField("visible",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(item)!);
        ReorderCheck(allHidden,"Inactive controls stop visible feedback");
        HomeSettingsCard.Content=previous;coordinator.SetActive(true);await ReorderSettle();
        File.WriteAllText(Path.Combine(output,"feedback-timing.json"),JsonSerializer.Serialize(new {transitions=durations.Count,maxCache,maxMilliseconds=durations.Max(),medianMilliseconds=durations.Order().ElementAt(durations.Count/2)}));
        ReorderCheck(wallpapers.Running.Count==0&&wallpapers.AllPlayersSnapshot().Count==0&&wallpapers.TaskbarProcessId==null,"UI/MCP regression never starts desktop playback or taskbar helper");
        await CheckRefinement79(output);
        await CheckRefinement80(output);
        await CheckRefinement81(output);
        await CheckRefinement82(output);
        wallpapers.Dispose();wallpapers=null;ReorderCheckIsolation();
    }
    private async Task CheckRefinement79(string output)
    {
        async Task Click(Button button) {
            var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await ReorderSettle();
        }
        var screen=SelectedScreen!.DeviceName;
        Navigate("wallpaper");RefreshHomePosition();await ReorderSettle();
        ReorderCheck(FillPicker.Items.Count==3&&FillPicker.SelectedIndex==2,"Position offsets show Custom without changing the underlying fill geometry");
        var original=RuntimeSettingsReader.Position(store,screen);
        FillPicker.SelectedIndex=1;await ReorderSettle();
        ReorderCheck(RuntimeSettingsReader.Position(store,screen)==new WallpaperPosition()&&modules.Effects.ScreenProfile(screen)!.Fill==FillMode.Contain,"Choosing a standard fill recenters in the same change");
        await RestoreHistory(false);
        ReorderCheck(RuntimeSettingsReader.Position(store,screen)==original&&FillPicker.SelectedIndex==2,"Undo restores position and custom fill presentation together");
        homePreviewAsset=new("position-fixture",Path.Combine(output,"position-fixture.png"),"位置预览",1,0,1600,500,"png","SDR","image");
        RefreshHomePosition();await ReorderSettle();
        await Click(HomePositionButton);await ReorderSettle();
        ReorderCheck(homePositionFlyout!.IsOpen,"Compact position entry opens a themed popup");
        await ReorderSettle();
        ReorderCheck(homePositionX!.ActualWidth>150,"Position popup provides direct full-width sliders");
        homePositionFlyout.Hide();await ReorderSettle();await ReorderCapture(output,"compact-home");
        Navigate("display");await ReorderSettle();
        ReorderCheck(EffectsTargetActions.Visibility==Visibility.Visible&&EffectTargetHelpHost.ActualWidth==16,"Scope help is present in the effects overview");
        await Click((Button)EffectTargetHelpHost.Content);
        ReorderCheck(activeHelp is {IsOpen:true}&&helpPinned&&((TextBlock)((ContentControl)activeHelp.Child).Content).FontSize==16,"Click-pinned details use readable 16px text and custom rounded template");
        DismissHelp();ReorderCheck(activeHelp==null&&!helpPinned,"Shared dismissal releases popup state");
        await Click((Button)EffectTargetHelpHost.Content);Navigate("wallpaper");
        ReorderCheck(activeHelp==null,"Navigation dismisses details synchronously");
        SetScreenFeatures(screen,false,false);
        ReorderCheck(EffectsEnableButton.IsEnabled&&!EffectsMasterButton.IsEnabled&&WidgetsEnableButton.IsEnabled,"Enable and disable are separate actions with correct state");
        ChangeScreenMaster(true,true);ChangeScreenMaster(false,true);
        ReorderCheck(!EffectsEnableButton.IsEnabled&&EffectsMasterButton.IsEnabled&&!WidgetsEnableButton.IsEnabled&&WidgetsMasterButton.IsEnabled,"Enabling restores the master flags and button states");
        var request=new OperationRequest("wallpaper_set_screen_options",ToolCatalog.Json(new {screenId=screen,expectedRevision=store.ConfigurationRevision(),taskbarVisual=new {frostedOpacity=62,blur=45,color="#123456",showBorder=false}}));
        var reply=await ExecuteRecordedAutomation(request,CancellationToken.None);
        ReorderCheck(!reply.IsError&&reply.Data.GetProperty("taskbarVisual").GetProperty("blur").GetInt32()==45,"MCP and UI share taskbar appearance parameters");
        ReorderCheck(modules.Effects.ScreenProfile(screen)!.Effects.GetCollection().TaskbarVisual.Color=="#123456","Taskbar tint persists in the profile");
        LoadEffectDraft(true);SyncTaskbarSettings();
        ReorderCheck(taskbarBlur.Value==45&&taskbarOpacity.Value==62&&!taskbarBorder.IsOn,"Taskbar controls restore saved values");
        // Render production controls in an isolated, bounded surface.
        var previous=HomeSettingsCard.Content;var surface=new StackPanel {Spacing=20};HomeSettingsCard.Content=surface;
        var layout=new PerformanceLayoutPicker();layout.Update(WidgetGroupFlow.Horizontal,2,8);
        ReorderCheck(layout.Children.Count==2,"Performance picker contains columns and summary only");
        var order=new OrderedTileList(true);order.UpdateArrangement(WidgetGroupFlow.Grid,2);order.Update(new[]{new OrderedTileList.Item("cpu","CPU"),new OrderedTileList.Item("ram","内存")});surface.Children.Add(layout);surface.Children.Add(order);
        await ReorderSettle();
        var previousButton=(Button)typeof(OrderedTileList).GetField("previous",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(order)!;
        var nextButton=(Button)typeof(OrderedTileList).GetField("next",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(order)!;
        ReorderCheck(previousButton.ActualWidth==44&&previousButton.ActualHeight==44&&nextButton.ActualWidth==44&&nextButton.ActualHeight==44,"Reorder arrows retain 44px square bounds");
        await ReorderCapture(output,"performance-layout");HomeSettingsCard.Content=previous;
    }

}
