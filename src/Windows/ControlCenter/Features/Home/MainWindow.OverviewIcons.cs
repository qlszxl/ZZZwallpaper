using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static BitmapImage? metalEffectsAtlas,metalWidgetsAtlas,orangeEffectsAtlas,orangeWidgetsAtlas;
    private sealed record OverviewIconSource(int Index,Image Sheet);

    private static BitmapImage OverviewIconAtlas(int index,bool active)
    {
        bool widgets=index>=25;
        if(active)return widgets
            ?(orangeWidgetsAtlas??=new BitmapImage(new Uri("ms-appx:///Assets/MetalIcons/widgets-orange.png")))
            :(orangeEffectsAtlas??=new BitmapImage(new Uri("ms-appx:///Assets/MetalIcons/effects-orange.png")));
        return widgets
            ?(metalWidgetsAtlas??=new BitmapImage(new Uri("ms-appx:///Assets/MetalIcons/widgets-silver.png")))
            :(metalEffectsAtlas??=new BitmapImage(new Uri("ms-appx:///Assets/MetalIcons/effects-silver.png")));
    }

    private static void SetOverviewIconActive(FrameworkElement icon,bool active)
    {
        // Only our atlas-backed icons opt in; uploaded images keep their colors.
        if(icon.Tag is not OverviewIconSource source)return;
        var atlas=OverviewIconAtlas(source.Index,active);
        if(!ReferenceEquals(source.Sheet.Source,atlas))source.Sheet.Source=atlas;
    }

    private static Viewbox CreateMetalIcon(int index,double size,bool active=false)
    {
        const double cellSize=300,atlasSize=1500;
        bool widgets=index>=25;int cell=widgets?index-25:index;
        // One decoded image per palette/atlas; each view clips one unchanged
        // silhouette. Toggling only replaces its shared source, not the card.
        var source=OverviewIconAtlas(index,active);
        var tile=new Canvas {Width=cellSize,Height=cellSize,Clip=new RectangleGeometry {Rect=new(0,0,cellSize,cellSize)},IsHitTestVisible=false};
        var sheet=new Image {Source=source,Width=atlasSize,Height=atlasSize,Stretch=Stretch.Fill,IsHitTestVisible=false};
        Canvas.SetLeft(sheet,-(cell%5)*cellSize);Canvas.SetTop(sheet,-(cell/5)*cellSize);tile.Children.Add(sheet);
        return new Viewbox {Width=size,Height=size,Stretch=Stretch.Uniform,Child=tile,Tag=new OverviewIconSource(index,sheet),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
    }

    private static Viewbox CreateEffectMetalIcon(EffectKind kind,double size,bool active=false)=>CreateMetalIcon(kind switch {
        EffectKind.Color=>0,EffectKind.RgbSplit=>1,EffectKind.Glow=>3,EffectKind.Vignette=>4,
        EffectKind.Noise=>5,EffectKind.Scanlines=>6,EffectKind.Glitch=>7,EffectKind.LargeGlitch=>8,EffectKind.Dots=>9,
        EffectKind.DotRipple=>10,EffectKind.Blur=>11,EffectKind.Distortion=>12,EffectKind.HideTaskbar=>13,EffectKind.AudioReactive=>14,
        EffectKind.ZoomTunnel=>15,EffectKind.Particles=>16,EffectKind.WaterWave=>17,EffectKind.EdgeOutline=>18,EffectKind.Pixelate=>19,
        EffectKind.Transform=>15,EffectKind.LightFlare=>3,EffectKind.SunPrism=>20,EffectKind.MaskLibrary=>21,_=>throw new ArgumentOutOfRangeException(nameof(kind))
    },size,active);

    private static int? MetalUtilityIconIndex(string name)=>name switch {
        "Search"=>22,"Monitor"=>23,"Spectrum"=>24,"Text"=>25,"Image"=>26,"Color"=>27,"Clock"=>28,
        "Countdown"=>29,"FocusTimer"=>30,"Link"=>31,"Widgets"=>32,"Settings"=>33,"Compare"=>34,"Layers"=>35,_=>null
    };
}
