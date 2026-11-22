using System.Text.Json;

namespace LumaWall.Automation;

public static class ConnectionPrompt
{
    public static string Configuration(string executable)=>JsonSerializer.Serialize(new {
        mcpServers=new Dictionary<string,object>{{"zzzwallpaper",new {command=executable,args=Array.Empty<string>()}}}
    },new JsonSerializerOptions {WriteIndented=true});

    public static string Create(string executable)
    {
        // PowerShell single-quoted literals preserve $, backticks and spaces; double embedded apostrophes.
        string command="& '"+executable.Replace("'","''")+"'";
        return $$"""
请连接我本机的 ZZZwallpaper，MCP 开关已开启。优先复用 zzzwallpaper MCP，调用 app_get_state；当前会话未加载时，在同一 Windows 电脑、用户和登录会话下执行以下 PowerShell 命令：
{{command}} --agent-connect

仅在调用成功且返回 mcpEnabled=true 后报告已连接。读取服务或返回结果中的 instructions；需要工具定义时，用同一程序的 --agent-tools。连接验证只读取状态，其他操作按我的要求执行，不修改源码或配置文件。没有本机工具或连接失败时如实说明，不开放公网接口。
""";
    }
}
