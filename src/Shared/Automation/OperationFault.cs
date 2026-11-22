using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaWall.Automation;

// Shared by CLI validation, the bridge and the application. Never classify localized messages.
public sealed class OperationFault(string code,string message,string? field=null,bool retryable=false,string? recovery=null,object? details=null) : ArgumentException(message)
{
    public string Code {get;}=code;
    public string? Field {get;}=field;
    public bool Retryable {get;}=retryable;
    public string? Recovery {get;}=recovery;
    public object? Details {get;}=details;
    public static OperationFault Conflict(string expected,string actual,string readTool)=>new("revision_conflict","配置已变化，请重新读取状态后提交。","arguments.expectedRevision",false,"reread_state",new {expectedRevision=expected,currentRevision=actual,readTool});
}

public static class OperationErrors
{
    public static OperationReply Normalize(OperationReply reply) {
        if(!reply.IsError)return reply;
        var node=JsonNode.Parse(reply.Data.GetRawText())!.AsObject();
        node["error"]??=JsonValue.Create("操作未完成，请读取状态或操作记录。");
        node["errorCode"]??=JsonValue.Create(node["renderConfirmed"]?.GetValue<bool>()==false?"render_not_confirmed":"operation_failed");
        node["retryable"]??=JsonValue.Create(false);
        if(!node.ContainsKey("field"))node["field"]=null;
        if(!node.ContainsKey("details"))node["details"]=null;
        if(!node.ContainsKey("recovery"))node["recovery"]="reread_state";
        return reply with {Data=JsonSerializer.SerializeToElement(node)};
    }
    public static OperationReply FromException(Exception error)=>error switch {
        LumaWall.Contracts.Application.ConfigurationConflictException conflict=>FromException(OperationFault.Conflict(conflict.ExpectedRevision,conflict.CurrentRevision,conflict.ReadContext)),
        OperationFault fault=>Create(fault.Message,fault.Code,fault.Field,fault.Retryable,fault.Recovery,fault.Details),
        JsonException=>Create("JSON 参数格式无效。","invalid_json","arguments"),
        ArgumentException=>Create(error.Message,"invalid_argument"),
        UnauthorizedAccessException=>Create(error.Message,"permission_denied"),
        OperationCanceledException=>Create("操作取消或超时，请查询操作结果和当前状态。","cancelled_state_requires_read",recovery:"query_operation"),
        TimeoutException=>Create(error.Message,"timeout",recovery:"query_operation"),
        IOException=>Create(error.Message,"io_error",recovery:"reread_state"),
        _=>Create(error.Message,"operation_failed",recovery:"reread_state")
    };
    public static OperationReply Create(string message,string code="operation_failed",string? field=null,bool retryable=false,string? recovery=null,object? details=null)=>
        new(true,ToolCatalog.Json(new {error=message,errorCode=code,field,retryable,recovery,details}));
}
