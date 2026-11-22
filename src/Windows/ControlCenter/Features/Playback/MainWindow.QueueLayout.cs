using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool? queueFooterCompact;

    private void QueueWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if(QueueTransport == null || e.NewSize.Width <= 0) return;
        bool compact = e.NewSize.Width < 620;
        if(queueFooterCompact == compact) return;
        queueFooterCompact = compact;
        Grid.SetColumnSpan(QueueNowPlaying, compact ? 2 : 1);
        Grid.SetRow(QueueTransport, compact ? 1 : 0);
        Grid.SetColumn(QueueTransport, compact ? 0 : 1);
        Grid.SetColumnSpan(QueueTransport, compact ? 2 : 1);
        QueueTransport.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        QueueFooter.RowSpacing = compact ? 8 : 0;
        Grid.SetColumnSpan(PlaybackQueueSummary, compact ? 2 : 1);
        Grid.SetRow(PlaybackCustomActions, compact ? 1 : 0);
        Grid.SetColumn(PlaybackCustomActions, compact ? 0 : 1);
        Grid.SetColumnSpan(PlaybackCustomActions, compact ? 2 : 1);
        PlaybackCustomActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }
}
