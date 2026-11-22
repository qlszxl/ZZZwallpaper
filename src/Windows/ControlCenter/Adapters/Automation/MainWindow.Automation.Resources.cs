using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Automation;
using LumaWall.Contracts;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<(byte[] Pixels,uint Width,uint Height)> AutomationImagePixels(string assetId,uint maximum,CancellationToken token)
    {
        var media=modules.Library.Assets().FirstOrDefault(m=>m.Id==assetId&&m.IsImage)??throw new OperationFault("invalid_argument","需要已导入的图片素材 ID",null);
        string path=AuthorizedMediaPath(media.Path,registered:true);var file=await StorageFile.GetFileFromPathAsync(path);using var input=await file.OpenReadAsync();
        if(input.Size>32_000_000)throw new OperationFault("invalid_argument","图片最多 32 MB",null);
        var decoder=await BitmapDecoder.CreateAsync(input);token.ThrowIfCancellationRequested();
        if(decoder.PixelWidth==0||decoder.PixelHeight==0||(ulong)decoder.PixelWidth*decoder.PixelHeight>32_000_000||decoder.PixelWidth>16384||decoder.PixelHeight>16384)throw new OperationFault("invalid_argument","图片尺寸超限",null);
        double scale=Math.Min(1,maximum/(double)Math.Max(decoder.PixelWidth,decoder.PixelHeight));
        uint width=Math.Max(1,(uint)(decoder.PixelWidth*scale)),height=Math.Max(1,(uint)(decoder.PixelHeight*scale));
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform {ScaledWidth=width,ScaledHeight=height},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb);
        token.ThrowIfCancellationRequested();return(data.DetachPixelData(),width,height);
    }
    private async Task<OperationReply> AutomationWidgetResources(OperationRequest request,CancellationToken token)
    {
        FlushHistoryEdits();var a=request.Arguments;var state=widgets!.State;
        if(request.Name=="widget_list_shortcuts")return OperationReply.Success(new {configurationRevision=store.ConfigurationRevision(),items=state.Shortcuts.Select(s=>new {shortcutId=s.Id,s.Name,kind=s.Kind.ToString(),url=s.Kind==ShortcutKind.Website?s.Url:null,widgetIds=state.Widgets.Where(w=>w.ShortcutId==s.Id).Select(w=>w.Id),launchAllowed=false}),groups=state.LayoutGroups});
        if(request.Name=="widget_remove_shortcut") {
            string id=Arg(a,"shortcutId");if(!state.Shortcuts.Any(s=>s.Id==id))throw new OperationFault("resource_not_found","快捷入口不存在",null);
            var references=state.Widgets.Where(w=>w.ShortcutId==id).ToArray();
            if(references.Any(state.IsEditingLocked))throw new OperationFault("widget_locked","引用组件已锁定","arguments.widgetId");
            if(references.Length>0&&!Flag(a,"removeInstances"))throw new OperationFault("invalid_state","入口正在被组件引用；需要显式 removeInstances=true",null);
            widgets.Save((state with {Shortcuts=state.Shortcuts.Where(s=>s.Id!=id).ToArray(),Widgets=state.Widgets.Where(w=>w.ShortcutId!=id).ToArray()}).Validate());
            return OperationReply.Success(new {saved=true,shortcutId=id,removedWidgetIds=references.Select(w=>w.Id)});
        }
        if(request.Name=="widget_edit_group") {
            string action=Arg(a,"action");string? id=Optional(a,"groupId");var group=state.LayoutGroups.FirstOrDefault(g=>g.Id==id);
            if(action=="create") {if(id!=null)throw new OperationFault("invalid_argument","新建组不接受自定义 ID",null);group=new() {Screen=OperationScreen(Arg(a,"screenId")).DeviceName};}
            if(group==null)throw new OperationFault("resource_not_found","组件组不存在",null);id=group.Id;
            if(action=="lock") {if(!a.TryGetProperty("locked",out var locked))throw new OperationFault("invalid_argument","需要 locked",null);group=group with {Locked=locked.GetBoolean()};}
            else {
                if(group.Locked||state.Widgets.Any(w=>w.LayoutGroupId==id&&w.Locked))throw new OperationFault("widget_locked","请先显式解锁组件组及成员","arguments.widgetId");
                if(a.TryGetProperty("locked",out _))throw new OperationFault("widget_locked","锁定状态只能通过 action=lock 修改","arguments.widgetId");
                if(Optional(a,"name") is { } name)group=group with {Name=name};
                if(Optional(a,"screenId") is { } screen)group=group with {Screen=OperationScreen(screen).DeviceName};
                if(a.TryGetProperty("x",out var x))group=group with {X=x.GetDouble()};if(a.TryGetProperty("y",out var y))group=group with {Y=y.GetDouble()};
                if(Optional(a,"flow") is { } flow)group=group with {Flow=Enum.Parse<WidgetGroupFlow>(flow)};
                if(a.TryGetProperty("gap",out var gap))group=group with {Gap=gap.GetDouble()};if(a.TryGetProperty("columns",out var cols))group=group with {Columns=cols.GetInt32()};
            }
            var members=Ids(a,"widgetIds");
            if(action=="members"&&!a.TryGetProperty("widgetIds",out _))throw new OperationFault("invalid_argument","设置组成员需要 widgetIds；解散空组使用 ungroup",null);
            if(a.TryGetProperty("widgetIds",out _)&&action is not ("members" or "create"))throw new OperationFault("invalid_argument","只有 create/members 接受 widgetIds",null);
            if(members.Any(member=>!state.Widgets.Any(w=>w.Id==member)))throw new OperationFault("resource_not_found","组成员不存在",null);
            if(members.Select(member=>state.Widgets.First(w=>w.Id==member)).Any(w=>state.IsEditingLocked(w)||w.LayoutGroupId!=null&&w.LayoutGroupId!=id))throw new OperationFault("widget_locked","成员已锁定或属于其他组，请先移出","arguments.widgetId");
            var updated=state.Widgets.Select(w=> {
                if(action=="ungroup"&&w.LayoutGroupId==id)return w with {LayoutGroupId=null};
                if(action is "members" or "create") {
                    int index=Array.IndexOf(members,w.Id);if(index>=0)return w with {LayoutGroupId=id,LayoutOrder=index,Screen=group.Screen};
                    if(w.LayoutGroupId==id)return w with {LayoutGroupId=null};
                }
                return w.LayoutGroupId==id?w with {Screen=group.Screen}:w;
            }).ToArray();
            widgets.Save((state with {Widgets=updated,LayoutGroups=action=="ungroup"?state.LayoutGroups.Where(g=>g.Id!=id).ToArray():state.LayoutGroups.Where(g=>g.Id!=id).Append(group).ToArray()}).Validate());
            var saved=widgets.State;
            return OperationReply.Success(new {saved=true,groupId=id,group=saved.LayoutGroups.FirstOrDefault(g=>g.Id==id),members=WidgetStacking.GroupMembers(saved,id).Select(w=>w.Id).ToArray()});
        }
        var widget=state.Widgets.FirstOrDefault(w=>w.Id==Arg(a,"widgetId"))??throw new OperationFault("resource_not_found","组件不存在","arguments.widgetId");
        if(request.Name=="widget_set_lock")widget=WidgetPatch.ApplyLocks(widget,a);
        else {
            if(state.IsEditingLocked(widget))throw new OperationFault("widget_locked","组件已锁定，请先显式解锁","arguments.widgetId");
            if(request.Name=="widget_reset_appearance") {
                if(widget.Kind is not (WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer))throw new OperationFault("invalid_argument","仅支持时间组件的外观细节重置",null);
                widget=widget with {Visual=widget.Visual with {TimeAppearance=a.TryGetProperty("group",out var group)?widget.Visual.TimeAppearance.ResetGroup(group.GetInt32()):new()}};
            } else {
                if(widget.Kind!=WidgetKind.Sticker)throw new OperationFault("invalid_argument","只有贴纸组件支持图片",null);
                if(Flag(a,"clear")==a.TryGetProperty("assetId",out _))throw new OperationFault("invalid_argument","请提供 assetId 或 clear=true 其中之一",null);
                string? encoded=null;
                if(!Flag(a,"clear")) {
                    var decoded=await AutomationImagePixels(Arg(a,"assetId"),1024,token);
                    using var output=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output);
                    encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,decoded.Width,decoded.Height,96,96,decoded.Pixels);await encoder.FlushAsync();token.ThrowIfCancellationRequested();
                    if(output.Size>2_000_000)throw new OperationFault("invalid_argument","PNG 转换后超过 2 MB，请使用更小图片",null);
                    using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size);byte[] png=new byte[(int)output.Size];reader.ReadBytes(png);encoded=Convert.ToBase64String(png);
                }
                // Do not overwrite edits made while image decoding yielded to the UI.
                RequireAutomationRevision(a);
                widget=widget with {Visual=widget.Visual with {Image=encoded}};
            }
        }
        widgets.Save((state with {Widgets=state.Widgets.Select(w=>w.Id==widget.Id?widget:w).ToArray()}).Validate());
        return OperationReply.Success(new {saved=true,widgetId=widget.Id,locked=widget.Locked,interactionLocked=widget.InteractionLocked,image=widget.Visual.Image==null?null:new {id=Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(widget.Visual.Image))),width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Convert.FromBase64String(widget.Visual.Image).AsSpan(16,4)),height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Convert.FromBase64String(widget.Visual.Image).AsSpan(20,4))}});
    }
    private async Task<OperationReply> AutomationMasks(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;string screen=OperationScreen(Arg(a,"screenId")).DeviceName;string? asset=Optional(a,"assetId");
        if(asset!=null&&!modules.Library.Assets().Any(m=>m.Id==asset))throw new OperationFault("resource_not_found","素材不存在","arguments.assetId");
        var profile=modules.Effects.EditingProfile(screen,asset);var effects=profile.Effects;
        if(request.Name=="effect_list_masks")return OperationReply.Success(new {configurationRevision=store.ConfigurationRevision(),scope=asset==null?"screen":"asset",masks=effects.Masks.Select(m=>m with {Image=""}),references=effects.EffectMasks});
        string action=Arg(a,"action");var mask=action=="create"?new EffectMask {Name="遮罩 "+Guid.NewGuid().ToString("N")[..6]}:effects.Masks.FirstOrDefault(m=>m.Id==Optional(a,"maskId"))??throw new OperationFault("resource_not_found","遮罩不存在",null);
        if(action=="create"&&a.TryGetProperty("maskId",out _))throw new OperationFault("invalid_argument","创建遮罩不接受自定义 ID",null);
        var links=new Dictionary<EffectKind,string>(effects.EffectMasks);
        if(action=="delete") {
            if(links.ContainsValue(mask.Id)&&!Flag(a,"detach"))throw new OperationFault("invalid_state","遮罩正在被引用；删除需要 detach=true",null);
            foreach(var key in links.Where(p=>p.Value==mask.Id).Select(p=>p.Key).ToArray())links.Remove(key);
            effects=effects with {Masks=effects.Masks.Where(m=>m.Id!=mask.Id).ToArray(),EffectMasks=links};
        } else {
            var node=JsonSerializer.SerializeToNode(mask)!.AsObject();if(a.TryGetProperty("parameters",out var parameters))foreach(var p in parameters.EnumerateObject())node[p.Name]=JsonNode.Parse(p.Value.GetRawText());
            mask=node.Deserialize<EffectMask>()!;
            if(Optional(a,"imageAssetId") is { } image) {
                var decoded=await AutomationImagePixels(image,512,token);var plane=new byte[512*512];
                int ox=(512-(int)decoded.Width)/2,oy=(512-(int)decoded.Height)/2;
                for(int y=0;y<decoded.Height;y++)for(int x=0;x<decoded.Width;x++) {int i=(y*(int)decoded.Width+x)*4;plane[(y+oy)*512+x+ox]=(byte)(((decoded.Pixels[i]*54+decoded.Pixels[i+1]*183+decoded.Pixels[i+2]*19)>>8)*decoded.Pixels[i+3]/255);}
                mask=mask with {Shape=5,Image=MaskPixels.Encode(plane)};
            }
            RequireAutomationRevision(a);
            mask.Validate();effects=effects with {Masks=effects.Masks.Where(m=>m.Id!=mask.Id).Append(mask).ToArray()};
        }
        effects.Validate();modules.Effects.SaveTargetEffects(screen,asset,profile with {Effects=effects},profile.Effects);wallpapers!.RefreshLiveEffects();LoadEffectDraft(true);
        var stored=modules.Effects.EditingProfile(screen,asset).Effects;
        var storedIds=stored.Masks.Where(m=>m.Id==mask.Id).Select(m=>m.Id).Concat(profile.Effects.EffectMasks.Where(p=>p.Value==mask.Id).Select(p=>stored.EffectMasks.GetValueOrDefault(p.Key)).Where(id=>id!=null)!).Distinct().ToArray();
        return OperationReply.Success(new {saved=true,maskId=action=="delete"?mask.Id:storedIds.FirstOrDefault(),storedMaskIds=action=="delete"?[]:storedIds,references=stored.EffectMasks});
    }
}
