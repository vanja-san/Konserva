using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Konserva.Utilities;

/// <summary>
/// Logging level
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Log record
/// </summary>
public sealed record LogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    DateTime Timestamp,
    string? Category = null
);

/// <summary>
/// Asynchronous logger built on Channels (.NET 10)
/// </summary>
public sealed class Logger : IAsyncDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    // Channel for asynchronous log writing.
    // SingleWriter must stay false: entries are written from the UI thread, the server
    // stdout/stderr readers, and the timers, so the single-writer contract was never true.
    private static readonly Channel<LogEntry> _logChannel =
        Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private static readonly string _logFilePath;
    private static readonly string _logDir;
    private static volatile bool _initialized;
    private static readonly Lock _initLock = new();
    private static CancellationTokenSource? _cts;
    private static Task? _writeTask;
    private static readonly string? _sessionLogFileName;

    // Throttles directory enumeration: without it every single log line triggered a
    // Directory.GetFiles + Task.Run hop, which dominated the cost of logging itself.
    private static long _lastRotationTicks;
    private static readonly TimeSpan RotationInterval = TimeSpan.FromMinutes(5);

    // Counter of pending writes and the signal for FlushAsync
    private static int _pendingLogEntries;
    private static TaskCompletionSource _flushTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Cache of recent logs (thread-safe)
    private static readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _recentLogs = new();
    private const int MaxRecentLogs = 500;

    static Logger()
    {
        // Determine the log path inside the application folder
        var exeDir = AppContext.BaseDirectory;
        _logDir = Path.Combine(exeDir, "Logs");
        Directory.CreateDirectory(_logDir);

        // Cleanup of old logs — keep at most 2 files
        CleanupOldLogs();

        // Session file name: logs-21.01.26-11.22.log
        _sessionLogFileName = $"logs-{SystemTime.Now:dd.MM.yy-HH.mm}.log";
        _logFilePath = Path.Combine(_logDir, _sessionLogFileName);
    }

    /// <summary>
    /// Deletes old logs — keeps at most 2 files
    /// </summary>
    private static void CleanupOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDir, "logs-*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            // Delete all except the 2 newest ones
            foreach (var file in logFiles.Skip(2))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete old log file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to cleanup old logs: {ex.Message}");
        }
    }

    /// <summary>
    /// Logger initialization
    /// </summary>
    public static void Initialize()
    {
        // Two threads (UI + a process reader) can both observe _initialized == false;
        // without the lock both would start a reader on the same channel.
        lock (_initLock)
        {
            if (_initialized)
                return;

            _initialized = true;
            _cts = new CancellationTokenSource();
            _writeTask = ProcessLogQueueAsync(_cts.Token);
        }

        Info("Logger initialized", "System");
        Info($".NET Runtime: {Environment.Version}", "System");
        Info($"OS: {Environment.OSVersion}", "System");
    }

    /// <summary>
    /// Background processing of the log queue
    /// </summary>
    private static async Task ProcessLogQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _logChannel.Reader.ReadAllAsync(ct))
            {
                // Store in the recent logs cache
                CacheLogEntry(entry);

                // Write to the file
                await WriteToFileAsync(entry);

                // Output to Debug
                System.Diagnostics.Debug.WriteLine(FormatLogEntry(entry));

                // Signal FlushAsync once all pending entries are processed
                if (Interlocked.Decrement(ref _pendingLogEntries) == 0)
                {
                    var previous = Interlocked.Exchange(ref _flushTcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                    previous.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logger fatal error: {ex}");
        }
    }

    /// <summary>
    /// Caching of a log entry
    /// </summary>
    private static void CacheLogEntry(LogEntry entry)
    {
        _recentLogs.Enqueue(entry);

        while (_recentLogs.Count > MaxRecentLogs && _recentLogs.TryDequeue(out _))
        {
            // Remove old entries
        }
    }

    /// <summary>
    /// Writing a log entry to the file
    /// </summary>
    private static async Task WriteToFileAsync(LogEntry entry)
    {
        try
        {
            var logLine = FormatLogEntry(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(_logFilePath, logLine, Utf8NoBom);

            RotateLogsIfNeeded();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
        }
    }

    /// <summary>
    /// Log rotation (deletion of old files), throttled to at most once per
    /// <see cref="RotationInterval"/>. Called for every entry, so an unconditional
    /// directory enumeration here would run per log line.
    /// </summary>
    private static void RotateLogsIfNeeded()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastRotationTicks);

        if (nowTicks - lastTicks < RotationInterval.Ticks)
            return;

        // Claim this slot; a concurrent writer that wins the race skips the rotation.
        if (Interlocked.CompareExchange(ref _lastRotationTicks, nowTicks, lastTicks) != lastTicks)
            return;

        try
        {
            const int maxSessionLogs = 50; // Keep no more than 50 session files

            var logFiles = Directory.GetFiles(_logDir, "logs-*.log")
                .Select(f => new { File = f, Time = File.GetLastWriteTime(f) })
                .OrderByDescending(f => f.Time)
                .ToList();

            // Delete old files, keeping at most maxSessionLogs
            foreach (var oldFile in logFiles.Skip(maxSessionLogs))
            {
                try
                {
                    File.Delete(oldFile.File);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to rotate log file {oldFile.File}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to rotate logs: {ex.Message}");
        }
    }

    /// <summary>
    /// Formatting of a log entry
    /// </summary>
    private static string FormatLogEntry(LogEntry entry)
    {
        var levelStr = entry.Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => entry.Level.ToString().ToUpperInvariant()
        };

        var category = entry.Category != null ? $" [{entry.Category}]" : "";
        var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = $"{entry.Message}{(entry.Exception != null ? $": {entry.Exception.Message}" : "")}";

        return $"[{timestamp}][{levelStr}]{category} {message}";
    }

    /// <summary>
    /// Log rotation (deletion of old files)
    /// </summary>
    private static async Task RotateLogsIfNeededAsync()
    {
        try
        {
            const int maxSessionLogs = 50; // Keep no more than 50 session files

            await Task.Run(() =>
            {
                // Get all log files and sort them by time
                var logFiles = Directory.GetFiles(_logDir, "logs-*.log")
                    .Select(f => new { File = f, Time = File.GetLastWriteTime(f) })
                    .OrderByDescending(f => f.Time)
                    .ToList();

                // Delete old files, keeping at most maxSessionLogs
                foreach (var oldFile in logFiles.Skip(maxSessionLogs))
                {
                    try
                    {
                        File.Delete(oldFile.File);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to rotate log file {oldFile.File}: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to rotate logs: {ex.Message}");
        }
    }

    #region Public Logging Methods

    /// <summary>
    /// Logging of an informational message
    /// </summary>
    public static void Info(string message, string? category = null) =>
        Log(LogLevel.Info, message, null, category);

    /// <summary>
    /// Logging of a warning
    /// </summary>
    public static void Warning(string message, string? category = null) =>
        Log(LogLevel.Warning, message, null, category);

    /// <summary>
    /// Logging of an error
    /// </summary>
    public static void Error(string message, Exception? ex = null, string? category = null) =>
        Log(LogLevel.Error, message, ex, category);

    /// <summary>
    /// Logging of a critical error
    /// </summary>
    public static void Critical(string message, Exception? ex = null, string? category = null) =>
        Log(LogLevel.Critical, message, ex, category);

    /// <summary>
    /// Logging of debug information
    /// </summary>
    public static void Debug(string message, string? category = null)
    {
#if DEBUG
        Log(LogLevel.Debug, message, null, category);
#endif
    }

    /// <summary>
    /// Core log writing
    /// </summary>
    private static void Log(LogLevel level, string message, Exception? ex, string? category)
    {
        if (!_initialized)
            Initialize();

        var entry = new LogEntry(level, message, ex, SystemTime.Now, category);

        Interlocked.Increment(ref _pendingLogEntries);

        // Push the entry to the channel; if the queue is full, skip it
        if (!_logChannel.Writer.TryWrite(entry))
        {
            Interlocked.Decrement(ref _pendingLogEntries);
            System.Diagnostics.Debug.WriteLine($"Log dropped (queue full): {message}");
        }
    }

    #endregion

    #region Public Utility Methods

    /// <summary>
    /// Get the most recent log entries
    /// </summary>
    public static IEnumerable<LogEntry> GetRecentLogs(int count = 100) =>
        _recentLogs.TakeLast(count);

    /// <summary>
    /// Get the most recent log lines in formatted form
    /// </summary>
    public static IEnumerable<string> GetRecentLogLines(int count = 100) =>
        _recentLogs.TakeLast(count).Select(FormatLogEntry);

    /// <summary>
    /// Wait for all logs to be written
    /// </summary>
    public static async Task FlushAsync()
    {
        // Capture the TCS before checking pending to avoid a race:
        // if pending drops to zero between the check and the wait — the TCS will already be completed
        var tcs = _flushTcs;
        if (Volatile.Read(ref _pendingLogEntries) <= 0)
            return;

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Clear the log cache
    /// </summary>
    public static void ClearCache()
    {
        while (_recentLogs.TryDequeue(out _))
        {
            // Clearing
        }
    }

    #endregion

    #region IAsyncDisposable

    /// <summary>
    /// Stops the background writer and flushes whatever is still queued.
    /// Every member of this class is static, so this is the only way to shut it down —
    /// the instance <see cref="IAsyncDisposable"/> implementation exists solely to satisfy
    /// the interface and is never invoked.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();

            // Wait for writing to finish
            if (_writeTask != null)
            {
                try
                {
                    await _writeTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                    System.Diagnostics.Debug.WriteLine("Logger disposal timed out");
                }
            }

            _cts.Dispose();
            _cts = null;
        }

        _logChannel.Writer.Complete();
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    #endregion
}