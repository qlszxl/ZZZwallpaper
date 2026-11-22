using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record EffectLayerControls(string? TargetKey,EffectReorderButton Card,Grid Container,Border DragOutline);
    private readonly Dictionary<EffectKind,EffectLayerControls> effectLayerControls=new();
    private Grid effectAuxiliaryPanel=null!;
    private TextBlock effectAuxiliaryHeading=null!;
    private void InitializeEffectOrder()
    {
        effectAuxiliaryHeading=UtilityText(UiLanguage.Text("辅助功能"),"UtilityHeading");
        effectAuxiliaryPanel=new Grid {ColumnSpacing=16,RowSpacing=16};
        EffectsOverview.Children.Insert(1,effectAuxiliaryHeading);EffectsOverview.Children.Insert(2,effectAuxiliaryPanel);
        EffectsOverview.Unloaded+=(_,_)=>ClearEffectLayerDrag();
        Activated+=(_,e)=> {if(e.WindowActivationState==WindowActivationState.Deactivated)ClearEffectLayerDrag();};
    }
    private void RefreshEffectOverview()
    {
        var collection=effectSession.Collection;
        var visual=EffectLayerPresentation.Visible(collection).Select(kind=>effectDefinitions.First(definition=>definition.Kind==kind)).ToArray();
        var auxiliary=EffectLayerPresentation.AuxiliaryKinds(effectTargetAssetId!=null).Where(kind=>(collection.Added&kind)!=0)
            .Select(kind=>effectDefinitions.First(definition=>definition.Kind==kind)).ToArray();
        var package=effectSession.ExtendedDraft.PackageEffects;
        string key=string.Join(";",package.Select(p=>p.ToString()))+effectSession.Key+":"+string.Join(";",visual.Concat(auxiliary).Select(definition=>definition.Kind));
        if(key!=effectsOverviewKey||EffectsListPanel.Children.Count==0) {
            ClearEffectLayerDrag();
            effectsOverviewKey=key;effectOverviewControls.Clear();effectLayerControls.Clear();EffectsListPanel.Children.Clear();effectAuxiliaryPanel.Children.Clear();
            if(visual.Length==0&&package.Length==0)EffectsListPanel.Children.Add(UtilityCard(UtilityText(UiLanguage.Text("暂无画面效果"))));
            foreach(var definition in visual)EffectsListPanel.Children.Add(EffectOverviewCard(definition));
            foreach(var item in package){
                var title=FeaturePacks.Effects.FirstOrDefault(p=>p.Key==item.Id)?.Entry.Title??item.Id;
                var card=WidgetButton(title,()=>EditPackageEffect(item,title));card.MinHeight=112;EffectsListPanel.Children.Add(card);
            }
            foreach(var definition in auxiliary)effectAuxiliaryPanel.Children.Add(EffectOverviewCard(definition));
        }
        EffectsCountText.Text=UiLanguage.Format($"画面效果 · {visual.Length+package.Length}");
        effectAuxiliaryHeading.Text=UiLanguage.Format($"辅助功能 · {auxiliary.Length}");
        RefreshEffectAuxiliaryVisibility();SyncEffectOverviewControls();
    }
    private void RefreshEffectAuxiliaryVisibility()
    {
        if(effectAuxiliaryPanel==null)return;
        effectAuxiliaryHeading.Visibility=effectAuxiliaryPanel.Visibility=SelectedScreen!=null&&effectAuxiliaryPanel.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
    }
    private FrameworkElement EffectLayerCard(EffectDefinition definition,MenuFlyout menu)
    {
        string? targetKey=effectSession.Key;
        var kind=definition.Kind;
        var button=(EffectReorderButton)EffectChoiceButton(definition,()=> {
            if(effectReorder==null&&targetKey!=null&&targetKey==effectSession.Key&&!loadingEffects&&SelectedScreen!=null)
                OpenEffect(kind);
        },112,overview:true,reorder:true);
        button.ContextFlyout=menu;button.CoordinateRoot=Root;
        AutomationProperties.SetHelpText(button,UiLanguage.Text("拖动或用方向键排序，点击编辑。"));
        var card=new Grid {Height=112,VerticalAlignment=VerticalAlignment.Top,ContextFlyout=menu};card.Children.Add(button);
        var outline=new Border {CornerRadius=new CornerRadius(24),BorderThickness=new Thickness(2),BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["InteractionAccentBrush"],IsHitTestVisible=false,Visibility=Visibility.Collapsed};card.Children.Add(outline);
        button.Padding=new Thickness(18,0,18,0);AddCardHelp(card,definition.Name);
        effectLayerControls[kind]=new(targetKey,button,card,outline);
        button.BeginDrag=point=>targetKey!=null&&BeginEffectCardDrag(kind,targetKey,point);
        button.MoveDrag=MoveEffectCardDrag;button.EndDrag=EndEffectCardDrag;
        button.PreviewKeyDown+=(_,e)=> {
            if(e.Key==VirtualKey.Escape&&effectReorder!=null) {e.Handled=true;ClearEffectLayerDrag();return;}
            if(effectReorder!=null)return;
            if(e.Key is VirtualKey.Left or VirtualKey.Up or VirtualKey.Right or VirtualKey.Down) {e.Handled=true;MoveEffectLayer(kind,e.Key is VirtualKey.Left or VirtualKey.Up?-1:1,targetKey);}
        };
        return card;
    }
    private void ClearEffectLayerDrag()
    {
        EndEffectCardDrag(false);
        foreach(var controls in effectLayerControls.Values.ToArray())controls.Card.CancelGesture();
    }
    private void RunEffectOrderGesture(Func<EffectCollection,EffectCollection> change,EffectKind? focus=null)
    {
        if(SelectedScreen==null||loadingEffects)return;
        string? targetKey=effectSession.Key;
        BeginHistoryGesture();
        try {
            var current=effectSession.Collection;var next=change(current);if(ReferenceEquals(next,current))return;
            effectSession.Collection=next;DraftChanged();RefreshEffectCollection();
        } finally {EndHistoryGesture();}
        if(focus is { } kind)DispatcherQueue.TryEnqueue(()=> {if(targetKey==effectSession.Key&&selectedEffectKind==null&&effectLayerControls.TryGetValue(kind,out var controls))controls.Card.Focus(FocusState.Programmatic);});
    }
    private void MoveEffectLayer(EffectKind kind,int direction,string? targetKey)
    {
        if(targetKey!=null&&targetKey==effectSession.Key)RunEffectOrderGesture(collection=>EffectLayerPresentation.MoveAdjacent(collection,kind,direction),kind);
    }
    private void SyncEffectLayerControls()
    {
        foreach(var controls in effectLayerControls.Values) {
            if(SelectedScreen==null||loadingEffects||controls.TargetKey!=effectSession.Key)controls.Card.CancelGesture();
        }
    }
}
