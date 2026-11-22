using System.Runtime.InteropServices;

namespace LumaWall.DesktopStyle;

// The fonts are registered for this process only; nothing is installed in Windows.
// Keep them available for the lifetime of the UI thread and its GDI controls.
public static class DesktopTheme
{
    public static readonly Color Accent = Color.FromArgb(229, 245, 0);
    public static readonly Color Surface = Color.FromArgb(19, 19, 19);
    public static readonly Color Border = Color.FromArgb(63, 63, 63);
    public static readonly Font Body;
    public static readonly Font Heavy;
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string path, uint flags, nint reserved);
    static DesktopTheme()
    {
        foreach (var weight in new[] { "Heavy", "Medium" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", $"SourceHanSansSC-{weight}.otf");
            if (File.Exists(path)) AddFontResourceEx(path, 0x10, 0);
        }
        Body = new Font("Source Han Sans SC Medium", 9, FontStyle.Regular);
        Heavy = new Font("Source Han Sans SC Heavy", 10, FontStyle.Regular);
    }
    public static ContextMenuStrip Menu() => new() {
        Font = Heavy, BackColor = Surface, ForeColor = Color.White,
        Renderer = new MenuRenderer(), ShowImageMargin = false, Padding = new Padding(6)
    };
    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Color.Gray : e.Item.Selected ? Color.Black : Color.White;
            base.OnRenderItemText(e);
        }
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => Accent;
        public override Color MenuItemSelectedGradientBegin => Accent;
        public override Color MenuItemSelectedGradientEnd => Accent;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
