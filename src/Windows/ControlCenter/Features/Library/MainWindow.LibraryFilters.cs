using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private LibraryFilter libraryFilter=new();
    private readonly TextBox librarySearch=new();
    private readonly ComboBox libraryKind=new(),libraryOrientation=new(),libraryResolution=new(),librarySort=new();
    private readonly TextBlock libraryResultCount=new();
    private readonly Grid libraryFilterOptions=new() {ColumnSpacing=12,RowSpacing=10,Visibility=Visibility.Collapsed};
    private readonly DispatcherTimer librarySearchDelay=new() {Interval=TimeSpan.FromMilliseconds(250)};
    private readonly DispatcherTimer libraryCacheIdle=new() {Interval=TimeSpan.FromSeconds(30)};
    private bool syncingLibraryFilter;
    private void InitializeLibraryFilters()
    {
        var toolbar=LibraryFilterGrid;toolbar.Children.Clear();toolbar.ColumnDefinitions.Clear();toolbar.RowDefinitions.Clear();
        LibraryFilterActions.Children.Clear();toolbar.ColumnSpacing=8;toolbar.MinWidth=1060;
        LibraryView.LibraryToolbarScroll.SizeChanged+=(_,e)=>{
            double width=Math.Max(toolbar.MinWidth,e.NewSize.Width);if(Math.Abs(toolbar.Width-width)>.5||double.IsNaN(toolbar.Width))toolbar.Width=width;
            var margin=new Thickness(0,0,0,width>e.NewSize.Width+1?20:0);if(toolbar.Margin!=margin)toolbar.Margin=margin;
        };
        void Add(FrameworkElement item,bool flexible=false) {Grid.SetRow(item,0);Grid.SetColumnSpan(item,1);Grid.SetColumn(item,toolbar.ColumnDefinitions.Count);toolbar.ColumnDefinitions.Add(new() {Width=flexible?new GridLength(1,GridUnitType.Star):GridLength.Auto});toolbar.Children.Add(item);}
        LibraryFolderPicker.Width=160;Add(LibraryFolderPicker);
        var searchFrame=new Grid();
        librarySearch.Style=HomeStyle("UtilityTextBox");librarySearch.PlaceholderText=UiLanguage.Text("搜索素材");
        DoubleClickEdit.SetEnabled(librarySearch,false);
        librarySearch.MaxLength=200;librarySearch.Height=48;librarySearch.Padding=new Thickness(38,0,30,0);
        AutomationProperties.SetName(librarySearch,UiLanguage.Text("搜索素材名称或文件名"));AutomationProperties.SetAutomationId(librarySearch,"LibrarySearch");
        searchFrame.Children.Add(librarySearch);
        searchFrame.Children.Add(new FontIcon {Glyph="\uE721",FontSize=16,Margin=new Thickness(14,0,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false});
        searchFrame.MinWidth=180;Add(searchFrame,true);
        if(RefreshFolderButton.Content is StackPanel refresh)refresh.Children.OfType<TextBlock>().First().Text=UiLanguage.Text("刷新");
        if(RemoveFolderButton.Content is StackPanel remove)remove.Children.OfType<TextBlock>().First().Text=UiLanguage.Text("移除");
        ToolTipService.SetToolTip(RefreshFolderButton,UiLanguage.Text("刷新文件夹"));ToolTipService.SetToolTip(RemoveFolderButton,UiLanguage.Text("移除来源"));
        Add(IncludeSubfoldersCheck);Add(RefreshFolderButton);Add(RemoveFolderButton);
        var filterButton=new Button {Content=UiLanguage.Text("筛选"),Style=HomeStyle("HomeButton"),Height=48,MinWidth=64,Padding=new Thickness(12,0,12,0),VerticalContentAlignment=VerticalAlignment.Center};
        AutomationProperties.SetAutomationId(filterButton,"LibraryFilterToggle");
        filterButton.Click+=(_,_)=>libraryFilterOptions.Visibility=libraryFilterOptions.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
        Add(filterButton);
        SetupLibraryChoice(librarySort,"LibrarySort",UiLanguage.Text("排序"),[UiLanguage.Text("最近添加"),UiLanguage.Text("名称 A–Z"),UiLanguage.Text("文件大小 ↓"),UiLanguage.Text("时长 ↓")]);
        librarySort.Width=128;Add(librarySort);
        var reset=new Button {Content=UiLanguage.Text("重置"),Style=HomeStyle("HomeButton"),Height=48,MinWidth=64,Padding=new Thickness(12,0,12,0),VerticalContentAlignment=VerticalAlignment.Center};
        AutomationProperties.SetAutomationId(reset,"LibraryFilterReset");
        reset.Click+=(_,_)=>{ClearLibraryFilter();RefreshLibrary();};Add(reset);
        SetupLibraryChoice(libraryKind,"LibraryKind",UiLanguage.Text("素材类型"),[UiLanguage.Text("全部类型"),UiLanguage.Text("视频"),UiLanguage.Text("图片")]);
        SetupLibraryChoice(libraryOrientation,"LibraryOrientation",UiLanguage.Text("画面比例"),[UiLanguage.Text("全部比例"),UiLanguage.Text("横屏"),UiLanguage.Text("竖屏"),UiLanguage.Text("正方形"),UiLanguage.Text("尺寸未知")]);
        SetupLibraryChoice(libraryResolution,"LibraryResolution",UiLanguage.Text("分辨率（短边）"),[UiLanguage.Text("全部分辨率"),UiLanguage.Text("低于 720p"),"720p–1079p","1080p–1439p","1440p–2159p",UiLanguage.Text("2160p 及以上"),UiLanguage.Text("尺寸未知")]);
        libraryFilterOptions.Children.Add(WidgetField(UiLanguage.Text("类型"),libraryKind));
        libraryFilterOptions.Children.Add(WidgetField(UiLanguage.Text("比例"),libraryOrientation));
        libraryFilterOptions.Children.Add(WidgetField(UiLanguage.Text("分辨率 · 短边"),libraryResolution));
        UtilityAdaptiveGrid(libraryFilterOptions,3,560);
        libraryResultCount.Style=HomeStyle("UtilityLabel");AutomationProperties.SetAutomationId(libraryResultCount,"LibraryResultCount");
        libraryResultCount.Visibility=Visibility.Collapsed;
        LibraryView.LibrarySearchControls.Children.Add(libraryFilterOptions);
        librarySearch.TextChanged+=(_,_)=>{if(syncingLibraryFilter)return;libraryFilter=ReadLibraryFilterControls();librarySearchDelay.Stop();librarySearchDelay.Start();};
        librarySearchDelay.Tick+=(_,_)=>{librarySearchDelay.Stop();RefreshLibrary();};
        foreach(var choice in new[]{libraryKind,libraryOrientation,libraryResolution,librarySort})choice.SelectionChanged+=(_,_)=> {
            if(syncingLibraryFilter)return;libraryFilter=ReadLibraryFilterControls();librarySearchDelay.Stop();RefreshLibrary();
        };
        libraryCacheIdle.Tick+=(_,_)=> {libraryCacheIdle.Stop();if(!LibraryView.IsActive)PosterImageCache.TrimIdle();};
    }
    private void SetupLibraryChoice(ComboBox box,string id,string name,string[] labels)
    {
        box.Style=HomeStyle("HomeComboBox");box.Height=48;box.MinWidth=0;box.HorizontalAlignment=HorizontalAlignment.Stretch;box.ItemsSource=labels;box.SelectedIndex=0;
        AutomationProperties.SetName(box,name);AutomationProperties.SetAutomationId(box,id);
    }
    private LibraryFilter ReadLibraryFilterControls()=>new LibraryFilter(librarySearch.Text,
        LibraryFilter.Kinds[Math.Max(0,libraryKind.SelectedIndex)],LibraryFilter.Orientations[Math.Max(0,libraryOrientation.SelectedIndex)],
        LibraryFilter.Resolutions[Math.Max(0,libraryResolution.SelectedIndex)],LibraryFilter.Sorts[Math.Max(0,librarySort.SelectedIndex)]).Validate();
    private void SetLibraryFilter(LibraryFilter value,bool refresh=true)
    {
        value=value.Validate();librarySearchDelay.Stop();syncingLibraryFilter=true;
        try {
            libraryFilter=value;librarySearch.Text=value.Query;
            libraryKind.SelectedIndex=Array.IndexOf(LibraryFilter.Kinds,value.Kind);libraryOrientation.SelectedIndex=Array.IndexOf(LibraryFilter.Orientations,value.Orientation);
            libraryResolution.SelectedIndex=Array.IndexOf(LibraryFilter.Resolutions,value.Resolution);librarySort.SelectedIndex=Array.IndexOf(LibraryFilter.Sorts,value.Sort);
        } finally {syncingLibraryFilter=false;}
        if(refresh)RefreshLibrary();
    }
}
