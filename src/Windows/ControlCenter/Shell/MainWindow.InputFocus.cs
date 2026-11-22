using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void InitializeInputFocus()
    {
        ScrollBarAutoHide.SetMotion(ShellViewport,()=>Appearance.Motion&&homeUiSettings?.AnimationsEnabled!=false);
        // A neutral target outside the page host also works while a drawer has
        // disabled that host. It joins the tab order only while already focused.
        var neutral=new ContentControl {Width=1,Height=1,HorizontalAlignment=HorizontalAlignment.Left,
            VerticalAlignment=VerticalAlignment.Top,IsTabStop=false,UseSystemFocusVisuals=false};
        Root.Children.Add(neutral);
        uint? backgroundPointer=null;
        void FocusNeutral()
        {
            neutral.IsTabStop=true;
            if(!neutral.Focus(FocusState.Pointer))neutral.IsTabStop=false;
        }
        neutral.LostFocus+=(_,_)=>neutral.IsTabStop=false;
        Root.GotFocus+=(_,e)=> {
            for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=Root;node=VisualTreeHelper.GetParent(node))
                if(node is NumberBox number) {NumericCommit.Observe(number);break;}
        };
        Root.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=> {
            if(!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)return;
            backgroundPointer=null;
            if(InteractionFocus.IsInteractive(e.OriginalSource as DependencyObject))return;
            backgroundPointer=e.Pointer.PointerId;
            if(FocusManager.GetFocusedElement(Root.XamlRoot) is not DependencyObject focused||focused==neutral)return;
            ScrubNumberInput? position=null;EffectValueInput? effect=null;NumberBox? number=null;
            for(var node=focused;node!=null&&node!=Root;node=VisualTreeHelper.GetParent(node)) {
                if(node is ScrubNumberInput p)position=p;
                if(node is EffectValueInput v)effect=v;
                if(node is NumberBox n)number=n;
            }
            if(position!=null)position.CommitEdit();
            else if(effect!=null)effect.CommitEdit();
            else if(number!=null)NumericCommit.Commit(number);
            if(focused is TextBox text)text.Select(text.Text.Length,0);
            FocusNeutral();
            widgets?.FlushPendingSave();
        }),true);
        Root.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=> {
            if(backgroundPointer!=e.Pointer.PointerId)return;
            backgroundPointer=null;
            // ScrollViewer focuses its first available child on release. Run
            // after that class handler, including an already-handled release.
            if(!InteractionFocus.IsInteractive(e.OriginalSource as DependencyObject))FocusNeutral();
        }),true);
        Root.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler((_,_)=>backgroundPointer=null),true);
        Root.AddHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler((_,e)=> {
            if(e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)backgroundPointer=null;
        }),true);
        var interaction=InteractionFocus.Initialize(Root,()=>Appearance.Motion&&homeUiSettings?.AnimationsEnabled!=false,ShellViewport);
        Activated+=(_,e)=> {
            bool active=e.WindowActivationState!=WindowActivationState.Deactivated;
            if(!active)backgroundPointer=null;
            interaction.SetActive(active);
            EffectsAddButton.WindowActive = WidgetsAddButton.WindowActive = active;
        };
        Closed+=(_,_)=>interaction.Dispose();
    }
}
