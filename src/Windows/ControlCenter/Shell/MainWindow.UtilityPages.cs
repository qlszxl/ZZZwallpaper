using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static ContentControl UtilityCard(UIElement content,string title="",bool hasOwnHeader=false)
    {
        var card=new ContentControl {Style=HomeStyle("UtilityCard"),Content=content};
        SectionChrome.SetTitle(card,title);SectionChrome.SetHasOwnHeader(card,hasOwnHeader);card.Loaded+=DecorateLoadedParameterCard;return card;
    }

    private static TextBlock UtilityText(string text, string style = "UtilityHint") => new() {
        Text = text, Style = HomeStyle(style), TextLineBounds=TextLineBounds.Full
    };

    private static Viewbox UtilityIcon(string icon, DependencyObject source, double size = 20) =>
        MetalUtilityIconIndex(icon) is { } index ? CreateMetalIcon(index,size) :
        icon is "Pause" or "Play" or "Monitor" or "Widgets" or "Home" or "Settings"
            ? CreateHomeVectorIcon(icon, size, source)
            : CreateVectorIcon(CloneIconGeometry("UtilityIcon" + icon), size, source);

    private static void UtilityButtonContent(Button button, string title, string? icon = null, bool iconOnly = false)
    {
        AutomationProperties.SetName(button, title);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) content.Children.Add(UtilityIcon(icon, button));
        if (!iconOnly) content.Children.Add(new TextBlock {
            Text = title, FontFamily = UiFonts.Display, FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Black, TextLineBounds = TextLineBounds.Full, VerticalAlignment = VerticalAlignment.Center
        });
        
        button.Content = content;
    }

    // The repeated dynamic cards use the same width rules as the static page.
    // Handlers are owned by their grids, so removing a refreshed card removes
    // its layout subscription too; the monitoring timer is not involved.
    private static void UtilityAdaptiveGrid(Grid grid, int columns, double threshold)
    {
        grid.SizeChanged += (_, e) => FlowCards(grid, e.NewSize.Width >= threshold ? columns : 1);
        FlowCards(grid, grid.ActualWidth >= threshold ? columns : 1);
    }

    private void InitializeUtilityPages()
    {
        foreach (var root in new FrameworkElement[] { WidgetsHeaderActions, PerformanceHeaderActions, WidgetsPanel, SettingsPanel })
        {
            foreach (var button in EffectsLogicalChildren(root).OfType<Button>())
                if (button.Tag is string tag && tag.StartsWith("utility-icon:", StringComparison.Ordinal))
                    UtilityButtonContent(button, (string)button.Content, tag[13..]);
            foreach (var host in EffectsLogicalChildren(root).OfType<ContentControl>())
                if (host.Tag is string tag && tag.StartsWith("utility-info:", StringComparison.Ordinal))
                {
                    host.Foreground = HomeBrush("HomeControlText");
                    host.Content = UtilityIcon(tag[13..], host, 24);
                }
        }
        WidgetsPanel.SizeChanged += (_, _) => LayoutUtilityPages();
        PerformancePanel.SizeChanged += (_, _) => LayoutUtilityPages();
        SettingsPanel.SizeChanged += (_, _) => LayoutUtilityPages();
        SettingsExitGrid.SizeChanged += (_, e) => StackHeader(SettingsExitGrid, SettingsExitDescription, (FrameworkElement)SettingsExitGrid.Children[1], e.NewSize.Width < 700);
    }

    private void LayoutUtilityPages()
    {
        double available = Root.ActualWidth - (Root.ActualWidth < 1060 ? 142 : 250);
        if (available <= 0) return;
        FlowCards(WidgetsHeaderActions, Math.Max(1,WidgetsHeaderActions.Children.Count));
        foreach(var column in WidgetsHeaderActions.ColumnDefinitions)column.Width=GridLength.Auto;
        WidgetsHeaderActions.ColumnSpacing=10;
        LayoutWidgetCards();
        double settingsWidth = SettingsPanel.ActualWidth > 0 ? SettingsPanel.ActualWidth : available;
        FlowCards(SettingsMenuGrid, settingsWidth < 960 ? 1 : 2);
    }

}
