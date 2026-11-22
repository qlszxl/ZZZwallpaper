using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool widgetRefreshPending, widgetRefreshScheduled, widgetDrawerCleanupPending;
    private bool focusAddAfterWidgetBack;
    private readonly HashSet<MenuFlyout> openWidgetMenus = [];
    private Popup? waitingWidgetPopup;

    private void RefreshWidgets()
    {
        if (widgets == null || applyingWidgetLive) return;
        widgetRefreshPending = true;
        ScheduleWidgetRefresh();
    }

    private void ScheduleWidgetRefresh()
    {
        if (widgetRefreshScheduled || widgetDrawerClosing || openWidgetMenus.Count != 0 ||
            waitingWidgetPopup != null || WidgetDrawerOverlay.Visibility == Visibility.Visible) return;
        if (!widgetDrawerCleanupPending && (!widgetRefreshPending || navigationPage != "widgets")) return;
        widgetRefreshScheduled = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DrainWidgetRefresh);
    }

    private void DrainWidgetRefresh()
    {
        try
        {
            if (widgetDrawerClosing || WidgetDrawerOverlay.Visibility == Visibility.Visible || openWidgetMenus.Count != 0) return;
            if (Root.XamlRoot is { } root &&
                VisualTreeHelper.GetOpenPopupsForXamlRoot(root).FirstOrDefault(p => p.IsOpen) is { } popup)
            {
                waitingWidgetPopup = popup;
                popup.Closed += WidgetPopupClosed;
                return;
            }
            if (widgetDrawerCleanupPending)
            {
                widgetDrawerCleanupPending = false;
                WidgetDrawerContent.Children.Clear();
            }
            if (widgetRefreshPending && navigationPage == "widgets")
            {
                widgetRefreshPending = false;
                RenderWidgetPage();
                if (focusAddAfterWidgetBack && selectedWidgetId == null && selectedLayoutGroupId == null)
                    WidgetsAddButton.Focus(FocusState.Programmatic);
                focusAddAfterWidgetBack = false;
            }
        }
        finally
        {
            widgetRefreshScheduled = false;
            // Changes delivered during rendering get one more dispatcher turn.
            ScheduleWidgetRefresh();
        }
    }

    private void WidgetPopupClosed(object sender, object args)
    {
        if (waitingWidgetPopup != null) waitingWidgetPopup.Closed -= WidgetPopupClosed;
        waitingWidgetPopup = null;
        ScheduleWidgetRefresh();
    }

    private MenuFlyout CreateWidgetMenu()
    {
        var menu = CreateCardMenu();
        menu.Opening += (_, _) => openWidgetMenus.Add(menu);
        menu.Closed += (_, _) => { openWidgetMenus.Remove(menu); ScheduleWidgetRefresh(); };
        return menu;
    }
}
