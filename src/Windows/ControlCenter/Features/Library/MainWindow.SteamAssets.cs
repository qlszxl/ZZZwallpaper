using LumaWall.Contracts;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using LumaWall.Contracts.Steam;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;

public sealed class SteamAssetRow(SteamWallpaperAsset asset) : INotifyPropertyChanged
{
    public SteamWallpaperAsset Asset { get; } = asset;
    public string Title => Asset.Title;
    public bool CanImport => Asset.CanImport;
    public double CardOpacity => CanImport ? 1 : 0.65;
    public string Detail => CanImport
        ? $"{(Asset.IsImage ? UiLanguage.Text("图片") : UiLanguage.Text("视频"))} · {LibraryPresentation.FileSize(Asset.Bytes)} · {(Asset.RequiresExtraction ? UiLanguage.Text("需提取") : UiLanguage.Text("可引用"))}"
        : Asset.Message;
    public string ToolTip => Asset.Project.Title + "\n" + Asset.RelativePath + "\n" + Detail;
    public BitmapImage? Preview { get; private set; }
    internal bool PreviewRequested { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetPreview(BitmapImage? image)
    {
        Preview = image;
        PropertyChanged?.Invoke(this, new(nameof(Preview)));
    }
}

public sealed partial class MainWindow
{
    private ContentDialog? steamAssetDialog;
    private GridView? steamAssetGrid;
    private TextBox? steamAssetSearch;
    private TextBlock? steamAssetStatus;
    private CancellationTokenSource? steamAssetCancellation;
    private List<SteamAssetRow> steamAssetRows = [];

    private async Task<SteamWallpaperAsset[]> SelectSteamProjectAssetsAsync(IReadOnlyList<SteamWallpaperProject> projects)
    {
        using var cancellation = new CancellationTokenSource();
        using var thumbnailGate = new SemaphoreSlim(2);
        var thumbnailTasks = new List<Task>();
        var cachedPreviews = new Queue<SteamAssetRow>();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<SteamDiscoveryIssue>();
        bool loading = true, refreshing = false, closed = false;
        var searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        steamAssetCancellation = cancellation;
        steamAssetRows = [];
        var body = new Grid { RowSpacing = 12 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var select = SteamButton(UiLanguage.Text("全选"), "SteamAssetSelectVisible");
        select.IsEnabled = false;
        actions.Children.Add(select);
        body.Children.Add(actions);
        var search = new TextBox { PlaceholderText = UiLanguage.Text("搜索素材"), Style = HomeStyle("UtilityTextBox"), MaxLength = 200 };
        DoubleClickEdit.SetEnabled(search, false);
        AutomationProperties.SetAutomationId(search, "SteamAssetSearch");
        AutomationProperties.SetName(search, UiLanguage.Text("搜索素材"));
        Grid.SetRow(search, 1); body.Children.Add(search);
        var grid = new GridView {
            Style = HomeStyle("LibraryGridView"), ItemContainerStyle = HomeStyle("LibraryGridViewItem"),
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["SteamWallpaperCard"],
            SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0, 0, 4, 0)
        };
        AutomationProperties.SetAutomationId(grid, "SteamAssetGrid");
        AutomationProperties.SetName(grid, UiLanguage.Text("项目素材"));
        grid.Resources["ListViewItemDisabledThemeOpacity"] = 0.65d;
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Visible);
        Grid.SetRow(grid, 2); body.Children.Add(grid);
        var status = UtilityText(UiLanguage.Text("正在读取素材…"), "UtilityLabel");
        status.TextWrapping = TextWrapping.Wrap; status.MaxLines = 3; status.MinHeight = 20;
        AutomationProperties.SetAutomationId(status, "SteamAssetStatus");
        Grid.SetRow(status, 3); body.Children.Add(status);
        var dialog = new ContentDialog {
            Title = UiLanguage.Text("选择素材"), Content = body, XamlRoot = Root.XamlRoot,
            PrimaryButtonText = UiLanguage.Text("下一步"), CloseButtonText = UiLanguage.Text("取消"), DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false, Style = HomeStyle("LibraryDialog")
        };
        dialog.Resources["ContentDialogMaxWidth"] = 940d;
        AutomationProperties.SetAutomationId(dialog, "SteamAssetBrowser");
        steamAssetDialog = dialog; steamAssetGrid = grid; steamAssetSearch = search; steamAssetStatus = status;

        void ResizeCards()
        {
            if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || grid.ActualWidth <= 0) return;
            double available = Math.Max(1, grid.ActualWidth - 22);
            int columns = Math.Clamp((int)(available / 205), 1, 4);
            panel.Orientation = Orientation.Horizontal;
            panel.MaximumRowsOrColumns = columns;
            panel.ItemWidth = available / columns;
            panel.ItemHeight = 214;
        }
        void Resize()
        {
            dialog.Resources["ContentDialogMaxHeight"] = Math.Max(360, Root.ActualHeight - 40);
            body.Width = Math.Max(300, Math.Min(830, Root.ActualWidth - 140));
            body.Height = Math.Max(260, Math.Min(610, Root.ActualHeight - 260));
            ResizeCards();
        }
        void SelectionStatus()
        {
            dialog.IsPrimaryButtonEnabled = !loading && selected.Count > 0;
            if (loading) return;
            status.Text = steamAssetRows.Count == 0 ? UiLanguage.Text("未找到可导入素材") :
                UiLanguage.Format($"素材 {steamAssetRows.Count} 项 · 可导入 {steamAssetRows.Count(row => row.CanImport)} 项 · 已选 {selected.Count} 项");
            if (issues.Count > 0) status.Text += UiLanguage.Format($" · {issues.Count} 项未读取");
            ToolTipService.SetToolTip(status, issues.Count == 0 ? null :
                string.Join("\n", issues.Take(8).Select(issue => issue.Path + "：" + issue.Message)));
        }
        void RefreshRows()
        {
            searchDebounce.Stop();
            if (closed || libraryShutdown) return;
            string query = search.Text.Trim();
            var visible = steamAssetRows.Where(row => query.Length == 0 ||
                row.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Asset.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Asset.Project.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            refreshing = true;
            try {
                grid.ItemsSource = visible;
                foreach (var row in visible) if (row.CanImport && selected.Contains(row.Asset.SourceId)) grid.SelectedItems.Add(row);
            } finally { refreshing = false; }
            SelectionStatus(); ResizeCards();
        }
        async Task Preview(SteamAssetRow row)
        {
            bool acquired = false;
            try {
                await thumbnailGate.WaitAsync(cancellation.Token); acquired = true;
                if (closed || grid.ContainerFromItem(row) is not GridViewItem) {
                    row.PreviewRequested = false; return;
                }
                byte[]? bytes = await steamCatalog.ReadPreviewAsync(row.Asset, cancellation.Token);
                if (closed || bytes == null || cancellation.IsCancellationRequested) return;
                using var memory = new MemoryStream(bytes, writable: false);
                using var stream = memory.AsRandomAccessStream();
                var bitmap = new BitmapImage { DecodePixelWidth = 320 };
                await bitmap.SetSourceAsync(stream);
                if (closed || cancellation.IsCancellationRequested) return;
                row.SetPreview(bitmap);
                cachedPreviews.Enqueue(row);
                // Only a small viewport-sized cache is retained, even for large Steam libraries.
                while (cachedPreviews.Count > 64) {
                    var old = cachedPreviews.Dequeue();
                    old.SetPreview(null); old.PreviewRequested = false;
                }
            } catch (OperationCanceledException) { }
            catch { /* A thumbnail failure must not prevent verified media import. */ }
            finally { if (acquired) thumbnailGate.Release(); }
        }
        grid.ContainerContentChanging += (_, args) => {
            if (args.InRecycleQueue || args.Item is not SteamAssetRow row) return;
            args.ItemContainer.IsEnabled = row.CanImport;
            if (row.CanImport && row.Asset.IsImage && !row.PreviewRequested && !closed) {
                row.PreviewRequested = true; thumbnailTasks.Add(Preview(row));
            }
        };
        grid.SizeChanged += (_, _) => ResizeCards();
        grid.Loaded += (_, _) => ResizeCards();
        grid.SelectionChanged += (_, args) => {
            if (refreshing) return;
            foreach (SteamAssetRow row in args.RemovedItems) selected.Remove(row.Asset.SourceId);
            foreach (SteamAssetRow row in args.AddedItems) if (row.CanImport) selected.Add(row.Asset.SourceId);
            SelectionStatus();
        };
        searchDebounce.Tick += (_, _) => RefreshRows();
        search.TextChanged += (_, _) => {
            searchDebounce.Stop();
            if (closed || libraryShutdown) return;
            if (string.IsNullOrWhiteSpace(search.Text)) RefreshRows();
            else searchDebounce.Start();
        };
        select.Click += (_, _) => {
            if (searchDebounce.IsEnabled) RefreshRows();
            var visible = grid.Items.OfType<SteamAssetRow>().Where(row => row.CanImport).ToArray();
            bool all = visible.Length > 0 && visible.All(row => selected.Contains(row.Asset.SourceId));
            foreach (var row in visible) {
                if (all) selected.Remove(row.Asset.SourceId);
                else selected.Add(row.Asset.SourceId);
            }
            RefreshRows();
        };
        async Task ReadAssets()
        {
            try {
                int index = 0;
                foreach (var project in projects) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    status.Text = UiLanguage.Format($"正在读取 {++index}/{projects.Count} · {project.Title}");
                    var result = await steamCatalog.InspectAssetsAsync(project, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (closed || libraryShutdown) return;
                    issues.AddRange(result.Issues);
                    steamAssetRows.AddRange(result.Assets.Select(asset => new SteamAssetRow(asset)));
                }
                steamAssetRows = steamAssetRows.DistinctBy(row => row.Asset.SourceId).ToList();
                loading = false; select.IsEnabled = true; RefreshRows();
            } catch (OperationCanceledException) { }
            catch (Exception error) {
                if (!closed) {
                    loading = false; select.IsEnabled = true; RefreshRows();
                    status.Text = UiLanguage.Text("素材读取失败：") + error.Message;
                }
            }
        }
        Task? scan = null;
        dialog.Opened += (_, _) => scan = ReadAssets();
        dialog.Closing += (_, _) => searchDebounce.Stop();
        SizeChangedEventHandler resized = (_, _) => Resize();
        Resize(); Root.SizeChanged += resized;
        try {
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary && !libraryShutdown
                ? steamAssetRows.Where(row => row.CanImport && selected.Contains(row.Asset.SourceId)).Select(row => row.Asset).ToArray()
                : [];
        } finally {
            closed = true; searchDebounce.Stop(); cancellation.Cancel();
            if (scan != null) await scan;
            await Task.WhenAll(thumbnailTasks);
            Root.SizeChanged -= resized;
            foreach (var row in steamAssetRows) row.SetPreview(null);
            steamAssetRows = [];
            steamAssetDialog = null; steamAssetGrid = null; steamAssetSearch = null; steamAssetStatus = null; steamAssetCancellation = null;
            grid.ItemsSource = null; dialog.Content = null; body.Children.Clear();
        }
    }
}
