using System.Text.Json;
using LumaWall.Diagnostics;
using System.Text.Json.Nodes;

namespace LumaWall.Automation;

// Bounded and instance-local. Contains business replies, never source/binary contents.
public sealed class OperationJournal
{
    private readonly Dictionary<string,OperationContext> contexts=new();
    public OperationContext? Context(string id)=>contexts.GetValueOrDefault(id);
    public string InstanceId {get;}=Guid.NewGuid().ToString("N");
    private readonly Dictionary<string,(string Signature,string Id)> keys=new();
    private readonly Dictionary<string,JsonElement> operations=new();
    public string Begin(OperationRequest request,out OperationReply? replay)
    {
        replay=null;string? key=request.Arguments.TryGetProperty("idempotencyKey",out var k)?k.GetString():null;
        string signature=request.Name+":"+Canonical(request.Arguments);
        if(key!=null&&keys.TryGetValue(key,out var known)) {
            if(known.Signature!=signature)throw new OperationFault("idempotency_conflict","idempotencyKey 已被不同参数使用","arguments.idempotencyKey");
            var previous=operations[known.Id];
            replay=previous.TryGetProperty("reply",out var reply)?new(previous.GetProperty("isError").GetBoolean(),reply.Clone()):OperationReply.Success(new {operationId=known.Id,instanceId=InstanceId,status="running"});
            return known.Id;
        }
        if(operations.Count>=256) {
            var oldest=operations.FirstOrDefault(p=>p.Value.GetProperty("status").GetString()!="running");
            if(oldest.Key==null)throw new InvalidOperationException("操作记录容量已满，请等待正在执行的任务");
            operations.Remove(oldest.Key);contexts.Remove(oldest.Key);foreach(var oldKey in keys.Where(p=>p.Value.Id==oldest.Key).Select(p=>p.Key).ToArray())keys.Remove(oldKey);
        }
        string page=request.Name.StartsWith("effect_")||request.Name.StartsWith("audio_")?"display":request.Name.StartsWith("media_")?"library":request.Name.StartsWith("widget_")?"widgets":request.Name.StartsWith("wallpaper_")?"wallpaper":request.Name.StartsWith("preset_")||request.Name.StartsWith("config_")?"presets":"settings";
        if(request.Name=="ui_navigate"&&request.Arguments.TryGetProperty("page",out var destination)&&destination.ValueKind==JsonValueKind.String)
            page=destination.GetString()!;
        var trace=OperationTrace.Begin(page,request.Name,source:"mcp");string id=trace.Id;contexts[id]=trace;
        operations[id]=ToolCatalog.Json(new {operationId=id,instanceId=InstanceId,tool=request.Name,startedAt=DateTimeOffset.UtcNow,status="running"});
        if(key!=null)keys[key]=(signature,id);return id;
    }
    public OperationReply Complete(string id,OperationReply reply,string? savedRevision)
    {
        reply=OperationErrors.Normalize(reply);
        var data=JsonNode.Parse(reply.Data.GetRawText())!.AsObject();
        data["operationId"]=id;data["instanceId"]=InstanceId;
        data["configurationRevision"]=savedRevision;
        // A revision read after an error is only the observed state, not proof of persistence.
        if(data["saved"]?.GetValue<bool>()==true)data["savedRevision"]=savedRevision;
        if(data["status"]==null)data["status"]=reply.IsError?(data["saved"]?.GetValue<bool>()==true?"saved_application_failed":"failed"):data["jobId"]!=null?"running":"completed";
        if(data["renderConfirmed"]==null)data["renderConfirmed"]=null;
        var result=new OperationReply(reply.IsError,JsonSerializer.SerializeToElement(data));
        var record=JsonNode.Parse(operations[id].GetRawText())!.AsObject();
        record["finishedAt"]=data["status"]?.GetValue<string>() is "running" or "queued" or "cancelling"?null:JsonValue.Create(DateTimeOffset.UtcNow);record["status"]=data["status"]!.DeepClone();record["isError"]=reply.IsError;record["reply"]=data;
        operations[id]=JsonSerializer.SerializeToElement(record);OperationTrace.Record(Context(id),"automation_reply",data["status"]?.GetValue<string>()??"unknown");return result;
    }
    public JsonElement Read(string id)=>operations.TryGetValue(id,out var result)?result:throw new ArgumentException("操作不存在或已过期；记录最多保留当前实例最近 256 项");
    public JsonElement[] List()=>operations.Values.ToArray();
    private static string Canonical(JsonElement value)=>value.ValueKind switch {
        JsonValueKind.Object=>"{"+string.Join(",",value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value)))+"}",
        JsonValueKind.Array=>"["+string.Join(",",value.EnumerateArray().Select(Canonical))+"]",_=>value.GetRawText()
    };
}
