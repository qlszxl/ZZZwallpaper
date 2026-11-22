using System.Runtime.InteropServices;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed class LibraryRow(MediaAsset asset) : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly SemaphoreSlim sourceChecks=new(4);
    private bool? sourceAvailable;
    private long sourceCheckedAt;
    private Task? sourceCheck;
    private CancellationTokenSource? sourceCancellation;
    private bool active,notificationPending;
    public MediaAsset Asset {get;}=asset;
    internal int LibraryOrder {get;set;}=int.MaxValue;
    public string Name => Asset.Name;
    private string appliedText="";
    public string AppliedText=>appliedText;
    public Visibility AppliedVisibility=>appliedText.Length==0?Visibility.Collapsed:Visibility.Visible;
    internal void SetApplied(string text) {
        if(text==appliedText)return;appliedText=text;
        if(!active){notificationPending=true;return;}
        PropertyChanged?.Invoke(this,new(nameof(AppliedText)));PropertyChanged?.Invoke(this,new(nameof(AppliedVisibility)));
    }
    public string? PosterPath {get;init;}
    public BitmapImage? Poster=>PosterImageCache.Load(PosterPath);
    public int PosterRevision {get;private set;}
    public double PosterAspect=>Asset.Height>0?Asset.Width/(double)Asset.Height:1;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void RefreshPoster()
    {
        PosterRevision++;
        if(!active) {notificationPending=true;return;}
        PropertyChanged?.Invoke(this,new(nameof(PosterRevision)));
        PropertyChanged?.Invoke(this,new(nameof(Poster)));
    }
    internal void SetActive(bool value)
    {
        active=value;
        if(!value)sourceCancellation?.Cancel();
        if(value&&notificationPending) {notificationPending=false;PropertyChanged?.Invoke(this,new(null));}
    }
    public string Resolution => $"{Asset.Width} × {Asset.Height}";
    public string Duration => Asset.IsImage?UiLanguage.Text("图片"):TimeSpan.FromSeconds(Math.Max(0,Asset.Duration)).ToString(@"hh\:mm\:ss");
    internal bool? SourceAvailable {get {_=RefreshSourceStatusAsync();return sourceAvailable;}}
    public string Availability => SourceAvailable!=false ? (Asset.IsImage?UiLanguage.Text("图片 · "):UiLanguage.Text("视频 · "))+Asset.Codec.ToUpperInvariant() : UiLanguage.Text("来源失效");
    public string Description => Describe(Asset,SourceAvailable);
    internal static string Describe(MediaAsset asset,bool? sourceAvailable=true) =>
        $"{asset.Width} × {asset.Height}  ·  {asset.Codec.ToUpperInvariant()}  ·  {TimeSpan.FromSeconds(Math.Max(0,asset.Duration)):mm\\:ss}  ·  {LibraryPresentation.FileSize(asset.Bytes)}"+(sourceAvailable!=false?"":UiLanguage.Text("  ·  来源失效"));
    internal Task RefreshSourceStatusAsync(bool force=false)
    {
        if(!active)return Task.CompletedTask;
        if(sourceCheck is {IsCompleted:false})return sourceCheck;
        if(!force&&sourceAvailable.HasValue&&Environment.TickCount64-sourceCheckedAt<5000)return Task.CompletedTask;
        return sourceCheck=CheckSourceStatusAsync();
    }
    private async Task CheckSourceStatusAsync()
    {
        // A disconnected drive must never block XAML binding or selection.
        using var cancellation=new CancellationTokenSource();sourceCancellation=cancellation;
        bool exists,acquired=false;
        try {
            await sourceChecks.WaitAsync(cancellation.Token);acquired=true;
            if(!active)return;
            exists=await Task.Run(()=>File.Exists(Asset.Path),cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
        } catch(OperationCanceledException) {return;}
        finally {if(acquired)sourceChecks.Release();if(ReferenceEquals(sourceCancellation,cancellation))sourceCancellation=null;}
        // An unknown source is presented as available until checked. A healthy
        // result needs no text/layout notification for each newly realized card.
        bool changed=(sourceAvailable!=false)!=exists;
        sourceAvailable=exists;sourceCheckedAt=Environment.TickCount64;
        if(!changed)return;
        if(!active) {notificationPending=true;return;}
        PropertyChanged?.Invoke(this,new(nameof(SourceAvailable)));
        PropertyChanged?.Invoke(this,new(nameof(Availability)));
        PropertyChanged?.Invoke(this,new(nameof(Description)));
    }
}
public sealed record ScreenRow(Screen Screen)
{
    public override string ToString() => $"{(Screen.Primary?UiLanguage.Text("主屏幕"):UiLanguage.Text("显示器"))} {Screen.DeviceName.Replace(@"\\.\DISPLAY","")}  ·  {Screen.Bounds.Width} × {Screen.Bounds.Height}";
}

public sealed partial class MainWindow : Window
{
    private readonly LibraryStore store;
    private WallpaperController? wallpapers;
    private DesktopMenuController? menu;
    private Action? quit;
    private MediaAsset? selected;
    private bool followWallpaperSelection=true;
    private bool refreshingLibrary;
    private string? librarySelectionId;
    private readonly LibraryImportLifetime importLifetime=new();
    private LibraryImportCoordinator libraryImports=null!;
    private bool loaded;
    private bool performancePageSelected,performanceShutdown;
    private int applying;
    private bool refreshingScreens;
    public event Action? LoadedReady;
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd,int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    public MainWindow(LibraryStore store,ControlCenterServices modules)
    {
        this.store=store;this.modules=modules;
        onboardingFirstRun=store.GetSetting("onboarding.v1")==null&&store.GetSetting("window.placement.v1")==null&&store.Assets().Count==0;
        Environment.SetEnvironmentVariable("ZZZWALLPAPER_AUDIO_CONFIG",AudioProcessingPath);
        posterCatalog=new PosterCatalogService(modules.Library,modules.Settings);
        posterCatalog.PosterCommitted+=RefreshPosterDisplays;
        libraryImports=new LibraryImportCoordinator(modules.Library,posterCatalog,importLifetime,Translate);
        InitializeComponent();InitializeResolutionScale();InitializeOperationDiagnostics();AttachLibraryPage();InitializeSharingPresenter();InitializeNotices();InitializeInputFocus();InitializeShell();InitializeHomeChrome();InitializeCassetteNavigation();InitializeEffects();InitializePlaybackEditor();InitializeSettings();InitializeAppearance();InitializeSharingPage();
        InitializeWindowPlacement();InitializeLibrarySelection();
        AppWindow.Changed+=(_,args)=> {
            // Moving a visible window does not change monitoring or motion.
            if(args.DidVisibilityChange||args.DidPresenterChange||args.DidSizeChange)
                UpdatePerformanceVisibility();
        };
        Activated+=(_,_)=>UpdatePerformanceVisibility();
        Root.Unloaded+=(_,_)=>StopPerformanceMonitoring();
        Closed+=(_,_)=>ShutdownPerformanceMonitor();
        RefreshScreens();RefreshFolders();RefreshLibrary();LoadPlaybackSettings();Navigate("wallpaper");InitializeNavigation();InitializeUndo();
    }
    public void Connect(WallpaperController controller,DesktopMenuController menuController,Action exit)
    {
        wallpapers=controller;menu=menuController;quit=exit;RefreshScreenMenuSettings();
        controller.FullscreenPause=modules.Settings.Get("fullscreenPause")!="false";FullscreenSwitch.IsOn=controller.FullscreenPause;
        controller.Changed+=RefreshStatus;LoadPlaybackSettings();RefreshStatus();SyncResourceLoadPauseSettings();
    }
    private void Root_Loaded(object sender,RoutedEventArgs e) {if(loaded)return;loaded=true;LoadedReady?.Invoke();UpdatePerformanceVisibility();_=RepairLibraryPosters();}
    public void ShowControlCenter() {EnsureWindowPlacementVisible();nint handle=WinRT.Interop.WindowNative.GetWindowHandle(this);ShowWindow(handle,IsIconic(handle)?9:5);Activate();SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));UpdatePerformanceVisibility();UpdateShellWordmark();}
    public void HideToTray() {CommitSpectrumInput();SaveWindowPlacement();CloseWidgetDrawer(false);widgets?.FlushPendingSave();CloseQueue(false);EndEffectEditing();StopPerformanceMonitoring();StopHomeMotion();ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),0);}
    private void UpdatePerformanceVisibility()
    {
        if(performanceShutdown)return;
        nint hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool visible=Root.IsLoaded&&IsWindowVisible(hwnd)&&!IsIconic(hwnd);
        LibraryView.SetActive(visible&&navigationPage=="library");
        if(LibraryView.IsActive&&LibraryView.NeedsRefresh)RefreshLibrary(LibraryView.PendingSelection);
        if(!performancePageSelected)StopDetailedPlayerMonitoring();
        if(visible)StartPerformanceMonitoring();
        else StopPerformanceMonitoring();
    }
    public void ShutdownPerformanceMonitor() {performanceShutdown=true;DisposeResourceLoadPauseMonitoring();DisposePerformanceMonitoring();}
    public void ShowError(string text,Exception? exception=null) {OperationTrace.Fault(OperationTrace.Current,"ui_error",exception?.ToString()??text);AppLog.Error("UI",text,exception);ShowNotice(Translate(text),InfoBarSeverity.Error,UiLanguage.Text("操作未完成"));}
    private void Inform(string text) => ShowNotice(text,InfoBarSeverity.Informational);
    private static string Translate(string text) => UiLanguage.Current == UiLanguage.English ? text : text.Replace("Source missing or unreadable","来源不存在或没有读取权限").Replace("No decodable video stream in this file","文件中没有可解码的视频轨").Replace("Explorer desktop host not available on this Windows build","当前 Windows 桌面宿主不可用，请确保 Explorer 已运行").Replace("HDR10/HLG requires verified SDR tone mapping; use an SDR source in this build","当前版本尚未验证 HDR 到 SDR 的色调映射，请使用 SDR 视频").Replace("Open source: Invalid data found when processing input","文件损坏、加密或格式无法读取");
    public void Navigate(string page)
    {
        using var trace=OperationTrace.Enter(OperationTrace.Current??OperationTrace.Begin(navigationPage,"navigate",page));
        OperationTrace.Record(OperationTrace.Current,"navigation","requested",page);
        int request=++navigationSaveRequest;
        if(effectsReady) {
            if(navigationReady)CommitSpectrumInput();
            effectSession.StopSchedule();effectSession.QueuePendingSave();
            if(store.HasPendingEffectWrite||effectSession.HasQueuedSaves) { _=NavigateAfterSaveAsync(page,request);return; }
        }
        NavigateCore(page);
    }
    private void NavigateCore(string page)
    {
        DismissHelp();homePositionFlyout?.Hide();
        if(page=="sharing")page="presets";
        bool pageChanged=page!=navigationPage;
        if(pageChanged&&(page=="widgets"||navigationPage=="widgets"))ResetWidgetDisclosureSession();
        if(pageChanged&&(page=="display"||navigationPage=="display"))ResetEffectDisclosureSession();
        bool settingsCategoryChanged=pageChanged&&IsSettingsPage(navigationPage)&&IsSettingsPage(page);
        // Settings tabs share one workspace. Finish any pending entry before
        // switching tabs so a quick click cannot leave the shared cards faded.
        if(settingsCategoryChanged)StopPageEntry();
        if(page!="wallpaper")homePreviewRequest=null;
        if(navigationReady)CommitSpectrumInput();
        if(page!=navigationPage||(page=="widgets"&&(selectedWidgetId!=null||selectedLayoutGroupId!=null))||(page=="display"&&selectedEffectKind!=null))RememberNavigation();
        if(page=="library")ClearLibrarySelection();
        navigationPage=page;
        OperationTrace.Record(OperationTrace.Current,"navigation","controls_updating",page);
        widgets?.FlushPendingSave();
        if(page=="widgets")ResetWidgetNavigation();
        if(page=="display") {FlushEffectChanges();ResetEffectsNavigation();}
        if(page!="display")EndEffectEditing();
        WallpaperPanel.Visibility=page=="wallpaper"?Visibility.Visible:Visibility.Collapsed;
        LibraryPanel.Visibility=page=="library"?Visibility.Visible:Visibility.Collapsed;
        LibraryView.SetActive(page=="library");
        if(page=="library"&&LibraryView.NeedsRefresh) {
            RefreshFolders();RefreshLibrary(LibraryView.PendingSelection);
        }
        DisplayPanel.Visibility=page=="display"?Visibility.Visible:Visibility.Collapsed;
        UpdateShellPage(page);
        SettingsPanel.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;
        WidgetsPanel.Visibility=page=="widgets"?Visibility.Visible:Visibility.Collapsed;
        PerformancePanel.Visibility=page=="performance"?Visibility.Visible:Visibility.Collapsed;
        performancePageSelected=page=="performance";UpdatePerformanceVisibility();
        if(performancePageSelected)RefreshPerformanceDetails();
        if(page=="widgets")RefreshWidgets();
        if(page=="presets")RefreshSharingPage();
        foreach(var button in new[]{WallpaperNav,LibraryNav,DisplayNav,WidgetsNav,PresetsNav,SettingsNav})
        {
            bool active=(string)button.Tag==MainNavigationPage(page);
            button.Background=active?HomeBrush("InteractionAccentBrush"):new SolidColorBrush(Windows.UI.Color.FromArgb(0,0,0,0));
            button.Foreground=new SolidColorBrush(active?Windows.UI.Color.FromArgb(255,13,15,0):Windows.UI.Color.FromArgb(255,235,235,235));
        }
        PageTitle.Text=IsSettingsPage(page)?UiLanguage.Text("设置"):page switch {"library"=>UiLanguage.Text("素材库"),"display"=>UiLanguage.Text("画面效果"),"widgets"=>UiLanguage.Text("组件"),"presets"=>UiLanguage.Text("预设"),_=>UiLanguage.Text("我的桌面")};
        PageSubtitle.Text=page switch {"library"=>"MEDIA LIBRARY","display"=>"VISUAL EFFECTS","widgets"=>"DESKTOP WIDGETS","presets"=>"PRESETS","performance"=>"SETTINGS / PERFORMANCE","appearance"=>"SETTINGS / APPEARANCE","packs"=>"SETTINGS / EXPANSIONS","history"=>"SETTINGS / UPDATE HISTORY","settings"=>"SETTINGS / GENERAL",_=>"WALLPAPER"};
        Title="ZZZwallpaper · "+PageTitle.Text+(IsSettingsPage(page)?" · "+(page switch {"appearance"=>UiLanguage.Text("外观"),"packs"=>UiLanguage.Text("扩展包"),"performance"=>UiLanguage.Text("性能监控"),"history"=>UiLanguage.Text("更新历史"),_=>UiLanguage.Text("通用")}):"");
        SetHomeChrome(page);
        if(page=="library")RefreshLibraryInformation();
        RefreshStatus();
        if(pageChanged&&!settingsCategoryChanged)AnimatePageEntry(page);
        OperationTrace.Record(OperationTrace.Current,"navigation","controls_updated",page);
    }
    private void Nav_Click(object sender,RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);
    public void RefreshScreens()
    {
        EndEffectEditing();
        string? previous=(ScreenPicker.SelectedItem as ScreenRow)?.Screen.DeviceName??modules.Settings.Get("preferredScreen");
        // AllScreens contains the displays currently enabled in the desktop topology.
        // Rebuilding ItemsSource fires SelectionChanged, so do not persist its
        // temporary empty selection or replace the saved preference on hot-unplug.
        var screens=Screen.AllScreens.Select(s=>new ScreenRow(s)).ToList();
        refreshingScreens=true;
        try {
            ScreenPicker.ItemsSource=screens;
            ScreenPicker.SelectedItem=screens.FirstOrDefault(s=>s.Screen.DeviceName==previous)??screens.FirstOrDefault(s=>s.Screen.Primary)??screens.FirstOrDefault();
        } finally {refreshingScreens=false;}
        ScreenDetails.Text=string.Join("\n",screens.Select(x=>x.ToString()));
        RefreshWidgets();RefreshScreenMenuSettings();
    }
    public void SelectScreen(string device)
    {
        if(ScreenPicker.ItemsSource is IEnumerable<ScreenRow> screens)ScreenPicker.SelectedItem=screens.FirstOrDefault(x=>x.Screen.DeviceName==device)??ScreenPicker.SelectedItem;
    }
    private Screen? SelectedScreen => (ScreenPicker.SelectedItem as ScreenRow)?.Screen;
    private FillMode DraftFill => FillPicker.SelectedIndex==2?homeBaseFill:FillPicker.SelectedIndex==1?FillMode.Contain:FillMode.Cover;
    private void ApplyLibraryRows(IReadOnlyList<MediaAsset> assets,string? id=null)
    {
        if(!LibraryView.IsActive) {LibraryView.NeedsRefresh=true;LibraryView.PendingSelection=id;return;}
        LibraryView.NeedsRefresh=false;LibraryView.PendingSelection=null;
        LibraryList.ResetInteraction();
        bool hadSelection=LibraryList.SelectedItems.Count>0;
        var keepIds=id==null?SelectedLibraryAssets().Select(a=>a.Id).ToHashSet():new HashSet<string> {id};
        string? primaryId=id??librarySelectionId;
        var previous=LibraryList.ItemsSource as IReadOnlyList<LibraryRow>??LibraryList.Items.OfType<LibraryRow>().ToArray();
        var previousRows=previous.ToDictionary(row=>row.Asset.Id);
        var rows=assets.Select(x=>previousRows.TryGetValue(x.Id,out var row)&&row.Asset==x?row:CreateLibraryRow(x)).ToList();
        for(int index=0;index<rows.Count;index++)rows[index].LibraryOrder=index;
        bool changed=!previous.SequenceEqual(rows);
        if(changed) {
            var retained=rows.ToHashSet();
            foreach(var row in previous)if(!retained.Contains(row)) {row.SetActive(false);row.PropertyChanged-=LibraryRowChanged;}
        }
        refreshingLibrary=true;
        try {
            if(changed)LibraryList.ItemsSource=rows;
            // Do not clear and re-add unchanged selection: each mutation makes
            // WinUI update selection state and remeasure realized containers.
            foreach(var row in LibraryList.SelectedItems.OfType<LibraryRow>().Where(row=>!keepIds.Contains(row.Asset.Id)).ToArray())
                LibraryList.SelectedItems.Remove(row);
            foreach(var row in rows.Where(row=>keepIds.Contains(row.Asset.Id)))
                if(!LibraryList.SelectedItems.Contains(row))LibraryList.SelectedItems.Add(row);
        } finally {refreshingLibrary=false;}
        var primary=ResolveLibrarySelectionRow(rows.FirstOrDefault(row=>row.Asset.Id==primaryId));
        librarySelectionId=primary?.Asset.Id;
        RefreshLibraryApplied(force:true);
        EmptyLibrary.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        LibraryEmptyState.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyLibrary.Text=libraryFilter.IsFiltered?UiLanguage.Text("没有符合筛选条件的素材"):SelectedLibraryFolder==null?UiLanguage.Text("暂无素材"):UiLanguage.Text("此文件夹暂无素材");
        bool refreshed=true;
        if(primary!=null)SetSelected(primary.Asset);
        else if(hadSelection)SetSelected(SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset,followWallpaper:true);
        else if(selected==null||modules.Library.FindAssetByPath(selected.Path)?.Id!=selected.Id)SetSelected(null);
        else refreshed=false;
        if(!refreshed)RefreshLibraryInformation();
        LoadPlaybackSettings();QueueLibrarySelectionScroll();
        var playing=SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset;
        RefreshHomePreview(HomePreviewTarget(playing),playing,forcePoster:true);
        if(LibraryList.ItemsPanelRoot is Panel panel)
            foreach(var container in panel.Children.OfType<GridViewItem>())
                if(container.Content is LibraryRow row)_=row.RefreshSourceStatusAsync(force:true);
    }
    private IEnumerable<LibraryRow> SelectedLibraryRows()=>LibraryList.SelectedItems.OfType<LibraryRow>()
        .OrderBy(row=>row.LibraryOrder==int.MaxValue?LibraryList.Items.IndexOf(row):row.LibraryOrder);
    private List<MediaAsset> SelectedLibraryAssets()=>SelectedLibraryRows().Select(row=>row.Asset).ToList();
    private LibraryRow CreateLibraryRow(MediaAsset asset)
    {
        var row=new LibraryRow(asset) {PosterPath=modules.Library.Poster(asset.Id)};
        row.SetActive(LibraryView.IsActive);
        row.PropertyChanged+=LibraryRowChanged;
        return row;
    }
    private void LibraryRowChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
    {
        if(!libraryShutdown&&LibraryView.IsActive&&args.PropertyName==nameof(LibraryRow.SourceAvailable)&&
            LibraryList.SelectedItems.Count==1&&ReferenceEquals(LibraryList.SelectedItems[0],sender))
            RefreshLibraryInformation();
    }
    private void Library_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(refreshingLibrary)return;
        var preferred=LibraryList.ConsumeInteraction() as LibraryRow;
        if(preferred==null||!LibraryList.SelectedItems.Contains(preferred))
            preferred=e.AddedItems.OfType<LibraryRow>().LastOrDefault(row=>LibraryList.SelectedItems.Contains(row));
        CommitLibrarySelection(ResolveLibrarySelectionRow(preferred));
    }
    private void ClearLibrarySelection()
    {
        LibraryList.ResetInteraction();
        refreshingLibrary=true;try {LibraryList.SelectedItems.Clear();librarySelectionId=null;}finally {refreshingLibrary=false;}
        RefreshLibraryInformation();
    }
    private void SetSelected(MediaAsset? asset,bool followWallpaper=false,bool updateStatus=true)
    {
        followWallpaperSelection=followWallpaper||asset==null;
        selected=asset;
        ApplyButton.IsEnabled=asset!=null;
        if(navigationPage=="library")RefreshLibraryInformation();
        if(navigationPage=="display")LoadEffectDraft();
        // Library selection does not change playback, audio or effect targets.
        if(updateStatus&&navigationPage!="library")RefreshStatus();
    }
    public void RefreshStatus()
    {
        RefreshScreenFeatureButtons();RefreshLibraryApplied();
        if(menu!=null) {MenuStatus.Text=menu.Status;FooterStatus.Text="●  "+menu.Status;}
        SyncMenuScreenSwitches();
        var current=SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName);
        var preview=HomePreviewTarget(current?.Asset);
        // Keep editing/library selection separate from the actual home cover.
        // On Home the apply controls must refer to the video shown by that cover.
        if(navigationPage=="wallpaper")
        {
            if(selected!=preview || followWallpaperSelection!=(homePreviewRequest==null))
                SetSelected(preview,followWallpaper:homePreviewRequest==null,updateStatus:false);
        }
        else if(wallpapers!=null&&followWallpaperSelection&&!(navigationPage=="display"&&selected!=null)&&selected!=current?.Asset)
            SetSelected(current?.Asset,followWallpaper:true,updateStatus:false);
        if(navigationPage=="display") {LoadEffectDraft();RefreshEffectsContext();}
        RefreshAudioStatus();
        // The home controls catch up immediately on navigation. Updating their
        // hidden visual tree every runtime tick only competes with the library.
        if(navigationPage!="wallpaper")return;
        RefreshHomePreview(preview,current?.Asset);
        if(wallpapers==null)return;
        PauseButton.IsEnabled=current!=null;
        CancelWallpaperButton.IsEnabled=SelectedScreen!=null&&wallpapers.CanCancel(SelectedScreen.DeviceName);
        CancelAllWallpapersButton.IsEnabled=wallpapers.CanCancel();
        RefreshHomePause(current?.Pause.Reasons.HasFlag(PauseReason.User)==true);
        if(current==null) {PlaybackStatus.Text=applying>0?UiLanguage.Text("正在准备新壁纸…"):UiLanguage.Text("当前屏幕使用系统壁纸");}
        else
        {
            PlaybackStatus.Text=(applying>0?UiLanguage.Text("正在准备 · 当前仍播放 "):current.Player.ActualState+" · ")+current.Asset.Name;
        }
        RefreshPlaybackStatus();
    }
    private static string PauseText(PauseReason reasons)
    {
        var labels=new List<string>();if(reasons.HasFlag(PauseReason.User))labels.Add(UiLanguage.Text("手动"));if(reasons.HasFlag(PauseReason.SessionLocked))labels.Add(UiLanguage.Text("锁屏"));if(reasons.HasFlag(PauseReason.DisplayOff))labels.Add(UiLanguage.Text("熄屏"));if(reasons.HasFlag(PauseReason.Suspend))labels.Add(UiLanguage.Text("休眠"));if(reasons.HasFlag(PauseReason.Fullscreen))labels.Add(UiLanguage.Text("全屏应用"));if(reasons.HasFlag(PauseReason.HighResourceLoad))labels.Add(UiLanguage.Text("其他程序 CPU / GPU 占用较高"));return string.Join("、",labels);
    }
    private async Task<string?> PickVideo()
    {
        var picker=new FileOpenPicker {SuggestedStartLocation=PickerLocationId.VideosLibrary,ViewMode=PickerViewMode.Thumbnail};
        foreach(var extension in MediaFormats.Videos.Concat(MediaFormats.Images))picker.FileTypeFilter.Add(extension);WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();return file?.Path;
    }
    public async Task Import(bool relink=false)
    {
        if(!relink) {await ImportVideos();return;}
        if(importLifetime.Active!=null||libraryShutdown||libraryFileBusy)return;
        var previous=selected;
        if(previous==null) {Inform(UiLanguage.Text("先选择需要重新关联的视频。"));return;}
        var request=BeginLibraryImport();
        try
        {
            var path=await PickVideo();request.Token.ThrowIfCancellationRequested();
            if(libraryShutdown||string.IsNullOrEmpty(path))return;
            var existing=modules.Library.FindAssetByPath(path);
            if(existing!=null&&existing.Id!=previous.Id)throw new IOException(UiLanguage.Text("这个文件已关联到另一个库内条目。"));
            Inform(UiLanguage.Text("正在读取媒体信息并挑选清晰封面…"));
            var result=await ProbePreservingPoster(path,previous.Id,request.Token);
            request.Token.ThrowIfCancellationRequested();if(libraryShutdown)return;
            var info=new FileInfo(path);var asset=new MediaAsset(previous.Id,info.FullName,Path.GetFileNameWithoutExtension(path),info.Length,result.Duration,result.Width,result.Height,result.Codec,result.Color,result.Decoder);
            modules.Library.Upsert(asset);modules.Library.RememberAssetAudio(asset,result.HasAudio);RememberPosterSelection(asset.Id,path);RefreshLibrary(asset.Id);Inform(UiLanguage.Text("已重新关联，应用后生效。"));
        }
        catch(OperationCanceledException) {if(!libraryShutdown) {if(request.IsCancellationRequested)Inform(UiLanguage.Text("关联已取消"));else ShowError(UiLanguage.Text("读取媒体超时，请检查来源后重新关联。"));}}
        catch(Exception ex) {if(!libraryShutdown)ShowError(ex.Message,ex);}
        finally {CompleteLibraryImport(request);}
    }
    private async void Import_Click(object sender,RoutedEventArgs e) => await Import();
    private async void Relink_Click(object sender,RoutedEventArgs e) {if(SelectedLibraryAssets() is [var asset]) {SetSelected(asset);await Import(true);}}
    private void CancelImport_Click(object sender,RoutedEventArgs e) => importLifetime.Active?.Cancel();
    private bool applyToAllScreens;
    private void HomeApplyCurrentScope_Click(object sender,RoutedEventArgs e)=>SetHomeApplyScope(false);
    private void HomeApplyAllScope_Click(object sender,RoutedEventArgs e)=>SetHomeApplyScope(true);
    private void SetHomeApplyScope(bool all) {
        applyToAllScreens=all;HomeApplyCurrentScope.IsChecked=!all;HomeApplyAllScope.IsChecked=all;
        HomeApplyLabel.Text=all?UiLanguage.Text("应用全部屏幕"):UiLanguage.Text("应用当前屏幕");
    }
    private async void Apply_Click(object sender,RoutedEventArgs e) => await ApplySelection(applyToAllScreens);
    private async void ApplyAll_Click(object sender,RoutedEventArgs e) => await ApplySelection(true);
    private async Task ApplySelection(bool all)
    {
        if(applying>0||libraryShutdown)return;
        if(selected==null||wallpapers==null||SelectedScreen==null) {Inform(UiLanguage.Text("请先从素材库选择一个素材。"));return;}
        EndEffectEditing();
        var asset=selected;var fill=DraftFill;var effects=DraftEffects;var edit=effectSession.Revision;var screens=all?Screen.AllScreens:[SelectedScreen];var cancelEpoch=wallpapers.CancellationEpoch;
        applying++;BusyRing.IsActive=true;RefreshStatus();
        try
        {
            foreach(var screen in screens) {
                if(cancelEpoch!=wallpapers.CancellationEpoch)throw new OperationCanceledException();
                var current=wallpapers.Running.GetValueOrDefault(screen.DeviceName);
                bool keepPause=current?.Asset.Id==asset.Id&&current.Pause.Reasons.HasFlag(PauseReason.User);
                var latest=effectSession.LatestFor(screen.DeviceName,asset.Id);
                bool editedSinceApply=latest!=null&&latest.Revision>edit;
                var profile=modules.Effects.ScreenProfile(screen.DeviceName);
                await wallpapers.Apply(asset,screen,editedSinceApply?latest!.Fill:profile?.Fill??current?.Binding.Fill??fill,keepPause,
                    editedSinceApply?latest!.Effects:profile?.Effects??current?.Binding.Effects??VideoEffects.Original);
            }
            if(libraryShutdown)return;
            if(selected?.Id==asset.Id&&SelectedScreen is { } activeScreen&&wallpapers.Running.GetValueOrDefault(activeScreen.DeviceName)?.Asset.Id==asset.Id)
                followWallpaperSelection=true;
            if(edit==effectSession.Revision)SetEffectsSaveStatus(UiLanguage.Text("已应用并保存"));
            Inform(all?UiLanguage.Text("壁纸已应用到全部屏幕"):UiLanguage.Text("壁纸已应用"));
        }
        catch(OperationCanceledException) { /* Superseded requests must not overwrite current status. */ }
        catch(Exception ex) {if(!libraryShutdown)ShowError(UiLanguage.Text("应用失败，原壁纸已保留。")+ex.Message,ex);}
        finally {applying--;if(applying==0)effectSession.ClearRetained();if(!libraryShutdown) {BusyRing.IsActive=applying>0||importLifetime.Active!=null;RefreshStatus();}}
    }
    private void Pause_Click(object sender,RoutedEventArgs e) {if(SelectedScreen!=null)wallpapers?.ToggleUserPause(SelectedScreen.DeviceName);}
    private void CancelWallpaper_Click(object sender,RoutedEventArgs e) {try {EndEffectEditing();if(SelectedScreen!=null) {wallpapers?.CancelWallpaper(SelectedScreen.DeviceName);Inform(UiLanguage.Text("已恢复当前屏幕的系统壁纸"));}}catch(Exception ex) {ShowError(UiLanguage.Text("取消壁纸失败：")+ex.Message,ex);}}
    private void CancelAllWallpapers_Click(object sender,RoutedEventArgs e) {try {EndEffectEditing();wallpapers?.CancelWallpaper();Inform(UiLanguage.Text("已恢复全部屏幕的系统壁纸"));}catch(Exception ex) {ShowError(UiLanguage.Text("取消壁纸失败：")+ex.Message,ex);}}
    private void Screen_Changed(object sender,SelectionChangedEventArgs e) {
        if(!refreshingScreens&&SelectedScreen is { } screen)modules.Settings.Set("preferredScreen",screen.DeviceName);
        CloseQueue(false);LoadEffectDraft();LoadPlaybackSettings();RefreshStatus();
    }
    private async void SelectForApply_Click(object sender,RoutedEventArgs e)
    {
        if(applying>0||SelectedLibraryAssets() is not [var asset])return;
        try {
            await FlushEffectChangesAsync();
            if(libraryShutdown)return;
            var screen=SelectedScreen?.DeviceName;
            homePreviewRequest=new(asset,screen,screen==null?null:wallpapers?.Running.GetValueOrDefault(screen)?.Asset.Id);
            SetSelected(asset);Navigate("wallpaper");
            await ApplySelection(false);
        }catch(Exception error) {ShowError(UiLanguage.Text("查看并应用失败：")+error.Message,error);}
    }
    private async void Remove_Click(object sender,RoutedEventArgs e)
    {
        var assets=SelectedLibraryAssets();
        if(assets.Count==0||wallpapers==null)return;
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=assets.Count==1?UiLanguage.Text("移除这个素材引用？"):UiLanguage.Format($"移除选中的 {assets.Count} 个素材引用？"),Content=UiLanguage.Text("保留源文件，相关屏幕恢复系统壁纸。"),PrimaryButtonText=UiLanguage.Text("移除引用"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        int removed=0;
        try {
            EndEffectEditing();
            using(var history=store.BeginUndoBatch("移除 "+assets.Count+" 个素材引用"))
                foreach(var asset in assets) {wallpapers.RemoveAsset(asset.Id);removed++;}
            Inform(UiLanguage.Format($"已移除 {removed} 个素材引用，原素材文件已保留。"));
        } catch(Exception error) {ShowError(UiLanguage.Format($"已移除 {removed} 个引用，其余未完成：")+error.Message,error);}
        finally {RefreshLibrary();}
    }
    private void DisableMenu_Click(object sender,RoutedEventArgs e) {menu?.SetEnabled(false);RefreshStatus();Inform(UiLanguage.Text("接管已暂停。再次右键桌面可使用系统菜单。"));}
    private void EnableMenu_Click(object sender,RoutedEventArgs e)=>ResumeDesktopMenu();
    private void Fullscreen_Toggled(object sender,RoutedEventArgs e)
    {
        if(wallpapers==null||syncingSettings)return;
        try {modules.Settings.Set("fullscreenPause",FullscreenSwitch.IsOn?"true":"false");wallpapers.FullscreenPause=FullscreenSwitch.IsOn;wallpapers.CheckFullscreen();}
        catch(Exception ex) {syncingSettings=true;FullscreenSwitch.IsOn=wallpapers.FullscreenPause;syncingSettings=false;ShowError(UiLanguage.Text("暂停设置未保存：")+ex.Message,ex);}
    }
    private void Exit_Click(object sender,RoutedEventArgs e) => quit?.Invoke();
}
