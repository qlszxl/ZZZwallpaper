using System.Diagnostics;
using System.ComponentModel;
using LumaWall.Lifecycle;
using ZZZwallpaper.InstallerPreview;

internal static class LifecycleChecks
{
    internal static async Task Child(string mode)
    {
        using var lifetime=new InstallationLifetime(AppContext.BaseDirectory);
        if(lifetime.IsStopping)return;
        var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(mode is "graceful" or "slow-graceful")lifetime.Listen(()=>stopped.TrySetResult());
        Console.WriteLine("ready");Console.Out.Flush();
        await stopped.Task;
        if(mode=="slow-graceful")await Task.Delay(450);
    }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        async Task<Process> Start(string directory,string mode,bool ready=true,string name="ZZZwallpaper.Mcp") {
            Directory.CreateDirectory(directory);
            foreach(var file in Directory.GetFiles(AppContext.BaseDirectory,"InstallerTests.*")) {
                var destination=Path.Combine(directory,Path.GetFileName(file));
                if(!File.Exists(destination))File.Copy(file,destination);
            }
            var exe=Path.Combine(directory,name+".exe");
            if(!File.Exists(exe))File.Copy(Path.Combine(AppContext.BaseDirectory,"InstallerTests.exe"),exe);
            var start=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--process-fixture");start.ArgumentList.Add(mode);
            var process=Process.Start(start)!;
            if(ready)try {
                if(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))!="ready")throw new Exception("Fixture failed: "+await process.StandardError.ReadToEndAsync());
            }catch {if(!process.HasExited)process.Kill();process.Dispose();throw;}
            return process;
        }
        var owned=Path.Combine(root,"owned");var sibling=Path.Combine(root,"owned-other");
        // Reproduce an executable query failing while the application is
        // already shutting down. Only this fixture PID is fault-injected.
        using(var child=await Start(owned,"slow-graceful")) {
            int attempts=0;
            string Inspect(Process process) {
                if(process.Id==child.Id&&++attempts<=3)throw new Win32Exception(5);
                return UninstallProcesses.ImagePath(process);
            }
            try {
                using var stop=new UninstallProcesses(owned,forInstallation:true,inspectImage:Inspect);
                await stop.StopAsync(TimeSpan.FromSeconds(2));
                check(attempts>=2&&child.HasExited&&child.ExitCode==0,"Transient access denial during graceful exit retries instead of failing installation");
            }finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
        }
        using var other=await Start(sibling,"unresponsive");
        try {
            using(var child=await Start(owned,"graceful")) {
                using var stop=new UninstallProcesses(owned);
                await stop.StopAsync(TimeSpan.FromSeconds(3));
                check(child.HasExited&&child.ExitCode==0,"Installation stop signal gracefully exits its own background process");
                check(!other.HasExited,"Uninstall preserves a same-name process in another directory");
                bool rejected=false;try {using var duplicate=new UninstallProcesses(owned);}catch(InstallerException) {rejected=true;}
                check(rejected,"Concurrent uninstall cannot reset the first stop signal");
                using var restarted=await Start(owned,"graceful",false);
                await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(restarted.ExitCode==0,"Relaunched process exits while uninstall is active");
            }
            using(var child=await Start(owned,"unresponsive")) {
                using var component=await Start(Path.Combine(owned,"Widgets"),"unresponsive");
                try {using var stop=new UninstallProcesses(owned);await stop.StopAsync(TimeSpan.FromMilliseconds(100));
                    check(child.HasExited&&!other.HasExited,"Unresponsive owned process is stopped without killing another installation");
                    check(component.HasExited,"All owned subdirectory components exit automatically before uninstall proceeds");}
                finally {if(!child.HasExited)child.Kill();if(!component.HasExited)component.Kill();}
            }
            using var cleared=new InstallationLifetime(owned);
            check(!cleared.IsStopping,"Stop signal is released after the uninstall scope ends");
            var progress=new Progress<InstallationProgress>();
            using(var child=await Start(owned,"graceful")) {
                string[] Discover()=>child.HasExited?[]:[owned];
                bool prompted=false;
                try {using var scope=await InstallationProcesses.PrepareAsync(owned,false,progress,CancellationToken.None,Discover);}
                catch(ApplicationRunningException){prompted=true;}
                check(prompted&&!child.HasExited&&!cleared.IsStopping,"Running program prompts without stopping anything before explicit choice");
                using(var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,CancellationToken.None,Discover)) {
                    check(child.HasExited&&child.ExitCode==0&&cleared.IsStopping,"Exit-and-install gracefully closes the app and holds restart prevention through installation");
                    using var restarted=await Start(owned,"graceful",false);
                    await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    check(restarted.ExitCode==0&&!other.HasExited,"Install guard prevents AI restart and preserves another same-name process");
                }
                check(!cleared.IsStopping,"Installation completion releases stop signal");
            }
            using(var child=await Start(owned,"unresponsive",name:"ZZZwallpaper")) {
                try {
                    using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
                    bool cancelled=false;
                    try {using var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,cancellation.Token,()=>[owned],TimeSpan.FromMilliseconds(50));}
                    catch(OperationCanceledException){cancelled=true;}
                    check(cancelled&&!child.HasExited&&!cleared.IsStopping,"Cancelling exit wait preserves an unresponsive main app and releases all guards");
                    bool timedOut=false;
                    try {using var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,CancellationToken.None,()=>[owned],TimeSpan.Zero);}
                    catch(InstallerException){timedOut=true;}
                    check(timedOut&&!child.HasExited&&!cleared.IsStopping,"Unresponsive main app times out without forced termination or continuing installation");
                }finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
            }
            using(var child=await Start(owned,"unresponsive")) {
                using var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,CancellationToken.None,()=>child.HasExited?[]:[owned],TimeSpan.FromMilliseconds(50));
                check(child.HasExited&&!other.HasExited,"Exit-and-install stops only verified orphaned components after main app exits");
            }
            using(var main=await Start(owned,"unresponsive",name:"ZZZwallpaper"))
            using(var component=await Start(Path.Combine(owned,"Widgets"),"unresponsive",name:"ZZZwallpaper.WidgetHost")) {
                string Inspect(Process process) => process.Id==main.Id ? throw new Win32Exception(5) : UninstallProcesses.ImagePath(process);
                try {
                    bool cancelled=false;
                    using(var stop=new UninstallProcesses(owned,forInstallation:true,inspectImage:Inspect)) {
                        using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        try {await stop.StopAsync(TimeSpan.Zero,cancellationToken:cancellation.Token);}
                        catch(OperationCanceledException){cancelled=true;}
                    }
                    check(cancelled&&!main.HasExited&&!component.HasExited&&!cleared.IsStopping,"Unresolved main process blocks component termination; cancellation releases the guard");
                }finally {
                    if(!main.HasExited){main.Kill();await main.WaitForExitAsync();}
                    if(!component.HasExited){component.Kill();await component.WaitForExitAsync();}
                }
            }
            using(var child=await Start(owned,"unresponsive")) {
                string Inspect(Process process) => process.Id==child.Id ? throw new Win32Exception(5) : UninstallProcesses.ImagePath(process);
                try {
                    bool blocked=false;
                    using(var stop=new UninstallProcesses(owned,forInstallation:true,inspectImage:Inspect)) {
                        try {await stop.StopAsync(TimeSpan.Zero);}
                        catch(InstallerException error) {blocked=error.Message.Contains("PID "+child.Id)&&error.Message.Contains("错误 5");}
                    }
                    check(blocked&&!child.HasExited&&!cleared.IsStopping,"Persistent unreadable process blocks installation with PID and error code, never counts as exited");
                }finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
            }
            File.WriteAllText(Path.Combine(owned,"ZZZwallpaper.dll"),"fixture");
            File.WriteAllText(Path.Combine(owned,"ZZZwallpaper.Launcher.exe"),"fixture");
            check(InstallationProcesses.ApplicationDirectory(Path.Combine(owned,"Mcp","ZZZwallpaper.Mcp.exe"))==owned&&
                InstallationProcesses.ApplicationDirectory(Path.Combine(owned,"Widgets","ZZZwallpaper.WidgetHost.exe"))==owned,"AI and widget executables resolve to their installation root");
            check(InstallationProcesses.ApplicationDirectory(Path.Combine(sibling,"ZZZwallpaper.Mcp.exe"))==null&&
                InstallationProcesses.ApplicationDirectory(Path.Combine(owned,"unrelated.exe"))==null,"Same-name unrelated executables and unrelated app names are excluded");
            using(var child=await Start(owned,"graceful")) {
                var discovered=InstallationProcesses.FindRunningDirectories();
                check(discovered.Contains(owned)&&!discovered.Contains(sibling),"Live process discovery identifies package roots without including a same-name unrelated executable");
                int attempts=0;
                string Transient(Process process) {
                    if(process.Id==child.Id&&++attempts<=2)throw new Win32Exception(5);
                    return UninstallProcesses.ImagePath(process);
                }
                discovered=InstallationProcesses.FindRunningDirectories(Transient);
                check(attempts>=3&&discovered.Contains(owned)&&!child.HasExited,"Preflight retries transient query failure and still detects the running installation");
                bool blocked=false;
                try {InstallationProcesses.FindRunningDirectories(process => process.Id==child.Id ? throw new Win32Exception(5) : UninstallProcesses.ImagePath(process));}
                catch(InstallerException error) {blocked=error.Message.Contains("PID "+child.Id)&&error.Message.Contains("错误 5");}
                check(blocked&&!child.HasExited,"Preflight never treats a persistently unreadable process as stopped");
                using var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,CancellationToken.None,()=>child.HasExited?[]:[owned]);
            }
            using(var conflict=new UninstallProcesses(owned)) {
                bool rejected=false;
                try {using var scope=await InstallationProcesses.PrepareAsync(owned,true,progress,CancellationToken.None,()=>[]);}
                catch(InstallerException){rejected=true;}
                check(rejected&&cleared.IsStopping,"Installation cannot reset an existing uninstall guard");
            }
        }finally {if(!other.HasExited) {other.Kill();await other.WaitForExitAsync();}}
    }
}
