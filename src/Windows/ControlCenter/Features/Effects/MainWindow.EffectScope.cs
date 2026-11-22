using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private EffectKind draftOverrides;
    // Start on display defaults. Only the target picker changes this selection;
    // library selection, playback and page history have independent state.
    private string? effectTargetAssetId;
    private string? cachedEffectTargetId;
    private long cachedEffectLibraryRevision=-1;
    private MediaAsset? cachedEffectTarget;
    private (MediaAsset? Asset,string? Screen,int Width,int Height,bool Primary,string? Playing)? effectScopeState;
    private MediaAsset? EffectTargetAsset
    {
        get {
            if(effectTargetAssetId==null)return null;
            if(cachedEffectTargetId!=effectTargetAssetId||cachedEffectLibraryRevision!=store.PlaybackLibraryRevision) {
                cachedEffectTarget=modules.Library.Assets().FirstOrDefault(a=>a.Id==effectTargetAssetId);
                cachedEffectTargetId=effectTargetAssetId;cachedEffectLibraryRevision=store.PlaybackLibraryRevision;
            }
            return cachedEffectTarget;
        }
    }
    private string? EffectPreviewAssetId=>effectTargetAssetId??(SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset.Id);
    private void RefreshEffectScope()
    {
        var target=EffectTargetAsset;
        var selectedScreen=SelectedScreen;
        var state=(target,selectedScreen?.DeviceName,selectedScreen?.Bounds.Width??0,selectedScreen?.Bounds.Height??0,
            selectedScreen?.Primary??false,wallpapers?.Running.GetValueOrDefault(selectedScreen?.DeviceName??"")?.Asset.Id);
        if(effectScopeState==state)return;
        effectScopeState=state;
        EffectsClearButton.IsEnabled=SelectedScreen!=null;
        EffectsCopyButton.IsEnabled=SelectedScreen!=null&&target!=null;
        EffectsCopyButton.Visibility=target!=null?Visibility.Visible:Visibility.Collapsed;
        string kind=target==null?UiLanguage.Text("显示器"):target.IsImage?UiLanguage.Text("图片"):UiLanguage.Text("视频");
        EffectsTargetKind.Text=UiLanguage.Text("作用对象");
        EffectsTargetIcon.Glyph=target==null?"\uE7F4":target.IsImage?"\uE91B":"\uE714";
        EffectsSelectedAsset.Text=target?.Name??(SelectedScreen is { } screen?$"{(screen.Primary?UiLanguage.Text("主屏幕"):UiLanguage.Text("显示器"))} {screen.DeviceName.Replace(@"\\.\DISPLAY","")}":UiLanguage.Text("尚未连接"));
        EffectsTargetResolution.Text=target!=null?$"{target.Width} × {target.Height}":SelectedScreen is { } display?$"{display.Bounds.Width} × {display.Bounds.Height}":"";
        string targetLabel=kind+" · "+EffectsSelectedAsset.Text+" · "+EffectsTargetResolution.Text;
        ToolTipService.SetToolTip(EffectsTargetButton,targetLabel);
        ToolTipService.SetToolTip(EffectsCopyButton,UiLanguage.Text("复制显示器效果，覆盖此素材的效果设置。"));
        ToolTipService.SetToolTip(EffectsClearButton,target==null?UiLanguage.Text("清空显示器效果，保留素材专属效果。"):UiLanguage.Text("关闭此素材的全部效果，包含继承效果。"));
        string hint=target==null
            ?UiLanguage.Text("显示器默认效果")
            :UiLanguage.Text("素材专属效果优先于显示器效果");
        if(target!=null&&wallpapers?.Running.GetValueOrDefault(SelectedScreen?.DeviceName??"")?.Asset.Id!=target.Id)
            hint+=UiLanguage.Text(" · 应用素材后生效");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EffectsTargetButton,UiLanguage.Text("选择作用对象：")+targetLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(EffectsTargetButton,hint);
    }
    private void EffectsClear_Click(object sender,RoutedEventArgs e)=>ChangeEffectTarget(false);
    private void EffectsCopy_Click(object sender,RoutedEventArgs e)=>ChangeEffectTarget(true);
    private void ChangeEffectTarget(bool copy)
    {
        if(SelectedScreen is not { } screen||copy&&effectTargetAssetId==null)return;
        try {
            EndEffectEditing();
            if(copy)modules.Effects.CopyScreenEffectsToAsset(screen.DeviceName,effectTargetAssetId!);
            else modules.Effects.ClearTargetEffects(screen.DeviceName,effectTargetAssetId);
            LoadEffectDraft(true);wallpapers?.RefreshLiveEffects(effectTargetAssetId==null?EffectChange.Screen(screen.DeviceName):EffectChange.Asset(effectTargetAssetId));
            SetEffectsSaveStatus(copy?UiLanguage.Text("已复制显示器效果"):effectTargetAssetId==null?UiLanguage.Text("已清空显示器效果"):UiLanguage.Text("已关闭素材效果"));
            AnimatePageEntry("display");
        }catch(Exception ex) {ShowError(UiLanguage.Text("修改效果失败：")+ex.Message,ex);LoadEffectDraft(true);}
    }
}
