using System.Globalization;
using System.Text.Json.Serialization;

namespace LumaWall.Contracts;

public sealed record ClockWidgetOptions
{
    public int Face {get;init;}
    public bool Use24Hour {get;init;}=true;
    public bool ShowSeconds {get;init;}=true;
    public bool ShowDate {get;init;}=true;
    public bool ShowWeekday {get;init;}=true;
    public int DateFormat {get;init;}
    public void Validate() {if(Face is <0 or >1||DateFormat is <0 or >2)throw new ArgumentException("时钟设置无效");}
    public string Time(DateTime now)=>now.ToString(Use24Hour?(ShowSeconds?"HH:mm:ss":"HH:mm"):(ShowSeconds?"hh:mm:ss tt":"hh:mm tt"),CultureInfo.GetCultureInfo(UiLanguage.Current));
    public string Date(DateTime now)=>string.Join("  ",new[]{ShowDate?now.ToString(DateFormat switch {1=>"yyyy/MM/dd",2=>UiLanguage.Text("MM月dd日"),_=>"yyyy.MM.dd"},CultureInfo.GetCultureInfo(UiLanguage.Current)):"",ShowWeekday?now.ToString("dddd",CultureInfo.GetCultureInfo(UiLanguage.Current)):""}.Where(s=>s.Length>0));
}

public sealed record CountdownWidgetOptions
{
    public string Title {get;init;}=UiLanguage.Text("倒计时");
    public DateTimeOffset Target {get;init;}=new(2030,1,1,0,0,0,TimeSpan.Zero);
    public bool ShowSeconds {get;init;}=true;
    public bool CountUpAfterEnd {get;init;}
    public void Validate() {if(Title==null||Title.Length>80||Target.Year is <1970 or >9998)throw new ArgumentException("倒计时设置无效");}
    public string Value(DateTimeOffset now) {
        var span=Target-now;
        if(span<=TimeSpan.Zero&&!CountUpAfterEnd)return UiLanguage.Text("已到时间");
        span=span.Duration();long seconds=(long)Math.Ceiling(span.TotalSeconds);
        return UiLanguage.Format($"{seconds/86400} 天  {seconds/3600%24:00}:{seconds/60%60:00}")+(ShowSeconds?$":{seconds%60:00}":"");
    }
}

public sealed record FocusWidgetOptions
{
    public int FocusMinutes {get;init;}=25;
    public int BreakMinutes {get;init;}=5;
    public int LongBreakMinutes {get;init;}=15;
    public int SessionsPerLongBreak {get;init;}=4;
    public bool AutoStart {get;init;}
    public bool Running {get;init;}
    public int Phase {get;init;}
    public int CompletedSessions {get;init;}
    public double RemainingSeconds {get;init;}=1500;
    public DateTimeOffset? Deadline {get;init;}
    [JsonIgnore] public int DurationSeconds=>(Phase==0?FocusMinutes:Phase==1?BreakMinutes:LongBreakMinutes)*60;
    [JsonIgnore] public string PhaseName=>Phase==0?UiLanguage.Text("专注"):Phase==1?UiLanguage.Text("短休息"):UiLanguage.Text("长休息");
    public double Remaining(DateTimeOffset now)=>Running&&Deadline is { } end?Math.Max(0,(end-now).TotalSeconds):RemainingSeconds;
    public void Validate() {
        if(FocusMinutes is <1 or >240||BreakMinutes is <1 or >120||LongBreakMinutes is <1 or >240||SessionsPerLongBreak is <1 or >12||Phase is <0 or >2||CompletedSessions<0||!double.IsFinite(RemainingSeconds)||RemainingSeconds is <0 or >14400||Running&&Deadline==null)
            throw new ArgumentException("专注计时设置无效");
    }
    public FocusWidgetOptions Advance(DateTimeOffset now) {
        if(!Running||Deadline is not { } end||end>now)return this;
        var value=this;
        // Fold complete cycles when resuming after a long absence without iterating every second.
        if(value.AutoStart&&(now-end).TotalSeconds>86400) {
            double cycle=(value.FocusMinutes+value.BreakMinutes)*(value.SessionsPerLongBreak-1)*60d+(value.FocusMinutes+value.LongBreakMinutes)*60d;
            long cycles=(long)((now-end).TotalSeconds/cycle);
            if(cycles>0) {end=end.AddSeconds(cycles*cycle);value=value with {Deadline=end,CompletedSessions=(int)Math.Min(int.MaxValue-value.SessionsPerLongBreak,(long)value.CompletedSessions+cycles*value.SessionsPerLongBreak)};}
        }
        do {
            int completed=value.CompletedSessions+(value.Phase==0&&value.CompletedSessions<int.MaxValue?1:0);
            int phase=value.Phase==0?(completed%value.SessionsPerLongBreak==0?2:1):0;
            value=value with {Phase=phase,CompletedSessions=completed};
            end=end.AddSeconds(value.DurationSeconds);
            value=value with {RemainingSeconds=value.DurationSeconds,Running=value.AutoStart,Deadline=value.AutoStart?end:null};
        } while(value.Running&&end<=now);
        return value;
    }
    public FocusWidgetOptions Action(string action,DateTimeOffset now) {
        var value=Advance(now);
        return action switch {
            "start"=>value.Running?value:value with {Running=true,Deadline=now.AddSeconds(value.RemainingSeconds>0?value.RemainingSeconds:value.DurationSeconds)},
            "pause"=>value with {RemainingSeconds=value.Remaining(now),Running=false,Deadline=null},
            "reset"=>value with {Phase=0,CompletedSessions=0,Running=false,Deadline=null,RemainingSeconds=value.FocusMinutes*60},
            "skip"=>(value with {Running=true,Deadline=now}).Advance(now),
            _=>throw new ArgumentException("不支持的计时器操作")
        };
    }
}

public static class TimeWidgets
{
    public static bool IsTime(WidgetKind kind)=>kind is WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer;
}
