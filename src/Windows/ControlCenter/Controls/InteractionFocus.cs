using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LumaWall.ControlCenter;

// Hover, focus and selection are independent. A control owns its feedback
// visual for its entire loaded lifetime; visuals never move between hosts.
public sealed class InteractionFocus : DependencyObject
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false,EnabledChanged));
    public static readonly DependencyProperty SurfaceProperty=DependencyProperty.RegisterAttached("Surface",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false));
    public static readonly DependencyProperty FeedbackSurfaceProperty=DependencyProperty.RegisterAttached("FeedbackSurface",typeof(FrameworkElement),typeof(InteractionFocus),new PropertyMetadata(null));
    public static readonly DependencyProperty InteractiveRegionProperty=DependencyProperty.RegisterAttached("InteractiveRegion",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false));
    public static readonly DependencyProperty CustomTargetProperty=DependencyProperty.RegisterAttached("CustomTarget",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false));
    public static readonly DependencyProperty SuppressFeedbackProperty=DependencyProperty.RegisterAttached("SuppressFeedback",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false));
    private static readonly DependencyProperty CoordinatorProperty=DependencyProperty.RegisterAttached("Coordinator",typeof(object),typeof(InteractionFocus),new PropertyMetadata(null));
    private static readonly DependencyProperty HookedProperty=DependencyProperty.RegisterAttached("Hooked",typeof(bool),typeof(InteractionFocus),new PropertyMetadata(false));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetSurface(DependencyObject target)=>(bool)target.GetValue(SurfaceProperty);
    public static void SetSurface(DependencyObject target,bool value)=>target.SetValue(SurfaceProperty,value);
    public static FrameworkElement? GetFeedbackSurface(DependencyObject target)=>target.GetValue(FeedbackSurfaceProperty) as FrameworkElement;
    public static void SetFeedbackSurface(DependencyObject target,FrameworkElement value)=>target.SetValue(FeedbackSurfaceProperty,value);
    public static bool GetInteractiveRegion(DependencyObject target)=>(bool)target.GetValue(InteractiveRegionProperty);
    public static void SetInteractiveRegion(DependencyObject target,bool value)=>target.SetValue(InteractiveRegionProperty,value);
    public static bool GetCustomTarget(DependencyObject target)=>(bool)target.GetValue(CustomTargetProperty);
    public static void SetCustomTarget(DependencyObject target,bool value)=>target.SetValue(CustomTargetProperty,value);
    public static bool GetSuppressFeedback(DependencyObject target)=>(bool)target.GetValue(SuppressFeedbackProperty);
    public static void SetSuppressFeedback(DependencyObject target,bool value)=>target.SetValue(SuppressFeedbackProperty,value);
    internal static Coordinator Initialize(FrameworkElement root,Func<bool> motion,FrameworkElement? contextRoot=null)
    {
        var coordinator=new Coordinator(root,motion);root.SetValue(CoordinatorProperty,coordinator);contextRoot?.SetValue(CoordinatorProperty,coordinator);Hook(root,true);return coordinator;
    }
    private static Coordinator? Owner(UIElement element)=>element.XamlRoot?.Content?.GetValue(CoordinatorProperty) as Coordinator;
    private static void EnabledChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        if(sender is FrameworkElement element)Hook(element,(bool)args.NewValue||element.GetValue(CoordinatorProperty) is Coordinator);
    }
    private static void Hook(FrameworkElement element,bool attach)
    {
        if((bool)element.GetValue(HookedProperty)==attach)return;element.SetValue(HookedProperty,attach);
        if(element is ComboBox or Slider or NumberBox)OptionWheelGuard.SetEnabled((Control)element,attach);
        void Pointer(RoutedEvent routed,PointerEventHandler handler) {if(attach)element.AddHandler(routed,handler,true);else element.RemoveHandler(routed,handler);}
        void Key(RoutedEvent routed,KeyEventHandler handler) {if(attach)element.AddHandler(routed,handler,true);else element.RemoveHandler(routed,handler);}
        Pointer(UIElement.PointerEnteredEvent,Entered);Pointer(UIElement.PointerMovedEvent,Moved);Pointer(UIElement.PointerExitedEvent,Exited);
        Pointer(UIElement.PointerPressedEvent,Pressed);Pointer(UIElement.PointerReleasedEvent,Released);
        Pointer(UIElement.PointerCanceledEvent,Canceled);Pointer(UIElement.PointerCaptureLostEvent,Canceled);
        Key(UIElement.PreviewKeyDownEvent,KeyDown);Key(UIElement.KeyUpEvent,KeyUp);
        if(attach) {element.GotFocus+=FocusChanged;element.LostFocus+=FocusChanged;}
        else {element.GotFocus-=FocusChanged;element.LostFocus-=FocusChanged;}
    }
    private static void Entered(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Move(e,false);
    private static void Moved(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Move(e,true);
    private static void Exited(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Exit(e);
    private static void Pressed(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Press(e);
    private static void Released(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Release(e);
    private static void Canceled(object sender,PointerRoutedEventArgs e)=>Owner((UIElement)sender)?.Cancel(e);
    private static void KeyDown(object sender,KeyRoutedEventArgs e)=>Owner((UIElement)sender)?.KeyDown(e);
    private static void KeyUp(object sender,KeyRoutedEventArgs e)=>Owner((UIElement)sender)?.KeyUp(e);
    private static void FocusChanged(object sender,RoutedEventArgs e) {var owner=Owner((UIElement)sender);owner?.Refresh();owner?.QueueRefresh();}
    private static bool IsComposite(Control control)=>control is NumberBox or Slider or ToggleSwitch or ComboBox;
    // Enabled observes routed events, including events from popup children.
    // It must never turn presenters, dialogs or other containers into actions.
    private static bool IsTarget(Control control)=>control is ButtonBase or TextBox or PasswordBox or SelectorItem or MenuFlyoutItem or ToggleMenuFlyoutItem or MenuFlyoutSubItem
        ||(GetCustomTarget(control)&&control.IsTabStop);
    internal static bool IsInteractive(DependencyObject? source)
    {
        for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node))
            if(GetInteractiveRegion(node)||node is Control control&&(IsComposite(control)||IsTarget(control)||control is ScrollBar or Thumb))return true;
        return false;
    }
    internal static Control? Target(DependencyObject? source)
    {
        Control? candidate=null;
        for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node)) {
            // Dismiss scrims handle clicks, but are not visible action surfaces.
            // Keep their input semantics while excluding their entire subtree.
            if(node is Slider||GetSuppressFeedback(node))return null;
            if(node is not Control control)continue;
            if(IsComposite(control))return Available(control)?control:null;
            if(candidate==null&&IsTarget(control))candidate=control;
        }
        return candidate!=null&&Available(candidate)?candidate:null;
    }
    private static bool Available(Control control)
    {
        if(!control.IsLoaded)return false;
        for(DependencyObject? node=control;node!=null;node=VisualTreeHelper.GetParent(node)) {
            if(node is Slider||GetSuppressFeedback(node))return false;
            if(node is UIElement element&&(element.Visibility!=Visibility.Visible||!element.IsHitTestVisible))return false;
            if(node is Control parent&&!parent.IsEnabled)return false;
        }
        return true;
    }
    internal static FrameworkElement SurfaceFor(Control control)
    {
        // Opt-in only: a section header can draw feedback around its containing card.
        if(GetFeedbackSurface(control) is { } surface&&surface.IsLoaded)return surface;
        // The NumberBox edit surface lives inside the InputBox child template.
        if(control is NumberBox&&NumericCommit.TextEditor(control) is { } editor)return SurfaceFor(editor);
        if(control is ToggleSwitch) {
            // A switch's label and hit target are wider than the visible capsule.
            // Use a stable transparent track overlay: OuterBorder fades out when on.
            FrameworkElement? Track(DependencyObject node) {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) {
                    var child=VisualTreeHelper.GetChild(node,i);
                    if(child is FrameworkElement field&&field.Name=="SwitchFeedbackSurface")return field;
                    if(Track(child) is { } found)return found;
                }
                return null;
            }
            if(Track(control) is { } track)return track;
        }
        FrameworkElement? Find(DependencyObject node,bool marked) {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) {
                if(VisualTreeHelper.GetChild(node,i) is not FrameworkElement child)continue;
                if(GetSurface(child)||!marked&&child.Name is "LayoutRoot" or "BorderElement" or "Background")return child;
                if(child is Control)continue; // Nested actions own their own surface.
                if(Find(child,marked) is { } found)return found;
            }
            return null;
        }
        return Find(control,true)??Find(control,false)??(VisualTreeHelper.GetChildrenCount(control)>0?VisualTreeHelper.GetChild(control,0) as FrameworkElement:null)??control;
    }

    internal sealed class Coordinator : IDisposable
    {
        private readonly FrameworkElement root;
        private readonly Func<bool> motion;
        private readonly Dictionary<Control,Feedback> feedback=new();
        private Control? hovered,focused,pointerTarget,keyTarget;
        private Control? displayedHover,displayedFocus;
        private uint? pressedPointer;
        private VirtualKey? pressedKey;
        private object? lastPointerEvent,lastKeyEvent;
        private bool disposed,active=true,queued,refreshing,pointerInside,keyboardInput;
        internal Coordinator(FrameworkElement root,Func<bool> motion)
        {
            this.root=root;this.motion=motion;root.Loaded+=RootLoaded;root.LayoutUpdated+=LayoutUpdated;
        }
        private bool BeginPointer(PointerRoutedEventArgs e)
        {
            if(disposed||!active||ReferenceEquals(lastPointerEvent,e))return false;lastPointerEvent=e;return true;
        }
        private bool Contains(Control control,PointerRoutedEventArgs e)
        {
            // The captured card follows the pointer. Keep its existing pressed
            // feedback stable without a second PointerPoint read per move.
            if(control is EffectReorderButton {IsMouseDragging:true})return true;
            var surface=feedback.TryGetValue(control,out var cached)&&cached.Surface.IsLoaded?cached.Surface:SurfaceFor(control);var point=e.GetCurrentPoint(surface).Position;
            return point.X>=0&&point.Y>=0&&point.X<surface.ActualWidth&&point.Y<surface.ActualHeight;
        }
        internal void Move(PointerRoutedEventArgs e,bool moved)
        {
            if(!BeginPointer(e)||e.Pointer.PointerDeviceType!=PointerDeviceType.Mouse)return;
            // Opening a popup can raise PointerEntered without mouse movement.
            // Only an actual move switches menu feedback back to pointer input.
            bool inputChanged=moved&&keyboardInput;if(moved)keyboardInput=false;
            if(pressedPointer!=null) {
                bool inside=pointerTarget!=null&&Contains(pointerTarget,e);if(inside==pointerInside&&!inputChanged)return;pointerInside=inside;
                hovered=pointerInside?pointerTarget:null;Refresh();return;
            }
            var next=Target(e.OriginalSource as DependencyObject);
            if(next!=null&&!Contains(next,e))next=null;
            if(hovered==next&&!inputChanged)return;hovered=next;Refresh();
        }
        internal void Exit(PointerRoutedEventArgs e)
        {
            if(!BeginPointer(e))return;
            if(pointerTarget is EffectReorderButton {IsMouseDragging:true})return;
            if(pressedPointer!=null) {
                pointerInside=pointerTarget!=null&&Contains(pointerTarget,e);
                hovered=pointerInside?pointerTarget:null;Refresh();return;
            }
            if(hovered!=null&&!Contains(hovered,e)) {hovered=null;Refresh();}
        }
        internal void Press(PointerRoutedEventArgs e)
        {
            if(!BeginPointer(e))return;keyboardInput=false;
            if(!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) {Refresh();QueueRefresh();return;}
            ClearPress();pointerTarget=Target(e.OriginalSource as DependencyObject);
            if(pointerTarget!=null) {pressedPointer=e.Pointer.PointerId;pointerInside=Contains(pointerTarget,e);}
            hovered=e.Pointer.PointerDeviceType==PointerDeviceType.Mouse?pointerTarget:null;Refresh();QueueRefresh();
        }
        internal void Release(PointerRoutedEventArgs e)
        {
            if(!BeginPointer(e)||pressedPointer!=e.Pointer.PointerId)return;
            ClearPress();if(hovered!=null&&!Contains(hovered,e))hovered=null;Refresh();
        }
        internal void Cancel(PointerRoutedEventArgs e)
        {
            if(!BeginPointer(e)||pressedPointer!=e.Pointer.PointerId)return;
            ClearPress();if(hovered!=null&&!Contains(hovered,e))hovered=null;Refresh();
        }
        internal void KeyDown(KeyRoutedEventArgs e)
        {
            if(disposed||!active||ReferenceEquals(lastKeyEvent,e))return;lastKeyEvent=e;keyboardInput=true;Refresh();
            if(pressedPointer!=null||e.Key is not (VirtualKey.Enter or VirtualKey.Space))return;
            if(focused is not (ButtonBase or SelectorItem or ToggleSwitch or ComboBox or MenuFlyoutItemBase))return;
            pressedKey=e.Key;keyTarget=focused;Refresh(); // No swallowed input or redirected focus.
        }
        internal void KeyUp(KeyRoutedEventArgs e)
        {
            if(disposed||ReferenceEquals(lastKeyEvent,e))return;lastKeyEvent=e;
            if(pressedKey==e.Key) {pressedKey=null;keyTarget=null;Refresh();}
        }
        private void ClearPress() {pressedPointer=null;pressedKey=null;pointerTarget=null;keyTarget=null;pointerInside=false;}
        internal void QueueRefresh()
        {
            if(disposed||queued)return;queued=true;
            if(!root.DispatcherQueue.TryEnqueue(()=> {queued=false;Refresh();}))queued=false;
        }
        internal void Refresh()
        {
            if(disposed||refreshing)return;refreshing=true;
            try {
                var next=!active||root.XamlRoot==null?null:Target(FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject);
                if(next!=focused&&keyTarget!=next) {pressedKey=null;keyTarget=null;}
                focused=next;
                if(hovered!=null&&(!active||!Available(hovered)))hovered=null;
                if(pointerTarget!=null&&!Available(pointerTarget)) {pressedPointer=null;pointerTarget=null;}
                // MenuFlyout automatically focuses its first item even when opened
                // by a pointer. Keep that real focus for input, but only draw its
                // focus cue during keyboard use; pointer hover stays independent.
                var focusFeedback=focused is MenuFlyoutItemBase&&!keyboardInput?null:focused;
                if(displayedHover!=null&&displayedHover!=focusFeedback&&displayedHover!=hovered&&feedback.TryGetValue(displayedHover,out var previousHover))previousHover.Hide();
                if(displayedFocus!=null&&displayedFocus!=focusFeedback&&displayedFocus!=hovered&&feedback.TryGetValue(displayedFocus,out var previousFocus))previousFocus.Hide();
                if(hovered!=null)Display(hovered,hovered==focusFeedback);
                if(focusFeedback!=null&&focusFeedback!=hovered)Display(focusFeedback,true);
                displayedHover=hovered;displayedFocus=focusFeedback;
            }finally {refreshing=false;}
        }
        private void Display(Control control,bool hasFocus)
        {
            var surface=feedback.TryGetValue(control,out var existing)&&existing.Surface.IsLoaded?existing.Surface:SurfaceFor(control);
            if(feedback.TryGetValue(control,out var old)&&old.Surface!=surface) {old.Dispose();feedback.Remove(control);control.Unloaded-=TargetUnloaded;}
            if(!feedback.TryGetValue(control,out var visual)) {
                // Collapsed pages can remain loaded for the application's whole
                // lifetime. Retain only a small working set of hover geometry.
                if(feedback.Count>=8) {
                    var cold=feedback.FirstOrDefault(entry=>entry.Key!=hovered&&entry.Key!=focused&&entry.Key!=pointerTarget&&entry.Key!=keyTarget);
                    if(cold.Key!=null) {feedback.Remove(cold.Key);cold.Key.Unloaded-=TargetUnloaded;cold.Value.Dispose();}
                }
                visual=new Feedback(control,surface);feedback.Add(control,visual);control.Unloaded+=TargetUnloaded;
            }
            visual.Show(hasFocus,(pressedPointer!=null&&pointerInside&&pointerTarget==control)||(pressedKey!=null&&keyTarget==control),motion());
        }
        internal void SetActive(bool value) {active=value;if(!value) {ClearPress();hovered=null;}Refresh();}
        private void RootLoaded(object sender,RoutedEventArgs e)=>Refresh();
        private void LayoutUpdated(object? sender,object e)
        {
            if(active&&(hovered!=null||focused!=null||displayedHover!=null||displayedFocus!=null))QueueRefresh();
        }
        private void TargetUnloaded(object sender,RoutedEventArgs e)
        {
            if(sender is not Control control)return;control.Unloaded-=TargetUnloaded;
            if(feedback.Remove(control,out var visual))visual.Dispose();
            if(hovered==control)hovered=null;if(pointerTarget==control||keyTarget==control)ClearPress();QueueRefresh();
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;ClearPress();root.Loaded-=RootLoaded;root.LayoutUpdated-=LayoutUpdated;Hook(root,false);root.ClearValue(CoordinatorProperty);
            foreach(var entry in feedback) {entry.Key.Unloaded-=TargetUnloaded;entry.Value.Dispose();}feedback.Clear();
        }
    }

    // Four clipped quadrants preserve asymmetric corners. The stroke's outer
    // edge stays on the surface bounds at both normal and pressed thickness.
    private sealed class Feedback : IDisposable
    {
        internal FrameworkElement Surface {get;}
        private readonly Control control;
        private readonly ContainerVisual layer;
        private readonly ContainerVisual? sharedHost;
        private readonly CompositionPropertySet state;
        private readonly CompositionColorBrush brush;
        private readonly ShapeVisual[] quadrants=new ShapeVisual[4];
        private readonly CompositionRoundedRectangleGeometry[] geometries=new CompositionRoundedRectangleGeometry[4];
        private readonly CompositionSpriteShape[] strokes=new CompositionSpriteShape[4];
        private readonly InsetClip[] clips=new InsetClip[4];
        private readonly object previousSystemFocus;
        private bool pressed;
        private bool visible;
        private bool disposed;
        private Vector2 lastSize;
        private CornerRadius lastRadius;
        internal Feedback(Control control,FrameworkElement surface)
        {
            this.control=control;Surface=surface;var compositor=ElementCompositionPreview.GetElementVisual(surface).Compositor;
            layer=compositor.CreateContainerVisual();layer.Comment="InteractionFeedback";layer.Opacity=0;
            state=compositor.CreatePropertySet();state.InsertScalar("Thickness",2);state.InsertScalar("Width",4);state.InsertScalar("Height",4);
            brush=InteractionAccent.GetBrush(compositor);
            for(int i=0;i<4;i++) {
                state.InsertScalar("R"+i,0);var quadrant=quadrants[i]=compositor.CreateShapeVisual();quadrant.Comment="InteractionFeedback.Corner."+i;
                var geometry=geometries[i]=compositor.CreateRoundedRectangleGeometry();var stroke=strokes[i]=compositor.CreateSpriteShape(geometry);stroke.StrokeBrush=brush;
                quadrant.Shapes.Add(stroke);layer.Children.InsertAtTop(quadrant);clips[i]=compositor.CreateInsetClip();quadrant.Clip=clips[i];
            }
            previousSystemFocus=control.ReadLocalValue(Control.UseSystemFocusVisualsProperty);control.UseSystemFocusVisuals=false;
            // SetElementChildVisual is committed by a later XAML render walk.
            // Never take its current child out of XAML's native collection and
            // reparent it ourselves: XAML still expects to remove that child.
            // An existing container keeps ownership of its content and lifetime.
            var existingChild=ElementCompositionPreview.GetElementChildVisual(surface);
            if(existingChild is ContainerVisual container) {
                sharedHost=container;sharedHost.Children.InsertAtTop(layer);
            } else if(existingChild==null)ElementCompositionPreview.SetElementChildVisual(surface,layer);
        }
        private void Animate(CompositionObject target,string property,string expression)
        {
            using var animation=state.Compositor.CreateExpressionAnimation(expression);animation.SetReferenceParameter("state",state);target.StartAnimation(property,animation);
        }
        private void StartGeometry()
        {
            for(int i=0;i<4;i++) {
                Animate(strokes[i],"StrokeThickness","state.Thickness");
                Animate(geometries[i],"Offset","Vector2(state.Thickness / 2, state.Thickness / 2)");
                Animate(geometries[i],"Size","Vector2(state.Width - state.Thickness, state.Height - state.Thickness)");
                Animate(geometries[i],"CornerRadius","Vector2(Max(0, state.R"+i+" - state.Thickness / 2), Max(0, state.R"+i+" - state.Thickness / 2))");
            }
        }
        private void StopGeometry()
        {
            for(int i=0;i<4;i++) {
                strokes[i].StopAnimation("StrokeThickness");geometries[i].StopAnimation("Offset");
                geometries[i].StopAnimation("Size");geometries[i].StopAnimation("CornerRadius");
            }
        }
        internal void Show(bool focused,bool down,bool motion)
        {
            if(disposed)return;
            var width=(float)Surface.ActualWidth;var height=(float)Surface.ActualHeight;
            if(width<4||height<4) {Hide();return;}
            var radius=Surface switch {Border border=>border.CornerRadius,Grid grid=>grid.CornerRadius,Control field=>field.CornerRadius,_=>control.CornerRadius};
            var size=new Vector2(width,height);
            if(lastSize!=size||lastRadius!=radius) {
                lastSize=size;lastRadius=radius;layer.Size=size;
                state.InsertScalar("Width",width);state.InsertScalar("Height",height);
                for(int i=0;i<4;i++) {
                    float corner=(float)(i switch {0=>radius.TopLeft,1=>radius.TopRight,2=>radius.BottomRight,_=>radius.BottomLeft});
                    quadrants[i].Size=size;state.InsertScalar("R"+i,Math.Clamp(corner,0,Math.Min(width,height)/2));
                    bool right=i is 1 or 2,bottom=i is 2 or 3;
                    clips[i].LeftInset=right?width/2:0;clips[i].RightInset=right?0:width/2;clips[i].TopInset=bottom?height/2:0;clips[i].BottomInset=bottom?0:height/2;
                }
            }
            // The shared accent clock owns color. Only this control's geometry
            // and press thickness change here; hover never restarts the breath.
            if(down!=pressed) {
                pressed=down;state.StopAnimation("Thickness");
                if(down||!motion)state.InsertScalar("Thickness",down?4:2);
                else {using var animation=state.Compositor.CreateScalarKeyFrameAnimation();animation.InsertKeyFrame(1,2);animation.Duration=TimeSpan.FromMilliseconds(100);state.StartAnimation("Thickness",animation);}
            }
            if(!visible) {StartGeometry();layer.Opacity=1;visible=true;}
        }
        internal void Hide() {if(disposed||!visible)return;visible=false;layer.Opacity=0;pressed=false;state.StopAnimation("Thickness");state.InsertScalar("Thickness",2);StopGeometry();}
        public void Dispose()
        {
            if(disposed)return;
            Hide();disposed=true;
            if(sharedHost!=null) {
                // Its owner may have already removed or replaced the container.
                if(layer.Parent==sharedHost)sharedHost.Children.Remove(layer);
            } else if(ElementCompositionPreview.GetElementChildVisual(Surface)==layer) {
                ElementCompositionPreview.SetElementChildVisual(Surface,null);
            }
            if(previousSystemFocus==DependencyProperty.UnsetValue)control.ClearValue(Control.UseSystemFocusVisualsProperty);else control.SetValue(Control.UseSystemFocusVisualsProperty,previousSystemFocus);
            state.StopAnimation("Thickness");
            for(int i=0;i<4;i++) {
                strokes[i].StopAnimation("StrokeThickness");
                geometries[i].StopAnimation("Offset");geometries[i].StopAnimation("Size");geometries[i].StopAnimation("CornerRadius");
                quadrants[i].Shapes.Clear();quadrants[i].Clip=null;
                strokes[i].StrokeBrush=null;
            }
            layer.Children.RemoveAll();
            // Do not Close/Dispose the hand-in visual here. XAML may still hold
            // it until UpdateHandInVisual/CleanupHandInVisual on its next frame.
            // Stopped animations and detached owned children do no further work;
            // COM reference release reclaims them after the last native owner.
            // The shared host and accent brush always remain with their owners.
        }
    }
}
