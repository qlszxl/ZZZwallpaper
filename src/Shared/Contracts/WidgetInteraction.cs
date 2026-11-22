namespace LumaWall.Contracts;

// Position locks guard layout/settings. Interaction locks guard functional
// actions independently; only explicit click-through makes a window transparent.
public static class WidgetInteraction
{
    public static bool CanEdit(WidgetConfiguration state,WidgetInstance widget)=>!state.IsEditingLocked(widget);
    public static bool CanInteract(WidgetConfiguration state,WidgetInstance widget)=>!state.IsInteractionLocked(widget);
    public static bool CanChangeLock(WidgetConfiguration state,WidgetInstance widget)=>
        !state.LayoutGroups.Any(group=>group.Id==widget.LayoutGroupId&&group.Locked);
    public static bool MousePassThrough(WidgetConfiguration state,WidgetInstance widget)=>
        widget.Kind is (WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer)&&widget.Visual.ClickThrough;

    public static WidgetConfiguration? ApplyDesktopCommand(WidgetConfiguration state,WidgetMessage message)
    {
        var widget=state.Widgets.FirstOrDefault(item=>item.Id==message.Id);
        if(widget==null)return null;
        switch(message.Type) {
            case "hide":
            case "visibility" when message.Enabled.HasValue:
                bool enabled=message.Type=="hide"?false:message.Enabled!.Value;
                if(widget.Enabled==enabled)return null;
                return state with {Widgets=state.Widgets.Select(item=>item.Id==widget.Id?item with {Enabled=enabled}:item).ToArray()};
            case "lock" when message.Locked.HasValue:
                if(!CanChangeLock(state,widget)||widget.Locked==message.Locked.Value)return null;
                return state with {Widgets=state.Widgets.Select(item=>item.Id==widget.Id?item with {Locked=message.Locked.Value}:item).ToArray()};
            case "interaction-lock" when message.InteractionLocked.HasValue:
                if(widget.InteractionLocked==message.InteractionLocked.Value)return null;
                return state with {Widgets=state.Widgets.Select(item=>item.Id==widget.Id?item with {InteractionLocked=message.InteractionLocked.Value}:item).ToArray()};
            case "group-lock" when message.Locked.HasValue:
                var group=state.LayoutGroups.FirstOrDefault(item=>item.Id==widget.LayoutGroupId);
                if(group==null||group.Locked==message.Locked.Value)return null;
                // An explicit group action changes only the group lock. Members'
                // own locks survive both group locking and group unlocking.
                return state with {LayoutGroups=state.LayoutGroups.Select(item=>item.Id==group.Id?item with {Locked=message.Locked.Value}:item).ToArray()};
            default:return null;
        }
    }
}
