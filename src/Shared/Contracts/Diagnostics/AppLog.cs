using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LumaWall.Diagnostics;

public static class AppLog
{
    private static RollingFileLog? sink;
    private static int initialized;
    private static string? initializationError;
    public static string Session { get; private set; } = Guid.NewGuid().ToString("N");
    public static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
    public static string? DirectoryPath => sink?.DirectoryPath;
    public static string? CurrentFile => sink?.CurrentFile;
    public static string? LastError => sink?.LastError ?? initializationError;

    public static void Initialize(string component, string? dataRoot = null, bool inherit = false, bool fallbackToDefaultDirectory = true)
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0) return;
        try
        {
            if (inherit && Environment.GetEnvironmentVariable("ZZZWALLPAPER_LOG_SESSION") is { Length: > 0 } parentSession) Session = parentSession;
            string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaWall", "logs");
            string preferred = inherit ? Environment.GetEnvironmentVariable("ZZZWALLPAPER_LOG_DIRECTORY") ?? standard :
                dataRoot == null ? standard : Path.Combine(dataRoot, "logs");
            foreach (string directory in new[] { preferred }.Concat(fallbackToDefaultDirectory ? new[] { standard } : Array.Empty<string>())
                .Append(Path.Combine(Path.GetTempPath(), "ZZZwallpaper", "logs")).Distinct())
            {
                try
                {
                    var candidate = new RollingFileLog(directory, component, Version, Session);
                    if (candidate.LastError != null) { initializationError = candidate.LastError; candidate.Dispose(); continue; }
                    sink = candidate; break;
                }
                catch (Exception ex) { initializationError = ex.Message; }
            }
            if (sink != null)
            {
                if (initializationError != null) Warning("Logging", "Preferred log directory unavailable; using fallback: " + initializationError);
                initializationError = null;
                Environment.SetEnvironmentVariable("ZZZWALLPAPER_LOG_DIRECTORY", sink.DirectoryPath);
                Environment.SetEnvironmentVariable("ZZZWALLPAPER_LOG_SESSION", Session);
            }
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Error("Unhandled", "Unhandled process exception; terminating=" + e.IsTerminating, e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => Error("BackgroundTask", "Unobserved background task exception", e.Exception);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { Info("Lifecycle", "Process exited; code=" + Environment.ExitCode); OperationTrace.Shutdown(); sink?.Dispose(); };
            Info("Lifecycle", $"Process started; OS={RuntimeInformation.OSDescription}; runtime={RuntimeInformation.FrameworkDescription}; architecture={RuntimeInformation.ProcessArchitecture}");
        }
        catch (Exception ex) { initializationError = ex.Message; }
    }

    public static void Info(string category, string message) => sink?.Write("Information", category, message, operationId:OperationTrace.Current?.Id);
    public static void Warning(string category, string message, Exception? exception = null) => sink?.Write("Warning", category, message, exception, operationId:OperationTrace.Current?.Id);
    public static void Error(string category, string message, Exception? exception = null) => sink?.Write("Error", category, message, exception, operationId:OperationTrace.Current?.Id);

    public static void ConfigureChild(ProcessStartInfo start)
    {
        if (DirectoryPath == null) return;
        start.Environment["ZZZWALLPAPER_LOG_DIRECTORY"] = DirectoryPath;
        start.Environment["ZZZWALLPAPER_LOG_SESSION"] = Session;
    }

    // Drain continuously with bounded memory, including when stderr has no newline.
    // Never let logging break the child's stdout protocol or block its stderr pipe.
    public static async Task CaptureStandardErrorAsync(Process process, string category, CancellationToken token = default)
    {
        try
        {
            int pid = process.Id;
            var buffer = new char[2048]; var pending = new StringBuilder();
            int count;
            while ((count = await process.StandardError.ReadAsync(buffer.AsMemory(), token)) > 0)
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] == '\n' || pending.Length >= 4096)
                    {
                        if (pending.Length > 0) Warning(category, $"pid={pid}; stderr: {pending}");
                        pending.Clear();
                    }
                    if (buffer[i] != '\n' && buffer[i] != '\r') pending.Append(buffer[i]);
                }
            if (pending.Length > 0) Warning(category, $"pid={pid}; stderr: {pending}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Warning(category, "Could not read child stderr", ex); }
    }

    public static void Export(string destination,DiagnosticExportOptions options,object? environment=null,JsonElement? configuration=null,CancellationToken token=default)
    {
        options.Validate();
        if (options.IncludeLogs&&DirectoryPath == null) throw new IOException("日志目录不可用：" + LastError);
        if(options.IncludeLogs)OperationTrace.Flush(wait:true);
        Info("Diagnostics", "Diagnostic export requested");
        object? system=options.IncludeEnvironment?new {
            os=RuntimeInformation.OSDescription,runtime=RuntimeInformation.FrameworkDescription,
            architecture=RuntimeInformation.ProcessArchitecture.ToString(),logicalProcessors=Environment.ProcessorCount,
            is64BitOperatingSystem=Environment.Is64BitOperatingSystem,details=environment
        }:null;
        DiagnosticBundle.Create(DirectoryPath??"",destination,new {
            product="ZZZwallpaper",version=Version,session=Session,exportedAt=DateTimeOffset.Now
        },options,system,configuration,token);
    }
}

public static class DiagnosticBundle
{
    public static void Create(string directory,string destination,object metadata,DiagnosticExportOptions options,object? environment=null,JsonElement? configuration=null,CancellationToken token=default)
    {
        options.Validate();token.ThrowIfCancellationRequested();
        if(options.IncludeEnvironment&&environment==null)throw new ArgumentException("缺少运行环境快照。");
        if(options.IncludeConfiguration&&configuration==null)throw new ArgumentException("缺少配置快照。");
        string output = Path.GetFullPath(destination);
        if (Path.GetDirectoryName(output) is not { } parent) throw new IOException("无效的导出位置");
        // Build beside the destination and replace only after the archive is complete.
        string temporary = Path.Combine(parent, ".zzzwallpaper-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var skipped = new List<string>();
                foreach (var file in (options.IncludeLogs?new DirectoryInfo(directory).GetFiles("zzzwallpaper-*.jsonl"):Array.Empty<FileInfo>())
                    .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(RollingFileLog.DefaultMaxFiles))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        long remaining = Math.Min(input.Length, RollingFileLog.DefaultMaxBytes);
                        using var entry = zip.CreateEntry("logs/" + file.Name, CompressionLevel.Fastest).Open();
                        byte[] buffer = new byte[81920];
                        while (remaining > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0) break;
                            entry.Write(buffer, 0, read); remaining -= read;
                        }
                    }
                    catch (FileNotFoundException) { skipped.Add(file.Name + " (rotated during export)"); }
                    catch (UnauthorizedAccessException) { skipped.Add(file.Name + " (access denied)"); }
                }
                void Json(string name,object value) {
                    using var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Fastest).Open(),new UTF8Encoding(false));
                    writer.Write(JsonSerializer.Serialize(value,new JsonSerializerOptions {WriteIndented=true}));
                }
                Json("manifest.json",new {formatVersion=2,metadata,includes=options,skippedLogs=skipped});
                if(options.IncludeEnvironment)Json("system-info.json",environment!);
                if(options.IncludeConfiguration)Json("configuration.json",configuration!.Value);
                using (var writer = new StreamWriter(zip.CreateEntry("说明.txt").Open(), new UTF8Encoding(false)))
                    writer.Write("本包仅包含 manifest.json 所列选项，不会自动上传。运行日志可能含路径、错误上下文和已有环境记录；运行环境选项控制独立系统报告。配置保留数值、开关和结构，文本、路径与标识使用包内代号，不可作为预设恢复。不包含素材文件或数据库。日志末行可能尚未写完。");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
