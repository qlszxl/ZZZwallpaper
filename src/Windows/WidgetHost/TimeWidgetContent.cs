using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class TimeWidgetContent : WidgetContent
{
    internal override W.FrameworkElement? DrawingElement=>timeArtwork;
    internal override IReadOnlyList<C.Button> ActionButtons=>timeButtons;
    private TimeWidgetArtwork? timeArtwork;
    private C.Button[] timeButtons=[];
    internal TimeWidgetContent(WidgetContentContext context):base(context) {
        var artwork=timeArtwork=new TimeWidgetArtwork();artwork.Update(instance,DateTimeOffset.Now,WidgetVisuals.Accent);
        var stage=new C.Canvas {Width=500,Height=artwork.DesignHeight};
        var handle=new C.Button {Content=artwork,Style=WidgetVisuals.PlainButton(),Width=500,Height=artwork.DesignHeight,HorizontalContentAlignment=W.HorizontalAlignment.Stretch,VerticalContentAlignment=W.VerticalAlignment.Stretch};
        DragHandle(handle,()=>Send(new() {Type="edit-widget",Id=instance.Id}),opensSettings:true);stage.Children.Add(handle);
        if(instance.Kind==WidgetKind.FocusTimer) {
            int hover=-1,down=-1,focus=-1;timeButtons=new C.Button[3];
            for(int i=0;i<3;i++) {
                int index=i;var r=artwork.ActionBounds(i);
                var button=new C.Button {Content=new C.Border {Background=M.Brushes.Transparent},Style=WidgetVisuals.PlainButton(),Width=r.Width,Height=r.Height,Visibility=artwork.ActionVisible(i)?W.Visibility.Visible:W.Visibility.Collapsed};
                button.ContextMenu=menu;C.ContextMenuService.SetShowOnDisabled(button,true);
                C.Canvas.SetLeft(button,r.X);C.Canvas.SetTop(button,r.Y);stage.Children.Add(button);timeButtons[i]=button;
                button.MouseEnter+=(_,_)=> {hover=index;artwork.Interaction(hover,down,focus);};button.MouseLeave+=(_,_)=> {hover=-1;artwork.Interaction(hover,down,focus);};
                button.PreviewMouseLeftButtonDown+=(_,_)=> {down=index;artwork.Interaction(hover,down,focus);};button.PreviewMouseLeftButtonUp+=(_,_)=> {down=-1;artwork.Interaction(hover,down,focus);};button.LostMouseCapture+=(_,_)=> {down=-1;artwork.Interaction(hover,down,focus);};
                button.GotKeyboardFocus+=(_,_)=> {focus=index;artwork.Interaction(hover,down,focus);};button.LostKeyboardFocus+=(_,_)=> {focus=-1;artwork.Interaction(hover,down,focus);};
                button.Click+=(_,_)=> {if(interactionLocked)return;Send(new() {Type="timer-action",Id=instance.Id,TimerAction=index==0?(instance.Focus.Advance(DateTimeOffset.UtcNow).Running?"pause":"start"):index==1?"reset":"skip"});};
                W.Automation.AutomationProperties.SetName(button,i==0?UiLanguage.Text("开始"):i==1?UiLanguage.Text("重置"):UiLanguage.Text("跳过"));
            }
        }
        Element=new C.Viewbox {Child=stage,Stretch=M.Stretch.Uniform,Margin=new W.Thickness(Math.Max(0,instance.Visual.Padding-12))};
        WidgetVisuals.Action(menu!,UiLanguage.Text("组件设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);UpdateTime();
    }
    internal override void UpdateTime() {
        if(timeArtwork==null)return;
        timeArtwork.Update(instance,DateTimeOffset.Now,WidgetVisuals.Accent);
        W.Automation.AutomationProperties.SetName(timeArtwork,timeArtwork.AccessibleText);
        for(int i=0;i<timeButtons.Length;i++) {timeButtons[i].IsEnabled=!interactionLocked;if(i==0)W.Automation.AutomationProperties.SetName(timeButtons[i],timeArtwork.Running?UiLanguage.Text("暂停"):UiLanguage.Text("开始"));}
    }
}
