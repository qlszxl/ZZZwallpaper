using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

// Owns the library's visual lifetime. Playback and import remain application
// services; hiding the page only suspends its presentation work.
public sealed partial class LibraryPage : UserControl
{
    internal bool IsActive { get; private set; }
    internal bool NeedsRefresh { get; set; } = true;
    internal string? PendingSelection { get; set; }
    internal event Action<bool>? ActiveChanged;

    public LibraryPage() => InitializeComponent();

    internal void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        LibraryList.SetPageActive(active);
        if (LibraryList.ItemsSource is IEnumerable<LibraryRow> rows)
            foreach (var row in rows) row.SetActive(active);
        Visit(this);
        ActiveChanged?.Invoke(active);
        if (!active) LibraryInfoPoster.Source = null;

        void Visit(DependencyObject node)
        {
            if (node is LibraryPosterImage image) image.SetActive(active);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Visit(VisualTreeHelper.GetChild(node, i));
        }
    }
}
