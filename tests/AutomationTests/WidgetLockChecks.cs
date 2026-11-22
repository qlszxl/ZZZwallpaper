using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;

internal static class WidgetLockChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        static JsonElement J(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        foreach(string payload in new[]{"\"locked\":true","\"interactionLocked\":true","\"locked\":false,\"interactionLocked\":true"}) {
            ToolCatalog.Validate("widget_set_lock",J("{\"widgetId\":\"test\",\"expectedRevision\":\"revision\","+payload+"}"));
            check(true,"Lock tool accepts independent and combined flags: "+payload);
        }
        reject(()=>ToolCatalog.Validate("widget_set_lock",J("""{"widgetId":"test","expectedRevision":"revision"}""")),"Lock tool requires at least one explicit lock flag");
        reject(()=>ToolCatalog.Validate("widget_set_lock",J("""{"widgetId":"test","interactionLocked":true}""")),"Interaction lock changes require current configuration revision");
        reject(()=>ToolCatalog.Validate("widget_set_lock",J("""{"widgetId":"test","expectedRevision":"revision","interactionLocked":"true"}""")),"Interaction lock requires a boolean");
        var widget=new WidgetInstance {Id="test",Kind=WidgetKind.FocusTimer,Locked=true};
        var onlyInteraction=WidgetPatch.ApplyLocks(widget,J("""{"interactionLocked":true}"""));
        check(onlyInteraction.Locked&&onlyInteraction.InteractionLocked,"Interaction patch preserves position lock");
        var onlyPosition=WidgetPatch.ApplyLocks(onlyInteraction,J("""{"locked":false}"""));
        check(!onlyPosition.Locked&&onlyPosition.InteractionLocked,"Position unlock preserves interaction lock");
        reject(()=>WidgetPatch.ApplyLocks(widget,J("{}")),"Empty lock patch is rejected by implementation");
        var grouped=widget with {LayoutGroupId="team"};
        var state=new WidgetConfiguration {Widgets=[grouped],LayoutGroups=[new() {Id="team",Locked=true}]};
        var action=J("""{"widgetId":"test","expectedRevision":"revision","timerAction":"start"}""");
        check(WidgetPatch.CanUpdate(state,grouped,action),"Timer-only action is allowed through position and group locks");
        check(WidgetPatch.Apply(grouped,action).Focus.Running,"Timer-only action starts the position-locked grouped timer");
        check(!WidgetPatch.CanUpdate(state,grouped,J("""{"widgetId":"test","timerAction":"start","x":0.2}""")),"Adding layout to a timer request does not bypass position protection");
        var functionalLock=grouped with {InteractionLocked=true};
        check(!WidgetPatch.CanUpdate(state,functionalLock,action),"Interaction lock blocks timer-only automation");
        reject(()=>WidgetPatch.Apply(functionalLock,action),"Functional timer mutation rechecks the interaction lock");
        var configOnly=functionalLock with {Locked=false,LayoutGroupId=null};
        check(WidgetPatch.CanUpdate(state,configOnly,J("""{"widgetId":"test","x":0.2}""")),"Interaction lock alone allows configuration and movement edits");
        var tool=ToolCatalog.All.Single(t=>t.Name=="widget_get");
        check(tool.OutputSchema.GetProperty("properties").TryGetProperty("interactionLocked",out var property)&&property.GetProperty("type").EnumerateArray().Any(v=>v.GetString()=="boolean"),"Widget state advertises the independent interaction flag");
    }
}
