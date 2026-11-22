using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Border sunGradientBar=null!;
    private Canvas sunPointCanvas=null!;
    private Slider sunPointPosition=null!;
    private TextBlock sunPointCaption=null!;
    private Button sunPointColor=null!,sunAddPoint=null!,sunRemovePoint=null!;
    private readonly List<Border> sunPointMarkers=new();
    private int sunSelectedPoint;
    private bool updatingSunPalette,sunDragging;
    private string? sunDragDraft;
    private uint sunDragPointer;

    private FrameworkElement CreateSunPaletteEditor()
    {
        var panel=new StackPanel {Spacing=10};
        panel.Children.Add(UtilityText(UiLanguage.Text("渐变色带"),"UtilityHeading"));
        sunGradientBar=new Border {Height=36,CornerRadius=new CornerRadius(8),Margin=new Thickness(14,0,14,0)};
        ParameterInputGate.SetSurface(sunGradientBar);
        sunGradientBar.DoubleTapped+=(_,e)=> {if(sunGradientBar.ActualWidth>0)AddSunPoint(e.GetPosition(sunGradientBar).X/sunGradientBar.ActualWidth);};
        panel.Children.Add(sunGradientBar);
        sunPointCanvas=new Canvas {Height=32,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        ParameterInputGate.SetSurface(sunPointCanvas);
        sunPointCanvas.SizeChanged+=(_,_)=>PositionSunMarkers();panel.Children.Add(sunPointCanvas);
        
        sunPointCaption=UtilityText("","UtilityLabel");panel.Children.Add(sunPointCaption);
        sunPointPosition=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=.1};
        sunPointPosition.ValueChanged+=(_,_)=> {if(!updatingSunPalette&&!loadingEffects)MoveSunPoint(sunPointPosition.Value/100);};
        panel.Children.Add(NumericSliderRow(UiLanguage.Text("关键点位置"),sunPointPosition,"%",1));
        sunPointColor=ColorChoiceButton(UiLanguage.Text("关键点颜色"),()=>SkinColor(effectSession.ExtendedDraft.SunPrism.Colors[Math.Clamp(sunSelectedPoint,0,effectSession.ExtendedDraft.SunPrism.Colors.Length-1)]),color=> {
            if(loadingEffects||updatingSunPalette)return;
            var colors=effectSession.ExtendedDraft.SunPrism.Colors.ToArray();colors[sunSelectedPoint]=ColorHex(color);
            effectSession.ExtendedDraft=effectSession.ExtendedDraft with {SunPrism=effectSession.ExtendedDraft.SunPrism with {Colors=colors}};RefreshSunPalette();DraftChanged();
        });
        panel.Children.Add(WidgetField(UiLanguage.Text("关键点颜色"),sunPointColor));
        var actions=new Grid {ColumnSpacing=8};
        for(int i=0;i<4;i++)actions.ColumnDefinitions.Add(new());
        var previous=WidgetButton(UiLanguage.Text("上一点"),()=> {sunSelectedPoint=Math.Max(0,sunSelectedPoint-1);RefreshSunPalette();});
        var next=WidgetButton(UiLanguage.Text("下一点"),()=> {sunSelectedPoint=Math.Min(effectSession.ExtendedDraft.SunPrism.Colors.Length-1,sunSelectedPoint+1);RefreshSunPalette();});
        sunAddPoint=WidgetButton(UiLanguage.Text("添加点"),()=>AddSunPoint(null));
        sunRemovePoint=WidgetButton(UiLanguage.Text("删除点"),RemoveSunPoint);
        Button[] buttons=[previous,next,sunAddPoint,sunRemovePoint];
        for(int i=0;i<buttons.Length;i++) {Grid.SetColumn(buttons[i],i);actions.Children.Add(buttons[i]);}
        panel.Children.Add(actions);RefreshSunPalette();return panel;
    }
    private void PositionSunMarkers()
    {
        if(sunPointCanvas is null)return;
        double width=Math.Max(1,sunPointCanvas.ActualWidth-28);var positions=effectSession.ExtendedDraft.SunPrism.GetColorPositions();
        for(int i=0;i<sunPointMarkers.Count&&i<positions.Length;i++) {Canvas.SetLeft(sunPointMarkers[i],positions[i]*width);Canvas.SetTop(sunPointMarkers[i],1);}
    }
    private void RefreshSunPalette()
    {
        if(sunPointCanvas is null)return;
        bool previous=updatingSunPalette;updatingSunPalette=true;
        try {
            var sun=effectSession.ExtendedDraft.SunPrism;var positions=sun.GetColorPositions();sunSelectedPoint=Math.Clamp(sunSelectedPoint,0,sun.Colors.Length-1);
            var gradient=new LinearGradientBrush {StartPoint=new Point(0,.5),EndPoint=new Point(1,.5)};
            for(int i=0;i<sun.Colors.Length;i++)gradient.GradientStops.Add(new GradientStop {Color=SkinColor(sun.Colors[i]),Offset=positions[i]});
            sunGradientBar.Background=gradient;
            // Keep pointer-captured markers alive throughout dragging and color changes.
            if(sunPointMarkers.Count!=sun.Colors.Length) {
                sunDragging=false;sunPointCanvas.Children.Clear();sunPointMarkers.Clear();
                for(int i=0;i<sun.Colors.Length;i++) {
                    int index=i;var marker=new Border {Width=28,Height=28,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(2)};
                    marker.PointerPressed+=(_,e)=> {
                        if(!e.GetCurrentPoint(marker).Properties.IsLeftButtonPressed)return;
                        sunSelectedPoint=index;sunDragDraft=effectSession.Key;sunDragPointer=e.Pointer.PointerId;sunDragging=marker.CapturePointer(e.Pointer);RefreshSunPalette();e.Handled=true;
                    };
                    marker.PointerMoved+=(_,e)=> {
                        if(!sunDragging||sunSelectedPoint!=index||sunDragPointer!=e.Pointer.PointerId||sunDragDraft!=effectSession.Key)return;
                        if(!e.GetCurrentPoint(marker).Properties.IsLeftButtonPressed) {sunDragging=false;marker.ReleasePointerCaptures();return;}
                        MoveSunPoint((e.GetCurrentPoint(sunPointCanvas).Position.X-14)/Math.Max(1,sunPointCanvas.ActualWidth-28));e.Handled=true;
                    };
                    marker.PointerReleased+=(_,e)=> {if(sunDragPointer!=e.Pointer.PointerId)return;sunDragging=false;marker.ReleasePointerCaptures();e.Handled=true;};
                    marker.PointerCaptureLost+=(_,_)=>sunDragging=false;
                    sunPointMarkers.Add(marker);sunPointCanvas.Children.Add(marker);
                }
            }
            for(int i=0;i<sunPointMarkers.Count;i++) {
                var marker=sunPointMarkers[i];marker.Background=new SolidColorBrush(SkinColor(sun.Colors[i]));
                marker.BorderBrush=HomeBrush(i==sunSelectedPoint?"InteractionAccentBrush":"HomeControlText");marker.BorderThickness=new Thickness(i==sunSelectedPoint?4:2);
                
                AutomationProperties.SetName(marker,UiLanguage.Format($"关键点 {i+1}"));
            }
            PositionSunMarkers();sunPointCaption.Text=UiLanguage.Format($"关键点 {sunSelectedPoint+1} / {sun.Colors.Length}");
            sunPointPosition.Value=positions[sunSelectedPoint]*100;
            if(sunPointColor?.Tag is Action refresh)refresh();
            if(sunAddPoint!=null)sunAddPoint.IsEnabled=sun.Colors.Length<12;
            if(sunRemovePoint!=null)sunRemovePoint.IsEnabled=sun.Colors.Length>2;
        }finally {updatingSunPalette=previous;}
    }
    private void MoveSunPoint(double position)
    {
        if(loadingEffects)return;
        var positions=effectSession.ExtendedDraft.SunPrism.GetColorPositions();int index=sunSelectedPoint;
        // Keep ordering stable, so the selected point and its color never swap during a drag.
        double min=index==0?0:positions[index-1]+.001,max=index==positions.Length-1?1:positions[index+1]-.001;
        if(min>max)return;
        position=Math.Clamp(position,min,max);if(Math.Abs(positions[index]-position)<.000001)return;
        positions[index]=position;effectSession.ExtendedDraft=effectSession.ExtendedDraft with {SunPrism=effectSession.ExtendedDraft.SunPrism with {ColorPositions=positions}};
        RefreshSunPalette();DraftChanged();
    }
    private void AddSunPoint(double? requested)
    {
        if(loadingEffects||effectSession.ExtendedDraft.SunPrism.Colors.Length>=12)return;
        var sun=effectSession.ExtendedDraft.SunPrism;var positions=sun.GetColorPositions();double at;
        if(requested is { } value)at=Math.Clamp(value,0,1);
        else {
            var boundaries=new[]{0d}.Concat(positions).Append(1d).ToArray();int widest=0;
            for(int i=1;i<boundaries.Length-1;i++)if(boundaries[i+1]-boundaries[i]>boundaries[widest+1]-boundaries[widest])widest=i;
            at=(boundaries[widest]+boundaries[widest+1])/2;
        }
        int nearest=Enumerable.Range(0,positions.Length).MinBy(i=>Math.Abs(positions[i]-at));
        if(Math.Abs(positions[nearest]-at)<.002) {sunSelectedPoint=nearest;RefreshSunPalette();return;}
        int right=Array.FindIndex(positions,p=>p>at);if(right<0)right=positions.Length;
        int left=Math.Max(0,right-1);string color=sun.Colors[Math.Min(right,sun.Colors.Length-1)];
        if(right>0&&right<positions.Length) {
            var a=SkinColor(sun.Colors[left]);var b=SkinColor(sun.Colors[right]);double t=(at-positions[left])/(positions[right]-positions[left]);
            color=ColorHex(Windows.UI.Color.FromArgb(255,(byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t)));
        }
        var colors=sun.Colors.ToList();var stops=positions.ToList();colors.Insert(right,color);stops.Insert(right,at);
        effectSession.ExtendedDraft=effectSession.ExtendedDraft with {SunPrism=sun with {Colors=colors.ToArray(),ColorPositions=stops.ToArray()}};sunSelectedPoint=right;RefreshSunPalette();DraftChanged();
    }
    private void RemoveSunPoint()
    {
        var sun=effectSession.ExtendedDraft.SunPrism;if(loadingEffects||sun.Colors.Length<=2)return;
        var positions=sun.GetColorPositions();
        effectSession.ExtendedDraft=effectSession.ExtendedDraft with {SunPrism=sun with {Colors=sun.Colors.Where((_,i)=>i!=sunSelectedPoint).ToArray(),ColorPositions=positions.Where((_,i)=>i!=sunSelectedPoint).ToArray()}};
        RefreshSunPalette();DraftChanged();
    }
}
