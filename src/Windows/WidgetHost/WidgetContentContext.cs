using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class WidgetInputState
{
    internal bool Composing,ImeCommit;
}

internal sealed record WidgetContentContext(
    Func<WidgetInstance> Instance,
    Func<Shortcut?> Card,
    Func<bool> EditingLocked,
    WidgetInputState Input,
    C.ContextMenu Menu,
    Action<C.Button,Action,bool> AttachDragHandle,
    Action SubmitSearch,
    Action ActivateCard,
    Action<string> ShowMessage,
    Action<WidgetMessage> Send);

internal readonly record struct SearchFieldState(string Text,int Start,int Length,bool Focused)
{
    internal static SearchFieldState Capture(C.TextBox? input)=>new(input?.Text??"",input?.SelectionStart??0,input?.SelectionLength??0,input?.IsKeyboardFocusWithin==true);
}
