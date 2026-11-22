using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LumaWall.ControlCenter;

// Keep the native ScrollViewer and its gutter intact. Only the bar's paint is
// hidden, so scroll offsets, virtualization and content width never change.
public sealed class ScrollBarAutoHide : DependencyObject
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ScrollBarAutoHide),new PropertyMetadata(false,EnabledChanged));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(object),typeof(ScrollBarAutoHide),new PropertyMetadata(null));
    private static readonly DependencyProperty MotionProperty=DependencyProperty.RegisterAttached("Motion",typeof(object),typeof(ScrollBarAutoHide),new PropertyMetadata(null));
    internal static readonly TimeSpan IdleDelay=TimeSpan.FromMilliseconds(1500);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    internal static void SetMotion(DependencyObject root,Func<bool> motion)=>root.SetValue(MotionProperty,motion);
    internal static bool IsRevealed(ScrollBar bar)=>(bar.GetValue(StateProperty) as State)?.Revealed==true;
    private static void EnabledChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not ScrollBar bar)return;
        (bar.GetValue(StateProperty) as State)?.Dispose();
        bar.SetValue(StateProperty,(bool)args.NewValue?new State(bar):null);
    }
    private sealed class State : IDisposable
    {
        private readonly ScrollBar bar;
        private readonly DispatcherQueueTimer timer;
        private ScrollViewer? viewer;
        private Storyboard? fade;
        private bool loaded,held;
        private uint? pointer;
        private long rangeToken;
        internal bool Revealed {get;private set;}
        internal State(ScrollBar bar)
        {
            this.bar=bar;timer=bar.DispatcherQueue.CreateTimer();timer.Interval=IdleDelay;timer.IsRepeating=false;timer.Tick+=Idle;
            bar.Loaded+=Loaded;bar.Unloaded+=Unloaded;
            if(bar.IsLoaded)Attach();else bar.Opacity=0;
        }
        private void Loaded(object sender,RoutedEventArgs e)=>Attach();
        private void Unloaded(object sender,RoutedEventArgs e)=>Detach();
        private static ScrollViewer? Owner(DependencyObject? node)
        {
            for(;node!=null;node=VisualTreeHelper.GetParent(node))if(node is ScrollViewer scroll)return scroll;
            return null;
        }
        private void Attach()
        {
            if(loaded)return;loaded=true;viewer=Owner(bar);
            bar.ValueChanged+=Changed;
            rangeToken=bar.RegisterPropertyChangedCallback(RangeBase.MaximumProperty,RangeChanged);
            bar.AddHandler(UIElement.PointerEnteredEvent,new PointerEventHandler(Moved),true);
            bar.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler(Moved),true);
            bar.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(Pressed),true);
            bar.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(Released),true);
            bar.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(Released),true);
            bar.AddHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler(Released),true);
            viewer?.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler(Wheel),true);
            Show();
        }
        private void Detach()
        {
            if(!loaded)return;loaded=false;held=false;pointer=null;timer.Stop();fade?.Stop();fade=null;Revealed=false;bar.Opacity=0;
            bar.ValueChanged-=Changed;bar.UnregisterPropertyChangedCallback(RangeBase.MaximumProperty,rangeToken);
            bar.RemoveHandler(UIElement.PointerEnteredEvent,new PointerEventHandler(Moved));
            bar.RemoveHandler(UIElement.PointerMovedEvent,new PointerEventHandler(Moved));
            bar.RemoveHandler(UIElement.PointerPressedEvent,new PointerEventHandler(Pressed));
            bar.RemoveHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(Released));
            bar.RemoveHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(Released));
            bar.RemoveHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler(Released));
            viewer?.RemoveHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler(Wheel));viewer=null;
        }
        private void RangeChanged(DependencyObject sender,DependencyProperty property)=>Show();
        private void Changed(object sender,RangeBaseValueChangedEventArgs e)=>Show();
        private void Moved(object sender,PointerRoutedEventArgs e)=>Show();
        private void Wheel(object sender,PointerRoutedEventArgs e)
        {
            // A nested list owns its own activity; do not reveal outer bars.
            if(Owner(e.OriginalSource as DependencyObject)==viewer)Show();
        }
        private void Pressed(object sender,PointerRoutedEventArgs e)
        {
            if(!e.GetCurrentPoint(bar).IsInContact)return;
            held=true;pointer=e.Pointer.PointerId;Show();
        }
        private void Released(object sender,PointerRoutedEventArgs e)
        {
            if(pointer!=e.Pointer.PointerId)return;
            held=false;pointer=null;Show();
        }
        private void Show()
        {
            timer.Stop();fade?.Stop();fade=null;
            Revealed=loaded&&bar.Maximum>bar.Minimum;
            bar.Opacity=Revealed?1:0;
            if(Revealed&&!held)timer.Start();
        }
        private void Idle(DispatcherQueueTimer sender,object args)
        {
            if(!loaded||held)return;
            Revealed=false;
            bool motion=bar.XamlRoot?.Content?.GetValue(MotionProperty) is Func<bool> enabled&&enabled();
            if(!motion){bar.Opacity=0;return;}
            fade=new Storyboard();
            var animation=new DoubleAnimation {From=1,To=0,Duration=new Duration(TimeSpan.FromMilliseconds(200))};
            Storyboard.SetTarget(animation,bar);Storyboard.SetTargetProperty(animation,"Opacity");fade.Children.Add(animation);fade.Begin();
        }
        public void Dispose()
        {
            Detach();timer.Stop();timer.Tick-=Idle;bar.Loaded-=Loaded;bar.Unloaded-=Unloaded;bar.Opacity=1;
        }
    }
}
