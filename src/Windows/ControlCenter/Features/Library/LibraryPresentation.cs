using System.Globalization;

namespace LumaWall.ControlCenter;

internal static class LibraryPresentation
{
    internal const double MinimumCardWidth = 240;
    internal const double CardSpacing = 12;
    internal const int WorkspaceColumns = 4;
    internal const double CardScrollbarReserve = 20;
    // Preserve enough width for both a card and the equal-width information
    // panel. Narrower windows use 1–3 columns with details below the list.
    private const double FourColumnViewportWidth = WorkspaceColumns * MinimumCardWidth + (WorkspaceColumns - 1) * CardSpacing;

    // Always calculate from the full workspace, even while the information
    // panel occupies its last column. Selection must never change card width.
    internal static (int Columns, double CardWidth) ResponsiveGrid(double availableWidth, int previousColumns = 0)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return (1, 1);
        int columns = availableWidth >= FourColumnViewportWidth
            ? WorkspaceColumns
            : (int)Math.Clamp(Math.Floor((availableWidth + CardSpacing) / (MinimumCardWidth + CardSpacing)), 1, WorkspaceColumns - 1);
        // There is no wide-window maximum: the page remains four equal slots
        // instead of adding more columns as the window grows.
        double cardWidth = (availableWidth + CardSpacing) / columns - CardSpacing;
        return (columns, Math.Max(1, Math.Floor(cardWidth)));
    }

    internal static (int Columns, double ItemWidth) Grid(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return (1, 1);
        // The target picker has at most three equal cells. The caller includes
        // the final container margin so the visible cards fill the viewport.
        int columns = (int)Math.Clamp(Math.Floor(availableWidth / (MinimumCardWidth + CardSpacing)), 1, 3);
        return (columns, Math.Max(1, Math.Floor(availableWidth / columns)));
    }

    internal static string FileSize(long bytes)
    {
        if (bytes < 0) return "—";
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        // Promote a rounded boundary rather than displaying 1024.0 KiB.
        if (unit > 0 && Math.Round(value, 1) >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
