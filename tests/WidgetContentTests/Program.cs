using System.Text;
using System.IO;
using LumaWall.Contracts;
using LumaWall.WidgetHost;
using C=System.Windows.Controls;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string name) {if(!value)throw new InvalidOperationException(name);checks++;}
    [STAThread] private static void Main(string[] args)
    {
        WidgetInteractionChecks.Run(Check);
        var messages=new List<WidgetMessage>();
        WidgetInstance current=new() {Id="initial",Kind=WidgetKind.Search};bool locked=false;
        var actions=new List<Action>();
        WidgetContentContext Context()=>new(()=>current,()=>null,()=>locked,new(),new C.ContextMenu(),(_,action,_)=>actions.Add(action),()=>{},()=>{},_=>{},messages.Add);
        var search=WidgetContentFactory.Create(Context(),new("保留搜索文本",2,3,false));
        Check(search.SearchInput?.Text=="保留搜索文本"&&search.SearchInput.SelectionStart==2&&search.SearchInput.SelectionLength==3,"Search state survives content reconstruction");
        search.Resize(400,80);

        foreach(var kind in ExpansionCatalog.Widgets.Keys) {
            current=new() {Id=kind.ToString(),Kind=kind};
            var content=WidgetContentFactory.Create(Context(),new("",0,0,false));
            Check(content.Element!=null,"Every built-in kind has a visual root: "+kind);
            // Constructing and updating visuals does not create a window or launch a helper.
            content.UpdateTime();content.UpdateSpectrum(SpectrumSample.Empty);content.Resize(500,300);
        }

        current=new() {Id="performance",Kind=WidgetKind.Performance,Metrics=PerformanceMetrics.Cpu|PerformanceMetrics.Memory};
        var performance=WidgetContentFactory.Create(Context(),new("",0,0,false));
        Performance80Checks.Run(Check);
        var sample=new SystemPerformanceSample(25,50,8,16,null,null,null,null,null,null);
        var lines=performance.UpdatePerformance(sample,Environment.TickCount64);
        Check(lines?.Count==2&&lines.Any(line=>line.Contains("25%")),"Performance values follow selected metrics");
        Check(performance.UpdatePerformance(sample,Environment.TickCount64)==null,"Performance cadence is retained inside content");
        performance.ResetPerformanceSchedule();Check(performance.UpdatePerformance(sample,Environment.TickCount64)!=null,"Changed settings reset the sampling deadline");

        current=new() {Id="timer",Kind=WidgetKind.FocusTimer};
        var timer=WidgetContentFactory.Create(Context(),new("",0,0,false));
        Check(timer.ActionButtons.Count==3&&timer.DrawingElement!=null,"Timer exposes accessible artwork and actions");
        locked=true;timer.UpdateTime();Check(timer.ActionButtons.All(button=>button.IsEnabled),"Position locking preserves timer controls");
        current=current with {InteractionLocked=true};timer.UpdateTime();Check(timer.ActionButtons.All(button=>!button.IsEnabled),"Independent interaction locking disables timer actions");
        timer.ActionButtons[0].RaiseEvent(new System.Windows.RoutedEventArgs(C.Button.ClickEvent));
        Check(messages.Count==0,"Locked timer does not emit mutations");
        locked=false;current=current with {Id="latest",InteractionLocked=false};timer.UpdateTime();
        timer.ActionButtons[0].RaiseEvent(new System.Windows.RoutedEventArgs(C.Button.ClickEvent));
        Check(messages.Single().Type=="timer-action"&&messages[0].Id=="latest","Content actions use the latest configuration through their context");

        using var source=new MemoryStream(Encoding.UTF8.GetBytes("{\"Type\":\"inspect\",\"Id\":\"one\"}\n{\"Type\":\"opened\",\"Id\":\"two\"}\n"));
        using var destination=new MemoryStream();
        using(var channel=new WidgetHostChannel(source,destination)) {
            var incoming=new List<WidgetMessage>();channel.ReadMessagesAsync(incoming.Add).GetAwaiter().GetResult();
            Check(incoming.Count==2&&incoming[0].Id=="one"&&incoming[1].Id=="two","Transport preserves line-framed message order");
            channel.Send(new() {Type="ready",Revision="stable"});
            Check(Encoding.UTF8.GetString(destination.ToArray()).Contains("\"Revision\":\"stable\""),"Transport writes the existing JSON contract");
        }
        Console.WriteLine($"Widget content and channel checks passed: {checks}");
    }
}
