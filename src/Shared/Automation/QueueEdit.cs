using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.Automation;

public static class QueueEdit
{
    public static PlaybackSettings Apply(PlaybackSettings current,JsonElement args,ISet<string> assets,ISet<string> folders)
    {
        current=current.Validate();
        string action=args.GetProperty("action").GetString()!;
        var ids=args.TryGetProperty("assetIds",out var list)?list.EnumerateArray().Select(v=>v.GetString()!).ToList():[];
        if(ids.Any(id=>!assets.Contains(id)))throw new ArgumentException("队列包含未知素材 ID");
        if(action is "set" or "add" or "remove" or "reorder"&&!args.TryGetProperty("assetIds",out _))throw new ArgumentException("此操作需要 assetIds");
        if(action=="folder") {
            if(args.TryGetProperty("assetIds",out _))throw new ArgumentException("文件夹来源不接受 assetIds");
            string folder=args.GetProperty("folderId").GetString()!;if(!folders.Contains(folder))throw new ArgumentException("未知文件夹来源");
            return (current with {FolderId=folder}).Validate();
        }
        if(args.TryGetProperty("folderId",out _))throw new ArgumentException("仅 folder 操作接受 folderId");
        if(current.FolderId!=null&&action is "add" or "remove" or "reorder")throw new ArgumentException("文件夹队列请先用 set 显式切换为素材列表");
        if(action=="reorder"&&(ids.Count!=current.AssetIds.Count||!ids.ToHashSet().SetEquals(current.AssetIds)))throw new ArgumentException("重排必须包含当前队列的全部且不重复的素材 ID");
        return (current with {FolderId=null,AssetIds=action switch {
            "set" or "reorder"=>ids,"add"=>current.AssetIds.Concat(ids).Distinct().ToList(),"remove"=>current.AssetIds.Where(id=>!ids.Contains(id)).ToList(),"clear"=>[],_=>throw new ArgumentException("未知队列操作")
        }}).Validate();
    }
}
