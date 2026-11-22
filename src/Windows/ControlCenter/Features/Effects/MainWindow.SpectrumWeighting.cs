using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildSpectrumWeighting(StackPanel host,string id,int? index,Func<bool> isActive)
    {
        bool refreshing=false;
        SpectrumOptions Root()=>widgets!.State.Widgets.First(w=>w.Id==id).Spectrum;
        SpectrumOptions Current()=>index is { } i?Root().SubSpectra[i]:Root();
        SpectrumWeighting Effective()=>SpectrumWeightCurve.Resolve(Root(),Current(),index!=null);
        void Change(Func<SpectrumWeighting,SpectrumWeighting> change) {
            if(refreshing||syncingWidgetEditor||!isActive())return;
            LiveWidget(id,w=>w with {Spectrum=index is { } i?w.Spectrum with {SubSpectra=w.Spectrum.SubSpectra.Select((s,n)=>n==i?s with {Weighting=change(s.Weighting)}:s).ToArray()}:w.Spectrum with {Weighting=change(w.Spectrum.Weighting)}});
        }
        var panel=new StackPanel {Spacing=14};panel.Children.Add(UtilityText(UiLanguage.Text("高度上限加权"),"UtilityHeading"));host.Children.Add(UtilityCard(panel));
        ToggleSwitch? follow=null;
        if(index!=null) {
            follow=WidgetToggle(UiLanguage.Text("与主频谱同步"),Current().Weighting.SyncWithMain);panel.Children.Add(WidgetToggleRow(UiLanguage.Text("与主频谱同步"),follow));
        }
        var enabled=WidgetToggle(UiLanguage.Text("启用高度上限加权"),Effective().Enabled);panel.Children.Add(WidgetToggleRow(UiLanguage.Text("启用高度上限加权"),enabled));
        var axis=UtilityText("");panel.Children.Add(axis);
        var editor=new SpectrumWeightEditor();panel.Children.Add(editor);
        
        var selected=UtilityText("","UtilityLabel");panel.Children.Add(selected);
        var position=new ScrubNumberInput(UiLanguage.Text("关键点位置"));var weight=new ScrubNumberInput(UiLanguage.Text("关键点高度上限"));
        var fields=new Grid {ColumnSpacing=16,RowSpacing=12};fields.Children.Add(WidgetField(UiLanguage.Text("横向位置"),position));fields.Children.Add(WidgetField(UiLanguage.Text("高度上限"),weight));UtilityAdaptiveGrid(fields,2,480);panel.Children.Add(fields);
        var smooth=WidgetToggle(UiLanguage.Text("平滑连接关键点"),Effective().Smooth);panel.Children.Add(WidgetToggleRow(UiLanguage.Text("平滑连接关键点"),smooth));
        var actions=new Grid {ColumnSpacing=10,RowSpacing=10};
        var previousPoint=WidgetButton(UiLanguage.Text("上一点"),()=>editor.SelectPoint(-1));
        var nextPoint=WidgetButton(UiLanguage.Text("下一点"),()=>editor.SelectPoint(1));
        var add=WidgetButton(UiLanguage.Text("添加关键点"),()=>editor.AddPoint(),"Add");
        var remove=WidgetButton(UiLanguage.Text("删除关键点"),()=>editor.DeleteSelected(),"Trash");
        var gaussian=WidgetButton(UiLanguage.Text("正态分布"),()=>Change(w=>w with {Points=SpectrumWeightCurve.Gaussian(),Smooth=true}));
        var flat=WidgetButton(UiLanguage.Text("全段 100%"),()=>Change(w=>w with {Points=SpectrumWeightCurve.Flat()}));
        foreach(var action in new[]{previousPoint,nextPoint,add,remove,gaussian,flat})actions.Children.Add(action);UtilityAdaptiveGrid(actions,3,680);panel.Children.Add(actions);
        var status=UtilityText("");panel.Children.Add(status);

        void RefreshPoint()
        {
            bool previous=refreshing;refreshing=true;
            try {
                var point=editor.SelectedPoint;var points=Effective().Points;int number=editor.SelectedIndex;
                bool editable=Effective().Enabled&&!(index!=null&&Current().Weighting.SyncWithMain);
                selected.Text=UiLanguage.Format($"关键点 {number+1} / {points.Length}");
                double min=number==0?0:points[number-1].Position*100+.1,max=number==points.Length-1?100:points[number+1].Position*100-.1;
                position.Configure(min,Math.Max(min,max),point.Position*100,.1,1,"%");weight.Configure(0,100,point.Weight*100,.1,1,"%");
                position.IsEnabled=editable&&editor.CanDelete;weight.IsEnabled=editable;
                add.IsEnabled=editable&&editor.CanAdd;remove.IsEnabled=editable&&editor.CanDelete;
                previousPoint.IsEnabled=editable&&number>0;nextPoint.IsEnabled=editable&&number<points.Length-1;
                gaussian.IsEnabled=flat.IsEnabled=smooth.IsEnabled=editable;
            }finally {refreshing=previous;}
        }
        void Refresh()
        {
            if(!isActive())return;bool previous=refreshing;refreshing=true;
            try {
                var local=Current();var curve=Effective();bool synced=index!=null&&local.Weighting.SyncWithMain;
                if(follow!=null)follow.IsOn=synced;enabled.IsOn=curve.Enabled;enabled.IsEnabled=!synced;
                editor.SetCurve(curve);editor.IsEnabled=curve.Enabled&&!synced;smooth.IsOn=curve.Smooth;
                axis.Text=local.Shape is SpectrumShape.Circle or SpectrumShape.RadialBars?UiLanguage.Text("横轴：顶部起点，顺时针 · 纵轴：长度上限（%）"):local.Direction>=2?UiLanguage.Text("横轴：从上到下 · 纵轴：长度上限（%）"):UiLanguage.Text("横轴：从左到右 · 纵轴：高度上限（%）");
                status.Text=synced?UiLanguage.Text("已同步主频谱"):"";status.Visibility=synced?Visibility.Visible:Visibility.Collapsed;
                RefreshPoint();
            }finally {refreshing=previous;}
        }
        if(follow!=null)follow.Toggled+=(_,_)=>Change(w=>w with {SyncWithMain=follow.IsOn});
        enabled.Toggled+=(_,_)=>Change(w=>w with {Enabled=enabled.IsOn});smooth.Toggled+=(_,_)=>Change(w=>w with {Smooth=smooth.IsOn});
        editor.PointsChanged+=points=>Change(w=>w with {Points=points});
        editor.SelectionChanged+=()=> {if(isActive())RefreshPoint();};
        editor.InteractionCompleted+=()=>widgets?.FlushPendingSave();
        position.ValueChanged+=value=> {if(!refreshing&&isActive())editor.MoveSelected(value/100,editor.SelectedPoint.Weight);};
        weight.ValueChanged+=value=> {if(!refreshing&&isActive())editor.MoveSelected(editor.SelectedPoint.Position,value/100);};
        position.InteractionCompleted+=()=>widgets?.FlushPendingSave();weight.InteractionCompleted+=()=>widgets?.FlushPendingSave();
        Refresh();return Refresh;
    }
}
