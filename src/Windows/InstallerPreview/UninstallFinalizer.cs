namespace ZZZwallpaper.InstallerPreview;

// The engine hands off to a temporary process. Personal-data deletion is part
// of completion and must not be skipped merely because the entry process exits.
internal static class UninstallFinalizer
{
    internal static async Task<PersonalDataCleanupResult?> FinishAsync(string directory,string doneFile,bool keep,
        Func<PersonalDataCleanupResult> cleanup,IProgress<string>? progress=null,TimeSpan? timeout=null)
    {
        if(!await UninstallCompletion.WaitAsync(directory,doneFile,timeout))
            throw new InstallerException("程序文件仍被占用，卸载尚未完成；个人数据还未清理。");
        if(keep)return null;
        progress?.Report("正在删除素材库、效果和组件设置");
        return await CleanAsync(cleanup);
    }
    internal static async Task<PersonalDataCleanupResult> CleanAsync(Func<PersonalDataCleanupResult> cleanup)
    {
        int deleted=0;PersonalDataCleanupResult result=new(0,1);
        for(int attempt=0;attempt<5;attempt++) {
            try {result=cleanup();deleted+=result.DeletedFiles;}
            catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException) {result=new(0,1);}
            if(result.FailedItems==0)return new(deleted,0);
            if(attempt<4)await Task.Delay(250);
        }
        return new(deleted,result.FailedItems);
    }
}
