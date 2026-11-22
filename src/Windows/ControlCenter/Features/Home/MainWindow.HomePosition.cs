using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private Slider? homePositionX,homePositionY,homePositionScale;
    private Flyout? homePositionFlyout;
    private FillMode homeBaseFill=FillMode.Cover;
    private void SyncHomeFill(FillMode? fill=null)
    {
        homeBaseFill=fill??(SelectedScreen is { } screen?modules.Effects.ScreenProfile(screen.DeviceName)?.Fill:null)??FillMode.Cover;
        bool previous=loadingEffects;loadingEffects=true;
        try {FillPicker.SelectedIndex=SelectedScreen is { } target&&CurrentHomePosition(target.DeviceName)!=new WallpaperPosition()?2:homeBaseFill==FillMode.Contain?1:0;}
        finally {loadingEffects=previous;}
    }
    private bool syncingHomePosition;
    private readonly DispatcherTimer homePositionSaveTimer=new() {Interval=TimeSpan.FromMilliseconds(250)};
    private (string Screen,string? Asset,WallpaperPosition Position)? pendingHomePosition;
    private (double Width,double Height,double SourceRatio,double ScreenRatio,bool Contain,WallpaperPosition Position)? homePreviewLayoutKey;
    private void InitializeHomePosition()
    {
        homePositionX=new Slider {Style=HomeStyle("HomeSlider"),Minimum=-100,Maximum=100,StepFrequency=1};
        homePositionY=new Slider {Style=HomeStyle("HomeSlider"),Minimum=-100,Maximum=100,StepFrequency=1};
        homePositionScale=new Slider {Style=HomeStyle("HomeSlider"),Minimum=10,Maximum=400,StepFrequency=1,Value=100};
        var body=new StackPanel {Spacing=16,Width=360};
        body.Children.Add(NumericSliderRow(UiLanguage.Text("水平位置"),homePositionX,"%",0));
        body.Children.Add(NumericSliderRow(UiLanguage.Text("垂直位置"),homePositionY,"%",0));
        body.Children.Add(NumericSliderRow(UiLanguage.Text("缩放"),homePositionScale,"%",0));
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(WidgetButton(UiLanguage.Text("居中"),()=> {if(SelectedScreen is { } screen){FlushHomePosition();SaveHomePosition(screen.DeviceName,CurrentHomePosition(screen.DeviceName) with {X=0,Y=0});}}));
        actions.Children.Add(WidgetButton(UiLanguage.Text("重置"),()=> {if(SelectedScreen is { } screen){FlushHomePosition();SaveHomePosition(screen.DeviceName,new());}}));body.Children.Add(actions);
        homePositionFlyout=new Flyout {Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top,FlyoutPresenterStyle=DetailFlyoutStyle(),Content=new ContentControl {Style=HomeStyle("HomeFrame"),Padding=new Thickness(20),Content=body}};
        HomePositionButton.Flyout=homePositionFlyout;
        homePositionFlyout.Opening+=(_,_)=>RefreshHomePosition();homePositionFlyout.Closed+=(_,_)=>FlushHomePosition();
        homePositionScale.ValueChanged+=(_,_)=>HomePositionChanged();
        homePositionX.ValueChanged+=(_,_)=>HomePositionChanged();homePositionY.ValueChanged+=(_,_)=>HomePositionChanged();
        homePositionSaveTimer.Tick+=(_,_)=>FlushHomePosition();
        HomePosterViewport.SizeChanged+=(_,_)=>LayoutHomePreview();
        Closed+=(_,_)=>FlushHomePosition();
    }
    private void HomePositionChanged()
    {
        if(syncingHomePosition||homePositionX==null||homePositionY==null||homePositionScale==null||SelectedScreen is not { } screen)return;
        var position=new WallpaperPosition((int)Math.Round(homePositionX.Value),(int)Math.Round(homePositionY.Value),(int)Math.Round(homePositionScale.Value));
        string? asset=HomePositionAsset(screen.DeviceName);
        if(pendingHomePosition is { } pending&&(pending.Screen!=screen.DeviceName||pending.Asset!=asset))FlushHomePosition();
        pendingHomePosition=(screen.DeviceName,asset,position);SyncHomeFill(homeBaseFill);
        wallpapers?.RefreshPosition(screen.DeviceName,position,asset);LayoutHomePreview();homePositionSaveTimer.Stop();homePositionSaveTimer.Start();
    }
    private void FlushHomePosition()
    {
        homePositionSaveTimer.Stop();if(pendingHomePosition is not { } pending)return;
        try {SaveHomePosition(pending.Screen,pending.Position,pending.Asset);}
        catch(Exception error) {ShowError(UiLanguage.Text("壁纸位置未保存：")+error.Message,error);}
    }
    private string? HomePositionAsset(string screen)=>SelectedScreen?.DeviceName==screen?homePreviewAsset?.Id??store.Bindings().FirstOrDefault(b=>b.ScreenId==screen)?.AssetId:store.Bindings().FirstOrDefault(b=>b.ScreenId==screen)?.AssetId;
    private void SaveHomePosition(string screen,WallpaperPosition position)=>SaveHomePosition(screen,position,HomePositionAsset(screen));
    private void SaveHomePosition(string screen,WallpaperPosition position,string? asset)
    {
        modules.Settings.Set(RuntimeSettingsReader.PositionKey(screen,asset),position.ToJson());
        if(pendingHomePosition is { } pending&&pending.Screen==screen&&pending.Asset==asset)pendingHomePosition=null;
        wallpapers?.RefreshPosition(screen,asset:asset);RefreshHomePosition();
    }
    private WallpaperPosition CurrentHomePosition(string screen)=>pendingHomePosition is { } pending&&pending.Screen==screen&&pending.Asset==HomePositionAsset(screen)?pending.Position:RuntimeSettingsReader.Position(store,screen,HomePositionAsset(screen));
    private void RefreshHomePosition()
    {
        if(homePositionX==null||homePositionY==null||homePositionScale==null)return;
        bool previous=syncingHomePosition;syncingHomePosition=true;
        try {
            HomePositionButton.IsEnabled=homePositionX.IsEnabled=homePositionY.IsEnabled=homePositionScale.IsEnabled=SelectedScreen!=null&&homePreviewAsset!=null;
            var position=SelectedScreen is { } screen?CurrentHomePosition(screen.DeviceName):new();
            homePositionX.Value=position.X;homePositionY.Value=position.Y;homePositionScale.Value=position.Scale;
        }finally {syncingHomePosition=previous;}
        SyncHomeFill();LayoutHomePreview();
    }
    private void LayoutHomePreview()
    {
        var screen=SelectedScreen;var asset=homePreviewAsset;double width=HomePosterViewport.ActualWidth,height=HomePosterViewport.ActualHeight;
        HomePreviewCanvas.Visibility=screen==null||asset==null?Visibility.Collapsed:Visibility.Visible;
        if(screen==null||asset==null||width<=0||height<=0)return;
        double ratio=asset.Width>0&&asset.Height>0?asset.Width/(double)asset.Height:screen.Bounds.Width/(double)screen.Bounds.Height;
        bool contain=(modules.Effects.ScreenProfile(screen.DeviceName)?.Fill??FillMode.Cover)==FillMode.Contain;
        var key=(width,height,ratio,screen.Bounds.Width/(double)screen.Bounds.Height,contain,CurrentHomePosition(screen.DeviceName));
        if(homePreviewLayoutKey==key)return;homePreviewLayoutKey=key;
        var layout=WallpaperPreviewLayout.Calculate(key.width,key.height,key.ratio,key.Item4,key.contain,key.Item6);
        HomePreviewCanvas.Clip=new RectangleGeometry {Rect=new Rect(0,0,width,height)};
        void Place(FrameworkElement element,PreviewRect rect) {
            Canvas.SetLeft(element,rect.X);Canvas.SetTop(element,rect.Y);element.Width=Math.Max(0,rect.Width);element.Height=Math.Max(0,rect.Height);
        }
        var view=layout.Screen;Place(PosterImage,layout.Content);Place(HomeMonitorFrame,view);
        Place(HomePreviewDimTop,new(0,0,width,view.Y));Place(HomePreviewDimBottom,new(0,view.Y+view.Height,width,height-view.Y-view.Height));
        Place(HomePreviewDimLeft,new(0,view.Y,view.X,view.Height));Place(HomePreviewDimRight,new(view.X+view.Width,view.Y,width-view.X-view.Width,view.Height));
    }
}
