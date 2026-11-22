using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumaWall.ControlCenter;

/// <summary>The approved three-panel pattern, with counter-moving stripes inside its alpha mask.</summary>
internal sealed class BrandBackdrop : IDisposable
{
    private const float PatternScale = 4;
    private const float CellWidth = 520 * PatternScale, CellHeight = 292.5f * PatternScale;
    private const float AtlasWidth = CellWidth * 6;
    private const float PatternSpeed = 3.2f * 5.5f, StripePitch = 10, StripeWidth = 4;
    private const float PatternAngleDegrees = -15;
    // Turn the enlarged artwork around the reference viewport's centre, keeping
    // the same focal area when changing from the earlier -45-degree angle.
    private static readonly Vector2 Origin = new Vector2(640, 360) + Vector2.Transform(
        new Vector2(-2560, -920) - new Vector2(640, 360),
        Matrix3x2.CreateRotation((PatternAngleDegrees + 45) * MathF.PI / 180));
    private static readonly string[] Layout = ["ABCBAC", "CABCBA", "ABCACB", "BCACBA"];
    private readonly UIElement host;
    private readonly Compositor compositor;
    private readonly List<CompositionObject> owned = new();
    private readonly List<LoadedImageSurface> images = new();
    private readonly List<SpriteVisual> tiles = new();
    private readonly List<CompositionSpriteShape> stripes = new();
    private readonly CompositionSurfaceBrush[] imageBrushes = new CompositionSurfaceBrush[3];
    private readonly ContainerVisual maskRoot, patternRotation, patternPlane, inkRoot, stripeMotion;
    private readonly ShapeVisual stripePlane;
    private readonly SpriteVisual baseInk, opacityMask, output;
    private readonly CompositionVisualSurface maskSurface, fadedMaskSurface, inkSurface;
    private readonly CompositionLinearGradientBrush opacityGradient;
    private readonly CompositionRectangleGeometry stripeGeometry;
    private readonly CompositionPropertySet phase;
    private readonly AnimationController patternController, stripeController;
    private readonly CompositionColorBrush stripeInk;
    private Vector2 size;
    private (int,int,int,int)? tileLayout;
    private bool moving, disposed;

    public BrandBackdrop(UIElement host)
    {
        this.host = host;
        compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        try
        {
            for (int i = 0; i < imageBrushes.Length; i++)
            {
                var image = LoadedImageSurface.StartLoadFromUri(new Uri($"ms-appx:///Assets/Background/Brand-{(char)('A' + i)}.png"));
                images.Add(image);
                var brush = Own(compositor.CreateSurfaceBrush(image));
                brush.Stretch = CompositionStretch.Fill;
                brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
                imageBrushes[i] = brush;
            }

            // Off-tree visual surfaces capture only the viewport. All repeated
            // panels share the same three decoded images, not a large bitmap atlas.
            maskRoot = Own(compositor.CreateContainerVisual());
            patternRotation = Own(compositor.CreateContainerVisual());
            patternRotation.Offset = new Vector3(Origin, 0);
            patternRotation.RotationAngleInDegrees = PatternAngleDegrees;
            patternPlane = Own(compositor.CreateContainerVisual());
            patternRotation.Children.InsertAtTop(patternPlane);
            maskRoot.Children.InsertAtTop(patternRotation);
            maskSurface = Own(compositor.CreateVisualSurface());
            maskSurface.SourceVisual = maskRoot;

            // Multiply the silhouette alpha by a viewport-wide fade, rather than
            // painting a dark overlay over the controls or the empty background.
            opacityGradient = Own(compositor.CreateLinearGradientBrush());
            opacityGradient.MappingMode = CompositionMappingMode.Absolute;
            opacityGradient.StartPoint = Vector2.Zero;
            opacityGradient.ColorStops.Add(Own(compositor.CreateColorGradientStop(0, Color.FromArgb(209, 255, 255, 255))));
            opacityGradient.ColorStops.Add(Own(compositor.CreateColorGradientStop(.55f, Color.FromArgb(140, 255, 255, 255))));
            opacityGradient.ColorStops.Add(Own(compositor.CreateColorGradientStop(1, Color.FromArgb(46, 255, 255, 255))));
            var fadedMaskBrush = Own(compositor.CreateMaskBrush());
            fadedMaskBrush.Source = opacityGradient;
            fadedMaskBrush.Mask = Own(compositor.CreateSurfaceBrush(maskSurface));
            opacityMask = Own(compositor.CreateSpriteVisual());
            opacityMask.Brush = fadedMaskBrush;
            fadedMaskSurface = Own(compositor.CreateVisualSurface());
            fadedMaskSurface.SourceVisual = opacityMask;

            inkRoot = Own(compositor.CreateContainerVisual());
            baseInk = Own(compositor.CreateSpriteVisual());
            baseInk.Brush = Own(compositor.CreateColorBrush(Color.FromArgb(255, 38, 38, 38)));
            inkRoot.Children.InsertAtBottom(baseInk);
            stripeMotion = Own(compositor.CreateContainerVisual());
            stripeMotion.Opacity = .5f;
            stripePlane = Own(compositor.CreateShapeVisual());
            // x' = x + y keeps the existing +45-degree stripes, independent of
            // the brand plane's shallow rotation and down-left movement.
            var shear = Matrix4x4.Identity;
            shear.M21 = 1;
            stripePlane.TransformMatrix = shear;
            stripeGeometry = Own(compositor.CreateRectangleGeometry());
            stripeInk = Own(compositor.CreateColorBrush(Color.FromArgb(255, 54, 54, 54)));
            stripeMotion.Children.InsertAtTop(stripePlane);
            inkRoot.Children.InsertAtTop(stripeMotion);
            inkSurface = Own(compositor.CreateVisualSurface());
            inkSurface.SourceVisual = inkRoot;

            var maskBrush = Own(compositor.CreateMaskBrush());
            maskBrush.Mask = Own(compositor.CreateSurfaceBrush(fadedMaskSurface));
            maskBrush.Source = Own(compositor.CreateSurfaceBrush(inkSurface));
            output = Own(compositor.CreateSpriteVisual());
            output.Brush = maskBrush;
            output.Clip = Own(compositor.CreateInsetClip());

            phase = Own(compositor.CreatePropertySet());
            phase.InsertScalar("Travel", 0);
            // Negative local X becomes (-X, +Y) after the negative rotation:
            // down-left on screen. Rotation preserves the 17.6 DIP/second speed.
            // Wrap all six columns together so the three variants remain seamless.
            var patternAnimation = Own(compositor.CreateScalarKeyFrameAnimation());
            var linear = Own(compositor.CreateLinearEasingFunction());
            patternAnimation.InsertKeyFrame(0, 0, linear);
            patternAnimation.InsertKeyFrame(1, AtlasWidth, linear);
            patternAnimation.Duration = TimeSpan.FromSeconds(AtlasWidth / PatternSpeed);
            patternAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
            phase.StartAnimation("Travel", patternAnimation);
            patternController = Own(phase.TryGetAnimationController("Travel")!);
            patternController.Pause();

            var stripeAnimation = Own(compositor.CreateScalarKeyFrameAnimation());
            stripeAnimation.InsertKeyFrame(0, 0, linear);
            stripeAnimation.InsertKeyFrame(1, StripePitch, linear);
            stripeAnimation.Duration = TimeSpan.FromSeconds(1.5);
            stripeAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
            stripeMotion.StartAnimation("Offset.X", stripeAnimation);
            stripeController = Own(stripeMotion.TryGetAnimationController("Offset.X")!);
            stripeController.Pause();
            ElementCompositionPreview.SetElementChildVisual(host, output);
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }

    private T Own<T>(T value) where T : CompositionObject { owned.Add(value); return value; }

    public void Resize(double width, double height)
    {
        if (disposed || width <= 0 || height <= 0) return;
        var nextSize = new Vector2((float)width, (float)height);
        if (size == nextSize) return;
        size = nextSize;
        output.Size = maskRoot.Size = opacityMask.Size = inkRoot.Size = baseInk.Size = size;
        maskSurface.SourceSize = fadedMaskSurface.SourceSize = inkSurface.SourceSize = size;
        opacityGradient.EndPoint = size;

        // Invert the fixed rotation to find local viewport bounds, then retain
        // a complete atlas width of overscan along the only moving local axis.
        var inverseRotation = Matrix3x2.CreateRotation(-PatternAngleDegrees * MathF.PI / 180);
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        foreach (var corner in new[] { Vector2.Zero, new Vector2(size.X, 0), new Vector2(0, size.Y), size })
        {
            var local = Vector2.Transform(corner - Origin, inverseRotation);
            minimum = Vector2.Min(minimum, local);
            maximum = Vector2.Max(maximum, local);
        }
        int firstColumn = (int)MathF.Floor(minimum.X / CellWidth) - 1;
        // Wrap individual columns outside the viewport. The cycle remains a
        // multiple of the six-column artwork, preserving phase and variants.
        int columns = Math.Max(6, ((int)MathF.Ceiling((maximum.X-minimum.X)/CellWidth)+2+5)/6*6);
        int lastColumn = firstColumn + columns - 1;
        int firstRow = (int)MathF.Floor(minimum.Y / CellHeight) - 1;
        int lastRow = (int)MathF.Ceiling(maximum.Y / CellHeight) + 1;
        var layout=(firstColumn,lastColumn,firstRow,lastRow);
        if(tileLayout!=layout) {
        tileLayout=layout;
        patternPlane.Children.RemoveAll();
        foreach(var tile in tiles)tile.Dispose();
        tiles.Clear();
        for (int row = firstRow; row <= lastRow; row++)
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int variant = Layout[Wrap(row, 4)][Wrap(column, 6)] - 'A';
                var tile = compositor.CreateSpriteVisual();
                tiles.Add(tile);
                tile.Brush = imageBrushes[variant];
                tile.Size = new Vector2(CellWidth, CellHeight);
                tile.Offset = new Vector3(column * CellWidth, row * CellHeight, 0);
                using var offset=compositor.CreateExpressionAnimation("Mod(x-phase.Travel-left+period*2,period)+left");
                offset.SetReferenceParameter("phase",phase);
                offset.SetScalarParameter("x",column*CellWidth);
                offset.SetScalarParameter("left",firstColumn*CellWidth);
                offset.SetScalarParameter("period",columns*CellWidth);
                tile.StartAnimation("Offset.X",offset);
                patternPlane.Children.InsertAtTop(tile);
            }
        }

        float stripeLeft = -MathF.Ceiling(size.Y / StripePitch) * StripePitch - StripePitch * 2;
        stripePlane.Offset = new Vector3(stripeLeft, 0, 0);
        stripePlane.Size = new Vector2(size.X - stripeLeft + StripePitch * 2, size.Y);
        stripeGeometry.Size = new Vector2(StripeWidth, size.Y);
        int stripeCount=(int)MathF.Ceiling((size.X+StripePitch*2-stripeLeft)/StripePitch);
        while(stripes.Count>stripeCount) {
            var last=stripes[^1];stripePlane.Shapes.Remove(last);stripes.RemoveAt(stripes.Count-1);last.Dispose();
        }
        while(stripes.Count<stripeCount)
        {
            var stripe = compositor.CreateSpriteShape(stripeGeometry);
            stripes.Add(stripe);
            stripe.Offset = new Vector2((stripes.Count-1)*StripePitch, 0);
            stripe.FillBrush = stripeInk;
            stripePlane.Shapes.Add(stripe);
        }
    }

    private static int Wrap(int value, int period) => (value % period + period) % period;

    public void SetVisible(bool visible)
    {
        if (disposed) return;
        output.IsVisible = visible;
        if (!visible) SetMotion(false);
    }

    public void SetMotion(bool enabled)
    {
        if (disposed || moving == enabled) return;
        if (enabled) { patternController.Resume(); stripeController.Resume(); }
        else { patternController.Pause(); stripeController.Pause(); }
        moving = enabled;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ElementCompositionPreview.SetElementChildVisual(host, null);
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        foreach (var stripe in stripes) stripe.Dispose();
        foreach (var tile in tiles) tile.Dispose();
        for (int i = owned.Count - 1; i >= 0; i--) owned[i]?.Dispose();
        foreach (var image in images) image.Dispose();
    }
}
