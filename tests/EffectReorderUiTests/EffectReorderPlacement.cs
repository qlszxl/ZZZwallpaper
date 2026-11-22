namespace LumaWall.ControlCenter;

// Test-only replacement for Shell/MainWindow.Placement.cs. The production
// constructor normally restores its presenter/desktop position; an off-screen
// layout fixture must never run that activation or persistence path.
public sealed partial class MainWindow
{
    private void InitializeWindowPlacement() { }
    private void SaveWindowPlacement() { }
    internal void EnsureWindowPlacementVisible() { }
    internal void ShutdownWindowPlacement() { }
}
