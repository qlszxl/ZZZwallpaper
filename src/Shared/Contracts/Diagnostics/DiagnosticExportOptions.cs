using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaWall.Diagnostics;

public sealed record DiagnosticExportOptions(bool IncludeLogs=true,bool IncludeEnvironment=true,bool IncludeConfiguration=false)
{
    public bool Any=>IncludeLogs||IncludeEnvironment||IncludeConfiguration;
    public void Validate() {if(!Any)throw new ArgumentException("请至少选择一项诊断内容。");}
}

public static class DiagnosticConfiguration
{
    public const int MaxBytes=8*1024*1024;

    // Call only with typed snapshots: property names must be schema names, not user dictionary keys.
    // Keep numeric/boolean parameters and stable relationships, never user text, paths or commands.
    public static JsonElement Redact(object snapshot)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(snapshot);
        if(bytes.Length>MaxBytes)throw new InvalidDataException("配置快照过大，无法加入诊断包。");
        var root=JsonNode.Parse(bytes)!;var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JsonNode? Clean(JsonNode? node) {
            if(node is JsonObject obj) {
                foreach(string key in obj.Select(p=>p.Key).ToArray()) {var old=obj[key];var clean=Clean(old);if(!ReferenceEquals(old,clean))obj[key]=clean;}
                return obj;
            }
            if(node is JsonArray array) {
                for(int i=0;i<array.Count;i++) {var old=array[i];var clean=Clean(old);if(!ReferenceEquals(old,clean))array[i]=clean;}
                return array;
            }
            if(node is JsonValue value&&value.TryGetValue<string>(out var text)&&text.Length>0) {
                if(!aliases.TryGetValue(text,out string? alias))aliases[text]=alias=$"[text-{aliases.Count+1}]";
                return JsonValue.Create(alias);
            }
            return node;
        }
        return JsonSerializer.SerializeToElement(Clean(root));
    }
}
