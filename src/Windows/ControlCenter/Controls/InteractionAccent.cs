using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumaWall.ControlCenter;

// XAML and hover/focus visuals borrow the same compositor-owned color clock.
// No animated XAML property or binding is evaluated on the UI thread per frame.
internal static class InteractionAccent
{
    [ThreadStatic] private static AccentClock? clock;
    private static AccentClock Current => clock ??= new AccentClock();

    internal static void Configure(Color yellow,Color green) => Current.Configure(yellow,green);
    internal static void SetMotion(bool enabled) => Current.SetMotion(enabled);
    internal static CompositionColorBrush GetBrush(Compositor compositor) => Current.GetBrush(compositor);

    private sealed class AccentClock
    {
        private const double PeriodSeconds=2;
        private readonly Dictionary<Compositor,CompositionColorBrush> compositionBrushes=new();
        private Color yellow=Color.FromArgb(255,244,230,0),green=Color.FromArgb(255,244,230,0);
        private bool running;
        private long started;

        internal AccentClock()
        {
            var dispatcher=DispatcherQueue.GetForCurrentThread()
                ??throw new InvalidOperationException("Interaction accents must be initialized on the XAML thread.");
            dispatcher.ShutdownStarting+=(_,_)=>SetMotion(false);
        }

        internal void Configure(Color first,Color second)
        {
            if(yellow.Equals(first)&&green.Equals(second))return;
            yellow=first;green=second;
            // Fallbacks (including native text selection) change only with the
            // skin, never at the animation cadence.
            if(Microsoft.UI.Xaml.Application.Current.Resources["InteractionAccentBrush"] is CompositionAccentBrush fallback)
                fallback.FallbackColor=first;
            UpdateAnimations();
        }

        internal void SetMotion(bool enabled)
        {
            if(running==enabled)return;
            running=enabled;
            UpdateAnimations();
        }

        internal CompositionColorBrush GetBrush(Compositor compositor)
        {
            if(!compositionBrushes.TryGetValue(compositor,out var brush)) {
                // A newly hovered control joins the current phase immediately.
                brush=compositor.CreateColorBrush(Sample(running?Stopwatch.GetElapsedTime(started).TotalSeconds%PeriodSeconds/PeriodSeconds:0));
                compositionBrushes.Add(compositor,brush);
                if(running)AnimateBrush(compositor,brush);
            }
            return brush;
        }

        private Color Sample(double phase)
        {
            double blend=(1-Math.Cos(phase*Math.PI*2))/2;
            byte Channel(byte first,byte second)=>(byte)Math.Round(first+(second-first)*blend);
            return Color.FromArgb(Channel(yellow.A,green.A),Channel(yellow.R,green.R),Channel(yellow.G,green.G),Channel(yellow.B,green.B));
        }
        private void UpdateAnimations()
        {
            foreach(var brush in compositionBrushes.Values) {brush.StopAnimation("Color");brush.Color=yellow;}
            if(!running)return;
            started=Stopwatch.GetTimestamp();
            foreach(var pair in compositionBrushes)AnimateBrush(pair.Key,pair.Value);
        }
        private void AnimateBrush(Compositor compositor,CompositionColorBrush brush)
        {
            using var animation=compositor.CreateColorKeyFrameAnimation();
            using var linear=compositor.CreateLinearEasingFunction();
            animation.Duration=TimeSpan.FromSeconds(PeriodSeconds);animation.IterationBehavior=AnimationIterationBehavior.Forever;animation.InterpolationColorSpace=CompositionColorSpace.Rgb;
            for(int i=0;i<=120;i++)animation.InsertKeyFrame(i/120f,Sample(i/120d),linear);
            brush.StartAnimation("Color",animation);
            if(brush.TryGetAnimationController("Color") is { } controller) {
                controller.Pause();controller.Progress=(float)(Stopwatch.GetElapsedTime(started).TotalSeconds%PeriodSeconds/PeriodSeconds);controller.Resume();
            }
        }
    }
}

// The resource can be shared by many controls. Disconnecting one wrapper must
// not dispose the native brush borrowed by other wrappers and focus visuals.
public sealed class CompositionAccentBrush : XamlCompositionBrushBase
{
    protected override void OnConnected()
    {
        CompositionBrush??=InteractionAccent.GetBrush(CompositionTarget.GetCompositorForCurrentThread());
    }

    protected override void OnDisconnected()
    {
        CompositionBrush=null;
    }
}
