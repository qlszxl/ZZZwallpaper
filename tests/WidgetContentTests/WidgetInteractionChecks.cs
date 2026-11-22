using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.WidgetHost;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

internal static class WidgetInteractionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var kind in Enum.GetValues<WidgetKind>())foreach(bool own in new[]{false,true})foreach(bool group in new[]{false,true})foreach(bool interaction in new[]{false,true})foreach(bool clickThrough in new[]{false,true}) {
            var item=new WidgetInstance {Id="kind-"+kind,Kind=kind,Locked=own,InteractionLocked=interaction,LayoutGroupId="team",Visual=new() {ClickThrough=clickThrough}};
            var state=new WidgetConfiguration {Widgets=[item],LayoutGroups=[new() {Id="team",Locked=group}]};
            bool supported=kind is WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer;
            check(state.IsEditingLocked(item)==(own||group)&&state.IsDragLocked(item)==(own||group),"Position and group locks affect editing and dragging only: "+kind);
            check(state.IsInteractionLocked(item)==interaction&&WidgetInteraction.CanInteract(state,item)==!interaction,"Functional access depends only on the individual interaction lock: "+kind);
            check(WidgetInteraction.MousePassThrough(state,item)==(supported&&clickThrough),"Neither lock changes explicit mouse pass-through: "+kind);
            var toggled=WidgetInteraction.ApplyDesktopCommand(state,new() {Type="interaction-lock",Id=item.Id,InteractionLocked=!interaction})!;
            check(toggled.Widgets.Single()==item with {InteractionLocked=!interaction}&&toggled.LayoutGroups.SequenceEqual(state.LayoutGroups),"Interaction lock commands preserve position and group lock state: "+kind);
            check(WidgetInteraction.ApplyDesktopCommand(state,new() {Type="interaction-lock",Id=item.Id,InteractionLocked=interaction})==null,"Repeated interaction lock is a no-op");
        }
        var member=new WidgetInstance {Id="member",Kind=WidgetKind.Search,LayoutGroupId="team",Locked=true,InteractionLocked=true};
        var sibling=new WidgetInstance {Id="sibling",Kind=WidgetKind.Shortcuts,LayoutGroupId="team"};
        var grouped=new WidgetConfiguration {Widgets=[member,sibling],LayoutGroups=[new() {Id="team"}]};
        check(grouped.IsDragLocked(sibling)&&WidgetInteraction.CanEdit(grouped,sibling)&&WidgetInteraction.CanInteract(grouped,sibling),"A sibling's position or interaction lock cannot disable normal actions or right-click settings");
        foreach(string type in new[]{"visibility","lock","group-lock","interaction-lock"}) {
            check(WidgetInteraction.ApplyDesktopCommand(grouped,new() {Type=type,Id="missing",Locked=true,InteractionLocked=true,Enabled=false})==null,"Unknown widget cannot mutate lock or visibility state");
            check(WidgetInteraction.ApplyDesktopCommand(grouped,new() {Type=type,Id=member.Id})==null,"Absent boolean cannot implicitly toggle lock or visibility state");
        }
        var decoded=JsonSerializer.Deserialize<WidgetMessage>(JsonSerializer.Serialize(new WidgetMessage {Type="interaction-lock",Id=member.Id,InteractionLocked=false}))!;
        check(decoded.InteractionLocked==false&&decoded.Locked==null&&WidgetInteraction.ApplyDesktopCommand(grouped,decoded)!.Widgets[0]==member with {InteractionLocked=false},"IPC preserves explicit interaction unlock without touching position lock");
        CheckMenuTransitions(check);CheckContentGuards(check);
    }
    private static void CheckMenuTransitions(Action<bool,string> check)
    {
        var menu=new C.ContextMenu();bool allowed=true;string caption="Lock widget";int invoked=0;
        var item=WidgetVisuals.Action(menu,()=>caption,"lock",()=>invoked++,()=>allowed);
        check(item.IsEnabled&&W.Automation.AutomationProperties.GetName(item)==caption,"Dynamic menu action has its current accessible label");
        allowed=false;caption="Locked";item.RaiseEvent(new W.RoutedEventArgs(C.MenuItem.ClickEvent));
        check(invoked==0,"A queued menu click rechecks permission before executing");
        WidgetVisuals.RefreshMenu(menu);check(!item.IsEnabled&&W.Automation.AutomationProperties.GetName(item)==caption,"Menu refresh updates label and availability in place");
        allowed=true;WidgetVisuals.RefreshMenu(menu);item.RaiseEvent(new W.RoutedEventArgs(C.MenuItem.ClickEvent));
        check(invoked==1,"An existing menu action resumes after unlock");
    }
    private static void CheckContentGuards(Action<bool,string> check)
    {
        foreach(var kind in ExpansionCatalog.Widgets.Keys) {
            var widget=new WidgetInstance {Id="content-"+kind,Kind=kind,LayoutGroupId="team"};
            var state=new WidgetConfiguration {Widgets=[widget],LayoutGroups=[new() {Id="team"}]};
            WidgetInstance Current()=>state.Widgets[0];
            var messages=new List<WidgetMessage>();var handles=new List<(Action Click,bool Settings)>();var menu=new C.ContextMenu();var inputState=new WidgetInputState();int operations=0;
            var context=new WidgetContentContext(Current,()=>null,()=>state.IsEditingLocked(Current()),inputState,menu,(_,click,settings)=>handles.Add((click,settings)),()=>operations++,()=>operations++,_=>{},messages.Add);
            using var content=WidgetContentFactory.Create(context,new("query",0,0,false));
            C.MenuItem[] Settings()=>menu.Items.OfType<C.MenuItem>().Where(item=>W.Automation.AutomationProperties.GetName(item).Contains("设置")).ToArray();
            check(Settings().Length>0,"Each component exposes guarded settings: "+kind);
            foreach(bool own in new[]{false,true})foreach(bool group in new[]{false,true})foreach(bool interaction in new[]{false,true}) {
                state=state with {Widgets=[Current() with {Locked=own,InteractionLocked=interaction}],LayoutGroups=[state.LayoutGroups[0] with {Locked=group}]};
                bool position=own||group;messages.Clear();operations=0;
                // Invoke callbacks BEFORE refresh: rendering a disabled button is
                // not sufficient to protect an already queued action.
                foreach(var setting in Settings())setting.RaiseEvent(new W.RoutedEventArgs(C.MenuItem.ClickEvent));
                foreach(var handle in handles)handle.Click();
                check(messages.All(m=>m.Type=="edit-widget")&&(position?messages.Count==0:messages.Count>0),"Only position lock guards settings and settings handles: "+kind);
                check(operations==((kind is WidgetKind.Search or WidgetKind.Shortcuts)&&!interaction?1:0),"Only interaction lock guards search and shortcut callbacks: "+kind);
                WidgetVisuals.RefreshMenu(menu);content.UpdateInteraction();content.UpdateTime();
                check(Settings().All(m=>m.IsEnabled==!position),"Right-click settings stay available under interaction-only lock: "+kind);
                if(kind==WidgetKind.FocusTimer) {
                    messages.Clear();foreach(var button in content.ActionButtons)button.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));
                    check(content.ActionButtons.All(button=>button.IsEnabled==!interaction)&&messages.Count==(interaction?0:3),"Start, pause/reset and skip obey interaction lock independently of position/group lock");
                }
                if(kind==WidgetKind.Search) {
                    var field=content.SearchInput!;check(field.IsEnabled==!interaction&&field.IsReadOnly==interaction&&field.Focusable==!interaction,"Search entry remains usable when only its position is locked");
                    operations=0;var key=new I.KeyEventArgs(I.Keyboard.PrimaryDevice,new TestPresentationSource(),0,I.Key.Enter) {RoutedEvent=I.Keyboard.PreviewKeyDownEvent};field.RaiseEvent(key);
                    check(operations==(interaction?0:1),"Queued Enter key obeys the independent interaction lock");
                    field.Text="preserved";var clear=menu.Items.OfType<C.MenuItem>().Single(m=>W.Automation.AutomationProperties.GetName(m)=="清空");clear.RaiseEvent(new W.RoutedEventArgs(C.MenuItem.ClickEvent));
                    check(field.Text==(interaction?"preserved":""),"Queued clear cannot modify interaction-locked search content");
                    check(menu.Items.OfType<C.MenuItem>().Where(m=>!Settings().Contains(m)).All(m=>m.IsEnabled==!interaction),"Search, copy, paste and clear availability share the functional lock");
                }
                if(kind==WidgetKind.Shortcuts)check(content.CardButton!.Focusable==!interaction&&content.CardButton.IsEnabled,"Interaction lock removes shortcut keyboard activation while retaining its drag handle");
            }
            if(kind==WidgetKind.FocusTimer) {
                var deadline=DateTimeOffset.UtcNow.AddMinutes(1);state=state with {Widgets=[Current() with {InteractionLocked=true,Focus=Current().Focus with {Running=true,Deadline=deadline}}]};
                content.UpdateTime();check(((TimeWidgetArtwork)content.DrawingElement!).Running&&Current().Focus.Deadline==deadline,"Interaction locking leaves an already-running focus timer running");
            }
            if(kind==WidgetKind.Search) {
                inputState.Composing=true;inputState.ImeCommit=true;content.UpdateInteraction();
                check(!inputState.Composing&&!inputState.ImeCommit,"Arrival of an interaction lock clears stale IME flags");
            }
            if(kind==WidgetKind.Performance) {
                content.ResetPerformanceSchedule();check(content.UpdatePerformance(new SystemPerformanceSample(25,50,8,16,null,null,null,null,null,null),Environment.TickCount64)!=null,"Locked performance widgets keep receiving visual updates");
            }
            state=state with {Widgets=[Current() with {Locked=false,InteractionLocked=false,Id="latest-"+kind}],LayoutGroups=[state.LayoutGroups[0] with {Locked=false}]};
            messages.Clear();operations=0;content.UpdateInteraction();content.UpdateTime();WidgetVisuals.RefreshMenu(menu);Settings()[0].RaiseEvent(new W.RoutedEventArgs(C.MenuItem.ClickEvent));
            check(messages.Count==1&&messages[0].Id==Current().Id,"Unlock reuses live configuration without replacing the content: "+kind);
        }
    }
    private sealed class TestPresentationSource : W.PresentationSource
    {
        public override M.Visual RootVisual {get;set;}=null!;
        public override bool IsDisposed=>false;
        protected override M.CompositionTarget GetCompositionTargetCore()=>null!;
    }
}