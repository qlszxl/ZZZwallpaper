using Microsoft.UI.Xaml;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private bool resolutionScaleReady;
    private double interfaceScale=1;
    private void InitializeResolutionScale()
    {
        Root.Loaded+=(_,_)=> {
            if(resolutionScaleReady||Root.XamlRoot==null)return;
            var display=Screen.FromHandle(WinRT.Interop.WindowNative.GetWindowHandle(this));
            interfaceScale=WindowPlacementGeometry.ResolutionScale(display.Bounds)/Math.Max(.25,Root.XamlRoot.RasterizationScale);
            resolutionScaleReady=true;
            LayoutScaledViewport();
            shellAppliedInputRects=null;QueueShellChromeLayout();
        };
        ShellViewport.SizeChanged+=(_,_)=>LayoutScaledViewport();
    }

    private void LayoutScaledViewport()
    {
        if(!resolutionScaleReady||ShellViewport.ActualWidth<=0||ShellViewport.ActualHeight<=0)return;
        double width=ShellViewport.ActualWidth/interfaceScale,height=ShellViewport.ActualHeight/interfaceScale;
        if(double.IsNaN(Root.Width)||Math.Abs(Root.Width-width)>.1)Root.Width=width;
        if(double.IsNaN(Root.Height)||Math.Abs(Root.Height-height)>.1)Root.Height=height;
    }
}
