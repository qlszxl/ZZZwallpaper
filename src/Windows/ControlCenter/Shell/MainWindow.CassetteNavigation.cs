using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool cassetteReady, cassetteCompact;
    private Flyout? cassetteNavigationFlyout;
    private readonly List<CassetteNavButton> cassetteFlyoutButtons = [];

    private void InitializeCassetteNavigation()
    {
        cassetteReady = true;
        CassetteNavigationBody.SizeChanged += (_, _) => LayoutCassetteHeight();
        Root.Loaded += (_, _) => LayoutCassetteNavigation(Root.ActualWidth);
        if (homeUiSettings != null) homeUiSettings.TextScaleFactorChanged += CassetteTextScaleChanged;
        Closed += (_, _) => {
            cassetteReady = false;
            if (homeUiSettings != null) homeUiSettings.TextScaleFactorChanged -= CassetteTextScaleChanged;
        };
        LayoutCassetteNavigation(Root.ActualWidth);
    }

    private void CassetteTextScaleChanged(UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() => LayoutCassetteNavigation(Root.ActualWidth));

    private void LayoutCassetteNavigation(double windowWidth)
    {
        if (!cassetteReady) return;
        if (windowWidth <= 0) windowWidth = 1280;
        bool compact = windowWidth < 1060;
        double fontSize = windowWidth >= 1280 ? 24 : 21.6;
        double textScale = Math.Max(1, homeUiSettings?.TextScaleFactor ?? 1);
        // Keep the original sidebar widths. Larger text grows the buttons
        // vertically instead of taking more space from the page.
        double width = compact ? 80 : windowWidth >= 1280 ? 224 : 200;
        if (Math.Abs(EffectsNavigationColumn.Width.Value - width) > .1)
            EffectsNavigationColumn.Width = new GridLength(width);
        cassetteCompact = compact;
        CassetteBrandLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CassetteSpineLabel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        CassetteLabelButton.Height = compact ? 44 : 64;
        CassetteLabelButton.IsHitTestVisible = compact;
        CassetteLabelButton.IsTabStop = compact;
        EffectsServiceLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CassetteServiceStatus.Height = compact ? 44 : Math.Max(36, 11 * textScale + 16);
        foreach (var button in EffectsGlobalNavigation.Children.OfType<CassetteNavButton>()) {
            var content = (StackPanel)button.Content;
            content.Spacing = compact ? 0 : 14;
            if (content.Children[0] is FontIcon fontIcon) fontIcon.FontSize = 28.8;
            else if (content.Children[0] is FrameworkElement vector) vector.Width = vector.Height = 28.8;
            var label = (TextBlock)content.Children[1];
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            label.FontSize = fontSize;
            label.MaxWidth = Math.Max(1, width - 110);
            label.TextWrapping = TextWrapping.Wrap;
            label.Measure(new Windows.Foundation.Size(label.MaxWidth, double.PositiveInfinity));
            button.Height = compact ? 56 : Math.Max(64, Math.Ceiling((Math.Max(28.8, label.DesiredSize.Height) + 22) / 4) * 4);
            button.Padding = compact ? new Thickness(0) : new Thickness(14, 0, 14, 0);
            button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
        if (!compact) cassetteNavigationFlyout?.Hide();
        LayoutCassetteHeight();
        UpdateCassetteSelection(navigationPage);
    }

    private void LayoutCassetteHeight()
    {
        if (!cassetteReady) return;
        double available = Math.Max(0, CassetteNavigationBody.ActualHeight);
        // The ScrollViewer's max height is the real remaining grid space,
        // not the outer window height. The footer never joins this scroller.
        if (Math.Abs(ShellNavigationScroll.MaxHeight - available) > .1)
            ShellNavigationScroll.MaxHeight = available;
        var buttons = EffectsGlobalNavigation.Children.OfType<CassetteNavButton>().ToArray();
        double navigationHeight = buttons.Sum(button => button.Height) + Math.Max(0, buttons.Length - 1) * EffectsGlobalNavigation.Spacing;
        CassetteRelief.Visibility = !cassetteCompact && available - navigationHeight >= 52 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCassetteSelection(string page)
    {
        string selected = MainNavigationPage(page);
        foreach (var button in EffectsGlobalNavigation.Children.OfType<CassetteNavButton>())
            button.SetNavigationState((string)button.Tag == selected, cassetteCompact);
        foreach (var button in cassetteFlyoutButtons)
            button.SetNavigationState((string)button.Tag == selected, false);
    }

    private void CassetteLabel_Click(object sender, RoutedEventArgs e)
    {
        if (!cassetteCompact) return;
        EnsureCassetteFlyout();
        UpdateCassetteSelection(navigationPage);
        double textScale = Math.Max(1, homeUiSettings?.TextScaleFactor ?? 1);
        foreach (var button in cassetteFlyoutButtons)
            button.Height = Math.Max(64, Math.Ceiling((21.6 * textScale * 1.5 + 22) / 4) * 4);
        if (cassetteNavigationFlyout!.Content is FrameworkElement content) {
            content.Width = Math.Max(256, Math.Ceiling((21.6 * textScale * 4 + 126) / 4) * 4);
            content.MaxHeight = Math.Max(120, Root.ActualHeight - 80);
        }
        cassetteNavigationFlyout.ShowAt(CassetteLabelButton);
    }

    private void EnsureCassetteFlyout()
    {
        if (cassetteNavigationFlyout != null) return;
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new CassetteSlice { Part = "Label", Height = 58, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var source in EffectsGlobalNavigation.Children.OfType<CassetteNavButton>()) {
            string page = (string)source.Tag;
            string name = ((TextBlock)((StackPanel)source.Content).Children[1]).Text;
            string icon = page switch { "wallpaper" => "Home", "library" => "Library", "display" => "Effects", "widgets" => "Widgets", "presets" => "Presets", _ => "Settings" };
            var button = new CassetteNavButton { Number = source.Number, Tag = page, Style = HomeStyle("CassetteNavButtonStyle") };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            row.Children.Add(CreateHomeVectorIcon(icon, 28.8, button));
            row.Children.Add(new TextBlock { Text = name, FontFamily = UiFonts.Display, FontSize = 21.6,
                FontWeight = Microsoft.UI.Text.FontWeights.Black, FontStyle = Windows.UI.Text.FontStyle.Italic, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { cassetteNavigationFlyout?.Hide(); Navigate(page); };
            cassetteFlyoutButtons.Add(button); stack.Children.Add(button);
        }
        var service = new TextBlock { FontSize = 12, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        service.SetBinding(TextBlock.TextProperty, new Binding { Source = EffectsServiceLabel, Path = new PropertyPath("Text") });
        stack.Children.Add(service);
        var scrolling = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var surface = new Border { Width = 256, Background = HomeBrush("CassetteCaseBrush"), BorderBrush = EffectsBrush(65, 65, 65),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Child = scrolling };
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        cassetteNavigationFlyout = new Flyout { Content = surface, Placement = FlyoutPlacementMode.Right, FlyoutPresenterStyle = style };
    }
}
