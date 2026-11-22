using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class SearchWidgetContent : WidgetContent
{
    private readonly C.TextBox input;
    private readonly C.Border searchFrame;
    internal override C.TextBox SearchInput=>input;
    internal SearchWidgetContent(WidgetContentContext context,SearchFieldState saved):base(context)
    {
            input=new C.TextBox {Background=M.Brushes.Transparent,BorderThickness=new W.Thickness(0),Foreground=WidgetVisuals.Ink,CaretBrush=WidgetVisuals.Accent,SelectionBrush=WidgetVisuals.Accent,SelectionOpacity=.45,
                FontFamily=WidgetVisuals.Body,FontSize=14,VerticalContentAlignment=W.VerticalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center,MaxLength=1000,Padding=new W.Thickness(0),MinWidth=0};
            var scroll=new W.FrameworkElementFactory(typeof(C.ScrollViewer));scroll.Name="PART_ContentHost";
            input.Template=new C.ControlTemplate(typeof(C.TextBox)) {VisualTree=scroll};
            W.Automation.AutomationProperties.SetName(input,UiLanguage.Text("搜索关键词"));
            var grid=new C.Grid();grid.ColumnDefinitions.Add(new C.ColumnDefinition {Width=new W.GridLength(44)});grid.ColumnDefinitions.Add(new C.ColumnDefinition());
            var icon=new C.Button {Content=CustomIcon(instance.Icon,"search"),Style=WidgetVisuals.PlainButton(),Width=32,Height=32,HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center,HorizontalContentAlignment=W.HorizontalAlignment.Center,VerticalContentAlignment=W.VerticalAlignment.Center,Focusable=false};
            DragHandle(icon,SubmitSearch);grid.Children.Add(icon);
            var field=new C.Grid {Margin=new W.Thickness(2,0,16,0)};C.Grid.SetColumn(field,1);grid.Children.Add(field);
            var hint=new C.TextBlock {Text=UiLanguage.Text("搜索…"),Foreground=WidgetVisuals.Muted,IsHitTestVisible=false,VerticalAlignment=W.VerticalAlignment.Center};field.Children.Add(hint);field.Children.Add(input);
            input.TextChanged+=(_,_)=>hint.Visibility=input.Text.Length==0?W.Visibility.Visible:W.Visibility.Collapsed;
            I.TextCompositionManager.AddPreviewTextInputStartHandler(input,(_,e)=> {if(interactionLocked)e.Handled=true;else Context.Input.Composing=true;});
            I.TextCompositionManager.AddPreviewTextInputUpdateHandler(input,(_,e)=> {if(interactionLocked)e.Handled=true;else Context.Input.Composing=true;});
            input.PreviewTextInput+=(_,e)=> {if(interactionLocked) {e.Handled=true;return;}if(Context.Input.Composing) {Context.Input.Composing=false;Context.Input.ImeCommit=true;}};
            input.PreviewKeyUp+=(_,_)=>Context.Input.ImeCommit=false;
            input.PreviewKeyDown+=(_,e)=> {if(interactionLocked) {e.Handled=true;return;}if(e.Key==I.Key.Enter||e.Key==I.Key.Return) {if(!Context.Input.Composing&&!Context.Input.ImeCommit)SubmitSearch();e.Handled=!Context.Input.Composing;}};
            I.CommandManager.AddPreviewExecutedHandler(input,(_,e)=> {if(interactionLocked)e.Handled=true;});
            searchFrame=WidgetVisuals.Frame(grid,out focusEdge);Element=searchFrame;
            input.Text=saved.Text;input.Select(Math.Min(saved.Start,input.Text.Length),Math.Min(saved.Length,input.Text.Length-Math.Min(saved.Start,input.Text.Length)));
            input.GotKeyboardFocus+=(_,_)=>focusEdge.Background=interactionLocked?WidgetVisuals.Edge:WidgetVisuals.Accent;
            input.LostKeyboardFocus+=(_,_)=>focusEdge.Background=WidgetVisuals.Edge;
            WidgetVisuals.Action(menu,UiLanguage.Text("搜索"),"search",SubmitSearch,()=>!interactionLocked);
            WidgetVisuals.Action(menu,UiLanguage.Text("复制"),"copy",()=>input.Copy(),()=>!interactionLocked);
            WidgetVisuals.Action(menu,UiLanguage.Text("粘贴"),"edit",()=> {try {input.Paste();}catch {ShowMessage(UiLanguage.Text("粘贴失败，请重试"));}},()=>!interactionLocked);
            WidgetVisuals.Action(menu,UiLanguage.Text("清空"),"close",()=>input.Clear(),()=>!interactionLocked);
            WidgetVisuals.Action(menu,UiLanguage.Text("组件设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);
            input.ContextMenu=menu;C.ContextMenuService.SetShowOnDisabled(input,true);UpdateInteraction();
    }
    internal override void UpdateInteraction()
    {
        input.IsReadOnly=interactionLocked;input.IsEnabled=!interactionLocked;input.Focusable=!interactionLocked;
        if(interactionLocked) {Context.Input.Composing=false;Context.Input.ImeCommit=false;}
        if(interactionLocked&&input.IsKeyboardFocusWithin)I.Keyboard.ClearFocus();
        base.UpdateInteraction();
    }
    internal override void Resize(double width,double height)=>WidgetVisuals.SetFrameRadius(searchFrame,instance.Search.RadiusFor(width,height));
}
