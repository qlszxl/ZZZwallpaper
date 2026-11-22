using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record EffectsParameterRow(Grid Grid, FrameworkElement Label, Slider Slider, FrameworkElement Value);
    private readonly List<EffectsParameterRow> effectsParameterRows = new();
    private readonly Dictionary<Grid, bool> effectsRowStacked = new();
    private readonly List<(Grid Grid, TextBlock Label, ComboBox Picker)> effectsChoiceRows = new();
    private readonly Dictionary<Grid, bool> effectsChoiceStacked = new();
    private readonly List<TextBlock> effectsParameterHints = new();
    private bool effectsLayoutInProgress;

    private static IEnumerable<FrameworkElement> EffectsLogicalChildren(FrameworkElement element)
    {
        IEnumerable<FrameworkElement> children = element switch {
            Panel panel => panel.Children.OfType<FrameworkElement>(),
            Border border when border.Child is FrameworkElement child => [child],
            ScrollViewer viewer when viewer.Content is FrameworkElement child => [child],
            ContentControl control => new[] {SectionChrome.GetHeader(control) as FrameworkElement,control.Content as FrameworkElement}.OfType<FrameworkElement>(),
            UserControl control when control.Content is FrameworkElement child => [child],
            _ => []
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in EffectsLogicalChildren(child)) yield return descendant;
        }
    }

    private void InitializeEffectsLayout()
    {
        foreach (var grid in EffectsLogicalChildren(EffectsParameterEditor).OfType<Grid>().ToArray())
        {
            if (grid.Children.OfType<ComboBox>().FirstOrDefault() is { } picker &&
                grid.Children.OfType<TextBlock>().FirstOrDefault() is { } choiceLabel)
            {
                var choiceRow = (Grid: grid, Label: choiceLabel, Picker: picker);
                effectsChoiceRows.Add(choiceRow);
                grid.SizeChanged += (_, e) => LayoutEffectsChoiceRow(choiceRow, e.NewSize.Width);
            }
            var slider = grid.Children.OfType<Slider>().FirstOrDefault();
            var label = grid.Children.OfType<TextBlock>().FirstOrDefault();
            FrameworkElement? value = grid.Children.OfType<EffectValueInput>().FirstOrDefault();
            if (value == null && slider != null && grid.Children.OfType<ContentControl>().FirstOrDefault(c =>
                c.Tag is string tag && tag.StartsWith("effect-value:", StringComparison.Ordinal)) is { } slot)
            {
                string[] format = ((string)slot.Tag).Split(':', 3);
                slot.Content = new EffectValueInput { Target = slider, Precision = int.Parse(format[1]), Unit = UiLanguage.Text(format[2]) };
                value = slot;
            }
            if (slider == null || label == null || value == null || grid.ColumnDefinitions.Count != 3) continue;
            var row = new EffectsParameterRow(grid, label, slider, value);
            effectsParameterRows.Add(row);
            grid.SizeChanged += (_, e) => LayoutEffectsParameterRow(row, e.NewSize.Width);
        }
        foreach (var hint in EffectsLogicalChildren(EffectsParameterEditor).OfType<TextBlock>()
            .Where(text => text.Tag as string == "effect-parameter-hint"))
        {
            effectsParameterHints.Add(hint);
            if (hint.Parent is FrameworkElement parent)
                parent.SizeChanged += (_, e) => LayoutEffectsHint(hint, e.NewSize.Width);
        }
        EffectsAudioMeters.SizeChanged += (_, e) => LayoutEffectsAudioMeters(e.NewSize.Width);
        AudioSourcePicker.SizeChanged += (_, e) =>
            AudioSourcePicker.MaxColumns = e.NewSize.Width < 720 ? 1 : 2;
        DisplayPanel.SizeChanged += (_, _) => LayoutEffectsPage();
        EffectsParameterScroll.SizeChanged += (_, _) => LayoutEffectsPage();
    }

    private static void LayoutEffectsHint(TextBlock hint, double width)
    {
        if (width <= 0) return;
        var margin = new Thickness(width < 600 ? 0 : 144, 0, 0, 4);
        if (hint.Margin != margin) hint.Margin = margin;
    }

    private void LayoutEffectsChoiceRow((Grid Grid, TextBlock Label, ComboBox Picker) row, double width)
    {
        if (width <= 0) return;
        bool stacked = width < 600;
        if (effectsChoiceStacked.TryGetValue(row.Grid, out bool previous) && previous == stacked) return;
        effectsChoiceStacked[row.Grid] = stacked;
        row.Grid.ColumnDefinitions.Clear(); row.Grid.RowDefinitions.Clear();
        row.Grid.ColumnDefinitions.Add(new() { Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(168) });
        if (!stacked)
        {
            row.Grid.ColumnDefinitions.Add(new());
            row.Grid.ColumnDefinitions.Add(new() { Width = new GridLength(198) });
        }
        row.Grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (stacked) row.Grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Grid.RowSpacing = stacked ? 6 : 0;
        Grid.SetRow(ParameterInputGate.Host(row.Picker), stacked ? 1 : 0); Grid.SetColumn(ParameterInputGate.Host(row.Picker), stacked ? 0 : 1);
    }

    private void LayoutEffectsAudioMeters(double width)
    {
        if (width <= 0) return;
        bool stacked = width < 520;
        int columns = stacked ? 1 : 3;
        if (EffectsAudioMeters.ColumnDefinitions.Count == columns) return;
        EffectsAudioMeters.ColumnDefinitions.Clear(); EffectsAudioMeters.RowDefinitions.Clear();
        EffectsAudioMeters.ColumnDefinitions.Add(new());
        if (!stacked)
        {
            EffectsAudioMeters.ColumnDefinitions.Add(new());
            EffectsAudioMeters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }
        foreach (var item in EffectsAudioMeters.Children.OfType<FrameworkElement>().Select((element, index) => (element, index)))
        {
            if (stacked) EffectsAudioMeters.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(item.element, stacked ? item.index : 0); Grid.SetColumn(item.element, stacked ? 0 : item.index);
        }
        EffectsAudioMeters.RowSpacing = stacked ? 12 : 0;
    }

    private void LayoutEffectsParameterRow(EffectsParameterRow row, double width)
    {
        if (width <= 0) return;
        bool stacked = width < 600;
        if (effectsRowStacked.TryGetValue(row.Grid, out bool previous) && previous == stacked) return;
        effectsRowStacked[row.Grid] = stacked;
        var grid = row.Grid;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Add(new() { Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(168) });
        if (!stacked) grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(198) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (stacked) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowSpacing = stacked ? 6 : 0;
        Grid.SetRow(ParameterLabelHost(row.Label), 0); Grid.SetColumn(ParameterLabelHost(row.Label), 0);
        Grid.SetRow(ParameterInputGate.Host(row.Value), 0); Grid.SetColumn(ParameterInputGate.Host(row.Value), stacked ? 1 : 2);
        Grid.SetRow(ParameterInputGate.Host(row.Slider), stacked ? 1 : 0); Grid.SetColumn(ParameterInputGate.Host(row.Slider), stacked ? 0 : 1);
        Grid.SetColumnSpan(ParameterInputGate.Host(row.Slider), stacked ? 2 : 1);
    }

    private void LayoutEffectsPage()
    {
        if (!effectsPageReady || effectsLayoutInProgress || DisplayPanel.Visibility != Visibility.Visible || DisplayPanel.ActualWidth <= 0) return;
        effectsLayoutInProgress = true;
        try
        {
            double width = DisplayPanel.ActualWidth;
            EffectsCopyButton.Content=width<700?UiLanguage.Text("迁移效果"):UiLanguage.Text("从显示器迁移效果");
            var unbounded=new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity);
            EffectsCollectionHeading.Measure(unbounded);EffectsCollectionActions.Measure(unbounded);
            StackHeader(EffectsCollectionHeader,EffectsCollectionHeading,EffectsCollectionActions,
                EffectsCollectionHeading.DesiredSize.Width+EffectsCollectionActions.DesiredSize.Width+24>EffectsParameterEditor.ActualWidth);
            LayoutPageHeader(PageHeader.ActualWidth);
            FlowCards(EffectsListPanel,EffectsListPanel.Children.Count<=1?1:width>=1040?3:width>=620?2:1);
            FlowCards(effectAuxiliaryPanel,effectAuxiliaryPanel.Children.Count<=1?1:width>=1040?3:width>=620?2:1);

            foreach (var row in effectsParameterRows)
                LayoutEffectsParameterRow(row, row.Grid.ActualWidth);
            foreach (var row in effectsChoiceRows)
                LayoutEffectsChoiceRow(row, row.Grid.ActualWidth);
            foreach (var hint in effectsParameterHints)
                if (hint.Parent is FrameworkElement parent) LayoutEffectsHint(hint, parent.ActualWidth);
            LayoutEffectsAudioMeters(EffectsAudioMeters.ActualWidth);
        }
        finally { effectsLayoutInProgress = false; }
    }
}
