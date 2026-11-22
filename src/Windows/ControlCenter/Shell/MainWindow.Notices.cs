using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? noticeTimer;
    private void InitializeNotices()
    {
        noticeTimer=DispatcherQueue.CreateTimer();noticeTimer.IsRepeating=false;
        noticeTimer.Tick+=(_,_)=>Notice.IsOpen=false;
        Notice.Closed+=(_,_)=>noticeTimer.Stop();
        Closed+=(_,_)=>noticeTimer.Stop();
    }
    private void ShowNotice(string message,InfoBarSeverity severity,string title="")
    {
        noticeTimer?.Stop();
        Notice.Title=title;Notice.Message=message;Notice.Severity=severity;Notice.IsOpen=true;
        if(noticeTimer==null)return;
        noticeTimer.Interval=TimeSpan.FromSeconds(severity==InfoBarSeverity.Error?8:4);
        noticeTimer.Start();
    }
}
