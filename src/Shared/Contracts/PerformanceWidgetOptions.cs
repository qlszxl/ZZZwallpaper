namespace LumaWall.Contracts;

public sealed record PerformanceWidgetOptions
{
    // Zero reads the legacy widget RefreshSeconds value without changing old presets.
    public int RefreshMilliseconds {get;init;}
    public int IntervalMilliseconds(int legacySeconds)=>RefreshMilliseconds==0?legacySeconds*1000:RefreshMilliseconds;
    public WidgetGroupFlow Flow {get;init;}=WidgetGroupFlow.Grid;
    public int Columns {get;init;}=2;
    public PerformanceMetrics[] Order {get;init;}=Enum.GetValues<PerformanceMetrics>();
    public PerformanceMetrics[] Selected(PerformanceMetrics metrics)=>Order.Concat(Enum.GetValues<PerformanceMetrics>()).Distinct().Where(m=>(metrics&m)!=0).ToArray();
    public int ColumnCount(int count)=>Flow switch {WidgetGroupFlow.Horizontal=>count,WidgetGroupFlow.Vertical=>1,_=>Math.Min(Columns,count)};
    public void Validate()
    {
        if(RefreshMilliseconds!=0&&RefreshMilliseconds is <100 or >60000)throw new ArgumentException("性能刷新间隔须为 100–60000 毫秒");
        if(!Enum.IsDefined(Flow)||Columns is <1 or >8||Order==null||Order.Length>Enum.GetValues<PerformanceMetrics>().Length||Order.Any(m=>!Enum.IsDefined(m))||Order.Distinct().Count()!=Order.Length)
            throw new ArgumentException("性能指标排列无效");
    }
}
