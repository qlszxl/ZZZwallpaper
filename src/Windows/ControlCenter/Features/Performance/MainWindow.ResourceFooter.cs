using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool? footerResourcesCompact;

    private void FooterResourceMetrics_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FooterWriteMetric == null || e.NewSize.Width <= 0) return;
        bool compact = e.NewSize.Width < 720;
        if (footerResourcesCompact == compact) return;
        footerResourcesCompact = compact;
        int columns = compact ? 3 : 5;
        FooterResourceMetrics.ColumnDefinitions.Clear();
        for (int i = 0; i < columns; i++) FooterResourceMetrics.ColumnDefinitions.Add(new ColumnDefinition());
        FooterResourceMetrics.RowSpacing = compact ? 6 : 0;
        var metrics = new FrameworkElement[] { FooterCpuMetric, FooterGpuMetric, FooterMemoryMetric, FooterReadMetric, FooterWriteMetric };
        for (int i = 0; i < metrics.Length; i++)
        {
            Grid.SetColumn(metrics[i], i % columns);
            Grid.SetRow(metrics[i], i / columns);
            Grid.SetColumnSpan(metrics[i], compact && i == 4 ? 2 : 1);
        }
    }

    private void ResetResourceFooter()
    {
        FooterCpuText.Text = FooterGpuText.Text = FooterMemoryText.Text = FooterReadText.Text = FooterWriteText.Text = "—";
    }

    private void UpdateResourceFooter(IReadOnlyList<ProcessPerformanceSample> samples, double? gpu)
    {
        if (performanceTotals is not { } totals || samples.Count == 0) { ResetResourceFooter(); return; }
        // Do not present an incomplete process sum as the application's total.
        FooterCpuText.Text = samples.All(s => s.CpuPercent.HasValue) ? Percent(totals.Cpu) : "—";
        FooterGpuText.Text = Percent(gpu);
        FooterMemoryText.Text = samples.All(s => s.WorkingSetBytes.HasValue) ? Bytes(totals.Memory) : "—";
        FooterReadText.Text = samples.All(s => s.ReadBytesPerSecond.HasValue) ? Rate(totals.Read) : "—";
        FooterWriteText.Text = samples.All(s => s.WriteBytesPerSecond.HasValue) ? Rate(totals.Write) : "—";
    }
}
