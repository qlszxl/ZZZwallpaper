namespace LumaWall.Automation;

public static class AgentGuidance
{
    public const int Version=51;
    private static readonly Lazy<string> instructions=new(()=> {
        using var stream=typeof(AgentGuidance).Assembly.GetManifestResourceStream("LumaWall.Automation.AgentGuide.md")
            ??throw new InvalidOperationException("缺少内置 Agent 操作指南，请使用完整构建。");
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    });
    public static string Instructions=>instructions.Value;
}
