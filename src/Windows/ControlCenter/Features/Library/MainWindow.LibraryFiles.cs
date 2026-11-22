using LumaWall.Application;
using LumaWall.Contracts;
using LumaWall.Contracts.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private ILibrarySourceFileOperations librarySourceFiles = new WindowsLibrarySourceFileOperations();
    private ContentDialog? libraryFileDialog;
    private CheckBox? libraryPermanentDelete;
    private bool libraryFileBusy;
    private bool LibraryFileOperationAvailable => !libraryShutdown && !libraryFileBusy &&
        importLifetime.Active == null && !historyBusy && !presetBusy && applying == 0;

    private SteamWallpaperSource? LibraryFileSteamSource(MediaAsset asset) =>
        LibrarySourceFiles.ReadSteamSource(modules.Settings.Get(SteamSourceIdentity.SettingKey(asset.Id)));

    private void RefreshLibraryFileActions(IReadOnlyList<MediaAsset> assets)
    {
        bool available = assets.Count == 1 && LibraryFileOperationAvailable;
        LibraryRevealSourceButton.IsEnabled = available;
        LibraryDeleteSourceButton.IsEnabled = available &&
            (LibraryList.SelectedItems.OfType<LibraryRow>().FirstOrDefault(row=>row.Asset.Id==assets[0].Id)?.SourceAvailable!=false);
        bool managed = false;
        if (assets.Count == 1)
            try { managed = SteamSourceIdentity.IsSteamManagedOriginal(assets[0].Path, LibraryFileSteamSource(assets[0])); }
            catch (IOException) { LibraryDeleteSourceButton.IsEnabled = false; }
        LibraryDeleteSourceButton.Content = managed ? UiLanguage.Text("在 Steam 中管理") : UiLanguage.Text("删除源文件");
        AutomationProperties.SetName(LibraryDeleteSourceButton, managed ? UiLanguage.Text("在 Steam 中管理") : UiLanguage.Text("删除源文件"));
    }

    private void LibraryRevealSource_Click(object sender, RoutedEventArgs e)
    {
        if (!LibraryFileOperationAvailable || SelectedLibraryAssets() is not { Count: 1 } assets) return;
        try { librarySourceFiles.Reveal(assets[0].Path); }
        catch (Exception error) { ShowError(UiLanguage.Text("无法定位源文件：") + error.Message, error); }
    }

    private async void LibraryDeleteSource_Click(object sender, RoutedEventArgs e) => await DeleteSelectedLibrarySourceAsync();

    private async Task DeleteSelectedLibrarySourceAsync()
    {
        if (!LibraryFileOperationAvailable || SelectedLibraryAssets() is not { Count: 1 } assets) return;
        var asset = assets[0]; LibrarySourceFile? snapshot = null; bool attempted = false;
        libraryFileBusy = true; RefreshLibraryFileActions(assets); RefreshFolderControls(); RefreshNativeLibraryDrop();
        try
        {
            var source = LibraryFileSteamSource(asset);
            if (SteamSourceIdentity.IsSteamManagedOriginal(asset.Path, source))
            {
                libraryFileDialog = new ContentDialog {
                    XamlRoot = Root.XamlRoot, Title = UiLanguage.Text("由 Steam 管理的原文件"), Style = HomeStyle("LibraryDialog"),
                    Content = new TextBlock { Text = UiLanguage.Text("请在 Steam 中管理此项目。\n\n") + asset.Path,
                        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 480 },
                    PrimaryButtonText = UiLanguage.Text("在 Steam 中管理"), SecondaryButtonText = UiLanguage.Text("打开文件夹"), CloseButtonText = UiLanguage.Text("取消"),
                    DefaultButton = ContentDialogButton.Close
                };
                AutomationProperties.SetAutomationId(libraryFileDialog, "LibrarySteamManagedDialog");
                var choice = await libraryFileDialog.ShowAsync();
                if (libraryShutdown) return;
                if (choice == ContentDialogResult.Primary) librarySourceFiles.ManageSteam(asset.Path, source?.WorkshopId);
                else if (choice == ContentDialogResult.Secondary) librarySourceFiles.Reveal(asset.Path);
                return;
            }
            snapshot = LibrarySourceFiles.Inspect(asset.Path);
            var references = await Task.Run(() => modules.Presets.FindFileReferences(snapshot.Path));
            if (libraryShutdown || importLifetime.Active != null) return;
            var same = modules.Library.Assets().Where(a => LibrarySourceFiles.SamePath(a.Path, snapshot.Path)).ToArray();
            var ids = same.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            var screens = store.Bindings().Where(b => ids.Contains(b.AssetId)).Select(b => b.ScreenId)
                .Concat(wallpapers?.Running.Where(p => LibrarySourceFiles.SamePath(p.Value.Asset.Path, snapshot.Path)).Select(p => p.Key) ?? [])
                .Distinct().ToArray();
            string presetText = references.Presets.Length == 0 ? "" :
                UiLanguage.Format($"删除后，{references.Presets.Length} 个预设将缺少此素材：\n") +
                string.Join("、", references.Presets.Take(6).Select(p => p.Name)) + (references.Presets.Length > 6 ? UiLanguage.Text(" 等") : "");
            if (references.UnreadablePresets != 0) presetText += UiLanguage.Text("\n部分预设未能检查，可能存在其他引用。");
            var body = new StackPanel { Spacing = 12, MaxWidth = 480 };
            body.Children.Add(new TextBlock { Text = snapshot.Path, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            body.Children.Add(new TextBlock { Text = UiLanguage.Format($"大小：{LibraryPresentation.FileSize(snapshot.Bytes)}\n") +
                UiLanguage.Format($"移除 {same.Length} 条素材记录及播放队列引用。") +
                (screens.Length == 0 ? "" : UiLanguage.Format($"\n停止 {screens.Length} 个屏幕的相关播放。")),
                TextWrapping = TextWrapping.Wrap });
            if (presetText.Length > 0) body.Children.Add(new TextBlock { Text = presetText.Trim(), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = UiLanguage.Text("文件删除无法通过软件撤销，相关文件操作记录将失效。"),
                TextWrapping = TextWrapping.Wrap });
            libraryPermanentDelete = new CheckBox { Content = UiLanguage.Text("永久删除，不移到回收站"), IsChecked = false,
                Style = HomeStyle("LibraryCheckBox") };
            AutomationProperties.SetAutomationId(libraryPermanentDelete, "LibraryPermanentDelete");
            body.Children.Add(libraryPermanentDelete);
            libraryFileDialog = new ContentDialog {
                XamlRoot = Root.XamlRoot, Title = UiLanguage.Text("删除素材源文件"), Style = HomeStyle("LibraryDialog"),
                Content = new ScrollViewer { Content = body, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Style = HomeStyle("HomeScrollViewer") },
                PrimaryButtonText = UiLanguage.Text("移到回收站"), CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Close
            };
            AutomationProperties.SetAutomationId(libraryFileDialog, "LibraryDeleteSourceDialog");
            libraryPermanentDelete.Checked += (_, _) => libraryFileDialog.PrimaryButtonText = UiLanguage.Text("永久删除");
            libraryPermanentDelete.Unchecked += (_, _) => libraryFileDialog.PrimaryButtonText = UiLanguage.Text("移到回收站");
            if (await libraryFileDialog.ShowAsync() != ContentDialogResult.Primary || libraryShutdown ||
                importLifetime.Active != null || historyBusy || presetBusy || applying != 0) return;
            bool permanent = libraryPermanentDelete.IsChecked == true;
            LibrarySourceFiles.RequireUnchanged(snapshot);
            await FlushEffectChangesAsync();
            if(libraryShutdown)return;
            EndEffectEditing();
            using var blocked = wallpapers?.BlockSourceFile(snapshot.Path);
            if (wallpapers != null) await wallpapers.StopSourceFileAsync(snapshot.Path);
            if (libraryShutdown || importLifetime.Active != null) return;
            if (homePreviewRequest is { } request && LibrarySourceFiles.SamePath(request.Asset.Path, snapshot.Path))
                homePreviewRequest = null;
            attempted = true;
            var result = store.DeleteSourceFile(snapshot.Path, () => {
                LibrarySourceFiles.RequireUnchanged(snapshot);
                librarySourceFiles.Delete(snapshot, permanent);
            });
            RefreshFolders(); RefreshLibrary(); LoadPlaybackSettings(); RefreshUndo(); RefreshStatus();
            Inform((permanent ? UiLanguage.Text("源文件已永久删除") : UiLanguage.Text("源文件已移到回收站")) + UiLanguage.Format($"，已清理 {result.AssetIds.Length} 条素材引用。"));
        }
        catch (Exception error)
        {
            if (!libraryShutdown)
            {
                bool gone = attempted && snapshot != null && !File.Exists(snapshot.Path);
                ShowError((gone ? UiLanguage.Text("源文件已删除，但库记录清理未完成。请刷新后使用“移除引用”清理；软件撤销不能恢复文件。 ") :
                    UiLanguage.Text("未完成文件删除，素材库记录保留。已停止的播放可重新应用。 ")) + UiLanguage.Text(error.Message), error);
            }
        }
        finally
        {
            libraryFileDialog = null; libraryPermanentDelete = null; libraryFileBusy = false;
            if (!libraryShutdown) { RefreshLibraryFileActions(SelectedLibraryAssets()); RefreshFolderControls(); RefreshNativeLibraryDrop(); }
        }
    }
}
