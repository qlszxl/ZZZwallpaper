using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class ShortcutWidgetContent : WidgetContent
{
    internal ShortcutWidgetContent(WidgetContentContext context):base(context)
    {
            var row=new C.Grid {Margin=new W.Thickness(instance.IconOnly?0:10,0,instance.IconOnly?0:10,0)};
            if(!instance.IconOnly) {row.ColumnDefinitions.Add(new C.ColumnDefinition {Width=new W.GridLength(22)});row.ColumnDefinitions.Add(new C.ColumnDefinition());}
            row.Children.Add(CustomIcon(instance.Icon??card?.Icon,card?.Kind switch {ShortcutKind.Folder=>"folder",ShortcutKind.Command=>"command",_=>"cards"}));
            var label=new C.TextBlock {Text=card?.Name??UiLanguage.Text("快捷卡片"),FontFamily=WidgetVisuals.Heavy,FontWeight=W.FontWeights.Black,TextTrimming=W.TextTrimming.CharacterEllipsis,VerticalAlignment=W.VerticalAlignment.Center,Margin=new W.Thickness(10,0,0,0)};
            if(!instance.IconOnly) {C.Grid.SetColumn(label,1);row.Children.Add(label);}
            cardButton=new C.Button {Content=WidgetVisuals.Frame(row,out focusEdge),Style=WidgetVisuals.PlainButton()};
            cardButton.GotKeyboardFocus+=(_,_)=>focusEdge.Background=interactionLocked?WidgetVisuals.Edge:WidgetVisuals.Accent;
            cardButton.LostKeyboardFocus+=(_,_)=>focusEdge.Background=WidgetVisuals.Edge;
            W.Automation.AutomationProperties.SetName(cardButton,card?.Name??UiLanguage.Text("设置快捷卡片"));
            DragHandle(cardButton,ActivateCard);Element=cardButton;
            WidgetVisuals.Action(menu,UiLanguage.Text("卡片设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);
            if(card!=null)WidgetVisuals.Action(menu,UiLanguage.Text("复制目标"),"copy",()=> {try {W.Clipboard.SetText(card.Url);}catch {ShowMessage(UiLanguage.Text("复制失败，请重试"));}},()=>!interactionLocked);
            UpdateInteraction();
    }
}
