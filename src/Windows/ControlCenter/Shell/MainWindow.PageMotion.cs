using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<Visual> pageEntryVisuals=new();
    private int pageEntryRevision;

    private void StopPageEntry()
    {
        pageEntryRevision++;
        foreach(var visual in pageEntryVisuals)
        {
            visual.StopAnimation("Opacity");visual.StopAnimation("Translation.Y");
            visual.Opacity=1;visual.Properties.InsertVector3("Translation",Vector3.Zero);
        }
        pageEntryVisuals.Clear();
    }

    private void AnimatePageEntry(string page)
    {
        StopPageEntry();
        if(!Root.IsLoaded||!Appearance.Motion||homeUiSettings?.AnimationsEnabled==false)return;
        int revision=pageEntryRevision;
        DispatcherQueue.TryEnqueue(()=>
        {
            if(revision!=pageEntryRevision||page!=navigationPage||shellChromeClosed)return;
            Panel? panel=page switch
            {
                "wallpaper"=>WallpaperPanel,"library"=>LibraryPanel,
                "display"=>selectedEffectKind==null?EffectsOverview:EffectsDetailPanel.Content as Panel,"widgets"=>WidgetsOverview,
                "settings"=>SettingsPanel,"appearance"=>AppearancePanel,
                "presets"=>SharingPanel,"performance"=>PerformancePanel,"packs"=>ExpansionPanel,"history"=>HistoryPanel,_=>null
            };
            if(panel==null)return;
            var blocks=new List<FrameworkElement>{PageHeader};
            if(page is "display" or "widgets")blocks.Add(PagePinnedActions);
            if(page=="display")blocks.Add(EffectsCollectionHeader);
            // These category cards live outside the settings content
            // panels, so include them explicitly before the selected page.
            if(IsSettingsPage(page))
                blocks.AddRange(SettingsCategoryGrid.Children.OfType<FrameworkElement>().Where(item=>item.Visibility==Visibility.Visible));
            foreach(var child in panel.Children.OfType<FrameworkElement>().Where(item=>item.Visibility==Visibility.Visible))
            {
                // Overview collections reveal each card; controls inside a card
                // keep their own layout, focus and hover animations untouched.
                if(child==EffectsListPanel||child==WidgetListPanel||child==SettingsMenuGrid)
                    blocks.AddRange(((Panel)child).Children.OfType<FrameworkElement>().Where(item=>item.Visibility==Visibility.Visible));
                else blocks.Add(child);
            }
            int index=0;
            var duration=TimeSpan.FromMilliseconds(1000);
            foreach(var block in blocks.Take(24))
            {
                ElementCompositionPreview.SetIsTranslationEnabled(block,true);
                var visual=ElementCompositionPreview.GetElementVisual(block);var compositor=visual.Compositor;
                var easing=compositor.CreateCubicBezierEasingFunction(new Vector2(.22f,.61f),new Vector2(.36f,1));
                var delay=TimeSpan.FromMilliseconds(Math.Min(index++*32,160));
                var move=compositor.CreateScalarKeyFrameAnimation();move.InsertKeyFrame(0,18);move.InsertKeyFrame(1,0,easing);
                move.Duration=duration;move.DelayTime=delay;
                var fade=compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1);
                fade.Duration=duration;fade.DelayTime=delay;
                visual.Opacity=0;visual.Properties.InsertVector3("Translation",new Vector3(0,18,0));
                visual.StartAnimation("Translation.Y",move);visual.StartAnimation("Opacity",fade);pageEntryVisuals.Add(visual);
            }
        });
    }
}
