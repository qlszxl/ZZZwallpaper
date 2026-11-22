using LumaWall.Contracts;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool historyBusy;
    private bool? undoSegmentsVertical;
    private void InitializeUndo()
    {
        UndoSegments.SizeChanged+=(_,e)=>LayoutUndoSegments(e.NewSize.Width);
        LayoutUndoSegments(UndoSegments.ActualWidth);
        store.HistoryChanged+=RefreshUndo;
        Root.Loaded+=(_,_)=>store.HistoryEnabled=true;
        // Release/numeric boundaries commit a gesture. Flushing again on every
        // press blocks ordinary clicks on a preceding background transaction
        // and needlessly submits the first value of the new slider drag.
        Root.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=> {if(!IsNumericInputSource(e.OriginalSource)&&!NumericInputFocused())EndHistoryGesture();}),true);
        Root.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,e)=> {if(!IsNumericInputSource(e.OriginalSource)&&!NumericInputFocused())EndHistoryGesture();}),true);
        void NumericBoundary(){if(historyBusy)return;FlushHomePosition();flushStartupSettings?.Invoke();widgets?.FlushPendingSave();FlushEffectChanges(background:true);store.BreakUndoGesture();}
        ScrubNumberInput.SetTransaction(ShellViewport,NumericBoundary,NumericBoundary);
        Root.PreviewKeyDown+=(_,e)=> {
            if(e.Handled||historyBusy||!HistoryShortcutAllowed())return;
            bool control=InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            bool shift=InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if(!control||e.Key is not (VirtualKey.Z or VirtualKey.Y))return;
            e.Handled=true;_=RunUndo(e.Key==VirtualKey.Y||shift);
        };
        UndoButton.Click+=async(_,_)=>await RunUndo(false);
        RedoButton.Click+=async(_,_)=>await RunUndo(true);
        Closed+=(_,_)=> {store.HistoryChanged-=RefreshUndo;store.HistoryEnabled=false;};
        RefreshUndo();
    }
    private void LayoutUndoSegments(double width)
    {
        if(width<=0)return;
        bool vertical=width<88;if(undoSegmentsVertical==vertical)return;undoSegmentsVertical=vertical;
        UndoSegments.ColumnDefinitions.Clear();UndoSegments.RowDefinitions.Clear();
        if(vertical) {
            UndoSegments.ColumnDefinitions.Add(new());
            UndoSegments.RowDefinitions.Add(new() {Height=new GridLength(44)});
            UndoSegments.RowDefinitions.Add(new() {Height=new GridLength(1)});
            UndoSegments.RowDefinitions.Add(new() {Height=new GridLength(44)});
        } else {
            UndoSegments.ColumnDefinitions.Add(new());
            UndoSegments.ColumnDefinitions.Add(new() {Width=new GridLength(1)});
            UndoSegments.ColumnDefinitions.Add(new());
            UndoSegments.RowDefinitions.Add(new() {Height=new GridLength(44)});
        }
        Grid.SetRow(UndoSegmentDivider,vertical?1:0);Grid.SetColumn(UndoSegmentDivider,vertical?0:1);
        Grid.SetRow(RedoButton,vertical?2:0);Grid.SetColumn(RedoButton,vertical?0:2);
        UndoButton.CornerRadius=vertical?new(12,12,0,0):new(12,0,0,12);
        RedoButton.CornerRadius=vertical?new(0,0,12,12):new(0,12,12,0);
        UndoSegmentDivider.Margin=vertical?new(8,0,8,0):new(0,8,0,8);
    }
    private bool HistoryShortcutAllowed()
    {
        if(!EffectsBodyHost.IsEnabled||VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p=>p.IsOpen&&p.Tag as string!="numeric-input-hint"))return false;
        for(var node=FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is TextBox or PasswordBox or NumberBox or ContentDialog)return false;
        return true;
    }
    private void FlushHistoryEdits() {FlushHomePosition();flushStartupSettings?.Invoke();CommitSpectrumInput();FlushEffectChanges();widgets?.FlushPendingSave();}
    private static bool IsNumericInputSource(object? source)
    {
        for(var node=source as DependencyObject;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is ScrubNumberInput||node is TextBox text&&DoubleClickEdit.GetEnabled(text))return true;
        return false;
    }
    private bool NumericInputFocused()=>IsNumericInputSource(FocusManager.GetFocusedElement(Root.XamlRoot));
    private void BeginHistoryGesture() {if(historyBusy)return;FlushHistoryEdits();store.BreakUndoGesture();}
    private void EndHistoryGesture() {if(historyBusy)return;FlushHomePosition();flushStartupSettings?.Invoke();CommitSpectrumInput();widgets?.FlushPendingSave();FlushEffectChanges(background:true);store.BreakUndoGesture();}
    private void RefreshUndo()
    {
        RefreshPresetUndo();
        UndoButton.IsEnabled=!historyBusy&&store.UndoLabel!=null;RedoButton.IsEnabled=!historyBusy&&store.RedoLabel!=null;
        ToolTipService.SetToolTip(UndoButton,store.UndoLabel is { } undo?UiLanguage.Text("撤销：")+undo+"（Ctrl+Z）":UiLanguage.Text("撤销（Ctrl+Z）"));
        ToolTipService.SetToolTip(RedoButton,store.RedoLabel is { } redo?UiLanguage.Text("重做：")+redo+"（Ctrl+Y）":UiLanguage.Text("重做（Ctrl+Y）"));
    }
    private async Task RunUndo(bool redo)
    {
        try {await RestoreHistory(redo);}
        catch(Exception ex) {ShowError((redo?UiLanguage.Text("重做"):UiLanguage.Text("撤销"))+UiLanguage.Text("未完成：")+ex.Message,ex);}
    }
    private async Task<string> RestoreHistory(bool redo,string? expectedRevision=null)
    {
        if(historyBusy)throw new InvalidOperationException(UiLanguage.Text("正在恢复操作，请稍后重试。"));
        FlushHistoryEdits();
        var operation=OperationTrace.Current??OperationTrace.Begin(navigationPage,redo?"history_redo":"history_undo");
        using var trace=OperationTrace.Enter(operation);
        historyBusy=true;RefreshUndo();bool bodyEnabled=EffectsBodyHost.IsEnabled;EffectsBodyHost.IsEnabled=false;
        try {
            var result=modules.History.Restore(redo,expectedRevision);
            // Invalidate drafts before a refresh can persist an old editor value.
            effectSession.ClearRetained();automationUndo.Clear();scopedEffectUndo.Clear();
            if(result.Settings.Contains("widgets.v1"))widgets?.ReloadHistoryState();
            if(result.Settings.Any(key=>key.StartsWith("screen.widgets.enabled:",StringComparison.Ordinal)))widgets?.Refresh();
            foreach(string key in result.Settings.Where(key=>key.StartsWith("screen.effects.enabled:",StringComparison.Ordinal)))wallpapers?.RefreshScreenEffectsState(key["screen.effects.enabled:".Length..]);
            foreach(string key in result.Settings.Where(key=>key.StartsWith("wallpaper.position:",StringComparison.Ordinal)))wallpapers?.RefreshPosition(key["wallpaper.position:".Length..].Split('|')[0]);
            RefreshHomePosition();
            if(result.Settings.Contains(AudioAutoAdaptKey))SyncAudioProcessingSettings();
            if(result.Settings.Any(key=>key.StartsWith("startup.delay.",StringComparison.Ordinal))) {
                StartupRegistration.SetEnabled(StartupRegistration.IsEnabled(),RuntimeSettingsReader.StartupDelay(store).EffectiveSeconds);syncStartupSettings?.Invoke();
            }
            RefreshPresetUndo();
            if(result.Tables.Contains("media")||result.Tables.Contains("folder_member")) {RefreshFolders();RefreshLibrary();}
            LoadEffectDraft(true,preserveView:true);LoadPlaybackSettings();
            if(result.Settings.Contains(RuntimeSettingsReader.PauseKey(LumaWall.Contracts.PauseReason.HighResourceLoad)))SyncResourceLoadPauseSettings();
            var warnings=new List<string>();
            if(wallpapers!=null) {
                foreach(string screenId in result.Settings.Where(k=>k.StartsWith("videoVolume:",StringComparison.Ordinal)||k.StartsWith("videoSound:",StringComparison.Ordinal))
                    .Select(k=>k[(k.IndexOf(':')+1)..]).Distinct())wallpapers.ReloadVideoSound(screenId);
                foreach(string key in result.Settings.Where(k=>k.StartsWith("playback:",StringComparison.Ordinal)))wallpapers.ReloadPlayback(key["playback:".Length..]);
                foreach(string screenId in result.Screens) {
                    try {
                        var binding=store.Bindings().FirstOrDefault(b=>b.ScreenId==screenId);
                        if(binding==null) {wallpapers.CancelWallpaper(screenId);continue;}
                        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==screenId);
                        var asset=modules.Library.Assets().FirstOrDefault(a=>a.Id==binding.AssetId);
                        if(screen==null||asset==null)throw new IOException(UiLanguage.Text("显示器或壁纸素材已不可用，配置已恢复但画面未同步。"));
                        await wallpapers.Apply(asset,screen,binding.Fill,binding.UserPaused,saveProfile:false);
                    } catch(Exception ex) {
                        try {wallpapers.CancelWallpaper(screenId,preserveBinding:true);}catch(Exception stopError) {AppLog.Warning("History","Could not stop stale wallpaper",stopError);}
                        warnings.Add(ex.Message);
                    }
                }
                try {wallpapers.RefreshLiveEffects(result.Effects);}catch(Exception ex) {warnings.Add(ex.Message);}
            }
            RefreshStatus();string message=(redo?UiLanguage.Text("已重做："):UiLanguage.Text("已撤销："))+result.Label;
            if(warnings.Count>0)message+=UiLanguage.Text("；配置已恢复，画面同步未完成：")+string.Join("；",warnings);
            OperationTrace.Record(operation,"history_refresh",warnings.Count>0?"partially_completed":"completed",string.Join("; ",warnings));
            Inform(message);return message;
        } catch(Exception error) {OperationTrace.Fault(operation,"history_replay",error.ToString());throw;}
        finally {historyBusy=false;EffectsBodyHost.IsEnabled=bodyEnabled;RefreshUndo();}
    }
}
