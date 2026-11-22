namespace LumaWall.ControlCenter;
// Test-only replacement: isolated UI tests must never modify the user's Run key.
internal static class StartupRegistration
{
    internal static bool Enabled;
    internal static int Delay;
    internal static bool IsEnabled()=>Enabled;
    internal static void MigrateLegacyName() { }
    internal static void SetEnabled(bool enabled,int delaySeconds=0) {
        if(delaySeconds is <0 or >3600)throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        Enabled=enabled;Delay=delaySeconds;
    }
}
