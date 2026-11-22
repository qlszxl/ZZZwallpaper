using System.Diagnostics;
using System.Text.Json;
using ZZZwallpaper.InstallerPreview;

internal static class TempCleanupChecks
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        var partial=Path.Combine(root,UninstallTempCleanup.Prefix+"partial-copy");Directory.CreateDirectory(partial);
        File.WriteAllText(Path.Combine(partial,UninstallTempCleanup.Executable),"partial");
        UninstallTempCleanup.Abandon(partial,root);
        check(!Directory.Exists(partial),"A failed initial copy is reclaimed even before the ownership marker exists");
        (string Directory,string Install,UninstallTempOwner Owner) Fixture(string suffix,bool complete=false) {
            var directory=Path.Combine(root,UninstallTempCleanup.Prefix+suffix);
            var install=Path.Combine(root,"Install-"+suffix);
            Directory.CreateDirectory(directory);Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(directory,UninstallTempCleanup.Executable),"owned test copy");
            UninstallTempCleanup.Prepare(directory,install);
            var owner=UninstallTempCleanup.Read(directory,root)! with {ProcessId=0,StartTicks=0,ProgramRemoved=complete};
            File.WriteAllText(Path.Combine(directory,UninstallTempCleanup.Marker),JsonSerializer.Serialize(owner));
            return(directory,install,owner);
        }
        async Task<int> Run(string directory,UninstallTempOwner owner) {
            var start=UninstallTempCleanup.CreateStart(directory,root,owner);
            start.RedirectStandardError=true;
            using var process=Process.Start(start)!;
            var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
            if(process.ExitCode==1)throw new Exception(await error);
            return process.ExitCode;
        }
        for(int cycle=1;cycle<=3;cycle++) {
            var f=Fixture("cycle"+cycle,true);
            check(await Run(f.Directory,f.Owner)==0&&!Directory.Exists(f.Directory)&&!Directory.Exists(f.Install),"Cycle "+cycle+": temporary copy and empty installation root reclaimed");
        }
        foreach(string reason in new[]{"cancel","failure","startup-failure"}) {
            var f=Fixture(reason);
            check(await Run(f.Directory,f.Owner)==0&&!Directory.Exists(f.Directory)&&Directory.Exists(f.Install),reason+": temporary copy removed without deleting installation directory");
        }
        var special=Fixture("中文 空格 & ' [x]",true);
        File.WriteAllText(Path.Combine(special.Install,"original.mp4"),"user media");
        File.WriteAllText(Path.Combine(special.Directory,"notes.txt"),"unknown file");
        check(await Run(special.Directory,special.Owner)==0&&File.Exists(Path.Combine(special.Install,"original.mp4"))&&File.Exists(Path.Combine(special.Directory,"notes.txt")),"Literal special-character paths and unrelated files are preserved");
        var bad=Fixture("changed-copy");File.AppendAllText(Path.Combine(bad.Directory,UninstallTempCleanup.Executable),"changed");
        check(await Run(bad.Directory,bad.Owner)==2&&File.Exists(Path.Combine(bad.Directory,UninstallTempCleanup.Executable)),"A mismatched executable hash refuses deletion");
        var wrong=Fixture("wrong-token");
        check(await Run(wrong.Directory,wrong.Owner with {Token=new string('0',32)})==2&&File.Exists(Path.Combine(wrong.Directory,UninstallTempCleanup.Executable)),"A mismatched ownership token refuses deletion");
        var outside=Fixture("outside");
        var outsideStart=UninstallTempCleanup.CreateStart(outside.Directory,Path.Combine(root,"Other"),outside.Owner);
        outsideStart.WorkingDirectory=root;
        using(var process=Process.Start(outsideStart)!) {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            check(process.ExitCode==2&&Directory.Exists(outside.Directory),"Paths outside the explicit temporary root are rejected");
        }
        var wait=Fixture("wait-for-owner",true);
        var childStart=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
        childStart.ArgumentList.Add("--cleanup-wait-fixture");
        using(var child=Process.Start(childStart)!) {
            var owner=wait.Owner with {ProcessId=child.Id,StartTicks=child.StartTime.ToUniversalTime().Ticks};
            File.WriteAllText(Path.Combine(wait.Directory,UninstallTempCleanup.Marker),JsonSerializer.Serialize(owner));
            var pending=Run(wait.Directory,owner);
            await Task.Delay(1200);
            check(!pending.IsCompleted&&File.Exists(Path.Combine(wait.Directory,UninstallTempCleanup.Executable)),"Cleanup waits for its exact owner process to exit");
            child.StandardInput.Close();await child.WaitForExitAsync();
            check(await pending==0&&!Directory.Exists(wait.Directory),"Cleanup resumes after the owner exits");
        }
        var stale=Fixture("pid-reused");using var current=Process.GetCurrentProcess();
        var staleOwner=stale.Owner with {ProcessId=current.Id,StartTicks=current.StartTime.ToUniversalTime().AddDays(-1).Ticks};
        File.WriteAllText(Path.Combine(stale.Directory,UninstallTempCleanup.Marker),JsonSerializer.Serialize(staleOwner));
        check(await Run(stale.Directory,staleOwner)==0,"A reused process id does not delay cleanup of a stale copy");
        var locked=Fixture("transient-lock");
        var handle=new FileStream(Path.Combine(locked.Directory,UninstallTempCleanup.Executable),FileMode.Open,FileAccess.Read,FileShare.None);
        var retry=Run(locked.Directory,locked.Owner);await Task.Delay(1300);handle.Dispose();
        check(await retry==0&&!Directory.Exists(locked.Directory),"A transient executable lock is retried");
        var sweep=Fixture("abandoned");UninstallTempCleanup.Sweep(root);
        for(int i=0;i<150&&Directory.Exists(sweep.Directory);i++)await Task.Delay(100);
        check(!Directory.Exists(sweep.Directory),"Startup sweep reclaims marked abandoned copies");
        var legacy=Path.Combine(root,UninstallTempCleanup.Prefix+"unmarked");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,UninstallTempCleanup.Executable),"unknown");
        UninstallTempCleanup.Sweep(root);await Task.Delay(300);
        check(Directory.Exists(legacy),"Unmarked historical directories are not blindly deleted");
    }
}
