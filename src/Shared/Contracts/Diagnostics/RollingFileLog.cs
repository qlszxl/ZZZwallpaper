using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LumaWall.Diagnostics;

// Each process owns its file. Shared readers can export a live log, but retention
// cannot delete another process's open file on Windows.
public sealed class RollingFileLog : IDisposable
{
    public const long DefaultMaxBytes = 2 * 1024 * 1024;
    public const int DefaultMaxFiles = 30;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object gate = new();
    private readonly string component, version, session, prefix;
    private readonly long maxBytes;
    private readonly int maxFiles;
    private readonly int maxEntriesPerSecond;
    private readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private FileStream? stream;
    private int part, burst, suppressed;
    private long rateWindow = Environment.TickCount64, retryAt;
    private bool disposed;
    public string DirectoryPath { get; }
    public string? CurrentFile { get; private set; }
    public string? LastError { get; private set; }

    public RollingFileLog(string directory, string component, string version, string session,
        long maxBytes = DefaultMaxBytes, int maxFiles = DefaultMaxFiles, int maxEntriesPerSecond = 100)
    {
        if (maxBytes < 1024 || maxFiles < 1 || maxEntriesPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        DirectoryPath = Path.GetFullPath(directory);
        this.component = component; this.version = version; this.session = session;
        this.maxBytes = maxBytes; this.maxFiles = maxFiles;
        this.maxEntriesPerSecond = maxEntriesPerSecond;
        string name = new(component.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        prefix = $"zzzwallpaper-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{name}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        lock (gate) { try { Open(); } catch (Exception ex) { Failed(ex); } }
    }

    public void Write(string level, string category, string message, Exception? exception = null, object? data = null, DateTimeOffset? occurredAt = null, int? originatingThread = null, bool flush = true, string? operationId = null)
    {
        lock (gate)
        {
            if (disposed) return;
            try
            {
                long now = Environment.TickCount64;
                if (stream == null && now < retryAt) return;
                if (now - rateWindow >= 1000) { rateWindow = now; burst = 0; }
                if (++burst > maxEntriesPerSecond && !(data != null && level == "Error")) { suppressed++; return; }
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    time = occurredAt ?? DateTimeOffset.Now, level, component, category, version, session, operationId, data,
                    processId = Environment.ProcessId, threadId = originatingThread ?? Environment.CurrentManagedThreadId,
                    message = Safe(message, 16000), exception = exception == null ? null : Safe(exception.ToString(), 32000),
                    suppressedSinceLastEntry = suppressed
                }, JsonOptions) + "\n");
                // Keep even unusually long exception records within the per-file limit.
                if (bytes.Length > maxBytes)
                    bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                        time = DateTimeOffset.Now, level, component, category, version, session,
                        processId = Environment.ProcessId, message = "Oversized log entry omitted", suppressedSinceLastEntry = suppressed
                    }, JsonOptions) + "\n");
                if (stream == null || stream.Length + bytes.Length > maxBytes) Open();
                stream!.Write(bytes); if(flush)stream.Flush();
                suppressed = 0; LastError = null;
            }
            catch (Exception ex) { Failed(ex); }
        }
    }

    public void Flush() {lock(gate) {try {stream?.Flush();}catch(Exception ex) {Failed(ex);}}}

    private string Safe(string text, int limit)
    {
        if (!string.IsNullOrEmpty(userProfile)) text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return text.Length <= limit ? text : text[..limit] + " [truncated]";
    }

    private void Open()
    {
        stream?.Dispose(); stream = null;
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, $"{prefix}-{part++:D5}.jsonl");
        stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        CurrentFile = path;
        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            // Reserve a slot for the open file even if timestamps tie or the
            // filesystem has not updated its timestamp yet.
            var files = new DirectoryInfo(DirectoryPath).GetFiles("zzzwallpaper-*.jsonl")
                .Where(f => f.FullName != CurrentFile && (f.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name,StringComparer.Ordinal).ToArray();
            for (int i = 0; i < files.Length; i++)
            {
                if (i >= maxFiles - 1 || files[i].LastWriteTimeUtc < DateTime.UtcNow - Retention)
                    try { files[i].Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Failed(Exception ex)
    {
        LastError = ex.Message; retryAt = Environment.TickCount64 + 10000;
        try { stream?.Dispose(); } catch { }
        stream = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (suppressed > 0) { burst = 0; Write("Warning", "Logging", "Repeated log entries were rate limited."); }
            disposed = true;
            try { stream?.Dispose(); } catch { }
            stream = null;
        }
    }
}
