using LumaWall.Automation;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<OperationReply> AutomationMediaTools(OperationRequest request,CancellationToken token)
    {
        var args=request.Arguments;
        if(request.Name=="wallpaper_set_decoder_mode") {
            var mode=Enum.Parse<DecoderMode>(Arg(args,"mode"));ChangeDecoderMode(mode);
            return OperationReply.Success(new {mode=mode.ToString(),saved=true});
        }
        var asset=modules.Library.Assets().FirstOrDefault(a=>a.Id==Arg(args,"assetId"))??throw new OperationFault("resource_not_found","素材不存在，请重新读取 media_list","arguments.assetId");
        if(asset.IsImage)throw new OperationFault("invalid_argument","此操作需要视频素材",null);
        if(!File.Exists(asset.Path))throw new IOException("素材源文件不可用");
        switch(request.Name) {
            case "diagnostics_benchmark_decode":return OperationReply.Success(await MeasureDecode(asset,args.TryGetProperty("fps",out var fps)?fps.GetInt32():wallpapers!.FrameRateLimit,token));
            case "media_set_poster":return OperationReply.Success(await SavePosterFrame(asset,args.GetProperty("seconds").GetDouble(),token,()=>RequireAutomationRevision(args)));
            case "media_reset_poster":return OperationReply.Success(await ResetPoster(asset,token,()=>RequireAutomationRevision(args)));
            case "media_preview_poster": {
                string directory=Path.Combine(store.Root,"effect-preview");Directory.CreateDirectory(directory);
                // IDs originate in the library; hash again so the cache name is always a filename.
                string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.Id)));
                string output=Path.Combine(directory,"mcp-poster-"+key+".bmp"),stage=output+"."+Guid.NewGuid().ToString("N")+".bmp";
                try {
                    var frame=await MediaOperations.Frame(asset.Path,stage,args.GetProperty("seconds").GetDouble(),args.TryGetProperty("direction",out var direction)?direction.GetInt32():0,token);
                    token.ThrowIfCancellationRequested();File.Move(stage,output,true);
                    return OperationReply.Success(new {assetId=asset.Id,previewPath=output,frame});
                }finally {try {File.Delete(stage);}catch { }}
            }
            default:throw new OperationFault("invalid_argument","不支持的媒体操作",null);
        }
    }
}
