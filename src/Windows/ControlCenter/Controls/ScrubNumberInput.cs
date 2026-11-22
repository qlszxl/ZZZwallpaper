using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Microsoft.UI.Input;

namespace LumaWall.ControlCenter;

// Drag by default; double-click or Enter/F2 explicitly starts a text draft.
// One completed drag or accepted draft is one history transaction.
public class ScrubNumberInput : UserControl
{
    private sealed class DragButton : Button
    {
        internal DragButton()=>ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
    private readonly TextBox input;
    private readonly DragButton dragSurface;
    private readonly TextBlock display;
    private readonly TextBlock unit;
    private readonly Popup hint;
    private readonly TextBlock hintText;
    private readonly DispatcherTimer hintTimer=new() {Interval=TimeSpan.FromSeconds(2.5)};
    private bool synchronizing,editing,committing,dragging;
    private int precision;
    private double value,minimum,maximum,step=1;
    private string label=UiLanguage.Text("参数数值");
    private ScrollViewer? viewport;
    private sealed record Transaction(Action Begin,Action End);
    private static readonly DependencyProperty TransactionProperty=DependencyProperty.RegisterAttached("Transaction",typeof(object),typeof(ScrubNumberInput),new PropertyMetadata(null));
    internal static void SetTransaction(DependencyObject root,Action begin,Action end)=>root.SetValue(TransactionProperty,new Transaction(begin,end));
    private Transaction? CurrentTransaction=>XamlRoot?.Content?.GetValue(TransactionProperty) as Transaction;
    internal static void RunTransaction(FrameworkElement source,Action change)
    {
        var transaction=source.XamlRoot?.Content?.GetValue(TransactionProperty) as Transaction;
        transaction?.Begin();try{change();}finally{transaction?.End();}
    }
    public event Action<double>? ValueChanged;
    public event Action? InteractionCompleted;
    public double Value {get=>value;set=>SetExternalValue(value);}
    public bool IsInteracting=>editing||dragging;
    internal TextBox Editor=>input;
    internal Control DragSurface=>dragSurface;
    internal string ValidationMessage {get;private set;}="";
    private static Style StyleOf(string key)=>(Style)Microsoft.UI.Xaml.Application.Current.Resources[key];
    public ScrubNumberInput():this(UiLanguage.Text("参数数值")) {}
    internal ScrubNumberInput(string name,bool compact=false)
    {
        Height=compact?32:40;MinWidth=0;IsTabStop=false;
        OptionWheelGuard.SetEnabled(this,true);
        input=new TextBox {Style=StyleOf("NumericEditTextBox"),MinHeight=0,MinWidth=0,Height=Height,
            CornerRadius=new CornerRadius(Height/2),FontFamily=UiFonts.Body,FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.Medium,
            BorderThickness=new Thickness(3),Padding=new Thickness(10,0,10,0),
            Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlRecess"],Foreground=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlText"],
            BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlEdge"],AcceptsReturn=false,IsReadOnly=true,IsSpellCheckEnabled=false,IsTextPredictionEnabled=false,
            Visibility=Visibility.Collapsed,IsTabStop=false};
        display=new TextBlock {FontFamily=UiFonts.Body,FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.Medium,
            TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        dragSurface=new DragButton {Style=StyleOf("NumericDragButton"),Content=display,MinHeight=0,MinWidth=0,Height=Height,
            CornerRadius=new CornerRadius(Height/2),HorizontalAlignment=HorizontalAlignment.Stretch};
        unit=new TextBlock {Margin=new Thickness(6,0,0,0),Opacity=.55,FontFamily=UiFonts.Body,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.Children.Add(dragSurface);row.Children.Add(input);Grid.SetColumn(unit,1);row.Children.Add(unit);Content=row;
        hintText=new TextBlock {FontFamily=UiFonts.Body,FontSize=12,TextWrapping=TextWrapping.Wrap,MaxWidth=260,Foreground=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlText"]};
        hint=new Popup {IsLightDismissEnabled=false,IsHitTestVisible=false,Tag="numeric-input-hint",Child=new Border {Padding=new Thickness(10,6,10,6),CornerRadius=new CornerRadius(6),Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlSurface"],BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["HomeControlEdge"],BorderThickness=new Thickness(1),Child=hintText}};
        hintTimer.Tick+=(_,_)=>HideHint();
        input.BeforeTextChanging+=(_,e)=>{if(!synchronizing&&e.NewText.Length>64){e.Cancel=true;ValidationMessage=UiLanguage.Text("数字过长");ShowHint(ValidationMessage);}};
        dragSurface.AddHandler(DoubleTappedEvent,new DoubleTappedEventHandler((_,e)=> {BeginEdit();e.Handled=true;}),true);
        dragSurface.PreviewKeyDown+=(_,e)=> {if(e.Key is VirtualKey.Enter or VirtualKey.F2){BeginEdit();e.Handled=true;}};
        input.TextChanging+=(_,_)=> {if(!synchronizing&&editing){ValidationMessage="";HideHint();}};
        input.PreviewKeyDown+=(_,e)=> {
            if(e.Key==VirtualKey.Enter){CommitEdit(false);e.Handled=true;}
            else if(e.Key==VirtualKey.Escape){CancelEdit();e.Handled=true;}
        };
        AttachDragHandle(dragSurface);
        input.LostFocus+=(_,_)=>CommitEdit();
        Loaded+=(_,_)=> {
            for(var parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll){viewport=scroll;viewport.ViewChanged+=ViewportChanged;break;}
        };
        Unloaded+=(_,_)=>{if(viewport!=null)viewport.ViewChanged-=ViewportChanged;viewport=null;CancelEdit();HideHint();};
        IsEnabledChanged+=(_,e)=> {bool enabled=(bool)e.NewValue;if(!enabled)CancelEdit();unit.Opacity=enabled ? .55 : .4;};
        SizeChanged+=(_,_)=>{input.Height=dragSurface.Height=ActualHeight;input.CornerRadius=dragSurface.CornerRadius=new CornerRadius(Math.Max(0,ActualHeight)/2);HideHint();};
        SetLabel(name);WriteText();
    }
    public void SetBounds(double max,double current)=>Configure(0,Math.Max(0,max),current,1,0,"px");
    public void Configure(double min,double max,double current,double increment,int digits,string unitValue)
    {
        minimum=min;maximum=Math.Max(min,max);step=increment>0?increment:1;precision=Math.Clamp(digits,0,4);
        unit.Text=unitValue;unit.Visibility=unitValue.Length==0?Visibility.Collapsed:Visibility.Visible;
        SetExternalValue(current);SetLabel(label);
    }
    internal void SetLabel(string name)
    {
        label=name;AutomationProperties.SetName(input,name);AutomationProperties.SetHelpText(input,NumericValueRules.Range(minimum,maximum,precision));
        AutomationProperties.SetName(dragSurface,name+"："+NumericValueRules.Format(value,precision));
        AutomationProperties.SetHelpText(dragSurface,UiLanguage.Text("左右拖动调整，双击或按 Enter / F2 输入。")+NumericValueRules.Range(minimum,maximum,precision));
    }
    private void SetExternalValue(double current)
    {
        if(!double.IsFinite(current))return;
        double next=Math.Clamp(Math.Round(current,precision,MidpointRounding.AwayFromZero),minimum,maximum);
        bool changed=next!=value;value=next;
        // Undo, slider or scope changes supersede drafts; unchanged refreshes do not.
        if(changed&&!committing){SetEditing(false);HideHint();}
        if(!editing&&!committing)WriteText();
    }
    private void WriteText(){synchronizing=true;try{display.Text=input.Text=NumericValueRules.Format(value,precision);AutomationProperties.SetName(dragSurface,label+"："+display.Text);}finally{synchronizing=false;}}
    private void SetEditing(bool active)
    {
        bool restoreFocus=!active&&editing&&input.FocusState!=FocusState.Unfocused;
        editing=active;input.IsReadOnly=!active;input.IsTabStop=active;dragSurface.IsTabStop=!active;
        if(active) {input.Visibility=Visibility.Visible;input.Focus(FocusState.Programmatic);dragSurface.Visibility=Visibility.Collapsed;}
        else {dragSurface.Visibility=Visibility.Visible;if(restoreFocus&&IsEnabled)dragSurface.Focus(FocusState.Programmatic);input.Visibility=Visibility.Collapsed;}
    }
    internal void BeginEdit()
    {
        if(!IsEnabled||dragging)return;
        // Release an armed click before swapping surfaces so its release cannot
        // recapture focus or change a value while the text draft is active.
        dragSurface.ReleasePointerCaptures();SetEditing(true);HideHint();input.SelectAll();
    }
    internal bool CommitEdit(bool restoreInvalid=true)
    {
        if(committing||!editing)return true;
        if(!IsEnabled){CancelEdit();return false;}
        if(!NumericValueRules.TryRead(input.Text,minimum,maximum,precision,out double next,out string error)) {
            ValidationMessage=error;
            if(restoreInvalid){SetEditing(false);WriteText();InteractionCompleted?.Invoke();}
            ShowHint(error);if(restoreInvalid)hintTimer.Start();
            return false;
        }
        committing=true;SetEditing(false);ValidationMessage="";HideHint();
        var transaction=CurrentTransaction;bool changed=next!=value;
        try {
            if(changed)transaction?.Begin();
            value=next;WriteText();if(changed)ValueChanged?.Invoke(value);
        } finally {
            committing=false;WriteText();InteractionCompleted?.Invoke();if(changed)transaction?.End();
        }
        return true;
    }
    internal void CancelEdit(){SetEditing(false);ValidationMessage="";WriteText();HideHint();}
    private void ShowHint(string text)
    {
        hintTimer.Stop();if(!IsLoaded||XamlRoot?.Content is not UIElement root)return;
        hintText.Text=text;hint.XamlRoot=XamlRoot;
        var point=TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        hint.HorizontalOffset=Math.Max(8,Math.Min(point.X,XamlRoot.Size.Width-290));
        hint.VerticalOffset=point.Y+ActualHeight+5;if(hint.VerticalOffset+56>XamlRoot.Size.Height)hint.VerticalOffset=Math.Max(4,point.Y-48);
        hint.IsOpen=true;
    }
    private void HideHint(){hintTimer.Stop();hint.IsOpen=false;}
    private void ViewportChanged(object? sender,ScrollViewerViewChangedEventArgs e)=>HideHint();
    internal void AttachDragHandle(FrameworkElement handle)
    {
        uint? pointer=null;double originX=0,originValue=0;Transaction? transaction=null;
        void Finish(){if(pointer==null)return;pointer=null;bool changed=dragging;dragging=false;if(changed)InteractionCompleted?.Invoke();transaction?.End();transaction=null;}
        handle.AddHandler(PointerPressedEvent,new PointerEventHandler((_,e)=> {
            if(!IsEnabled||!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
            CommitEdit();HideHint();if(!handle.CapturePointer(e.Pointer))return;
            pointer=e.Pointer.PointerId;originX=e.GetCurrentPoint(null).Position.X;originValue=value;
            dragSurface.Focus(FocusState.Pointer);e.Handled=true;
        }),true);
        handle.AddHandler(PointerMovedEvent,new PointerEventHandler((_,e)=> {
            if(pointer!=e.Pointer.PointerId)return;
            if(!IsEnabled){Finish();handle.ReleasePointerCaptures();return;}
            double distance=e.GetCurrentPoint(null).Position.X-originX;
            if(!dragging){if(Math.Abs(distance)<4){e.Handled=true;return;}dragging=true;transaction=CurrentTransaction;transaction?.Begin();}
            double next=Math.Clamp(Math.Round(originValue+distance*step,precision),minimum,maximum);
            if(value!=next){value=next;WriteText();ValueChanged?.Invoke(value);}e.Handled=true;
        }),true);
        handle.AddHandler(PointerReleasedEvent,new PointerEventHandler((_,e)=> {if(pointer!=e.Pointer.PointerId)return;Finish();handle.ReleasePointerCapture(e.Pointer);e.Handled=true;}),true);
        handle.PointerCanceled+=(_,_)=>Finish();handle.PointerCaptureLost+=(_,_)=>Finish();handle.Unloaded+=(_,_)=>Finish();
    }
}
