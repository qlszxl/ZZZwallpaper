using System.Text.Json;
using LumaWall.Application.Steam;
using LumaWall.Contracts;
using LumaWall.Contracts.Steam;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed record SteamWallpaperRow(SteamWallpaperProject Project, bool Imported)
{
    public string Title => Project.Title;
    public bool HasAssets => Project.Status == SteamProjectStatus.Unsupported && Project.Type is "scene" or "web";
    public bool CanImport => Project.Status == SteamProjectStatus.Supported || HasAssets;
    public double CardOpacity => CanImport ? 1 : 0.9;
    public string Detail => HasAssets ? (Project.Type == "scene" ? UiLanguage.Text("场景素材") : UiLanguage.Text("网页素材")) : CanImport
        ? $"{(MediaFormats.IsImage(Project.MediaPath ?? "") ? UiLanguage.Text("图片") : UiLanguage.Text("视频"))} · {LibraryPresentation.FileSize(Project.Bytes)}{(Imported ? UiLanguage.Text(" · 已导入") : "")}"
        : Project.Message;
    public string ToolTip => Title + "\n" + Detail + "\n" + Project.ProjectDirectory;
    public BitmapImage? Preview => PosterImageCache.Load(Project.PreviewPath ?? (MediaFormats.IsImage(Project.MediaPath ?? "") ? Project.MediaPath : null), 320);
}

public sealed partial class MainWindow
{
    private const string SteamManualDirectorySetting = "library.steam.manual-directory.v1";
    private readonly SteamWallpaperCatalog steamCatalog = new();
    private readonly HashSet<string> steamSelectedIds = new(StringComparer.Ordinal);
    private List<SteamWallpaperRow> steamRows = [];
    private ContentDialog? steamImportDialog;
    private GridView? steamImportGrid;
    private TextBox? steamSearchBox;
    private TextBlock? steamScanStatus;
    private Button? steamAutomaticButton, steamManualButton, steamSelectButton, steamCancelScanButton;
    private CancellationTokenSource? steamScanCancellation;
    private Task? steamScanTask;
    private DispatcherTimer? steamSearchDebounce;
    private bool steamRefreshingSelection, steamBrowserOpen;
    private string[] steamScanRoots = [];

    private async void SteamImport_Click(object sender, RoutedEventArgs e)
    {
        try { await OpenSteamImportAsync(); }
        catch (Exception error) { if (!libraryShutdown) ShowError(UiLanguage.Text("无法打开 Wallpaper Engine 素材：") + error.Message, error); }
    }

    private void ShutdownSteamImport()
    {
        steamSearchDebounce?.Stop();
        steamScanCancellation?.Cancel();
        steamImportDialog?.Hide();
        steamOptionsDialog?.Hide();
        steamAssetCancellation?.Cancel();
        steamAssetDialog?.Hide();
    }

    private Button SteamButton(string title, string id)
    {
        var button = new Button { Content = title, Style = HomeStyle("HomeButton"), Height = 42, Padding = new Thickness(14, 0, 14, 0) };
        AutomationProperties.SetName(button, title);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private async Task OpenSteamImportAsync(IEnumerable<string>? roots = null)
    {
        if (libraryShutdown || libraryFileBusy || importLifetime.Active != null || steamBrowserOpen) return;
        steamBrowserOpen = true;
        try { await ShowSteamImportBrowserAsync(roots); }
        finally { steamBrowserOpen = false; }
    }

    private async Task ShowSteamImportBrowserAsync(IEnumerable<string>? roots)
    {
        steamRows = []; steamSelectedIds.Clear();
        string saved = modules.Settings.Get(SteamManualDirectorySetting) ?? "";
        steamScanRoots = roots?.ToArray() ?? SteamInstallDiscovery.CandidateRoots()
            .Concat(Path.IsPathFullyQualified(saved) ? [saved] : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var body = new Grid { RowSpacing = 12 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        steamAutomaticButton = SteamButton(UiLanguage.Text("重新扫描"), "SteamScanAutomatic");
        steamManualButton = SteamButton(UiLanguage.Text("选择目录…"), "SteamChooseSource");
        steamSelectButton = SteamButton(UiLanguage.Text("全选"), "SteamSelectVisible");
        steamCancelScanButton = SteamButton(UiLanguage.Text("取消扫描"), "SteamScanCancel");
        foreach (var button in new[] { steamAutomaticButton, steamManualButton, steamSelectButton, steamCancelScanButton }) actions.Children.Add(button);
        body.Children.Add(actions);
        steamSearchBox = new TextBox { PlaceholderText = UiLanguage.Text("搜索壁纸"), Style = HomeStyle("UtilityTextBox"), MaxLength = 200 };
        DoubleClickEdit.SetEnabled(steamSearchBox, false);
        AutomationProperties.SetName(steamSearchBox, UiLanguage.Text("搜索 Wallpaper Engine 壁纸"));
        AutomationProperties.SetAutomationId(steamSearchBox, "SteamSearch");
        Grid.SetRow(steamSearchBox, 1); body.Children.Add(steamSearchBox);
        steamImportGrid = new GridView {
            Style = HomeStyle("LibraryGridView"), ItemContainerStyle = HomeStyle("LibraryGridViewItem"),
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["SteamWallpaperCard"],
            SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0, 0, 4, 0)
        };
        AutomationProperties.SetAutomationId(steamImportGrid, "SteamProjectGrid");
        AutomationProperties.SetName(steamImportGrid, UiLanguage.Text("Wallpaper Engine 壁纸"));
        steamImportGrid.Resources["ListViewItemDisabledThemeOpacity"] = 0.65d;
        ScrollViewer.SetHorizontalScrollBarVisibility(steamImportGrid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(steamImportGrid, ScrollBarVisibility.Visible);
        steamImportGrid.ContainerContentChanging += (_, args) => {
            if (!args.InRecycleQueue && args.Item is SteamWallpaperRow row) args.ItemContainer.IsEnabled = row.CanImport;
        };
        steamImportGrid.SizeChanged += (_, _) => SizeSteamCards();
        steamImportGrid.Loaded += (_, _) => SizeSteamCards();
        steamImportGrid.SelectionChanged += (_, args) => {
            if (steamRefreshingSelection) return;
            foreach (SteamWallpaperRow row in args.RemovedItems) steamSelectedIds.Remove(row.Project.SourceId);
            foreach (SteamWallpaperRow row in args.AddedItems) if (row.CanImport) steamSelectedIds.Add(row.Project.SourceId);
            RefreshSteamSelectionStatus();
        };
        Grid.SetRow(steamImportGrid, 2); body.Children.Add(steamImportGrid);
        steamScanStatus = UtilityText("", "UtilityLabel");
        steamScanStatus.TextWrapping = TextWrapping.Wrap;
        steamScanStatus.MaxLines = 3;
        steamScanStatus.MinHeight = 20;
        AutomationProperties.SetAutomationId(steamScanStatus, "SteamScanStatus");
        Grid.SetRow(steamScanStatus, 3); body.Children.Add(steamScanStatus);
        var dialog = new ContentDialog {
            Title = UiLanguage.Text("从 Wallpaper Engine 导入"), Content = body, XamlRoot = Root.XamlRoot,
            PrimaryButtonText = UiLanguage.Text("下一步"), CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false, Style = HomeStyle("LibraryDialog")
        };
        dialog.Resources["ContentDialogMaxWidth"] = 940d;
        AutomationProperties.SetAutomationId(dialog, "SteamImportBrowser");
        void Resize()
        {
            dialog.Resources["ContentDialogMaxHeight"] = Math.Max(360, Root.ActualHeight - 40);
            body.Width = Math.Max(300, Math.Min(830, Root.ActualWidth - 140));
            body.Height = Math.Max(260, Math.Min(610, Root.ActualHeight - 260));
            // On a narrow window put the action row vertically; the card viewport remains bounded.
            actions.Orientation = body.Width < 550 ? Orientation.Vertical : Orientation.Horizontal;
            SizeSteamCards();
        }
        SizeChangedEventHandler resized = (_, _) => Resize();
        Resize(); Root.SizeChanged += resized;
        steamImportDialog = dialog;
        var searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        steamSearchDebounce = searchDebounce;
        searchDebounce.Tick += (_, _) => {
            searchDebounce.Stop();
            if (!libraryShutdown && steamImportDialog == dialog) RefreshSteamRows();
        };
        steamSearchBox.TextChanged += (_, _) => {
            searchDebounce.Stop();
            if (libraryShutdown || steamImportDialog != dialog) return;
            if (string.IsNullOrWhiteSpace(steamSearchBox.Text)) RefreshSteamRows();
            else searchDebounce.Start();
        };
        steamAutomaticButton.Click += (_, _) => StartSteamScan();
        steamManualButton.Click += async (_, _) => {
            if (steamScanCancellation != null) return;
            steamManualButton.IsEnabled = false;
            try {
                string? directory = await PickSteamFolder();
                if (directory == null || libraryShutdown || steamImportDialog != dialog) return;
                modules.Settings.Set(SteamManualDirectorySetting, directory);
                steamScanRoots = steamScanRoots.Append(directory).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray();
                StartSteamScan();
            } catch (Exception error) { if (steamScanStatus != null) steamScanStatus.Text = UiLanguage.Text("无法选择目录：") + error.Message; }
            finally { if (steamManualButton != null) steamManualButton.IsEnabled = steamScanCancellation == null; }
        };
        steamSelectButton.Click += (_, _) => {
            if (steamImportGrid == null) return;
            // A click during the debounce uses the newest search text.
            if (searchDebounce.IsEnabled) RefreshSteamRows();
            var visible = steamImportGrid.Items.OfType<SteamWallpaperRow>().Where(row => row.CanImport).ToArray();
            bool allSelected = visible.Length > 0 && visible.All(row => steamSelectedIds.Contains(row.Project.SourceId));
            foreach (var row in visible) {
                if (allSelected) steamSelectedIds.Remove(row.Project.SourceId);
                else steamSelectedIds.Add(row.Project.SourceId);
            }
            RefreshSteamRows();
        };
        steamCancelScanButton.Click += (_, _) => steamScanCancellation?.Cancel();
        dialog.Opened += (_, _) => StartSteamScan();
        dialog.Closing += (_, _) => searchDebounce.Stop();
        SteamWallpaperProject[] selectedProjects = [];
        try {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !libraryShutdown)
                selectedProjects = steamRows.Where(row => row.CanImport && steamSelectedIds.Contains(row.Project.SourceId)).Select(row => row.Project).ToArray();
        } finally {
            searchDebounce.Stop(); steamSearchDebounce = null;
            steamScanCancellation?.Cancel();
            if (steamScanTask != null) await steamScanTask;
            Root.SizeChanged -= resized;
            steamImportDialog = null; steamImportGrid = null; steamSearchBox = null; steamScanStatus = null;
            steamAutomaticButton = steamManualButton = steamSelectButton = steamCancelScanButton = null;
            dialog.Content = null; body.Children.Clear();
        }
        if (selectedProjects.Length == 0 || libraryShutdown) return;
        if (selectedProjects.Any(project => project.Status != SteamProjectStatus.Supported)) {
            var assets = await SelectSteamProjectAssetsAsync(selectedProjects);
            if (assets.Length == 0 || libraryShutdown) return;
            var assetChoice = await ConfirmSteamImportOptionsAsync(assets.Length, assets.Sum(asset => asset.Bytes),
                assets.Count(asset => asset.RequiresExtraction));
            if (assetChoice != null && !libraryShutdown) await ImportSteamAssetsAsync(assets, assetChoice);
            return;
        }
        var choice = await ConfirmSteamImportOptionsAsync(selectedProjects.Length, selectedProjects.Sum(project => project.Bytes));
        if (choice != null && !libraryShutdown) await ImportSteamProjectsAsync(selectedProjects, choice.CopyDirectory);
    }

    private static async Task<string?> PickSteamFolderForWindow(MainWindow window)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private Task<string?> PickSteamFolder() => PickSteamFolderForWindow(this);

    private void SizeSteamCards()
    {
        if (steamImportGrid?.ItemsPanelRoot is not ItemsWrapGrid panel || steamImportGrid.ActualWidth <= 0) return;
        double available = Math.Max(1, steamImportGrid.ActualWidth - 22);
        int columns = Math.Clamp((int)(available / 205), 1, 4);
        panel.Orientation = Orientation.Horizontal;
        panel.MaximumRowsOrColumns = columns;
        panel.ItemWidth = available / columns;
        panel.ItemHeight = 214;
    }

    private void StartSteamScan()
    {
        if (steamScanCancellation != null || steamImportDialog == null) return;
        steamScanTask = ScanSteamWallpapersAsync(steamScanRoots);
    }

    private async Task ScanSteamWallpapersAsync(IEnumerable<string> roots)
    {
        using var cancellation = new CancellationTokenSource();
        steamScanCancellation = cancellation;
        steamAutomaticButton!.IsEnabled = steamManualButton!.IsEnabled = steamSelectButton!.IsEnabled = false;
        steamCancelScanButton!.Visibility = Visibility.Visible;
        steamImportDialog!.IsPrimaryButtonEnabled = false;
        steamScanStatus!.Text = UiLanguage.Text("正在扫描…");
        try {
            var result = await steamCatalog.DiscoverAsync(roots, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (libraryShutdown) return;
            var assets = modules.Library.Assets();
            var sourceSettings = modules.Settings.GetByPrefix(SteamSourceIdentity.SettingKey(""));
            // Capture the database once; metadata parsing and filesystem checks
            // belong to the scan worker, never to the result-binding UI pass.
            var rows = await Task.Run(() => {
                var imported = new HashSet<string>(StringComparer.Ordinal);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(asset.Path)));
                    try {
                        var json = sourceSettings.GetValueOrDefault(SteamSourceIdentity.SettingKey(asset.Id));
                        if (json != null && JsonSerializer.Deserialize<SteamWallpaperSource>(json) is { } source &&
                            string.Equals(asset.Path, source.ImportedPath, StringComparison.OrdinalIgnoreCase) && File.Exists(asset.Path)) imported.Add(source.SourceId);
                    } catch (JsonException) { }
                }
                return result.Projects.Select(project => {
                    cancellation.Token.ThrowIfCancellationRequested();
                    return new SteamWallpaperRow(project,imported.Contains(project.SourceId) ||
                        project.MediaPath != null && paths.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.MediaPath))));
                }).ToList();
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (libraryShutdown) return;
            // The image cache has no synchronous file-stat work on its hot path.
            // Explicit rescans invalidate potentially changed external previews.
            foreach (var project in result.Projects) {
                string? preview = project.PreviewPath ?? (MediaFormats.IsImage(project.MediaPath ?? "") ? project.MediaPath : null);
                if (preview != null) PosterImageCache.Invalidate(preview);
            }
            steamRows = rows;
            steamSelectedIds.IntersectWith(steamRows.Where(row => row.CanImport).Select(row => row.Project.SourceId));
            RefreshSteamRows();
            if (result.Issues.Length > 0) {
                steamScanStatus.Text += UiLanguage.Format($" · {result.Issues.Length} 个目录未完整读取");
                ToolTipService.SetToolTip(steamScanStatus, string.Join("\n", result.Issues.Take(5).Select(issue => issue.Path + "：" + issue.Message)));
            }
        } catch (OperationCanceledException) { if (!libraryShutdown) steamScanStatus!.Text = UiLanguage.Text("扫描已取消"); }
        catch (Exception error) { if (!libraryShutdown) steamScanStatus!.Text = UiLanguage.Text("扫描未完成：") + error.Message; }
        finally {
            steamScanCancellation = null;
            if (steamAutomaticButton != null) steamAutomaticButton.IsEnabled = steamManualButton!.IsEnabled = steamSelectButton!.IsEnabled = true;
            if (steamCancelScanButton != null) steamCancelScanButton.Visibility = Visibility.Collapsed;
            if (steamImportDialog != null) steamImportDialog.IsPrimaryButtonEnabled = steamSelectedIds.Count > 0;
        }
    }

    private void RefreshSteamRows()
    {
        steamSearchDebounce?.Stop();
        if (steamImportGrid == null) return;
        string query = steamSearchBox?.Text.Trim() ?? "";
        var visible = steamRows.Where(row => query.Length == 0 || row.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Project.ProjectDirectory.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        steamRefreshingSelection = true;
        try {
            steamImportGrid.ItemsSource = visible;
            foreach (var row in visible) if (row.CanImport && steamSelectedIds.Contains(row.Project.SourceId)) steamImportGrid.SelectedItems.Add(row);
        } finally { steamRefreshingSelection = false; }
        RefreshSteamSelectionStatus(); SizeSteamCards();
    }

    private void RefreshSteamSelectionStatus()
    {
        if (steamImportDialog != null) steamImportDialog.IsPrimaryButtonEnabled = steamScanCancellation == null && steamSelectedIds.Count > 0;
        if (steamScanStatus != null) steamScanStatus.Text = steamRows.Count == 0
            ? UiLanguage.Text("未找到壁纸")
            : UiLanguage.Format($"找到 {steamRows.Count} 项 · 可选 {steamRows.Count(row => row.CanImport)} 项 · 已选 {steamSelectedIds.Count} 项");
    }
}
