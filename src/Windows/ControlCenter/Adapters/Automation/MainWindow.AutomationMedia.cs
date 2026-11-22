using LumaWall.Contracts;
using LumaWall.Automation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private async Task<OperationReply> ImportAutomationMedia(string path,CancellationToken token,Action? beforeCommit=null)
    {
        path=AuthorizedMediaPath(path);
        if(!File.Exists(path))throw new OperationFault("resource_not_found","素材文件不存在或不可读取。","arguments.assetId");
        if(importLifetime.Active!=null||libraryShutdown)throw new OperationFault("invalid_state","素材导入正在进行，请稍后重试。",null);
        var operation=BeginLibraryImport();var summary=new ImportSummary();
        using var cancelled=token.Register(()=>operation.Cancel());
        try {
            await ImportPaths([path],false,summary,operation.Token,beforeCommit);
            CheckImportActive(operation.Token);
            if(summary.Failed>0)return OperationReply.Error(string.Join("；",summary.Errors));
            var asset=modules.Library.FindAssetByPath(path)??throw new IOException("素材没有保存成功。");
            return OperationReply.Success(new {assetId=asset.Id,name=asset.Name,kind=asset.Kind,asset.Width,asset.Height,asset.Duration,alreadyImported=summary.Existing>0,applied=false});
        }finally {
            CompleteLibraryImport(operation);
            if(!libraryShutdown) {RefreshFolders();RefreshLibrary();}
        }
    }
}
