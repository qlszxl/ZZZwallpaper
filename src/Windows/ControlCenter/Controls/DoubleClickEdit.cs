using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace LumaWall.ControlCenter;

// Shared text-entry gate. Read-only output remains read-only. Settings writers
// subscribe to Accepted so drafts and Esc never create saved intermediate values.
public sealed class DoubleClickEdit : DependencyObject
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(DoubleClickEdit),new PropertyMetadata(false,Changed));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(object),typeof(DoubleClickEdit),new PropertyMetadata(null));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    private sealed class State
    {
        internal bool Active,Editing,Finishing;
        internal string Original="";
        internal Func<bool>? Accepted;
    }
    private static State GetState(TextBox box)
    {
        if(box.GetValue(StateProperty) is State state)return state;
        state=new();box.SetValue(StateProperty,state);return state;
    }
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not TextBox box)return;
        if((bool)args.NewValue){box.Loaded+=Loaded;box.Unloaded+=Unloaded;box.AddHandler(UIElement.DoubleTappedEvent,new DoubleTappedEventHandler(DoubleTapped),true);box.PreviewKeyDown+=KeyDown;box.LostFocus+=LostFocus;box.IsEnabledChanged+=EnabledChanged;if(box.IsLoaded)Activate(box);}
        else {box.Loaded-=Loaded;box.Unloaded-=Unloaded;box.RemoveHandler(UIElement.DoubleTappedEvent,new DoubleTappedEventHandler(DoubleTapped));box.PreviewKeyDown-=KeyDown;box.LostFocus-=LostFocus;box.IsEnabledChanged-=EnabledChanged;if(GetState(box).Active){Finish(box,false);box.IsReadOnly=false;GetState(box).Active=false;}}
    }
    private static void Activate(TextBox box){var state=GetState(box);if(state.Active||box.IsReadOnly)return;state.Active=true;box.IsReadOnly=true;}
    private static void Loaded(object sender,RoutedEventArgs e)=>Activate((TextBox)sender);
    private static void EnabledChanged(object sender,DependencyPropertyChangedEventArgs e){if(!(bool)e.NewValue)Finish((TextBox)sender,false);}
    private static void Unloaded(object sender,RoutedEventArgs e)=>Commit((TextBox)sender);
    private static void DoubleTapped(object sender,DoubleTappedRoutedEventArgs e){if(Begin((TextBox)sender))e.Handled=true;}
    private static void LostFocus(object sender,RoutedEventArgs e){var box=(TextBox)sender;if(!Commit(box))Finish(box,false);}
    private static void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        var box=(TextBox)sender;var state=GetState(box);
        if(!state.Editing&&e.Key is VirtualKey.Enter or VirtualKey.F2){e.Handled=Begin(box);return;}
        if(!state.Editing)return;
        if(e.Key==VirtualKey.Escape){Finish(box,false);e.Handled=true;}
        else if(e.Key==VirtualKey.Enter&&(!box.AcceptsReturn||(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0)){Commit(box);e.Handled=true;}
    }
    internal static bool Begin(TextBox box)
    {
        var state=GetState(box);if(!state.Active||!box.IsEnabled)return false;
        if(!state.Editing){state.Original=box.Text;state.Editing=true;box.IsReadOnly=false;}
        box.Focus(FocusState.Programmatic);box.SelectAll();return true;
    }
    internal static void OnAccepted(TextBox box,Action action)=>OnAccepted(box,()=>{action();return true;});
    internal static void OnAccepted(TextBox box,Func<bool> action){SetEnabled(box,true);GetState(box).Accepted=action;}
    internal static bool Commit(TextBox box)=>Finish(box,true);
    private static bool Finish(TextBox box,bool commit)
    {
        var state=GetState(box);if(!state.Editing||state.Finishing)return true;
        state.Finishing=true;
        try {
            if(commit&&box.IsEnabled){bool accepted=true;ScrubNumberInput.RunTransaction(box,()=>accepted=state.Accepted?.Invoke()!=false);if(!accepted)return false;}
            else box.Text=state.Original;
            state.Editing=false;box.IsReadOnly=true;box.Select(box.Text.Length,0);return true;
        }finally{state.Finishing=false;}
    }
}
