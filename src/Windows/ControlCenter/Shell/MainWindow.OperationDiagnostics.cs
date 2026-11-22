using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed class ControlTrace {public OperationContext? Operation;public bool Hooked;}
    private readonly ConditionalWeakTable<DependencyObject,ControlTrace> controlTraces=new();
    private OperationContext? activeUiOperation,lastUiOperation,frameOperation;
    private DependencyObject? traceInputOwner;
    private System.Threading.Timer? diagnosticTimer;
    private NativeUiFaultSnapshot? nativeUiSnapshot;
    private long uiProbeSent,lastFaultAt,lastInputAt;
    private int uiProbePending,diagnosticClosed,watchdogReported;
    private bool tracePointerDown,traceFrameHooked;
    private readonly Queue<OperationContext> recentUiOperations=new();

    private void InitializeOperationDiagnostics()
    {
        OperationTrace.Initialize(AppLog.DirectoryPath,()=>activeUiOperation);
        DiagnosticInput.Register=TrackDiagnosticCombo;
        try {
            nativeUiSnapshot=new NativeUiFaultSnapshot();
            OperationTrace.Record(null,"native_context_reader",nativeUiSnapshot.IsSupported?"ready":"unsupported_binary","Version-checked read-only context reader; active only on faults.");
        }catch(Exception ex) {AppLog.Warning("Diagnostics","Native UI context initialization unavailable",ex);}
        Root.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=> {
            TraceInput(e.OriginalSource,"pointer");tracePointerDown=true;
        }),true);
        Root.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=> {tracePointerDown=false;TraceInputDispatched();}),true);
        Root.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler((_,_)=> {tracePointerDown=false;TraceInputDispatched();}),true);
        Root.AddHandler(UIElement.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>TraceInput(e.OriginalSource,"keyboard")),true);
        Root.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,_)=>TraceInputDispatched()),true);
        using(ExecutionContext.SuppressFlow())diagnosticTimer=new(_=>ProbeUi(),null,1000,1000);
        Closed+=(_,_)=> {
            Interlocked.Exchange(ref diagnosticClosed,1);diagnosticTimer?.Dispose();diagnosticTimer=null;
            DiagnosticInput.Register=null;
            if(traceFrameHooked)CompositionTarget.Rendering-=TraceRendering;
            OperationTrace.Record(lastUiOperation,"window","closed");
        };
    }
    private DependencyObject? TraceOwner(object? source)
    {
        DependencyObject? fallback=source as DependencyObject;
        DependencyObject? control=null;
        for(var node=fallback;node!=null;node=VisualTreeHelper.GetParent(node)) {
            if(node is ComboBox or ScrubNumberInput)return node;
            if(control==null&&node is (ComboBoxItem or ButtonBase or ToggleSwitch or Slider or TextBox))control=node;
        }
        return control??fallback;
    }
    private string TraceTarget(DependencyObject? target)
    {
        string name=target is FrameworkElement element?element.Name:"";
        if(target!=null&&DiagnosticInput.GetTarget(target) is {Length:>0} diagnosticName)name=diagnosticName;
        // Do not copy text boxes, file names, or arbitrary user content into breadcrumbs.
        return (target?.GetType().Name??"Window")+(name.Length>0?":"+name:"")+
            (navigationPage=="display"?":"+selectedEffectKind:"");
    }
    private void TraceInput(object? source,string action)
    {
        try {
            var owner=TraceOwner(source);long now=Environment.TickCount64;
            if(activeUiOperation!=null&&ReferenceEquals(owner,traceInputOwner)&&(tracePointerDown||now-lastInputAt<350)) {lastInputAt=now;return;}
            activeUiOperation=OperationTrace.Begin(navigationPage,action,TraceTarget(owner));lastUiOperation=activeUiOperation;
            traceInputOwner=owner;lastInputAt=now;
            if(owner!=null)controlTraces.GetOrCreateValue(owner).Operation=activeUiOperation;
            RememberTrace(activeUiOperation);
            if(owner is ComboBox combo)TrackDiagnosticCombo(combo);
            QueueTraceFrame(activeUiOperation);
        } catch { }
    }
    private void RememberTrace(OperationContext operation)
    {
        lock(recentUiOperations) {if(recentUiOperations.Count>=12)recentUiOperations.Dequeue();recentUiOperations.Enqueue(operation);}
    }
    private void TraceInputDispatched()
    {
        var operation=activeUiOperation;if(operation==null)return;
        // This confirms dispatch only, never persistence or physical presentation.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=> {
            OperationTrace.Record(operation,"input_dispatch","returned");
            if(activeUiOperation==operation&&!tracePointerDown)activeUiOperation=null;
        });
    }
    private void TrackDiagnosticCombo(ComboBox combo)
    {
        var binding=controlTraces.GetOrCreateValue(combo);if(binding.Hooked)return;binding.Hooked=true;
        combo.DropDownOpened+=(_,_)=> {
            binding.Operation=activeUiOperation??OperationTrace.Begin(navigationPage,"dropdown_open",TraceTarget(combo));
            activeUiOperation=lastUiOperation=binding.Operation;RememberTrace(binding.Operation);
            OperationTrace.Record(binding.Operation,"dropdown","opened_event");QueueTraceFrame(binding.Operation);
            try {
                foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
                    if(popup.Child is UIElement child&&!controlTraces.GetOrCreateValue(child).Hooked) {
                        // Popup input does not bubble through the main window root.
                        var popupTrace=controlTraces.GetOrCreateValue(child);popupTrace.Hooked=true;popupTrace.Operation=binding.Operation;
                        child.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>TraceInput(e.OriginalSource,"popup_pointer")),true);
                        child.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=>TraceInputDispatched()),true);
                        child.AddHandler(UIElement.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>TraceInput(e.OriginalSource,"popup_keyboard")),true);
                        child.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,_)=>TraceInputDispatched()),true);
                    }
            } catch { }
        };
        combo.SelectionChanged+=(_,_)=> {
            var operation=activeUiOperation??binding.Operation;
            if(operation==null)return;
            binding.Operation=operation;
            OperationTrace.Record(operation,"dropdown_selection","changed",$"index={combo.SelectedIndex}");QueueTraceFrame(operation);
        };
        combo.DropDownClosed+=(_,_)=> {
            OperationTrace.Record(binding.Operation,"dropdown","closed_event");
            if(activeUiOperation==binding.Operation)TraceInputDispatched();
        };
    }
    private void QueueTraceFrame(OperationContext operation)
    {
        frameOperation=operation;if(traceFrameHooked)return;
        traceFrameHooked=true;CompositionTarget.Rendering+=TraceRendering;
    }
    private void TraceRendering(object? sender,object args)
    {
        CompositionTarget.Rendering-=TraceRendering;traceFrameHooked=false;
        OperationTrace.Record(frameOperation,"ui_render","callback_observed","Not a GPU/present success acknowledgement.");frameOperation=null;
    }
    private void ProbeUi()
    {
        if(Volatile.Read(ref diagnosticClosed)!=0)return;
        long now=Environment.TickCount64;
        if(Interlocked.CompareExchange(ref uiProbePending,1,0)==0) {
            Interlocked.Exchange(ref uiProbeSent,now);
            if(!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=> {
                if(Interlocked.Exchange(ref watchdogReported,0)!=0)OperationTrace.Record(lastUiOperation,"ui_dispatcher","recovered");
                Interlocked.Exchange(ref uiProbePending,0);
            }))Interlocked.Exchange(ref uiProbePending,0);
        } else if(now-Interlocked.Read(ref uiProbeSent)>8000&&Interlocked.Exchange(ref watchdogReported,1)==0) {
            // Suspected stall: a blocked dispatcher is evidence, not proof of a layout fault.
            CaptureUiFault(null,"dispatcher_timeout",$"No dispatcher acknowledgement for {now-Interlocked.Read(ref uiProbeSent)} ms; last operation is a candidate.",false);
        }
    }
    internal void CaptureUiFault(Exception? error,string stage,string detail,bool readFocus=true)
    {
        try {
            long now=Environment.TickCount64,previous=Interlocked.Read(ref lastFaultAt);
            if(previous!=0&&now-previous<30000)return;
            if(Interlocked.CompareExchange(ref lastFaultAt,now,previous)!=previous)return;
            string native=nativeUiSnapshot?.Capture()??"Native snapshot unavailable";
            OperationContext? operation=null;string association="recent_operation_candidate";
            try {if(readFocus&&Root.XamlRoot is { } root) {
                for(var node=FocusManager.GetFocusedElement(root) as DependencyObject;node!=null;node=VisualTreeHelper.GetParent(node))
                    if(controlTraces.TryGetValue(node,out var trace)&&trace.Operation!=null) {operation=trace.Operation;association="focused_control_last_operation";break;}
            }}catch {association="focus_unavailable_recent_candidate";}
            operation??=OperationTrace.Current??lastUiOperation;
            string candidates;lock(recentUiOperations)candidates=string.Join(",",recentUiOperations.Select(o=>o.Id));
            // Read the retained native contexts immediately, before reporting through XAML.
            OperationTrace.Fault(operation,stage,$"association={association}; candidates={candidates}; hr=0x{error?.HResult:X8}; {detail}");
            OperationTrace.NativeSnapshot(operation,native);
        } catch(Exception captureError) {OperationTrace.Record(lastUiOperation,"fault_capture","unavailable",captureError.GetType().Name);}
    }
}
