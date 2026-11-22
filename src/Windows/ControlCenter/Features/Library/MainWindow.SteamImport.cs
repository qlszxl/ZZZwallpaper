using LumaWall.Contracts;
using System.Text.Json;
using LumaWall.Contracts.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private const string SteamCopyDirectorySetting = "library.steam.copy-directory.v1";
    private sealed record SteamImportChoice(string? CopyDirectory, string? ExtractionDirectory = null);
    private ContentDialog? steamOptionsDialog;
    private RadioButton? steamReferenceChoice, steamCopyChoice;
    private TextBox? steamDestinationEditor;

    private async Task<SteamImportChoice?> ConfirmSteamImportOptionsAsync(int count, long bytes, int extractionCount = 0)
    {
        bool extractionOnly = count == extractionCount;
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(UtilityText(UiLanguage.Format($"已选择 {count} 项 · {LibraryPresentation.FileSize(bytes)}"), "UtilityLabel"));
        steamReferenceChoice = new RadioButton { Content = UiLanguage.Text("引用原文件"), GroupName = "SteamImportMode", IsChecked = !extractionOnly,
            Style = HomeStyle("EffectsRadioButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        steamCopyChoice = new RadioButton { Content = UiLanguage.Text("复制到指定目录"), GroupName = "SteamImportMode", IsChecked = extractionOnly,
            Style = HomeStyle("EffectsRadioButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        if (extractionOnly) steamReferenceChoice.Visibility = steamCopyChoice.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(steamReferenceChoice, "SteamReference");
        AutomationProperties.SetAutomationId(steamCopyChoice, "SteamCopy");
        body.Children.Add(steamReferenceChoice);
        var referenceHint = UtilityText(UiLanguage.Text("取消订阅或移动原文件后，需要重新关联。"), "UtilityLabel");
        referenceHint.TextWrapping = TextWrapping.Wrap;
        if (!extractionOnly) body.Children.Add(referenceHint);
        body.Children.Add(steamCopyChoice);
        if (extractionCount > 0) body.Children.Add(UtilityText(UiLanguage.Format($"{extractionCount} 项素材需提取到所选目录"), "UtilityLabel"));
        var copyPanel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var path = new TextBox {
            Style = HomeStyle("UtilityTextBox"), Text = modules.Settings.Get(SteamCopyDirectorySetting) ?? "",
            PlaceholderText = UiLanguage.Text("选择素材保存目录"), MaxLength = 32760
        };
        DoubleClickEdit.SetEnabled(path, false); steamDestinationEditor = path;
        AutomationProperties.SetName(path, UiLanguage.Text("复制目标目录"));
        AutomationProperties.SetAutomationId(path, "SteamCopyDirectory");
        copyPanel.Children.Add(path);
        var choose = SteamButton(UiLanguage.Text("选择文件夹…"), "SteamChooseDestination"); copyPanel.Children.Add(choose);
        body.Children.Add(copyPanel);
        var error = UtilityText("", "UtilityLabel"); error.TextWrapping = TextWrapping.Wrap; body.Children.Add(error);
        var scroll = new ScrollViewer {
            Style = HomeStyle("HomeScrollViewer"), Content = body,
            Width = Math.Max(260, Math.Min(560, Root.ActualWidth - 140)), MaxHeight = Math.Max(200, Root.ActualHeight - 260),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        var dialog = new ContentDialog {
            Title = extractionOnly ? UiLanguage.Text("保存素材") : UiLanguage.Text("导入方式"), Content = scroll, XamlRoot = Root.XamlRoot, PrimaryButtonText = UiLanguage.Text("导入"),
            CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Close, Style = HomeStyle("LibraryDialog")
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660d;
        AutomationProperties.SetAutomationId(dialog, "SteamImportOptions");
        bool choosing = false;
        string pickerError = "";
        string? Destination()
        {
            try {
                string value = path.Text.Trim();
                if (!Path.IsPathFullyQualified(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
                string full = Path.GetFullPath(value);
                return File.Exists(full) ? null : full;
            } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }
        void Validate()
        {
            bool copy = steamCopyChoice?.IsChecked == true;
            bool needsDirectory = copy || extractionCount > 0;
            copyPanel.Visibility = needsDirectory ? Visibility.Visible : Visibility.Collapsed;
            referenceHint.Visibility = !copy ? Visibility.Visible : Visibility.Collapsed;
            error.Text = needsDirectory && Destination() == null ? UiLanguage.Text("请选择素材保存目录") : pickerError;
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = !choosing && (!needsDirectory || Destination() != null);
        }
        steamReferenceChoice.Checked += (_, _) => Validate();
        steamCopyChoice.Checked += (_, _) => Validate();
        path.TextChanged += (_, _) => { pickerError = ""; Validate(); };
        choose.Click += async (_, _) => {
            if (choosing) return;
            choosing = true; pickerError = ""; choose.IsEnabled = false; Validate();
            try {
                string? directory = await PickSteamFolder();
                if (directory != null && !libraryShutdown && steamOptionsDialog == dialog) path.Text = directory;
            } catch (Exception ex) { pickerError = UiLanguage.Text("无法选择目录：") + ex.Message; }
            finally { choosing = false; choose.IsEnabled = true; Validate(); }
        };
        dialog.PrimaryButtonClick += (_, args) => { Validate(); if (!dialog.IsPrimaryButtonEnabled) args.Cancel = true; };
        steamOptionsDialog = dialog; Validate();
        try {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || libraryShutdown) return null;
            string? destination = steamCopyChoice.IsChecked == true || extractionCount > 0 ? Destination() : null;
            if (destination != null) modules.Settings.Set(SteamCopyDirectorySetting, destination);
            return new(steamCopyChoice.IsChecked == true ? destination : null, extractionCount > 0 ? destination : null);
        } finally {
            steamOptionsDialog = null; steamReferenceChoice = steamCopyChoice = null; steamDestinationEditor = null;
            scroll.Content = null; dialog.Content = null;
        }
    }

    private sealed record SteamImportItem(string Id, string Title, Func<CancellationToken, Task<SteamPreparedImport>> Prepare);

    private Task ImportSteamProjectsAsync(IReadOnlyList<SteamWallpaperProject> projects, string? copyDirectory) =>
        ImportSteamItemsAsync(projects.Select(project => new SteamImportItem(project.SourceId, project.Title,
            token => steamCatalog.PrepareAsync(project, copyDirectory, token))).ToArray());

    private Task ImportSteamAssetsAsync(IReadOnlyList<SteamWallpaperAsset> assets, SteamImportChoice choice) =>
        ImportSteamItemsAsync(assets.Select(asset => new SteamImportItem(asset.SourceId, asset.Title,
            token => steamCatalog.PrepareAssetAsync(asset, asset.RequiresExtraction ? choice.ExtractionDirectory : choice.CopyDirectory, token))).ToArray());

    private async Task ImportSteamItemsAsync(IReadOnlyList<SteamImportItem> items)
    {
        if (libraryShutdown || libraryFileBusy || importLifetime.Active != null) return;
        var operation = BeginLibraryImport();
        var summary = new ImportSummary();
        var warnings = new List<string>();
        try {
            int index = 0;
            foreach (var item in items.DistinctBy(item => item.Id)) {
                CheckImportActive(operation.Token);
                Inform(UiLanguage.Format($"正在导入 {++index}/{items.Count} · {item.Title}"));
                SteamPreparedImport? prepared = null;
                bool committed = false;
                try {
                    prepared = await item.Prepare(operation.Token);
                    CheckImportActive(operation.Token);
                    bool existed = modules.Library.FindAssetByPath(prepared.Path) != null;
                    int failedBefore = summary.Failed;
                    // The existing importer checks the full file stamp and preserves user-selected posters.
                    await ImportPaths([prepared.Path], false, summary, operation.Token);
                    var asset = modules.Library.FindAssetByPath(prepared.Path);
                    if (asset != null && summary.Failed == failedBefore) {
                        committed = true;
                        modules.Settings.Set(SteamSourceIdentity.SettingKey(asset.Id), JsonSerializer.Serialize(prepared.Source));
                        if (!existed) modules.Library.Upsert(asset with { Name = item.Title });
                    }
                } catch (OperationCanceledException) { throw; }
                catch (Exception ex) {
                    if (committed) {
                        if (warnings.Count < 3) warnings.Add(item.Title + UiLanguage.Text(" 已导入，名称或来源保存失败：") + ex.Message);
                    } else {
                        summary.Failed++;
                        if (summary.Errors.Count < 3) summary.Errors.Add(item.Title + "：" + ex.Message);
                    }
                } finally {
                    // Only clean a new copy created by this import, and only if no library record owns it.
                    if (!committed && prepared is { Copied: true, Reused: false } &&
                        modules.Library.FindAssetByPath(prepared.Path) == null) {
                        try { File.Delete(prepared.Path); }
                        catch (Exception ex) {
                            if (summary.Errors.Count < 3) summary.Errors.Add(UiLanguage.Text("未完成的副本保留在 ") + prepared.Path + "：" + ex.Message);
                        }
                    }
                }
            }
            CheckImportActive(operation.Token);
            Inform(UiLanguage.Text("导入完成 · ") + summary.Text +
                (summary.Errors.Count == 0 ? "" : UiLanguage.Text("。失败：") + string.Join("；", summary.Errors)) +
                (warnings.Count == 0 ? "" : " " + string.Join("；", warnings)));
        } catch (OperationCanceledException) {
            if (!libraryShutdown) Inform(UiLanguage.Text("导入已取消。已完成的条目保留，") + summary.Text + "。");
        } catch (Exception ex) {
            if (!libraryShutdown) ShowError(UiLanguage.Text("Wallpaper Engine 导入未完成：") + ex.Message, ex);
        } finally {
            try {
                if (!libraryShutdown) {
                    ClearLibraryFilter(); RefreshFolders(); RefreshLibrary(summary.LastId); Navigate("library");
                }
            } finally { CompleteLibraryImport(operation); }
        }
    }
}
