using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Konserva.Utilities;

/// <summary>
/// Уровень логирования
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
/// Запись лога
/// </summary>
public sealed record LogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    DateTime Timestamp,
    string? Category = null
);

/// <summary>
/// Асинхронный логгер с использованием Channels (.NET 10)
/// </summary>
public sealed class Logger : IAsyncDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    // Channel для асинхронной записи логов
    private static readonly Channel<LogEntry> _logChannel =
        Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    private static readonly string _logFilePath;
    private static readonly string _logDir;
    private static volatile bool _initialized;
    private static CancellationTokenSource? _cts;
    private static Task? _writeTask;
    private static readonly string? _sessionLogFileName;

    // Кэш последних логов (thread-safe)
    private static readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _recentLogs = new();
    private const int MaxRecentLogs = 500;

    static Logger()
    {
        // Определяем путь к логам в папке приложения
        var exeDir = AppContext.BaseDirectory;
        _logDir = Path.Combine(exeDir, "Logs");
        Directory.CreateDirectory(_logDir);

        // Очистка старых логов — оставляем максимум 2 файла
        CleanupOldLogs();

        // Имя файла сессии: logs-21.01.26-11.22.log
        _sessionLogFileName = $"logs-{DateTime.Now:dd.MM.yy-HH.mm}.log";
        _logFilePath = Path.Combine(_logDir, _sessionLogFileName);
    }

    /// <summary>
    /// Удаляет старые логи — оставляет максимум 2 файла
    /// </summary>
    private static void CleanupOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDir, "logs-*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            // Удаляем все кроме 2 самых новых
            foreach (var file in logFiles.Skip(2))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Игнорируем ошибки удаления
                }
            }
        }
        catch
        {
            // Игнорируем ошибки
        }
    }

    /// <summary>
    /// Инициализация логгера
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        _cts = new CancellationTokenSource();
        _writeTask = ProcessLogQueueAsync(_cts.Token);

        Info("Logger initialized", "System");
        Info($".NET Runtime: {Environment.Version}", "System");
        Info($"OS: {Environment.OSVersion}", "System");
    }

    /// <summary>
    /// Фоновая обработка очереди логов
    /// </summary>
    private static async Task ProcessLogQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _logChannel.Reader.ReadAllAsync(ct))
            {
                // Сохраняем в кэш последних логов
                CacheLogEntry(entry);

                // Записываем в файл
                await WriteToFileAsync(entry);

                // Выводим в Debug
                System.Diagnostics.Debug.WriteLine(FormatLogEntry(entry));
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logger fatal error: {ex}");
        }
    }

    /// <summary>
    /// Кэширование записи лога
    /// </summary>
    private static void CacheLogEntry(LogEntry entry)
    {
        _recentLogs.Enqueue(entry);

        while (_recentLogs.Count > MaxRecentLogs && _recentLogs.TryDequeue(out _))
        {
            // Удаляем старые записи
        }
    }

    /// <summary>
    /// Запись лога в файл
    /// </summary>
    private static async Task WriteToFileAsync(LogEntry entry)
    {
        try
        {
            var logLine = FormatLogEntry(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(_logFilePath, logLine, Utf8NoBom);

            // Периодическая ротация логов
            await RotateLogsIfNeededAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
        }
    }

    /// <summary>
    /// Форматирование записи лога
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
    /// Ротация логов (удаление старых)
    /// </summary>
    private static async Task RotateLogsIfNeededAsync()
    {
        try
        {
            const int maxSessionLogs = 50; // Хранить не более 50 файлов сессии

            await Task.Run(() =>
            {
                // Получаем все файлы логов и сортируем по времени
                var logFiles = Directory.GetFiles(_logDir, "logs-*.log")
                    .Select(f => new { File = f, Time = File.GetLastWriteTime(f) })
                    .OrderByDescending(f => f.Time)
                    .ToList();

                // Удаляем старые файлы, оставляя максимум maxSessionLogs
                foreach (var oldFile in logFiles.Skip(maxSessionLogs))
                {
                    try
                    {
                        File.Delete(oldFile.File);
                    }
                    catch
                    {
                        // Игнорируем ошибки удаления
                    }
                }
            });
        }
        catch
        {
            // Игнорируем ошибки ротации
        }
    }

    #region Public Logging Methods

    /// <summary>
    /// Логирование информационного сообщения
    /// </summary>
    public static void Info(string message, string? category = null) =>
        Log(LogLevel.Info, message, null, category);

    /// <summary>
    /// Логирование предупреждения
    /// </summary>
    public static void Warning(string message, string? category = null) =>
        Log(LogLevel.Warning, message, null, category);

    /// <summary>
    /// Логирование ошибки
    /// </summary>
    public static void Error(string message, Exception? ex = null, string? category = null) =>
        Log(LogLevel.Error, message, ex, category);

    /// <summary>
    /// Логирование критической ошибки
    /// </summary>
    public static void Critical(string message, Exception? ex = null, string? category = null) =>
        Log(LogLevel.Critical, message, ex, category);

    /// <summary>
    /// Логирование отладочной информации
    /// </summary>
    public static void Debug(string message, string? category = null)
    {
#if DEBUG
        Log(LogLevel.Debug, message, null, category);
#endif
    }

    /// <summary>
    /// Базовая запись лога
    /// </summary>
    private static void Log(LogLevel level, string message, Exception? ex, string? category)
    {
        if (!_initialized)
            Initialize();

        var entry = new LogEntry(level, message, ex, DateTime.Now, category);

        // Отправляем запись в channel, если очередь заполнена - пропускаем
        if (!_logChannel.Writer.TryWrite(entry))
        {
            System.Diagnostics.Debug.WriteLine($"Log dropped (queue full): {message}");
        }
    }

    #endregion

    #region Public Utility Methods

    /// <summary>
    /// Получить последние записи лога
    /// </summary>
    public static IEnumerable<LogEntry> GetRecentLogs(int count = 100) =>
        _recentLogs.TakeLast(count);

    /// <summary>
    /// Получить последние строки лога в форматированном виде
    /// </summary>
    public static IEnumerable<string> GetRecentLogLines(int count = 100) =>
        _recentLogs.TakeLast(count).Select(FormatLogEntry);

    /// <summary>
    /// Дождаться записи всех логов
    /// </summary>
    public static async Task FlushAsync()
    {
        while (!_logChannel.Reader.Completion.IsCompleted)
        {
            if (_logChannel.Reader.TryPeek(out _))
                await Task.Delay(10);
            else
                break;
        }
    }

    /// <summary>
    /// Очистить кэш логов
    /// </summary>
    public static void ClearCache()
    {
        while (_recentLogs.TryDequeue(out _))
        {
            // Очищаем
        }
    }

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();

            // Дожидаемся завершения записи
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
        }

        _logChannel.Writer.Complete();
    }

    #endregion
}