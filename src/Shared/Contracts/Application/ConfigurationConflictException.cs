namespace LumaWall.Contracts.Application;

/// <summary>Transport-independent optimistic concurrency failure. Adapters choose their error envelope.</summary>
public sealed class ConfigurationConflictException(string expectedRevision,string currentRevision,string readContext) : ArgumentException("配置已变化，请重新读取状态后提交。")
{
    public string ExpectedRevision {get;}=expectedRevision;
    public string CurrentRevision {get;}=currentRevision;
    public string ReadContext {get;}=readContext;
}
