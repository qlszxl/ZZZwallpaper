using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using LumaWall.Contracts;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private EffectCardReorderSession? effectReorder;
    private EffectKind[] effectDragOriginal=[];
    private readonly Dictionary<EffectKind,Point> effectDragSlots=new();
    private readonly Dictionary<EffectKind,EffectDragVisual> effectDragVisuals=new();
    private CubicBezierEasingFunction? effectDragEasing;
    private EffectReorderButton? effectDragButton;
    private XamlRoot? effectDragRoot;
    private EffectDragTransform effectDragToPanel,effectDragToScroll,effectDragFromClient;
    private Size effectDragRootSize,effectDragScrollSize;
    private double effectDragScale,effectDragScrollX,effectDragScrollY,effectDragZoom;
    private nint effectDragWindow;
    private int effectDragMouseButton;
    private Point effectDragGrab,effectDragLastRoot,effectDragTopLeft;
    private int effectDragColumns;
    private double effectDragWidth,effectDragCardWidth;
    private long effectDragFrame;
    private bool EffectDragMotion=>Appearance.Motion&&homeUiSettings?.AnimationsEnabled!=false;

    [DllImport("user32.dll",EntryPoint="ScreenToClient")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool EffectDragScreenToClient(nint window,ref ShellResizePoint point);

    // Snapshot the affine coordinate bases once. Composition translations do
    // not change layout; only the ScrollViewer offsets vary during a gesture.
    private readonly record struct EffectDragTransform(Point Origin,Point X,Point Y)
    {
        internal static EffectDragTransform Capture(UIElement from,UIElement to)
        {
            var transform=from.TransformToVisual(to);
            return new(transform.TransformPoint(new()),transform.TransformPoint(new(1,0)),transform.TransformPoint(new(0,1)));
        }
        internal Point Apply(Point value)=>new(Origin.X+(X.X-Origin.X)*value.X+(Y.X-Origin.X)*value.Y,
            Origin.Y+(X.Y-Origin.Y)*value.X+(Y.Y-Origin.Y)*value.Y);
    }
    private sealed class EffectDragVisual : IDisposable
    {
        internal readonly Visual Visual;
        private readonly CompositionPropertySet properties;
        private Vector3KeyFrameAnimation? animation;
        private Vector3 destination;
        private bool animated;
        internal EffectDragVisual(FrameworkElement card)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(card,true);
            Visual=ElementCompositionPreview.GetElementVisual(card);properties=Visual.Properties;
            Visual.StopAnimation("Translation");Visual.StopAnimation("Scale");
            properties.InsertVector3("Translation",Vector3.Zero);Visual.Scale=Vector3.One;
        }
        internal void Move(Vector3 target,CubicBezierEasingFunction? easing)
        {
            bool animate=easing!=null;
            if(destination==target&&animated==animate)return;
            destination=target;animated=animate;
            if(!animate) {Visual.StopAnimation("Translation");properties.InsertVector3("Translation",target);return;}
            if(animation==null) {
                animation=Visual.Compositor.CreateVector3KeyFrameAnimation();
                animation.InsertExpressionKeyFrame(0,"this.StartingValue");animation.Duration=TimeSpan.FromMilliseconds(160);
            }
            animation.InsertKeyFrame(1,target,easing);Visual.StartAnimation("Translation",animation);
        }
        // Visual and its property set belong to XAML. Only our animation is owned.
        public void Dispose()=>animation?.Dispose();
    }

    private bool BeginEffectCardDrag(EffectKind kind,string targetKey,Point pressRoot)
    {
        if(effectReorder!=null||SelectedScreen==null||loadingEffects||selectedEffectKind!=null||targetKey!=effectSession.Key
            ||!effectLayerControls.TryGetValue(kind,out var source)||EffectsListPanel.ActualWidth<=0)return false;
        StopPageEntry();
        effectDragOriginal=EffectLayerPresentation.Visible(effectSession.Collection);
        if(effectDragOriginal.Length<2)return false;
        effectDragColumns=Math.Max(1,EffectsListPanel.ColumnDefinitions.Count);
        effectDragWidth=EffectsListPanel.ActualWidth;effectDragCardWidth=source.Container.ActualWidth;
        effectDragSlots.Clear();
        foreach(var id in effectDragOriginal) {
            var container=effectLayerControls[id].Container;
            effectDragVisuals.Add(id,new(container));
            effectDragSlots[id]=container.TransformToVisual(EffectsListPanel).TransformPoint(new Point());
        }
        effectDragRoot=Root.XamlRoot;effectDragScale=effectDragRoot.RasterizationScale;
        effectDragRootSize=effectDragRoot.Size;
        effectDragScrollSize=new(EffectsParameterScroll.ActualWidth,EffectsParameterScroll.ActualHeight);
        effectDragScrollX=EffectsParameterScroll.HorizontalOffset;effectDragScrollY=EffectsParameterScroll.VerticalOffset;
        effectDragZoom=EffectsParameterScroll.ZoomFactor;
        effectDragToPanel=EffectDragTransform.Capture(Root,EffectsListPanel);
        effectDragToScroll=EffectDragTransform.Capture(Root,EffectsParameterScroll);
        effectDragFromClient=EffectDragTransform.Capture(effectDragRoot.Content,Root);
        effectDragWindow=WinRT.Interop.WindowNative.GetWindowHandle(this);
        effectDragMouseButton=GetSystemMetrics(23)!=0?2:1;
        effectDragButton=source.Card;effectDragButton.SampleMouseDrag=SampleEffectDragMouse;
        effectReorder=new(effectSession.Collection,targetKey,kind);
        var local=effectDragToPanel.Apply(pressRoot);var slot=effectDragSlots[kind];
        effectDragGrab=new(local.X-slot.X,local.Y-slot.Y);effectDragTopLeft=slot;effectDragLastRoot=pressRoot;
        Canvas.SetZIndex(source.Container,100);source.DragOutline.Visibility=Visibility.Visible;
        var lifted=effectDragVisuals[kind].Visual;
        lifted.CenterPoint=new((float)(source.Container.ActualWidth/2),56,0);lifted.Scale=new(1.025f,1.025f,1);lifted.Opacity=.97f;
        effectDragFrame=Stopwatch.GetTimestamp();CompositionTarget.Rendering+=RenderEffectCardDrag;
        return true;
    }
    private bool EffectDragTargetCurrent()=>effectReorder is { } drag&&drag.TargetKey==effectSession.Key&&SelectedScreen!=null
        &&!loadingEffects&&selectedEffectKind==null&&navigationPage=="display"&&drag.InitialCollection==effectSession.Collection
        &&effectDragColumns==EffectsListPanel.ColumnDefinitions.Count&&Math.Abs(effectDragWidth-EffectsListPanel.ActualWidth)<.5
        &&effectDragRoot is { } root&&root.RasterizationScale==effectDragScale&&root.Size==effectDragRootSize
        &&EffectsParameterScroll.ActualWidth==effectDragScrollSize.Width&&EffectsParameterScroll.ActualHeight==effectDragScrollSize.Height
        &&EffectsParameterScroll.ZoomFactor==effectDragZoom;

    private Point? SampleEffectDragMouse()
    {
        // A physical button-up can precede delivery of PointerReleased. Leave
        // the commit to that event, which flushes its exact final coordinate.
        if((GetAsyncKeyState(effectDragMouseButton)&0x8000)==0||!GetCursorPos(out var point)
            ||!EffectDragScreenToClient(effectDragWindow,ref point))return null;
        return effectDragFromClient.Apply(new(point.X/effectDragScale,point.Y/effectDragScale));
    }

    private void MoveEffectCardDrag(Point rootPoint)
    {
        if(effectReorder is not { } drag)return;
        if(!EffectDragTargetCurrent()) {EndEffectCardDrag(false);return;}
        effectDragLastRoot=rootPoint;
        var local=effectDragToPanel.Apply(rootPoint);
        local.X+=(EffectsParameterScroll.HorizontalOffset-effectDragScrollX)/effectDragZoom;
        local.Y+=(EffectsParameterScroll.VerticalOffset-effectDragScrollY)/effectDragZoom;
        var origin=effectDragSlots[drag.Source];
        effectDragTopLeft=new(local.X-effectDragGrab.X,local.Y-effectDragGrab.Y);
        effectDragVisuals[drag.Source].Move(new((float)(effectDragTopLeft.X-origin.X),(float)(effectDragTopLeft.Y-origin.Y),0),null);
        int target=EffectCardReorderSession.TargetIndex(effectDragTopLeft.X+effectDragCardWidth/2,effectDragTopLeft.Y+56,
            effectDragWidth,effectDragColumns,effectDragOriginal.Length);
        if(!drag.MoveToIndex(target))return;
        for(int i=0;i<drag.PreviewOrder.Count;i++) {
            var id=drag.PreviewOrder[i];if(id==drag.Source)continue;
            var from=effectDragSlots[id];var to=effectDragSlots[effectDragOriginal[i]];
            effectDragVisuals[id].Move(new((float)(to.X-from.X),(float)(to.Y-from.Y),0),EffectDragAnimation());
        }
    }
    private CubicBezierEasingFunction? EffectDragAnimation()=>!EffectDragMotion?null:effectDragEasing??=
        effectDragVisuals[effectDragOriginal[0]].Visual.Compositor.CreateCubicBezierEasingFunction(new(.2f,.7f),new(.2f,1));
    private static void MoveEffectCardVisual(FrameworkElement card,Vector3 target,bool animate)
    {
        var visual=ElementCompositionPreview.GetElementVisual(card);
        if(!animate) {visual.StopAnimation("Translation");visual.Properties.InsertVector3("Translation",target);return;}
        using var animation=visual.Compositor.CreateVector3KeyFrameAnimation();
        using var easing=visual.Compositor.CreateCubicBezierEasingFunction(new(.2f,.7f),new(.2f,1));
        animation.InsertExpressionKeyFrame(0,"this.StartingValue");animation.InsertKeyFrame(1,target,easing);
        animation.Duration=TimeSpan.FromMilliseconds(160);visual.StartAnimation("Translation",animation);
    }
    private void RenderEffectCardDrag(object? sender,object e)
    {
        if(effectReorder==null)return;
        if(!EffectDragTargetCurrent()) {EndEffectCardDrag(false);return;}
        if(effectDragButton?.ReadDragPosition() is { } latest)effectDragLastRoot=latest;
        long now=Stopwatch.GetTimestamp();double dt=Math.Clamp((now-effectDragFrame)/(double)Stopwatch.Frequency,0,.04);effectDragFrame=now;
        var pointer=effectDragToScroll.Apply(effectDragLastRoot);
        double height=effectDragScrollSize.Height,velocity=0;
        if(pointer.X>=0&&pointer.X<=effectDragScrollSize.Width&&height>0) {
            if(pointer.Y<48)velocity=-720*Math.Clamp((48-pointer.Y)/48,0,1);
            else if(pointer.Y>height-48)velocity=720*Math.Clamp((pointer.Y-height+48)/48,0,1);
        }
        if(velocity!=0) {
            double offset=Math.Clamp(EffectsParameterScroll.VerticalOffset+velocity*dt,0,EffectsParameterScroll.ScrollableHeight);
            if(Math.Abs(offset-EffectsParameterScroll.VerticalOffset)>.01)EffectsParameterScroll.ChangeView(null,offset,null,true);
        }
        MoveEffectCardDrag(effectDragLastRoot);
    }
    private void EndEffectCardDrag(bool commit)
    {
        if(effectReorder is not { } drag)return;
        bool save=commit&&EffectDragTargetCurrent()&&drag.HasChanges;
        var final=save?drag.Commit():drag.Cancel();var source=effectLayerControls.GetValueOrDefault(drag.Source);
        var landing=effectDragTopLeft;
        effectReorder=null;CompositionTarget.Rendering-=RenderEffectCardDrag;
        if(effectDragButton!=null)effectDragButton.SampleMouseDrag=null;
        foreach(var pair in effectLayerControls) {
            var controls=pair.Value;
            controls.DragOutline.Visibility=Visibility.Collapsed;Canvas.SetZIndex(controls.Container,0);
            if(!effectDragVisuals.TryGetValue(pair.Key,out var state))continue;
            state.Visual.Scale=Vector3.One;state.Visual.Opacity=1;
            state.Move(Vector3.Zero,!save?EffectDragAnimation():null);
        }
        foreach(var state in effectDragVisuals.Values)state.Dispose();
        effectDragVisuals.Clear();effectDragEasing?.Dispose();effectDragEasing=null;effectDragRoot=null;effectDragButton=null;
        source?.Card.CancelGesture();
        if(!save)return;
        int index=Array.IndexOf(EffectLayerPresentation.Visible(final),drag.Source);
        var destination=effectDragSlots[effectDragOriginal[index]];
        RunEffectOrderGesture(_=>final,drag.Source);
        if(effectLayerControls.TryGetValue(drag.Source,out var arrived)) {
            ElementCompositionPreview.SetIsTranslationEnabled(arrived.Container,true);
            var visual=ElementCompositionPreview.GetElementVisual(arrived.Container);
            visual.Properties.InsertVector3("Translation",new((float)(landing.X-destination.X),(float)(landing.Y-destination.Y),0));
            MoveEffectCardVisual(arrived.Container,Vector3.Zero,EffectDragMotion);
        }
    }
}
