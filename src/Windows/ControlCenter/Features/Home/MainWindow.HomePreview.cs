using LumaWall.Contracts;
using Microsoft.UI.Xaml;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record HomePreviewRequest(MediaAsset Asset,string? Screen,string? PlayingAsset);
    private HomePreviewRequest? homePreviewRequest;
    private MediaAsset? homePreviewAsset;
    private LibraryRow? homePreviewRow;
    private bool homePreviewInitialized;

    public void OpenHome()
    {
        homePreviewRequest=null;
        Navigate("wallpaper");
        WallpaperScroll.UpdateLayout();
        WallpaperScroll.ChangeView(null,0,null,true);
    }

    private MediaAsset? HomePreviewTarget(MediaAsset? playing)
    {
        // Only “查看并应用” opens a draft preview. Merely selecting a library
        // card must never pin the home cover to that card.
        if(homePreviewRequest is { } request)
        {
            if(request.Screen==SelectedScreen?.DeviceName && request.PlayingAsset==playing?.Id)
                return request.Asset;
            homePreviewRequest=null;
        }
        return playing;
    }

    private void RefreshHomePreview(MediaAsset? asset,MediaAsset? playing,bool forcePoster=false)
    {
        if(!homePreviewInitialized || asset!=homePreviewAsset || forcePoster)
        {
            if(homePreviewAsset?.Id!=asset?.Id)FlushHomePosition();
            homePreviewInitialized=true;homePreviewAsset=asset;
            if(homePreviewRow?.Asset!=asset) {
                if(homePreviewRow!=null)homePreviewRow.PropertyChanged-=HomePreviewSourceChanged;
                homePreviewRow=asset==null?null:new LibraryRow(asset);
                if(homePreviewRow!=null)homePreviewRow.PropertyChanged+=HomePreviewSourceChanged;
            }
            PreviewTitle.Text=asset?.Name??string.Empty;
            HomeAssetTitle.Visibility=asset==null?Visibility.Collapsed:Visibility.Visible;
            LayoutHomeTransport(HomeTransport.ActualWidth);
            PreviewMeta.Text=homePreviewRow?.Description??string.Empty;
            if(forcePoster&&homePreviewRow!=null)_=homePreviewRow.RefreshSourceStatusAsync(force:true);
            PreviewMeta.Visibility=asset==null?Visibility.Collapsed:Visibility.Visible;
            EmptyPreview.Visibility=asset==null?Visibility.Visible:Visibility.Collapsed;
            PosterImage.Source=asset==null?null:LoadPosterImage(modules.Library.Poster(asset.Id),1280);
        }
        HeroLabel.Text=asset==null?UiLanguage.Text("暂无壁纸"):(asset.Id==playing?.Id?UiLanguage.Text("当前壁纸"):UiLanguage.Text("待应用素材"))+(asset.IsImage?UiLanguage.Text(" · 图片"):UiLanguage.Text(" · 视频封面"));
        RefreshHomeVolume();
        RefreshHomePosition();
    }
    private void HomePreviewSourceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
    {
        if(!libraryShutdown&&args.PropertyName==nameof(LibraryRow.SourceAvailable)&&
            homePreviewRow!=null&&ReferenceEquals(sender,homePreviewRow))
            PreviewMeta.Text=homePreviewRow.Description;
    }
}
