namespace LumaWall.Contracts;

public static class DesktopMenuPreference
{
    public static string Key(string screen)=>"menu.screen."+screen;
    public static bool IsEnabled(string? value)=>value=="true";
}
