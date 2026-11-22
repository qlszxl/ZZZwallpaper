using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record AutomationImportPlan(string Screen,string Revision,SharedConfiguration Configuration);
    private readonly Dictionary<string,AutomationImportPlan> automationImportPlans=new();
    private async Task<OperationReply> AutomationSharing(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;FlushHistoryEdits();
        if(request.Name=="config_cancel_import")return OperationReply.Success(new {cancelled=automationImportPlans.Remove(Arg(a,"importPlanId"))});
        if(request.Name=="config_preview_import") {
            if(automationImportPlans.Count>=8)throw new OperationFault("invalid_state","待导入预览最多 8 项，请先提交或取消",null);
            string screen=OperationScreen(Arg(a,"screenId")).DeviceName,revision=store.ConfigurationRevision();
            if(a.TryGetProperty("code",out _)==a.TryGetProperty("imageAssetId",out _))throw new OperationFault("invalid_argument","需要分享文本或二维码图片素材 ID，两者不能同时提供",null);
            SharedConfiguration parsed;
            if(Optional(a,"imageAssetId") is { } image) {
                var pixels=await AutomationImagePixels(image,16384,token);
                for(int i=0;i<pixels.Pixels.Length;i+=4)(pixels.Pixels[i],pixels.Pixels[i+2])=(pixels.Pixels[i+2],pixels.Pixels[i]);
                parsed=await Task.Run(()=>SharingQrImage.Read(pixels.Pixels,(int)pixels.Width,(int)pixels.Height),token);
            } else parsed=await Task.Run(()=>ShareCode.Decode(Arg(a,"code")),token);
            if(parsed.Widgets.Shortcuts.Any(c=>c.Kind!=ShortcutKind.Website))throw new UnauthorizedAccessException("AI 分享导入不允许命令或文件夹入口");
            if(store.ConfigurationRevision()!=revision)throw new OperationFault("invalid_state","解析期间配置已变化，请重新预览",null);
            string id=Guid.NewGuid().ToString("N");automationImportPlans[id]=new(screen,revision,parsed);
            return OperationReply.Success(new {importPlanId=id,expectedRevision=revision,screenId=screen,replaceAllWidgets=true,oldWidgetCount=widgets!.State.Widgets.Length,newWidgetCount=parsed.Widgets.Widgets.Length,replaceDisplayEffects=parsed.HasEffects,assetOverridesPreserved=true,warnings=parsed.Warnings,removedWidgetIds=widgets.State.Widgets.Select(w=>w.Id)});
        }
        if(request.Name=="config_commit_import") {
            string id=Arg(a,"importPlanId");if(!automationImportPlans.TryGetValue(id,out var plan))throw new OperationFault("resource_not_found","导入预览不存在",null);
            if(plan.Revision!=store.ConfigurationRevision()||plan.Revision!=Arg(a,"expectedRevision"))throw new OperationFault("import_plan_expired","预览已过期，请重新预览","arguments.importPlanId",recovery:"preview_import");
            OperationScreen(plan.Screen);if(widgets!.State.Widgets.Any(w=>widgets.State.IsEditingLocked(w)))throw new OperationFault("widget_locked","当前有锁定组件，不能替换全部组件","arguments.widgetId");
            var imported=sharing.Apply(plan.Configuration,plan.Screen,plan.Revision);
            automationImportPlans.Remove(id);
            string warning=imported.Warning;
            return new OperationReply(warning.Length>0,ToolCatalog.Json(new {saved=true,importPlanId=id,widgetIds=imported.Widgets.Widgets.Select(w=>w.Id),message=warning.Length==0?"分享配置已保存":warning}));
        }
        string target=OperationScreen(Arg(a,"screenId")).DeviceName;var source=modules.Effects.ScreenProfile(target)?.Effects??VideoEffects.Original;
        var config=widgets!.State;var excluded=config.Shortcuts.Where(s=>s.Kind!=ShortcutKind.Website).Select(s=>s.Id).ToHashSet();
        config=config with {Shortcuts=config.Shortcuts.Where(s=>!excluded.Contains(s.Id)).ToArray(),Widgets=config.Widgets.Where(w=>w.ShortcutId==null||!excluded.Contains(w.ShortcutId)).ToArray()};
        var selection=new ShareSelection {Effects=source.GetCollection().Added,WidgetIds=Ids(a,"widgetIds"),Text=Flag(a,"text"),Images=Flag(a,"images"),Targets=Flag(a,"websiteTargets")};
        if(selection.WidgetIds.Any(id=>!config.Widgets.Any(w=>w.Id==id)))throw new OperationFault("resource_not_found","选择包含不存在或不允许分享的组件",null);
        var document=ShareProjection.Create(source,config,selection);var packet=ShareCode.EncodePacket(document);string text=ShareCode.ToText(packet),path=AutomationArtifactPath(".txt");
        await File.WriteAllTextAsync(path,text,token);string? qrPath=null;int? parts=null;
        if(Flag(a,"qr")) {var qr=await Task.Run(()=>SharingQrImage.Create(packet),token);qrPath=AutomationArtifactPath(".png");await File.WriteAllBytesAsync(qrPath,qr.Png,token);parts=qr.Parts;}
        return OperationReply.Success(new {path,code=text.Length<=500000?text:null,qrPath,parts,excludedCommandAndFolderEntries=excluded.Count});
    }
}
