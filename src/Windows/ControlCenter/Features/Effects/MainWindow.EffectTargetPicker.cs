using System.Numerics;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace LumaWall.ControlCenter;

public sealed record EffectTargetRow(MediaAsset Asset,string PosterPath)
{
    private readonly LibraryRow libraryRow=new(Asset);
    public string Name=>Asset.Name;
    public string Path=>Asset.Path;
    public string Resolution=>libraryRow.Resolution;
    public string Duration=>libraryRow.Duration;
    public string Availability=>libraryRow.Availability;
    // Bindings request posters only for realized cards; recycled cards release their images.
    public BitmapImage? Poster=>PosterImageCache.Load(PosterPath);
}

public sealed partial class MainWindow
{
    private EffectTargetRow[] effectTargetRows=[];

    private void EffectsTargetOpen_Click(object sender,RoutedEventArgs e)
    {
        if(!effectsReady)return;
        EndEffectEditing();CloseQueue(false);CloseWidgetDrawer(false);
        // Snapshot metadata once per opening; card bindings load only the visible covers.
        effectTargetRows=modules.Library.Assets().Select(a=>new EffectTargetRow(a,modules.Library.Poster(a.Id))).ToArray();
        EffectsTargetSearch.Text="";EffectsTargetType.SelectedIndex=0;
        EffectsTargetScreenButton.IsEnabled=SelectedScreen!=null;
        EffectsTargetScreenLabel.Text=UiLanguage.Text("显示器 · ")+(SelectedScreen is { } screen?new ScreenRow(screen).ToString():UiLanguage.Text("尚未连接"));
        bool screenSelected=effectTargetAssetId==null&&SelectedScreen!=null;
        EffectsTargetScreenCheck.Visibility=screenSelected?Visibility.Visible:Visibility.Collapsed;
        EffectsTargetScreenButton.BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeRimBrush"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EffectsTargetScreenButton,EffectsTargetScreenLabel.Text+(screenSelected?UiLanguage.Text("，已选中"):""));
        EffectsTargetOverlay.Visibility=Visibility.Visible;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=false;
        RefreshEffectTargetResults();LayoutCenteredModals();ResizeEffectTargetCards();
        EffectsTargetSearch.Focus(FocusState.Programmatic);
        if(EffectsTargetList.SelectedItem is { } current)EffectsTargetList.ScrollIntoView(current);
        AnimateEffectTargetPicker();
    }

    private void EffectsTargetSearch_Changed(object sender,TextChangedEventArgs e)=>RefreshEffectTargetResults();
    private void EffectsTargetType_Changed(object sender,SelectionChangedEventArgs e)=>RefreshEffectTargetResults();
    private void RefreshEffectTargetResults()
    {
        if(!effectsPageReady||EffectsTargetOverlay.Visibility!=Visibility.Visible)return;
        string query=EffectsTargetSearch.Text.Trim();int type=EffectsTargetType.SelectedIndex;
        var matches=effectTargetRows.Where(row=>(type!=1||!row.Asset.IsImage)&&(type!=2||row.Asset.IsImage)&&
            (query.Length==0||row.Asset.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||row.Path.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
        EffectsTargetList.ItemsSource=matches;
        EffectsTargetList.SelectedItem=matches.FirstOrDefault(row=>row.Asset.Id==effectTargetAssetId);
        EffectsTargetEmpty.Text=effectTargetRows.Length==0?UiLanguage.Text("暂无素材"):UiLanguage.Text("无匹配素材");
        EffectsTargetEmpty.Visibility=matches.Length==0?Visibility.Visible:Visibility.Collapsed;
    }

    private void EffectsTargetGrid_Loaded(object sender,RoutedEventArgs e)=>ResizeEffectTargetCards();
    private void EffectsTargetGrid_SizeChanged(object sender,SizeChangedEventArgs e)=>ResizeEffectTargetCards();
    private void ResizeEffectTargetCards()=>ResizeLibraryCardGrid(EffectsTargetList);

    private void EffectsTargetSearch_KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key!=VirtualKey.Down&&e.Key!=VirtualKey.Enter)return;
        if(EffectsTargetList.Items.Count==0)return;
        // Enter only commits an unambiguous match; otherwise move to the results.
        if(e.Key==VirtualKey.Enter&&EffectsTargetList.Items.Count==1)
            SelectEffectTarget(((EffectTargetRow)EffectsTargetList.Items[0]).Asset.Id);
        else {
            EffectsTargetList.SelectedIndex=0;EffectsTargetList.ScrollIntoView(EffectsTargetList.Items[0]);
            EffectsTargetList.Focus(FocusState.Keyboard);
        }
        e.Handled=true;
    }
    private void EffectsTargetAsset_Click(object sender,ItemClickEventArgs e)
    {
        if(e.ClickedItem is EffectTargetRow row)SelectEffectTarget(row.Asset.Id);
    }
    private void EffectsTargetScreen_Click(object sender,RoutedEventArgs e)=>SelectEffectTarget(null);
    private void SelectEffectTarget(string? next)
    {
        if(next!=null&&!modules.Library.Assets().Any(a=>a.Id==next)) {
            effectTargetRows=modules.Library.Assets().Select(a=>new EffectTargetRow(a,modules.Library.Poster(a.Id))).ToArray();
            RefreshEffectTargetResults();return;
        }
        CloseEffectsTarget(true);
        if(next==effectTargetAssetId)return;
        EndEffectEditing();effectTargetAssetId=next;LoadEffectDraft(true);AnimatePageEntry("display");
    }
    private void EffectsTargetClose_Click(object sender,RoutedEventArgs e)=>CloseEffectsTarget(true);
    private void CloseEffectsTarget(bool restoreFocus)
    {
        if(EffectsTargetOverlay==null||EffectsTargetOverlay.Visibility!=Visibility.Visible)return;
        // The picker may have opened with motion disabled, before its first
        // translation animation ever initialized this optional property.
        ElementCompositionPreview.SetIsTranslationEnabled(EffectsTargetDrawer,true);
        var visual=ElementCompositionPreview.GetElementVisual(EffectsTargetDrawer);
        visual.StopAnimation("Opacity");visual.StopAnimation("Translation.Y");visual.Opacity=1;visual.Properties.InsertVector3("Translation",Vector3.Zero);
        EffectsTargetOverlay.Visibility=Visibility.Collapsed;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=true;
        EffectsTargetList.ItemsSource=null;effectTargetRows=[];
        if(restoreFocus&&DisplayPanel.Visibility==Visibility.Visible)EffectsTargetButton.Focus(FocusState.Programmatic);
    }
    private void AnimateEffectTargetPicker()
    {
        if(!Appearance.Motion||homeUiSettings?.AnimationsEnabled==false)return;
        ElementCompositionPreview.SetIsTranslationEnabled(EffectsTargetDrawer,true);
        var visual=ElementCompositionPreview.GetElementVisual(EffectsTargetDrawer);var compositor=visual.Compositor;
        var easing=compositor.CreateCubicBezierEasingFunction(new Vector2(.22f,.61f),new Vector2(.36f,1));
        var move=compositor.CreateScalarKeyFrameAnimation();move.InsertKeyFrame(0,18);move.InsertKeyFrame(1,0,easing);move.Duration=TimeSpan.FromMilliseconds(1000);
        var fade=compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1);fade.Duration=move.Duration;
        visual.StartAnimation("Translation.Y",move);visual.StartAnimation("Opacity",fade);
    }
}
