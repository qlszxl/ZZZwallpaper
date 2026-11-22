using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed record PlaybackSourceRow(string? FolderId, string Label, string? PlaylistId = null)
{
    public override string ToString() => Label;
}

public sealed record PlaybackQueueRow(string Id, string Name, string Description, MediaAsset? Asset) : System.ComponentModel.INotifyPropertyChanged
{
    public int Number { get; init; }
    public Uri? PosterUri { get; init; }
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Poster=>PosterImageCache.Load(PosterUri?.LocalPath,160);
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void RefreshPoster()=>PropertyChanged?.Invoke(this,new(nameof(Poster)));
}

public sealed partial class MainWindow
{
    private static readonly PlaybackMode[] playbackModeOptions=[PlaybackMode.Automatic,PlaybackMode.SingleLoop,PlaybackMode.SingleOnce,PlaybackMode.Sequential,PlaybackMode.Shuffle];
    private bool playbackEditorReady, playbackEditorLoading, playbackActionInFlight;
    private string? playbackEditorScreen;
    private PlaybackSettings playbackDraft = new();
    private List<PlaybackQueueRow> playbackQueueRows = new();
    private Dictionary<string, MediaAsset> playbackLibraryAssets = new();
    private bool playbackPlaylistDialogOpen;

    private void InitializePlaybackEditor()
    {
        PlaybackIntervalBox.Configure(10,86400,300,5,0,"");PlaybackIntervalBox.SetLabel(UiLanguage.Text("每段播放秒数"));
        PlaybackIntervalBox.ValueChanged+=PlaybackInterval_Changed;
        playbackEditorReady = true;
        LoadPlaybackSettings();
    }

    // Called on screen/library/source changes, never from the one-second status refresh.
    private void LoadPlaybackSettings()
    {
        if (!playbackEditorReady || playbackEditorLoading || libraryShutdown) return;
        playbackEditorLoading = true;
        try
        {
            playbackEditorScreen = SelectedScreen?.DeviceName;
            playbackDraft = (playbackEditorScreen == null ? new PlaybackSettings() :
                wallpapers?.GetPlayback(playbackEditorScreen) ?? modules.Playback.Read(playbackEditorScreen)).Validate();
            playbackDraft = playbackDraft with { AssetIds = playbackDraft.AssetIds.ToList() };
            playbackLibraryAssets = modules.Library.Assets().ToDictionary(asset => asset.Id);
            RebuildPlaybackSources();
            PlaybackModePicker.SelectedIndex = Array.IndexOf(playbackModeOptions,playbackDraft.Mode);
            PlaybackTriggerPicker.SelectedIndex = (int)playbackDraft.Trigger;
            PlaybackTransitionPicker.SelectedIndex = (int)playbackDraft.Transition;
            PlaybackIntervalBox.Value = playbackDraft.IntervalSeconds;
            RebuildPlaybackQueue();
        }
        catch (Exception error)
        {
            ShowError(UiLanguage.Text("读取播放设置失败：") + error.Message,error);
        }
        finally
        {
            playbackEditorLoading = false;
            RefreshPlaybackStatus();
        }
    }

    private void RebuildPlaybackSources()
    {
        bool wasLoading = playbackEditorLoading;
        playbackEditorLoading = true;
        try
        {
            var sources = (playbackDraft.CustomPlaylists ?? []).Select(list =>
                new PlaybackSourceRow(null, UiLanguage.Text("自定义 · ") + list.Name, list.Id)).ToList();
            sources.AddRange(modules.Library.Folders().Select(folder => new PlaybackSourceRow(folder.Id, UiLanguage.Text("文件夹 · ") + folder.Path)));
            if (playbackDraft.FolderId is { } missing && sources.All(source => source.FolderId != missing))
                sources.Add(new(missing, UiLanguage.Text("文件夹引用已移除")));
            PlaybackSourcePicker.ItemsSource = sources;
            PlaybackSourcePicker.SelectedItem = sources.FirstOrDefault(source => playbackDraft.FolderId != null
                ? source.FolderId == playbackDraft.FolderId
                : source.FolderId == null && source.PlaylistId == playbackDraft.PlaylistId);
        }
        finally { playbackEditorLoading = wasLoading; }
    }

    private void RebuildPlaybackQueue(string? selectId = null)
    {
        selectId ??= (PlaybackQueueList.SelectedItem as PlaybackQueueRow)?.Id;
        var entries = playbackDraft.FolderId is { } folder
            ? modules.Library.FolderAssets(folder).OrderBy(asset => asset.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase).Select(asset => (asset.Id, Asset: (MediaAsset?)asset)).ToList()
            : playbackDraft.AssetIds.Select(id => (Id: id, Asset: playbackLibraryAssets.GetValueOrDefault(id))).ToList();
        playbackQueueRows = entries.Select((entry, index) => {
            var asset = entry.Asset;
            string? poster = asset == null ? null : modules.Library.Poster(asset.Id);
            bool sourceAvailable = asset != null && File.Exists(asset.Path);
            return new PlaybackQueueRow(entry.Id, asset?.Name ?? UiLanguage.Text("库内引用已移除"),
                asset == null ? UiLanguage.Text("素材不可用") :
                (sourceAvailable ? "" : UiLanguage.Text("来源暂不可用 · ")) + LibraryRow.Describe(asset,sourceAvailable), asset)
            {
                Number = index + 1,
                // Images are decoded at thumbnail size only as list rows are realized.
                PosterUri = poster != null ? new Uri(Path.GetFullPath(poster)) : null
            };
        }).ToList();
        QueueButtonLabel.Text = UiLanguage.Format($"播放队列  {playbackQueueRows.Count}");
        PlaybackQueueList.ItemsSource = playbackQueueRows;
        PlaybackQueueList.SelectedItem = playbackQueueRows.FirstOrDefault(row => row.Id == selectId);
        int invalid = playbackQueueRows.Count(row => row.Asset == null);
        PlaybackQueueSummary.Text = UiLanguage.Format($"{playbackQueueRows.Count} 个素材") + (invalid > 0 ? UiLanguage.Format($" · {invalid} 项失效") : "");
        PlaybackQueueSummary.Visibility = playbackQueueRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaybackQueueEmpty.Visibility = playbackQueueRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool CanEditPlayback() => playbackEditorReady && !playbackEditorLoading && !libraryShutdown &&
        !playbackActionInFlight && applying == 0 && playbackEditorScreen != null && SelectedScreen?.DeviceName == playbackEditorScreen;

    private bool PersistPlaybackSettings(bool rebuildQueue = false, string? selectId = null)
    {
        if (!CanEditPlayback()) return false;
        try
        {
            playbackDraft = playbackDraft.Validate();
            if (wallpapers != null) wallpapers.ConfigurePlayback(playbackEditorScreen!, playbackDraft);
            else modules.Playback.Save(playbackEditorScreen!, playbackDraft);
            if (rebuildQueue) { RebuildPlaybackSources(); RebuildPlaybackQueue(selectId); }
            RefreshPlaybackStatus();
            return true;
        }
        catch (Exception error)
        {
            ShowError(UiLanguage.Text("保存播放设置失败：") + error.Message,error);
            LoadPlaybackSettings();
            return false;
        }
    }

    private void PlaybackOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanEditPlayback() || PlaybackModePicker.SelectedIndex < 0 || PlaybackModePicker.SelectedIndex >= playbackModeOptions.Length || PlaybackTriggerPicker.SelectedIndex < 0) return;
        playbackDraft = playbackDraft with
        {
            Mode = playbackModeOptions[PlaybackModePicker.SelectedIndex],
            Trigger = (PlaybackTrigger)PlaybackTriggerPicker.SelectedIndex
        };
        PersistPlaybackSettings();
    }

    private void PlaybackInterval_Changed(double value)
    {
        if (!CanEditPlayback() || !double.IsFinite(value)) return;
        var seconds = (int)Math.Clamp(Math.Round(value), 10, 86400);
        if (seconds == playbackDraft.IntervalSeconds) return;
        playbackDraft = playbackDraft with { IntervalSeconds = seconds };
        PersistPlaybackSettings();
    }

    private void PlaybackTransition_Changed(object sender, SelectionChangedEventArgs e)
    {
        if(!CanEditPlayback()||PlaybackTransitionPicker.SelectedIndex is <0 or >1)return;
        playbackDraft=playbackDraft with {Transition=(PlaybackTransition)PlaybackTransitionPicker.SelectedIndex};
        PersistPlaybackSettings();
    }

    private void PlaybackSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanEditPlayback() || PlaybackSourcePicker.SelectedItem is not PlaybackSourceRow source) return;
        if (source.FolderId != null) playbackDraft = playbackDraft with { FolderId = source.FolderId };
        else
        {
            var playlist = playbackDraft.CustomPlaylists?.FirstOrDefault(list => list.Id == source.PlaylistId);
            if (playlist == null) return;
            playbackDraft = playbackDraft with { FolderId = null, PlaylistId = playlist.Id, AssetIds = playlist.AssetIds.ToList() };
        }
        PersistPlaybackSettings(true);
    }

    private async void PlaybackNewPlaylist_Click(object sender, RoutedEventArgs e) => await EditPlaybackPlaylistName(true);
    private async void PlaybackRenamePlaylist_Click(object sender, RoutedEventArgs e) => await EditPlaybackPlaylistName(false);

    private async Task EditPlaybackPlaylistName(bool create)
    {
        if (!CanEditPlayback() || playbackPlaylistDialogOpen) return;
        var current = playbackDraft.CustomPlaylists?.FirstOrDefault(list => list.Id == playbackDraft.PlaylistId);
        if (!create && (playbackDraft.FolderId != null || current == null)) return;
        string? screen = playbackEditorScreen;
        long revision = store.HistoryRevision;
        var name = new TextBox { Style = HomeStyle("UtilityTextBox"), Text = create ? "" : current!.Name, MaxLength = 64, PlaceholderText = UiLanguage.Text("播放列表名称") };
        // A modal naming operation is already an explicit request to edit text.
        DoubleClickEdit.SetEnabled(name, false);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, UiLanguage.Text("播放列表名称"));
        var errorText = UtilityText(""); errorText.Visibility = Visibility.Collapsed;
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(WidgetField(UiLanguage.Text("名称"), name)); fields.Children.Add(errorText);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = create ? UiLanguage.Text("新建播放列表") : UiLanguage.Text("重命名播放列表"), Content = fields,
            PrimaryButtonText = create ? UiLanguage.Text("创建") : UiLanguage.Text("保存"), CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Primary, Style = HomeStyle("LibraryDialog") };
        void ValidateName()
        {
            string value = name.Text.Trim();
            bool duplicate = (playbackDraft.CustomPlaylists ?? []).Any(list => (create || list.Id != current!.Id) && string.Equals(list.Name, value, StringComparison.CurrentCultureIgnoreCase));
            dialog.IsPrimaryButtonEnabled = value.Length > 0 && !duplicate;
            errorText.Text = duplicate ? UiLanguage.Text("已有同名播放列表") : "";
            errorText.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;
        }
        name.TextChanged += (_, _) => ValidateName(); ValidateName();
        dialog.Opened += (_, _) => { name.Focus(FocusState.Programmatic); name.SelectAll(); };
        playbackPlaylistDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !CanEditPlayback() || playbackEditorScreen != screen) return;
            if (store.HistoryRevision != revision) { Inform(UiLanguage.Text("播放设置已更新，请重新操作。")); return; }
            string value = name.Text.Trim();
            if (value.Length == 0 || (playbackDraft.CustomPlaylists ?? []).Any(list => (create || list.Id != current!.Id) && string.Equals(list.Name, value, StringComparison.CurrentCultureIgnoreCase))) return;
            var lists = (playbackDraft.CustomPlaylists ?? []).ToList();
            if (create)
            {
                var playlist = new PlaybackPlaylist { Id = Guid.NewGuid().ToString("N"), Name = value, AssetIds = [] };
                lists.Add(playlist);
                playbackDraft = playbackDraft with { CustomPlaylists = lists, PlaylistId = playlist.Id, FolderId = null, AssetIds = [] };
            }
            else
            {
                lists = lists.Select(list => list.Id == current!.Id ? list with { Name = value } : list).ToList();
                playbackDraft = playbackDraft with { CustomPlaylists = lists };
            }
            using var history = store.BeginUndoBatch(create ? "创建播放列表" : "重命名播放列表");
            if (PersistPlaybackSettings(true)) Inform(create ? UiLanguage.Text("播放列表已创建") : UiLanguage.Text("播放列表已重命名"));
        }
        catch (Exception error) { if (!libraryShutdown) ShowError(UiLanguage.Text("保存播放列表失败：") + error.Message, error); }
        finally { playbackPlaylistDialogOpen = false; if (!libraryShutdown) PlaybackSourcePicker.Focus(FocusState.Programmatic); }
    }

    private async void PlaybackDeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlayback() || playbackPlaylistDialogOpen || playbackDraft.FolderId != null) return;
        var current = playbackDraft.CustomPlaylists?.FirstOrDefault(list => list.Id == playbackDraft.PlaylistId);
        if (current == null) return;
        string? screen = playbackEditorScreen; long revision = store.HistoryRevision;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = UiLanguage.Text("删除播放列表？"), Content = UiLanguage.Format($"删除“{current.Name}”。素材库和原文件保留。"),
            PrimaryButtonText = UiLanguage.Text("删除列表"), CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Close, Style = HomeStyle("LibraryDialog") };
        playbackPlaylistDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !CanEditPlayback() || playbackEditorScreen != screen) return;
            if (store.HistoryRevision != revision) { Inform(UiLanguage.Text("播放设置已更新，请重新操作。")); return; }
            var lists = playbackDraft.CustomPlaylists!.Where(list => list.Id != current.Id).ToList();
            var next = lists.FirstOrDefault();
            playbackDraft = playbackDraft with { CustomPlaylists = lists, PlaylistId = next?.Id, AssetIds = next?.AssetIds.ToList() ?? [] };
            using var history = store.BeginUndoBatch("删除播放列表");
            if (PersistPlaybackSettings(true)) Inform(UiLanguage.Text("播放列表已删除"));
        }
        catch (Exception error) { if (!libraryShutdown) ShowError(UiLanguage.Text("删除播放列表失败：") + error.Message, error); }
        finally { playbackPlaylistDialogOpen = false; if (!libraryShutdown) PlaybackSourcePicker.Focus(FocusState.Programmatic); }
    }

    private void PlaybackQueue_Changed(object sender, SelectionChangedEventArgs e) => RefreshPlaybackStatus();

    private void PlaybackAddSelected_Click(object sender, RoutedEventArgs e)
    {
        var assets=SelectedLibraryAssets();
        if(assets.Count==0&&navigationPage!="library"&&selected!=null)assets.Add(selected);
        if(assets.Count==0) {Inform(UiLanguage.Text("先在素材库选择要加入的素材。"));return;}
        AddPlaybackAssets(assets);
    }

    private void PlaybackAddAll_Click(object sender, RoutedEventArgs e) => AddPlaybackAssets(modules.Library.Assets());

    private void AddPlaybackAssets(IEnumerable<MediaAsset> assets)
    {
        if (!CanEditPlayback()) return;
        var additions = assets.ToList();
        if (additions.Count == 0) { Inform(UiLanguage.Text("素材库为空")); return; }
        var lists = (playbackDraft.CustomPlaylists ?? []).ToList();
        var target = lists.FirstOrDefault(list => list.Id == playbackDraft.PlaylistId) ?? lists.FirstOrDefault();
        if (target == null)
        {
            target = new PlaybackPlaylist { Id = Guid.NewGuid().ToString("N"), Name = UiLanguage.Text("自定义播放列表"), AssetIds = [] };
            lists.Add(target);
        }
        var ids = target.AssetIds.ToList();
        foreach (var asset in additions)
        {
            playbackLibraryAssets[asset.Id] = asset;
            if (!ids.Contains(asset.Id)) ids.Add(asset.Id);
        }
        bool wasFolder = playbackDraft.FolderId != null;
        playbackDraft = playbackDraft with { FolderId = null, PlaylistId = target.Id, CustomPlaylists = lists, AssetIds = ids };
        if (PersistPlaybackSettings(true, additions[0].Id))
            Inform(UiLanguage.Format($"已加入{(wasFolder?UiLanguage.Text("自定义播放列表"):UiLanguage.Text("播放列表"))}"));
    }

    private void PlaybackRemove_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlayback() || playbackDraft.FolderId != null || PlaybackQueueList.SelectedItem is not PlaybackQueueRow row) return;
        playbackDraft = playbackDraft with { AssetIds = playbackDraft.AssetIds.Where(id => id != row.Id).ToList() };
        PersistPlaybackSettings(true);
    }

    private void PlaybackUp_Click(object sender, RoutedEventArgs e) => MovePlaybackItem(-1);
    private void PlaybackDown_Click(object sender, RoutedEventArgs e) => MovePlaybackItem(1);

    private void MovePlaybackItem(int direction)
    {
        if (!CanEditPlayback() || playbackDraft.FolderId != null || PlaybackQueueList.SelectedItem is not PlaybackQueueRow row) return;
        var ids = playbackDraft.AssetIds.ToList();
        int current = ids.IndexOf(row.Id), next = current + direction;
        if (current < 0 || next < 0 || next >= ids.Count) return;
        (ids[current], ids[next]) = (ids[next], ids[current]);
        playbackDraft = playbackDraft with { AssetIds = ids };
        PersistPlaybackSettings(true, row.Id);
    }

    private void PlaybackClear_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlayback() || playbackDraft.FolderId != null) return;
        playbackDraft = playbackDraft with { AssetIds = new() };
        if (PersistPlaybackSettings(true)) Inform(UiLanguage.Text("播放列表已清空"));
    }

    private void PlaybackClearInvalid_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlayback() || playbackDraft.FolderId != null) return;
        try
        {
            // Resolve against the current library. A temporarily disconnected
            // disk is not a deleted library reference and must retain its place.
            playbackLibraryAssets = modules.Library.Assets().ToDictionary(asset => asset.Id);
            var retained = playbackDraft.AssetIds.Where(playbackLibraryAssets.ContainsKey).ToList();
            int removed = playbackDraft.AssetIds.Count - retained.Count;
            if (removed == 0) { RebuildPlaybackQueue(); RefreshPlaybackStatus(); return; }
            playbackDraft = playbackDraft with { AssetIds = retained };
            if (PersistPlaybackSettings(true)) Inform(UiLanguage.Format($"已清除 {removed} 个失效队列项。"));
        }
        catch (Exception error)
        {
            ShowError(UiLanguage.Text("清除失效队列项失败：") + error.Message, error);
            LoadPlaybackSettings();
        }
    }

    private async void PlayQueue_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlayback() || wallpapers == null || SelectedScreen == null || applying > 0) return;
        var screen = SelectedScreen;
        bool started = false;
        try
        {
            var available = playbackQueueRows.Where(row => row.Asset != null && File.Exists(row.Asset.Path)).Select(row => row.Asset!).ToList();
            if (available.Count == 0) { Inform(UiLanguage.Text("列表中没有可用素材")); return; }
            var asset = playbackDraft.Mode == PlaybackMode.Shuffle ? available[Random.Shared.Next(available.Count)] : available[0];
            EndEffectEditing();
            var profile = modules.Effects.EffectiveProfile(screen.DeviceName, asset.Id);
            var current = wallpapers.Running.GetValueOrDefault(screen.DeviceName);
            var fill = profile?.Fill ?? current?.Binding.Fill ?? DraftFill;
            var effects = profile?.Effects ?? current?.Binding.Effects ?? DraftEffects;
            long cancelEpoch = wallpapers.CancellationEpoch;
            followWallpaperSelection=true;
            playbackActionInFlight = true; applying++; started = true; BusyRing.IsActive = true; RefreshStatus();
            await wallpapers.Apply(asset, screen, fill, false, effects);
            if (!libraryShutdown && cancelEpoch == wallpapers.CancellationEpoch)
                Inform(UiLanguage.Format($"{screen.DeviceName.Replace(@"\\.\DISPLAY", UiLanguage.Text("屏幕 "))} · 已开始播放列表"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!libraryShutdown) ShowError(UiLanguage.Text("启动播放列表失败，原壁纸已保留。") + error.Message,error); }
        finally
        {
            if (started)
            {
                playbackActionInFlight = false; applying--;
                if (!libraryShutdown) { BusyRing.IsActive = applying > 0 || importLifetime.Active != null; RefreshStatus(); }
            }
        }
    }

    private async void PlaybackPrevious_Click(object sender, RoutedEventArgs e) => await StepPlaybackFromEditor(-1);
    private async void PlaybackNext_Click(object sender, RoutedEventArgs e) => await StepPlaybackFromEditor(1);

    private async Task StepPlaybackFromEditor(int direction)
    {
        if (!CanEditPlayback() || wallpapers == null || SelectedScreen == null || applying > 0) return;
        string screen = SelectedScreen.DeviceName;
        if (!wallpapers.Running.ContainsKey(screen)) return;
        followWallpaperSelection=true;
        playbackActionInFlight = true; applying++; BusyRing.IsActive = true; RefreshStatus();
        try { EndEffectEditing(); await wallpapers.StepPlayback(screen, direction); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!libraryShutdown) ShowError(UiLanguage.Text("切换素材失败，当前壁纸已保留。") + error.Message,error); }
        finally
        {
            playbackActionInFlight = false; applying--;
            if (!libraryShutdown) { BusyRing.IsActive = applying > 0 || importLifetime.Active != null; RefreshStatus(); }
        }
    }

    private void RefreshPlaybackStatus()
    {
        if (!playbackEditorReady || playbackEditorLoading || libraryShutdown) return;
        string? screen = SelectedScreen?.DeviceName;
        var current = screen == null ? null : wallpapers?.Running.GetValueOrDefault(screen);
        bool ready = screen != null && screen == playbackEditorScreen;
        bool busy = playbackActionInFlight || applying > 0;
        var resolvedMode=playbackDraft.ResolveMode(playbackQueueRows.Count(row=>row.Asset!=null));
        bool rotation = resolvedMode is PlaybackMode.Sequential or PlaybackMode.Shuffle;
        bool custom = playbackDraft.FolderId == null;
        PlaybackModePicker.IsEnabled = ready && !busy;
        PlaybackSourcePicker.IsEnabled = ready && !busy;
        PlaybackNewPlaylistButton.IsEnabled = ready && !busy;
        PlaybackTransitionPicker.IsEnabled = ready && !busy;
        PlaybackTriggerPicker.IsEnabled = ready && !busy && rotation;
        PlaybackIntervalBox.IsEnabled = rotation && !busy;
        bool editCustom = ready && !busy && custom;
        bool hasCustom = playbackDraft.CustomPlaylists?.Any(list => list.Id == playbackDraft.PlaylistId) == true;
        PlaybackRenamePlaylistButton.IsEnabled = PlaybackDeletePlaylistButton.IsEnabled = editCustom && hasCustom;
        PlaybackAddSelectedButton.IsEnabled = editCustom && (selected != null || LibraryList.SelectedItems.Count > 0);
        PlaybackAddAllButton.IsEnabled = editCustom;
        int selectedIndex = PlaybackQueueList.SelectedIndex;
        PlaybackRemoveButton.IsEnabled = editCustom && selectedIndex >= 0;
        PlaybackUpButton.IsEnabled = editCustom && selectedIndex > 0;
        PlaybackDownButton.IsEnabled = editCustom && selectedIndex >= 0 && selectedIndex < playbackQueueRows.Count - 1;
        PlaybackClearButton.IsEnabled = editCustom && playbackQueueRows.Count > 0;
        PlaybackClearInvalidButton.IsEnabled = editCustom && playbackQueueRows.Any(row => row.Asset == null);
        PlayQueueButton.IsEnabled = ready && !busy && wallpapers != null && playbackQueueRows.Any(row => row.Asset != null);
        HomePreviousButton.IsEnabled = HomeNextButton.IsEnabled = PlaybackPreviousButton.IsEnabled = PlaybackNextButton.IsEnabled = ready && !busy && current != null && rotation && playbackQueueRows.Count > 0;
        // Disabling the clicked action moves native focus to its neighbour.
        // ApplySelection already rejects repeated requests while applying; keep
        // both apply controls focusable throughout that asynchronous operation.
        bool canApply = selected != null && ready && wallpapers != null;
        ApplyButton.IsEnabled = HomeApplyScopeButton.IsEnabled = canApply;
        string mode = resolvedMode switch
        {
            PlaybackMode.SingleOnce => UiLanguage.Text("单次播放"),
            PlaybackMode.Sequential => UiLanguage.Text("顺序轮播"),
            PlaybackMode.Shuffle => UiLanguage.Text("随机轮播"),
            _ => UiLanguage.Text("单个循环")
        };
        if(playbackDraft.Mode==PlaybackMode.Automatic)mode=UiLanguage.Text("自动 · ")+mode;
        PlaybackCurrentText.Text = current == null ? "" : $"{current.Player.ActualState} · {current.Asset.Name}";
        PlaybackCurrentText.Visibility = current == null ? Visibility.Collapsed : Visibility.Visible;
        PlaybackPlanStatus.Text = !ready ? UiLanguage.Text("未选择屏幕") : busy ? UiLanguage.Text("正在准备素材…") :
            playbackQueueRows.Count==0 ? "" : rotation&&playbackDraft.Trigger!=PlaybackTrigger.EndOfVideo
                ? $"{mode} · {UiLanguage.Format($"每 {playbackDraft.IntervalSeconds} 秒切换")}" : mode;
        PlaybackPlanStatus.Visibility = PlaybackPlanStatus.Text.Length==0 ? Visibility.Collapsed : Visibility.Visible;
        PlaybackEffectTargetText.Text = screen==null?UiLanguage.Text("未选择屏幕"):
            UiLanguage.Format($"当前播放：{current?.Asset.Name ?? UiLanguage.Text("系统壁纸")}");
    }
}
