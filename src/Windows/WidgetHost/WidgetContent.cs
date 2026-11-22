using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

// A content instance owns only its WPF visual tree and presentation state.
// WidgetWindow owns placement, native hooks, drag lifecycle and feedback.
internal abstract class WidgetContent(WidgetContentContext context) : IDisposable
{
    protected WidgetContentContext Context {get;}=context;
    protected WidgetInstance instance=>Context.Instance();
    protected Shortcut? card=>Context.Card();
    protected bool editingLocked=>Context.EditingLocked();
    protected bool interactionLocked=>instance.InteractionLocked;
    protected C.ContextMenu menu=>Context.Menu;
    protected C.Border? focusEdge;
    protected C.Button? cardButton;
    internal W.FrameworkElement Element {get;private protected set;}=null!;
    internal C.Border? FocusEdge=>focusEdge;
    internal C.Button? CardButton=>cardButton;
    internal virtual C.TextBox? SearchInput=>null;
    internal virtual W.FrameworkElement? DrawingElement=>null;
    internal virtual IReadOnlyList<C.Button> ActionButtons=>Array.Empty<C.Button>();
    internal Action? RefreshPointerRegion {get;set;}
    internal virtual bool PerformanceUpdateDue=>false;
    internal virtual void ResetPerformanceSchedule() { }
    internal virtual void Resize(double width,double height) { }
    internal virtual void UpdateTime() { }
    internal virtual void UpdateInteraction()
    {
        if(cardButton!=null) {
            cardButton.Focusable=!interactionLocked;
            if(interactionLocked&&cardButton.IsKeyboardFocusWithin)I.Keyboard.ClearFocus();
        }
        if(focusEdge!=null)focusEdge.Background=!interactionLocked&&(SearchInput?.IsKeyboardFocusWithin==true||cardButton?.IsKeyboardFocusWithin==true)?WidgetVisuals.Accent:WidgetVisuals.Edge;
    }
    internal virtual void SetPlaybackPaused(bool value) { }
    public virtual void Dispose() { }
    internal virtual void UpdateSpectrum(SpectrumSample sample) { }
    internal virtual IReadOnlyList<string>? UpdatePerformance(SystemPerformanceSample sample,long sampledAt)=>null;
    protected void DragHandle(C.Button button,Action click,bool opensSettings=false)=>Context.AttachDragHandle(button,click,opensSettings);
    protected void Send(WidgetMessage message)
    {
        // Recheck at invocation: a menu or queued callback can predate a lock.
        if(editingLocked&&message.Type is "edit" or "edit-widget" or "edit-group" or "group")return;
        if(interactionLocked&&message.Type is "timer-action" or "open" or "open-card")return;
        Context.Send(message);
    }
    protected void ShowMessage(string message)=>Context.ShowMessage(message);
    protected void SubmitSearch() {if(!interactionLocked)Context.SubmitSearch();}
    protected void ActivateCard() {if(!interactionLocked)Context.ActivateCard();}
    protected static W.FrameworkElement CustomIcon(string? encoded,string fallback)
    {
        if(encoded!=null)try {
            using var stream=new MemoryStream(Convert.FromBase64String(encoded));var bitmap=new M.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=M.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
            return new C.Image {Source=bitmap,Stretch=M.Stretch.Uniform,Width=20,Height=20,HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center};
        } catch(ArgumentException) { }catch(IOException) { }catch(NotSupportedException) { }
        return WidgetVisuals.Icon(fallback,WidgetVisuals.Ink);
    }
}
