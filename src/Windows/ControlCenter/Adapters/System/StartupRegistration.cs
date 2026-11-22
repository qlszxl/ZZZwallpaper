using LumaWall.Contracts;
using Microsoft.Win32;

namespace LumaWall.ControlCenter;

internal static class StartupRegistration
{
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Name="ZZZwallpaper";
    private static readonly string[] LegacyNames=["ZZZwallpaper.Modular","LumaWall.Modular","LumaWall"];
    internal static bool IsEnabled() {using var key=Registry.CurrentUser.OpenSubKey(Key);return Enabled(key,Name)||LegacyNames.Any(name=>Enabled(key,name));}
    private static bool Enabled(RegistryKey? key,string name)=>key?.GetValue(name) is string value&&!string.IsNullOrWhiteSpace(value);
    internal static void MigrateLegacyName()
    {
        using var key=Registry.CurrentUser.OpenSubKey(Key);
        // Keep the existing opt-in. A disabled installation stays disabled.
        string? source=new[]{Name}.Concat(LegacyNames).FirstOrDefault(name=>Enabled(key,name));
        if(source==null)return;
        // Windows can disable a Run item independently of our toggle. Preserve
        // that approval record before changing its name, or renaming would
        // accidentally re-enable an item disabled in Task Manager.
        using(var approval=Registry.CurrentUser.OpenSubKey(ApprovalKey))
        {
            if(approval?.GetValue(Name)==null&&approval?.GetValue(source) is byte[] state)
            {
                using var target=Registry.CurrentUser.CreateSubKey(ApprovalKey,true)??throw new IOException(UiLanguage.Text("无法保留 Windows 启动项状态。"));
                target.SetValue(Name,state,RegistryValueKind.Binary);
            }
        }
        int delay=0;
        if(key?.GetValue(source) is string command) {
            var match=System.Text.RegularExpressions.Regex.Match(command,@"--startup-delay\s+(\d+)\b");
            if(match.Success&&int.TryParse(match.Groups[1].Value,out int parsed)&&parsed is >=1 and <=3600)delay=parsed;
        }
        SetEnabled(true,delay);
    }
    internal static void SetEnabled(bool enabled,int delaySeconds=0)
    {
        if(delaySeconds is <0 or >3600)throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        using var key=Registry.CurrentUser.CreateSubKey(Key,true)??throw new IOException(UiLanguage.Text("无法打开当前用户的启动设置。"));
        if(!enabled) {key.DeleteValue(Name,false);foreach(string legacy in LegacyNames)key.DeleteValue(legacy,false);return;}
        string launcher=Path.Combine(AppContext.BaseDirectory,"ZZZwallpaper.Launcher.exe");
        if(!File.Exists(launcher))throw new FileNotFoundException(UiLanguage.Text("启动器不存在，请完整解压发布包。"),launcher);
        string command='"'+launcher+"\" --startup"+(delaySeconds>0?$" --startup-delay {delaySeconds}":"");
        if(command.Length>260)throw new IOException(UiLanguage.Text("启动路径过长，请将软件移动到较短的目录后重试。"));
        if(!string.Equals(key.GetValue(Name) as string,command,StringComparison.Ordinal))key.SetValue(Name,command,RegistryValueKind.String);
        foreach(string legacy in LegacyNames)key.DeleteValue(legacy,false);
    }
}
