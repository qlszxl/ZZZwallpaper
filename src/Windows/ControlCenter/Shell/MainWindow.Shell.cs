using LumaWall.Contracts;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumaWall.ControlCenter;

internal static class UiFonts
{
    public static FontFamily Display => (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["DisplayFont"];
    public static FontFamily Body => (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["BodyFont"];
}

public sealed partial class MainWindow
{
    private bool homeTransportLayoutInProgress, homeSettingsLayoutInProgress, libraryLayoutInProgress, pageHeaderLayoutInProgress;

    private void InitializeShell()
    {
        SettingsVersionLabel.Text=UiLanguage.Format($"ZZZwallpaper · 正式版 v{ApplicationVersion}");
        InitializeWindowChrome();
        LibraryList.Loaded += (_, _) => {LibraryList.QueueCardResize();QueueLibrarySelectionScroll();};
        // The list host spans both columns in XAML, including before Loaded.
        // Cards and the information panel share one workspace measurement.
        LibraryWorkspace.SizeChanged += (_, _) => {LayoutLibrary(LibraryPanel.ActualWidth);LibraryList.QueueCardResize();};
        LibraryPanel.SizeChanged += (_, e) => LayoutLibrary(e.NewSize.Width);
        WidgetsPanel.SizeChanged += (_, _) => LayoutWidgetCards();
        HomeTransport.SizeChanged += (_, e) => LayoutHomeTransport(e.NewSize.Width);
        HomeSettingsGrid.SizeChanged += (_, e) => LayoutHomeSettings(e.NewSize.Width);
        PageHeader.SizeChanged+=(_,e)=>LayoutPageHeader(e.NewSize.Width);
        PageHeaderActions.SizeChanged+=(_,_)=>LayoutPageHeader(PageHeader.ActualWidth);
        PageHeadingText.SizeChanged+=(_,_)=>LayoutPageHeader(PageHeader.ActualWidth);
        EffectsParameterEditor.SizeChanged+=(_,_)=>LayoutEffectsPage();
        EffectsCollectionHeading.SizeChanged+=(_,_)=>LayoutEffectsPage();
        EffectsCollectionActions.SizeChanged+=(_,_)=>LayoutEffectsPage();
        InitializeUtilityPages();
    }

    private void UpdateShellPage(string page)
    {
        CloseQueue(false);
        CloseEffectsTarget(false);
        CloseEffectsAdd(false);
        CloseWidgetDrawer(false);
        WallpaperScroll.Visibility = WallpaperPanel.Visibility;
        UpdateSettingsNavigation(page);
        AppearanceScroll.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        SharingScroll.Visibility = page == "presets" ? Visibility.Visible : Visibility.Collapsed;
        ExpansionScroll.Visibility = page == "packs" ? Visibility.Visible : Visibility.Collapsed;
        HistoryScroll.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        WidgetsScroll.Visibility = page == "widgets" ? Visibility.Visible : Visibility.Collapsed;
        PerformanceScroll.Visibility = page == "performance" ? Visibility.Visible : Visibility.Collapsed;
        PagePinnedActions.Visibility=page is "display" or "widgets"?Visibility.Visible:Visibility.Collapsed;
        EffectsAddButton.Visibility=page=="display"?Visibility.Visible:Visibility.Collapsed;
        WidgetsAddButton.Visibility=page=="widgets"?Visibility.Visible:Visibility.Collapsed;
        foreach (var (element, target) in new (FrameworkElement, string)[] {
            (HomeHeaderActions, "wallpaper"), (LibraryHeaderActions, "library"),
            (EffectsHeaderActions, "display"), (WidgetsHeaderActions, "widgets"), (PerformanceHeaderActions, "performance") })
            element.Visibility = page == target ? Visibility.Visible : Visibility.Collapsed;
        ShellFooter.Visibility = Visibility.Visible;
        Grid.SetRowSpan(ShellPageContent, 1);
        if (Root.ActualWidth > 0) LayoutShell(Root.ActualWidth);
        if(page=="library")QueueLibrarySelectionScroll();
    }

    private void MenuNavigate_Click(object sender, RoutedEventArgs e) => Navigate((string)((MenuFlyoutItem)sender).Tag);
    private async void HomeImportVideo_Click(object sender, RoutedEventArgs e) => await Import();
    private void HomeImportFolder_Click(object sender, RoutedEventArgs e) => ImportFolder_Click(sender, e);

    private void QueueOpen_Click(object sender, RoutedEventArgs e)
    {
        CloseEffectsTarget(false);
        CloseEffectsAdd(false);
        QueueOverlay.Visibility = Visibility.Visible;
        EffectsBodyHost.IsEnabled = ScreenPicker.IsEnabled = false;
        QueueCloseButton.Focus(FocusState.Programmatic);
    }
    private void QueueClose_Click(object sender, RoutedEventArgs e) => CloseQueue(true);
    private void CloseQueue(bool restoreFocus)
    {
        if (QueueOverlay == null || QueueOverlay.Visibility != Visibility.Visible) return;
        QueueOverlay.Visibility = Visibility.Collapsed;
        EffectsBodyHost.IsEnabled = ScreenPicker.IsEnabled = true;
        if (restoreFocus) QueueOpenButton.Focus(FocusState.Programmatic);
    }

    private void RefreshLibraryInformation()
    {
        if(LibraryPanel.Visibility!=Visibility.Visible)return;
        var rows=SelectedLibraryRows().ToArray();
        var assets=rows.Select(row=>row.Asset).ToList();
        var asset=assets.Count==1?assets[0]:null;
        bool multiple=assets.Count>1;
        LibraryInformationPanel.Visibility=assets.Count==0?Visibility.Collapsed:Visibility.Visible;

        int posterWidth=PosterImageCache.SizeBucket(Math.Max(384,LibraryInfoPosterFrame.ActualWidth)*(Root.XamlRoot?.RasterizationScale??1));
        LibraryInfoPoster.Source=asset==null?null:LoadPosterImage(modules.Library.Poster(asset.Id),posterWidth);
        LibraryInfoTitle.Text=multiple?UiLanguage.Format($"已选择 {assets.Count} 个素材"):asset?.Name??"";
        LibraryInfoMetadata.Text=multiple?UiLanguage.Format($"共 {LibraryPresentation.FileSize(assets.Sum(a=>a.Bytes))}"):asset==null?"":rows[0].Description;
        LibraryInfoPath.Text=multiple?UiLanguage.Text("已多选 ")+assets.Count+UiLanguage.Text(" 个素材"):asset?.Path??"";
        LibraryApplyButton.IsEnabled=LibraryRelinkButton.IsEnabled=assets.Count==1;
        LibraryPosterButton.IsEnabled=LibraryDecodeTestButton.IsEnabled=assets.Count==1&&asset is {IsImage:false}&&rows[0].SourceAvailable!=false;
        LibraryQueueButton.IsEnabled=LibraryRemoveButton.IsEnabled=assets.Count>0;
        RefreshLibraryFileActions(assets);
        LayoutLibrary(LibraryPanel.ActualWidth);
    }

    private void LibraryGrid_SizeChanged(object sender, SizeChangedEventArgs e) {LibraryList.QueueCardResize();QueueLibrarySelectionScroll();}
    private void ResizeLibraryCards()=>LibraryList.ResizeCards();
    private static void ResizeLibraryCardGrid(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || grid.ActualWidth <= 0) return;
        // Fill the target picker with three equal cards at its normal width;
        // only narrow windows reduce the column count. Keep scrollbar space.
        double available=Math.Max(1,grid.ActualWidth-grid.Padding.Left-grid.Padding.Right-LibraryPresentation.CardScrollbarReserve);
        var (columns,itemWidth)=LibraryPresentation.Grid(available+LibraryPresentation.CardSpacing);
        var margin=new Thickness(0,0,-LibraryPresentation.CardSpacing,0);
        if(panel.Margin!=margin)panel.Margin=margin;
        if(panel.MaximumRowsOrColumns!=columns)panel.MaximumRowsOrColumns=columns;
        if(panel.ItemWidth!=itemWidth)panel.ItemWidth=itemWidth;
    }

    // All repeated card groups use the available logical width, including display scaling.
    private static void FlowCards(Grid grid, int columns)
    {
        int rows = (grid.Children.Count + columns - 1) / columns;
        if (grid.ColumnDefinitions.Count != columns || grid.RowDefinitions.Count != rows)
        {
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new());
            for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        for (int i = 0; i < grid.Children.Count; i++)
        {
            var item = (FrameworkElement)grid.Children[i];
            Grid.SetRow(item, i / columns); Grid.SetColumn(item, i % columns);
        }
        grid.RowSpacing = Math.Max(grid.RowSpacing, 12);
    }
    private void LayoutWidgetCards()
    {
        double width=WidgetsPanel.ActualWidth;
        FlowCards(WidgetListPanel, WidgetListPanel.Children.Count<=1?1:width>=1040?3:width>=620?2:1);
        LayoutCenteredModals();
    }

    private static void StackHeader(Grid grid, FrameworkElement title, FrameworkElement actions, bool stacked)
    {
        if (grid.RowDefinitions.Count != (stacked ? 2 : 1))
        {
            grid.RowDefinitions.Clear();
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (stacked) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        Grid.SetColumnSpan(title, stacked ? 2 : 1);
        Grid.SetRow(actions, stacked ? 1 : 0); Grid.SetColumn(actions, stacked ? 0 : 1);
        Grid.SetColumnSpan(actions, stacked ? 2 : 1);
    }

    private void LayoutHomeTransport(double width)
    {
        if (homeTransportLayoutInProgress || WallpaperPanel.Visibility != Visibility.Visible || width <= 0) return;
        homeTransportLayoutInProgress = true;
        try
        {
            // Measure the action groups, not the filename: a long title must truncate
            // inside the remaining star column instead of forcing another control row.
            var unconstrained = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
            HomePlaybackButtons.Measure(unconstrained);
            HomeApplyButtons.Measure(unconstrained);
            double playbackWidth = Math.Ceiling(HomePlaybackButtons.DesiredSize.Width);
            double applyWidth = Math.Ceiling(HomeApplyButtons.DesiredSize.Width);
            double contentWidth = Math.Max(0, width - HomeTransport.Padding.Left - HomeTransport.Padding.Right);
            double spacing = HomeTransport.ColumnSpacing;
            const double informationBudget = 425;
            bool hasTitle = HomeAssetTitle.Visibility == Visibility.Visible;
            int rows = contentWidth >= informationBudget + playbackWidth + applyWidth + 2 * spacing ? 1 :
                contentWidth >= playbackWidth + applyWidth + spacing ? 2 : 3;
            if (!hasTitle) rows = contentWidth >= playbackWidth + applyWidth + 2 * spacing ? 1 : 2;
            int columns = rows == 1 ? 3 : hasTitle && rows == 2 ? 2 : 1;
            if (HomeTransport.RowDefinitions.Count != rows)
            {
                HomeTransport.RowDefinitions.Clear();
                for (int i = 0; i < rows; i++) HomeTransport.RowDefinitions.Add(new() { Height = GridLength.Auto });
            }
            if (HomeTransport.ColumnDefinitions.Count != columns)
            {
                HomeTransport.ColumnDefinitions.Clear();
                HomeTransport.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 1; i < columns; i++) HomeTransport.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            }
            HomeAssetTitle.MinWidth = 0;
            HomeAssetTitle.MaxWidth = rows == 1 ? Math.Max(0, contentWidth - playbackWidth - applyWidth - 2 * spacing) : contentWidth;
            Grid.SetRow(HomeAssetTitle, 0); Grid.SetColumn(HomeAssetTitle, 0);
            Grid.SetColumnSpan(HomeAssetTitle, rows == 1 ? 1 : columns);
            Grid.SetRow(HomePlaybackButtons, rows == 1 || !hasTitle ? 0 : 1);
            Grid.SetColumn(HomePlaybackButtons, rows == 1 ? 1 : 0);
            Grid.SetColumnSpan(HomePlaybackButtons, 1);
            Grid.SetRow(HomeApplyButtons, rows == 1 ? 0 : rows - 1);
            Grid.SetColumn(HomeApplyButtons, rows == 1 ? 2 : columns == 2 ? 1 : 0);
            Grid.SetColumnSpan(HomeApplyButtons, 1);
            HomePlaybackButtons.HorizontalAlignment = HorizontalAlignment.Left;
            HomeApplyButtons.HorizontalAlignment = columns == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            double available = Root.ActualWidth - (Root.ActualWidth < 1060 ? 142 : 250);
            double preferredHeight = Math.Clamp(Root.ActualHeight - (available < 820 ? 450 : 365), 300, 650);
            HomePreviewCard.Height = Math.Max(preferredHeight, rows == 3 ? 480 : rows == 2 ? 400 : 340);
        }
        finally { homeTransportLayoutInProgress = false; }
    }

    private void LayoutHomeSettings(double width)
    {
        if (homeSettingsLayoutInProgress || WallpaperPanel.Visibility != Visibility.Visible || width <= 0) return;
        homeSettingsLayoutInProgress = true;
        try
        {
            // Six visible fields, with the rotation container occupying two
            // columns. Width comes from the actual card, not the outer window.
            int rows = width >= 1040 ? 1 : width >= 650 ? 2 : 3;
            double[] widths = rows == 1 ? [160, 175, 190, 108, 144, 100] :
                rows == 2 ? [160, 175, 144, 100] : [1, 1];
            if (HomeSettingsGrid.ColumnDefinitions.Count != widths.Length)
            {
                HomeSettingsGrid.ColumnDefinitions.Clear();
                for (int i = 0; i < widths.Length; i++) HomeSettingsGrid.ColumnDefinitions.Add(new());
            }
            // Apply widths even when the initial XAML already has six columns.
            // Reserve readable transition text and keep the numeric field compact.
            for (int i = 0; i < widths.Length; i++)
            {
                bool fixedWidth = rows != 3 && (i >= widths.Length - 2 || (rows == 1 && i == 3));
                HomeSettingsGrid.ColumnDefinitions[i].Width = new GridLength(widths[i], fixedWidth ? GridUnitType.Pixel : GridUnitType.Star);
            }
            if (HomeSettingsGrid.RowDefinitions.Count != rows)
            {
                HomeSettingsGrid.RowDefinitions.Clear();
                for (int i = 0; i < rows; i++) HomeSettingsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            }
            Grid.SetRow(HomeModeSetting, 0); Grid.SetColumn(HomeModeSetting, 0);
            Grid.SetRow(HomeFillSetting, 0); Grid.SetColumn(HomeFillSetting, 1);
            Grid.SetRow(PlaybackRotationControls, rows == 1 ? 0 : 1);
            Grid.SetColumn(PlaybackRotationControls, rows == 1 ? 2 : 0);
            Grid.SetColumnSpan(PlaybackRotationControls, rows == 2 ? 4 : 2);
            Grid.SetRow(HomeTransitionSetting, rows == 3 ? 2 : 0);
            Grid.SetColumn(HomeTransitionSetting, rows == 1 ? 4 : rows == 2 ? 2 : 0);
            Grid.SetRow(HomeMoreSetting, rows == 3 ? 2 : 0);
            Grid.SetColumn(HomeMoreSetting, rows == 1 ? 5 : rows == 2 ? 3 : 1);
            HomeMoreSetting.HorizontalAlignment = rows == 3 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
        finally { homeSettingsLayoutInProgress = false; }
    }

    private void LayoutPageHeader(double width)
    {
        if(pageHeaderLayoutInProgress||width<=0)return;
        pageHeaderLayoutInProgress=true;
        try {
            var unbounded=new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity);
            if(DisplayPanel.Visibility==Visibility.Visible) {
                EffectsHeaderActions.MaxWidth=width;
                EffectsBackActions.Measure(unbounded);
                double backWidth=EffectsBackActions.DesiredSize.Width;
                bool stackActions=backWidth>0&&backWidth+360+12>width;
                StackHeader(EffectsHeaderActions,EffectsTargetActions,EffectsBackActions,stackActions);
                EffectsTargetCaption.Measure(unbounded);
                double captionWidth=EffectsTargetCaption.DesiredSize.Width+EffectsTargetActions.Spacing;
                EffectsTargetButton.Width=Math.Max(120,Math.Min(320,(stackActions?width:width-backWidth-(backWidth>0?12:0))-40)-captionWidth);
                EffectsTargetResolution.Visibility=EffectsTargetButton.Width>=230&&(homeUiSettings?.TextScaleFactor??1)<1.5?Visibility.Visible:Visibility.Collapsed;
            }
            PageHeadingText.Measure(unbounded);PageHeaderActions.Measure(unbounded);
            bool stacked=DisplayPanel.Visibility==Visibility.Visible||navigationPage=="widgets"
                ?PageHeadingText.DesiredSize.Width+PageHeaderActions.DesiredSize.Width+PageHeader.ColumnSpacing>width
                :width<820;
            StackHeader(PageHeader,PageHeadingText,PageHeaderActions,stacked);
        } finally {pageHeaderLayoutInProgress=false;}
    }

    private void LayoutShell(double width)
    {
        double available = width - (width < 1060 ? 142 : 250);
        LayoutPageHeader(PageHeader.ActualWidth>0?PageHeader.ActualWidth:available);
        LayoutLibrary(available);
        LayoutEffectsPage();
        // Estimate before the first arrange; SizeChanged refines this using the real
        // frame/scrollbar width, including DPI scaling and the current control sizes.
        LayoutHomeTransport(Math.Max(0, available - 26));
        LayoutHomeSettings(Math.Max(0, available - 50));
        LayoutUtilityPages();
        LayoutCenteredModals();
        LayoutWidgetCards(); ResizeLibraryCards();
    }

    private (double Width,double WorkspaceWidth,int SelectionShape)? libraryLayoutState;
    private void LayoutLibrary(double width)
    {
        if (libraryLayoutInProgress || LibraryPanel.Visibility != Visibility.Visible || width <= 0) return;
        libraryLayoutInProgress = true;
        try
        {
            // Use the arranged page width when available, so DPI and the thicker
            // shared navigation shell do not squeeze the filter actions or cards.
            width = LibraryPanel.ActualWidth > 0 ? LibraryPanel.ActualWidth : width;
            double workspaceWidth=LibraryWorkspace.ActualWidth>0?LibraryWorkspace.ActualWidth:width;
            var layoutState=(width,workspaceWidth,Math.Min(2,LibraryList.SelectedItems.Count));
            if(libraryLayoutState==layoutState)return;
            libraryLayoutState=layoutState;
            double cardAreaWidth=Math.Max(1,workspaceWidth-LibraryList.Padding.Left-LibraryList.Padding.Right-LibraryPresentation.CardScrollbarReserve);
            var (workspaceColumns,cardWidth)=LibraryPresentation.ResponsiveGrid(cardAreaWidth);
            bool showInfo=LibraryList.SelectedItems.Count>0;
            bool stackInfo=showInfo&&workspaceColumns<LibraryPresentation.WorkspaceColumns;
            bool replaceLastColumn=showInfo&&!stackInfo;
            LibraryInformationPanel.Visibility=showInfo?Visibility.Visible:Visibility.Collapsed;
            double informationLeft=LibraryList.Padding.Left+(workspaceColumns-1)*(cardWidth+LibraryPresentation.CardSpacing);
            LibraryInfoColumn.Width=new GridLength(replaceLastColumn?Math.Max(0,workspaceWidth-informationLeft):0);
            LibraryWorkspace.ColumnSpacing=0;
            LibraryInformationPanel.Width=replaceLastColumn?cardWidth:double.NaN;
            LibraryInformationPanel.HorizontalAlignment=replaceLastColumn?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
            if (LibraryWorkspace.RowDefinitions.Count != (stackInfo ? 2 : 1))
            {
                LibraryWorkspace.RowDefinitions.Clear(); LibraryWorkspace.RowDefinitions.Add(new());
                if (stackInfo) LibraryWorkspace.RowDefinitions.Add(new() { Height = new GridLength(240) });
            }
            Grid.SetRow(LibraryInformationPanel, stackInfo ? 1 : 0);
            Grid.SetColumn(LibraryInformationPanel, stackInfo ? 0 : 1);
            Grid.SetColumnSpan(LibraryInformationPanel,stackInfo?2:1);
            LibraryInfoPosterFrame.Visibility = stackInfo||LibraryList.SelectedItems.Count!=1 ? Visibility.Collapsed : Visibility.Visible;
            LibraryList.SetWorkspaceLayout(workspaceColumns-(replaceLastColumn?1:0),cardWidth);
            ResizeLibraryCards();
        }
        finally { libraryLayoutInProgress = false; }
    }
}
