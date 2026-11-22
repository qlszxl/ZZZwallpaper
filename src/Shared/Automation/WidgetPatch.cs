using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Contracts;

namespace LumaWall.Automation;

// Reuse the same bounded options and validation as the component editor.
// Images and executable/folder shortcuts remain outside this operation surface.
public static class WidgetPatch
{
    public static WidgetInstance ApplyLocks(WidgetInstance widget,JsonElement args)
    {
        bool position=args.TryGetProperty("locked",out var locked),interaction=args.TryGetProperty("interactionLocked",out var interactionLocked);
        if(!position&&!interaction)throw new ArgumentException("请指定 locked 或 interactionLocked");
        return widget with {Locked=position?locked.GetBoolean():widget.Locked,InteractionLocked=interaction?interactionLocked.GetBoolean():widget.InteractionLocked};
    }
    public static bool CanUpdate(WidgetConfiguration state,WidgetInstance widget,JsonElement args)
    {
        bool action=args.TryGetProperty("timerAction",out _);
        if(action&&!WidgetInteraction.CanInteract(state,widget))return false;
        if(action&&widget.Kind==WidgetKind.FocusTimer&&args.EnumerateObject().All(p=>p.Name is "widgetId" or "expectedRevision" or "timerAction"))return true;
        return WidgetInteraction.CanEdit(state,widget)&&(widget.LayoutGroupId==null||args.EnumerateObject().All(p=>p.Name is "widgetId" or "expectedRevision" or "idempotencyKey" or "alwaysOnTop"));
    }
    public static readonly IReadOnlyDictionary<string,JsonElement> Schemas=LoadSchemas();
    public static JsonElement InputSchema(string group,bool create)
    {
        var node=JsonNode.Parse(Schemas[group].GetRawText())!;
        void StripDefaults(JsonNode? item) {
            if(item is JsonObject obj) {obj.Remove("default");foreach(var child in obj.ToArray())StripDefaults(child.Value);}
            else if(item is JsonArray array)foreach(var child in array)StripDefaults(child);
        }
        // Storage defaults migrate legacy documents; patch omissions must never reset fields.
        StripDefaults(node);
        if(create&&group=="Clock")node["properties"]!["ShowSeconds"]!["default"]=false;
        return JsonSerializer.SerializeToElement(node);
    }
    private static IReadOnlyDictionary<string,JsonElement> LoadSchemas()
    {
        using var stream=typeof(WidgetPatch).Assembly.GetManifestResourceStream("LumaWall.Automation.WidgetsSchema.json")
            ??throw new InvalidOperationException("缺少组件参数定义");
        var root=JsonNode.Parse(stream)!["properties"]!["Widgets"]!["items"]!["properties"]!;
        var result=new Dictionary<string,JsonElement>();
        foreach(var group in new[]{"Visual","Search","Performance","Spectrum","Clock","Countdown","Focus","GridOptions"}) {
            var schema=root[group]!.DeepClone();
            if(group=="Focus")foreach(var key in new[]{"Running","Phase","CompletedSessions","RemainingSeconds","Deadline"})schema["properties"]!.AsObject().Remove(key);
            if(group=="Visual")schema["properties"]!.AsObject().Remove("Image");
            NormalizeSchema(schema);
            result[group]=JsonSerializer.SerializeToElement(schema);
        }
        return result;
    }
    private static void NormalizeSchema(JsonNode schema)
    {
        if(schema["type"]==null&&schema["enum"] is JsonArray choices)
            schema["type"]=choices[0]!.GetValueKind()==JsonValueKind.String?"string":"integer";
        if(schema["properties"] is JsonObject properties)
            foreach(var property in properties)NormalizeSchema(property.Value!);
        if(schema["items"] is { } items)NormalizeSchema(items);
    }
    private static T Merge<T>(T current,JsonElement patch)
    {
        var node=JsonSerializer.SerializeToNode(current)!;
        void Apply(JsonNode target,JsonElement value) {
            foreach(var property in value.EnumerateObject()) {
                if(property.Value.ValueKind==JsonValueKind.Object&&target[property.Name] is JsonObject child)Apply(child,property.Value);
                else target[property.Name]=JsonNode.Parse(property.Value.GetRawText());
            }
        }
        Apply(node,patch);return node.Deserialize<T>()!;
    }
    public static WidgetInstance Apply(WidgetInstance widget,JsonElement args)
    {
        if(args.TryGetProperty("x",out var x))widget=widget with {X=x.GetDouble()};
        if(args.TryGetProperty("y",out var y))widget=widget with {Y=y.GetDouble()};
        if(args.TryGetProperty("opacity",out var opacity))widget=widget with {Opacity=opacity.GetDouble()};
        if(args.TryGetProperty("enabled",out var enabled))widget=widget with {Enabled=enabled.GetBoolean()};
        if(args.TryGetProperty("alwaysOnTop",out var topmost))widget=widget with {AlwaysOnTop=topmost.GetBoolean()};
        if(args.TryGetProperty("layer",out var layer))widget=widget with {Layer=layer.GetInt32()};
        foreach(var group in Schemas.Keys) {
            var argument=char.ToLowerInvariant(group[0])+group[1..];
            if(!args.TryGetProperty(argument,out var patch))continue;
            ToolCatalog.ValidateValue(patch,Schemas[group],argument);
            bool supported=group switch {"Visual"=>widget.Kind is WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer,
                "Search"=>widget.Kind==WidgetKind.Search,"Performance"=>widget.Kind==WidgetKind.Performance,"Clock"=>widget.Kind==WidgetKind.Clock,"Countdown"=>widget.Kind==WidgetKind.Countdown,"Focus"=>widget.Kind==WidgetKind.FocusTimer,"GridOptions"=>FeaturePacks.GridFor(widget)!=null,_=>widget.Kind==WidgetKind.Spectrum};
            if(!supported)throw new ArgumentException(argument+" 不适用于此组件类型");
            widget=group switch {
                "Visual"=>widget with {Visual=Merge(widget.Visual,patch)},
                "Search"=>widget with {Search=Merge(widget.Search,patch)},
                "Performance"=>widget with {Performance=Merge(widget.Performance,patch)},
                "Clock"=>widget with {Clock=Merge(widget.Clock,patch)},
                "Countdown"=>widget with {Countdown=Merge(widget.Countdown,patch)},
                "Focus"=>widget with {Focus=UpdateFocus(widget.Focus,patch)},
                "GridOptions"=>widget with {GridOptions=Merge(FeaturePacks.GridFor(widget)!.Options(widget),patch)},
                _=>widget with {Spectrum=Merge(widget.Spectrum,patch)}
            };
        }
        if(args.TryGetProperty("text",out var text)) {
            if(widget.Kind!=WidgetKind.Text)throw new ArgumentException("text 仅用于文本组件");
            widget=widget with {Visual=widget.Visual with {Text=text.GetString()!}};
        }
        if(args.TryGetProperty("iconOnly",out var iconOnly)) {
            if(widget.Kind!=WidgetKind.Shortcuts)throw new ArgumentException("iconOnly 仅用于快捷卡片");
            widget=widget with {IconOnly=iconOnly.GetBoolean()};
        }
        if(args.TryGetProperty("metrics",out var metrics)) {
            if(widget.Kind!=WidgetKind.Performance)throw new ArgumentException("metrics 仅用于性能组件");
            widget=widget with {Metrics=(PerformanceMetrics)metrics.GetInt32()};
        }
        if(args.TryGetProperty("widthDip",out var width)||args.TryGetProperty("heightDip",out _)) {
            int? w=width.ValueKind==JsonValueKind.Number?width.GetInt32():null;
            int? h=args.TryGetProperty("heightDip",out var height)?height.GetInt32():null;
            widget=widget.Kind switch {
                WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer=>widget with {Visual=widget.Visual with {Width=w??widget.Visual.Width,Height=h??widget.Visual.Height}},
                WidgetKind.Search=>widget with {Search=widget.Search with {Width=w??widget.Search.Width,Height=h??widget.Search.Height}},
                WidgetKind.Spectrum=>widget with {Spectrum=widget.Spectrum with {Width=w??widget.Spectrum.Width,Height=h??widget.Spectrum.Height}},
                WidgetKind.Extension when FeaturePacks.GridFor(widget) is {} grid&&h==null=>widget with {GridOptions=grid.Options(widget) with {Width=w??grid.Options(widget).Width}},
                WidgetKind.Extension when FeaturePacks.GridFor(widget)!=null=>throw new ArgumentException("网格高度由行数和宽度确定"),
                _=>throw new ArgumentException("此类型组件的尺寸由内容布局决定")
            };
        }
        if(args.TryGetProperty("timerAction",out var action)) {if(widget.Kind!=WidgetKind.FocusTimer)throw new ArgumentException("timerAction 仅用于专注计时器");if(widget.InteractionLocked)throw new ArgumentException("组件交互已锁定");widget=widget with {Focus=widget.Focus.Action(action.GetString()!,DateTimeOffset.UtcNow)};}
        if(widget.GridOptions is {} options&&FeaturePacks.GridFor(widget) is {} limits
            &&(options.Rows<limits.MinRows||options.Rows>limits.MaxRows||options.Width<limits.MinWidth||options.Width>limits.MaxWidth))
            throw new ArgumentOutOfRangeException("gridOptions","扩展组件尺寸超出允许范围");
        widget.Clock.Validate();widget.Countdown.Validate();widget.Focus.Validate();widget.Visual.Validate();widget.Search.Validate();widget.Performance.Validate();widget.Spectrum.Validate();
        return widget;
    }
    private static FocusWidgetOptions UpdateFocus(FocusWidgetOptions current,JsonElement patch) {
        var next=Merge(current,patch);return next.FocusMinutes!=current.FocusMinutes||next.BreakMinutes!=current.BreakMinutes||next.LongBreakMinutes!=current.LongBreakMinutes||next.SessionsPerLongBreak!=current.SessionsPerLongBreak?next.Action("reset",DateTimeOffset.UtcNow):next;
    }
    public static WidgetConfiguration AddWebsite(WidgetConfiguration state,WidgetInstance widget,string name,string url)
    {
        if(widget.Kind!=WidgetKind.Shortcuts)throw new ArgumentException("网站参数仅用于快捷卡片");
        var card=new Shortcut(Guid.NewGuid().ToString("N"),name,ShortcutTargets.Normalize(ShortcutKind.Website,url),widget.Group,
            state.Shortcuts.Where(c=>c.Group==widget.Group).Select(c=>c.Order).DefaultIfEmpty(-1).Max()+1);
        return (state with {Shortcuts=[..state.Shortcuts,card],Widgets=[..state.Widgets,widget with {ShortcutId=card.Id}]}).Validate();
    }
    public static WidgetConfiguration UpdateWebsite(WidgetConfiguration state,WidgetInstance widget,JsonElement args)
    {
        var card=state.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId);
        if(widget.Kind!=WidgetKind.Shortcuts||card?.Kind!=ShortcutKind.Website)throw new ArgumentException("只能修改已有的网站快捷卡片");
        var next=card with {Name=args.TryGetProperty("name",out var name)?name.GetString()!:card.Name,
            Url=args.TryGetProperty("url",out var url)?ShortcutTargets.Normalize(ShortcutKind.Website,url.GetString()!):card.Url};
        if(next!=card&&state.Widgets.Any(w=>w.ShortcutId==card.Id&&w.Id!=widget.Id&&(state.IsEditingLocked(w)||w.LayoutGroupId!=null)))
            throw new ArgumentException("此网站同时被锁定或分组的组件引用，请先在界面中解除限制");
        return state with {Shortcuts=state.Shortcuts.Select(c=>c.Id==next.Id?next:c).ToArray()};
    }
}
