using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

internal static class WidgetLockChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var old=WidgetConfiguration.FromJson("""{"Version":18,"Widgets":[{"Id":"old","Kind":0,"Locked":true}]}""");
        check(old.Widgets[0].Locked&&!old.Widgets[0].InteractionLocked,"Legacy position locks leave functional interaction enabled");
        foreach(var kind in Enum.GetValues<WidgetKind>())foreach(bool position in new[]{false,true})foreach(bool interaction in new[]{false,true})foreach(bool group in new[]{false,true}) {
            var widget=new WidgetInstance {Id="locks",Kind=kind,PackageWidgetId=kind==WidgetKind.Extension?"test/clock":null,Locked=position,InteractionLocked=interaction,LayoutGroupId="team"};
            var state=new WidgetConfiguration {Widgets=[widget],LayoutGroups=[new() {Id="team",Locked=group}]};
            check(state.IsEditingLocked(widget)==(position||group)&&state.IsDragLocked(widget)==(position||group),"Position/group locking preserves layout semantics: "+kind+position+interaction+group);
            check(state.IsInteractionLocked(widget)==interaction&&WidgetInteraction.CanInteract(state,widget)==!interaction,"Functional lock is independent of position/group: "+kind+position+interaction+group);
            check(!WidgetInteraction.MousePassThrough(state,widget),"Locks do not imply mouse transparency: "+kind+position+interaction+group);
            var saved=WidgetConfiguration.FromJson(state.ToJson());
            check(saved.Widgets[0].Locked==position&&saved.Widgets[0].InteractionLocked==interaction,"Both flags round-trip independently: "+kind+position+interaction+group);
        }
        var current=new WidgetInstance {Id="test",Kind=WidgetKind.FocusTimer,Locked=true,InteractionLocked=false,LayoutGroupId="team"};
        var source=new WidgetConfiguration {Widgets=[current],LayoutGroups=[new() {Id="team",Locked=true}]};
        var changed=WidgetInteraction.ApplyDesktopCommand(source,new() {Type="interaction-lock",Id=current.Id,InteractionLocked=true})!;
        check(changed.Widgets[0].InteractionLocked&&changed.Widgets[0].Locked&&changed.LayoutGroups[0].Locked,"Interaction-lock IPC changes no position/group state");
        var cleared=WidgetInteraction.ApplyDesktopCommand(changed,new() {Type="interaction-lock",Id=current.Id,InteractionLocked=false})!;
        check(!cleared.Widgets[0].InteractionLocked&&cleared.Widgets[0].Locked,"Interaction can be unlocked while a group position lock remains");
        check(WidgetInteraction.ApplyDesktopCommand(changed,new() {Type="interaction-lock",Id=current.Id,Locked=false})==null,"Interaction-lock IPC requires its own explicitly supplied flag");
        check(WidgetInteraction.ApplyDesktopCommand(changed,new() {Type="interaction-lock",Id="missing",InteractionLocked=false})==null,"Interaction-lock IPC ignores unknown instances");
        check(WidgetDelta.Between(source,changed).Apply(source).ToJson()==changed.ToJson(),"Independent lock changes reach host through incremental deltas");
        var both=changed.Widgets[0];
        var selected=ShareProjection.Widgets(changed,[both],new() {Visibility=true});
        check(selected.Widgets[0].Locked&&selected.Widgets[0].InteractionLocked,"Sharing/preset projection retains both selected lock flags");
        var excluded=ShareProjection.Widgets(changed,[both],new() {Visibility=false});
        check(!excluded.Widgets[0].Locked&&!excluded.Widgets[0].InteractionLocked,"Excluding visibility resets both flags to independent defaults");
        var document=new ShareDocument(1,[new ShareSection("widgets",1,JsonSerializer.SerializeToNode(selected)!.AsObject())]);
        var restored=ShareCode.Decode(ShareCode.Encode(document)).OnScreen("target").Widgets.Single();
        check(restored.Locked&&restored.InteractionLocked,"Actual share encode/decode retains both locks");
        var explicitClickThrough=both with {Visual=both.Visual with {ClickThrough=true}};
        check(WidgetInteraction.MousePassThrough(changed,explicitClickThrough),"Explicit click-through remains separate from locks");
        var now=DateTimeOffset.UtcNow;var running=both with {Focus=both.Focus.Action("start",now)};
        check(running.Focus.Remaining(now.AddSeconds(5))<running.Focus.Remaining(now),"Interaction lock does not stop a running timer's wall clock");
    }
}
