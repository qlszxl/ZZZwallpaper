using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void InitializeModalLayout()
    {
        Root.SizeChanged+=(_,_)=>LayoutCenteredModals();
        foreach(var overlay in new[]{EffectsAddOverlay,EffectsTargetOverlay,WidgetDrawerOverlay,QueueOverlay})overlay.SizeChanged+=(_,_)=>LayoutCenteredModals();
        EffectsAddList.SizeChanged+=(_,_)=>LayoutModalChoices(EffectsAddList);
        EffectsAddAuxiliaryList.SizeChanged+=(_,_)=>LayoutModalChoices(EffectsAddAuxiliaryList);
        LayoutCenteredModals();
    }
    private void LayoutCenteredModals()
    {
        if(Root==null||Root.ActualWidth<=0)return;
        foreach(var pair in new[]{(EffectsAddDrawer,EffectsAddOverlay),(EffectsTargetDrawer,EffectsTargetOverlay),(WidgetDrawer,WidgetDrawerOverlay),(QueueDrawer,QueueOverlay)}) {
            if(pair.Item1==null||pair.Item2==null)continue;
            double width=pair.Item2.ActualWidth>0?pair.Item2.ActualWidth:Root.ActualWidth;
            double height=pair.Item2.ActualHeight>0?pair.Item2.ActualHeight:Root.ActualHeight-72;
            pair.Item1.Width=Math.Min(pair.Item1==WidgetDrawer?widgetDrawerWidth:1040,Math.Max(0,width-48));
            pair.Item1.Height=Math.Min(pair.Item1==WidgetDrawer?widgetDrawerHeight:720,Math.Max(0,height-48));
        }
    }
    private static Grid ModalChoicesGrid()
    {
        var grid=new Grid {ColumnSpacing=16,RowSpacing=16};
        grid.SizeChanged+=(_,_)=>LayoutModalChoices(grid);return grid;
    }
    private static void LayoutModalChoices(Grid grid)
    {
        double width=grid.ActualWidth;
        FlowCards(grid,width>=880?3:width>=540?2:1);
    }
}
