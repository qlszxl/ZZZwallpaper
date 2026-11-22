using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumaWall.Automation;

public static partial class ToolCatalog
{
    public static void ValidateValue(JsonElement value,JsonElement schema,string path)
    {
        if(schema.ValueKind==JsonValueKind.True)return;
        if(schema.ValueKind==JsonValueKind.False)throw new OperationFault("invalid_combination",path+" 不允许此参数组合",path);
        if(schema.TryGetProperty("type",out var type)) {
            var types=type.ValueKind==JsonValueKind.Array?type.EnumerateArray().Select(t=>t.GetString()).ToArray():[type.GetString()];
            bool matches=types.Any(t=>t switch {"object"=>value.ValueKind==JsonValueKind.Object,"array"=>value.ValueKind==JsonValueKind.Array,"string"=>value.ValueKind==JsonValueKind.String,"boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,"null"=>value.ValueKind==JsonValueKind.Null,"integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out _),"number"=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var d)&&double.IsFinite(d),_=>false});
            if(!matches)throw new OperationFault("invalid_type",path+" 类型无效",path);
        }
        if(value.ValueKind==JsonValueKind.Object) {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            bool hasProperties=schema.TryGetProperty("properties",out var properties);
            bool rejectExtra=schema.TryGetProperty("additionalProperties",out var extra)&&extra.ValueKind==JsonValueKind.False;
            foreach(var p in value.EnumerateObject()) {
                string field=path+"."+p.Name;
                if(!seen.Add(p.Name))throw new OperationFault("duplicate_field",field+" 字段重复",field);
                if(hasProperties&&properties.TryGetProperty(p.Name,out var rule))ValidateValue(p.Value,rule,field);
                else if(rejectExtra)throw new OperationFault("unknown_field",field+" 字段不受支持",field);
            }
            if(schema.TryGetProperty("required",out var required))foreach(var p in required.EnumerateArray())if(!seen.Contains(p.GetString()!))
                throw new OperationFault("missing_required_field",path+" 缺少字段："+p.GetString(),path+"."+p.GetString());
        }
        if(value.ValueKind==JsonValueKind.String) {
            string s=value.GetString()!;
            if(schema.TryGetProperty("minLength",out var min)&&s.Length<min.GetInt32()||schema.TryGetProperty("maxLength",out var max)&&s.Length>max.GetInt32())throw new OperationFault("invalid_length",path+" 文本长度无效",path);
            if(schema.TryGetProperty("pattern",out var pattern)&&!Regex.IsMatch(s,pattern.GetString()!,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new OperationFault("invalid_format",path+" 文本格式无效",path);
        }
        if(value.ValueKind==JsonValueKind.Number) {
            if(!value.TryGetDouble(out double n)||!double.IsFinite(n))throw new OperationFault("invalid_type",path+" 必须为有限数值",path);
            if(schema.TryGetProperty("minimum",out var min)&&n<min.GetDouble()||schema.TryGetProperty("maximum",out var max)&&n>max.GetDouble())throw new OperationFault("out_of_range",path+" 超出允许范围",path);
        }
        if(value.ValueKind==JsonValueKind.Array) {
            if(schema.TryGetProperty("minItems",out var min)&&value.GetArrayLength()<min.GetInt32()||schema.TryGetProperty("maxItems",out var max)&&value.GetArrayLength()>max.GetInt32())throw new OperationFault("invalid_length",path+" 数组长度无效",path);
            if(schema.TryGetProperty("uniqueItems",out var unique)&&unique.GetBoolean()&&value.EnumerateArray().Select(v=>v.GetRawText()).Distinct().Count()!=value.GetArrayLength())throw new OperationFault("duplicate_item",path+" 包含重复项",path);
            if(schema.TryGetProperty("items",out var itemRule)) {int i=0;foreach(var item in value.EnumerateArray())ValidateValue(item,itemRule,path+"["+(i++)+"]");}
        }
        if(schema.TryGetProperty("enum",out var choices)&&!choices.EnumerateArray().Any(c=>c.GetRawText()==value.GetRawText()))throw new OperationFault("invalid_enum",path+" 选项无效",path);
        if(schema.TryGetProperty("const",out var constant)&&constant.GetRawText()!=value.GetRawText())throw new OperationFault("invalid_combination",path+" 值与条件不符",path);
        if(schema.TryGetProperty("allOf",out var all))foreach(var rule in all.EnumerateArray())ValidateValue(value,rule,path);
        if(schema.TryGetProperty("anyOf",out var any)&&!any.EnumerateArray().Any(r=>Matches(value,r,path)))throw new OperationFault("invalid_combination",path+" 至少需要一个允许的参数组合",path);
        if(schema.TryGetProperty("oneOf",out var one)&&one.EnumerateArray().Count(r=>Matches(value,r,path))!=1)throw new OperationFault("invalid_combination",path+" 必须且只能选择一个参数组合",path);
        if(schema.TryGetProperty("not",out var not)&&Matches(value,not,path))throw new OperationFault("invalid_combination",path+" 含互斥或无关字段",path);
        if(schema.TryGetProperty("if",out var condition)) {
            string branch=Matches(value,condition,path)?"then":"else";
            if(schema.TryGetProperty(branch,out var rule))ValidateValue(value,rule,path);
        }
    }
    private static bool Matches(JsonElement value,JsonElement schema,string path) {try {ValidateValue(value,schema,path);return true;}catch(OperationFault) {return false;}}
}
