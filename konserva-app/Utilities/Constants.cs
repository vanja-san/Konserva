using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Application-wide constants
/// </summary>
public static class Constants
{
    #region Common

    /// <summary>
    /// Newline characters used for Split
    /// </summary>
    public static readonly char[] NewLineChars = { '\r', '\n' };

    #endregion

    #region Basic constants

    /// <summary>
    /// Milliseconds in one second
    /// </summary>
    public const int MsPerSecond = 1000;

    #endregion

    #region Timeouts and delays (ms)

    /// <summary>
    /// Timeout for waiting on the Java check (10 sec)
    /// </summary>
    public const int JavaCheckTimeoutMs = 10000;

    /// <summary>
    /// Timeout for waiting on the Java path check (5 sec)
    /// </summary>
    public const int JavaPathCheckTimeoutMs = 5000;

    /// <summary>
    /// Timeout for waiting on server shutdown (60 sec)
    /// </summary>
    public const int ServerStopTimeoutMs = 60000;

    /// <summary>
    /// Delay between server status checks (1 sec)
    /// </summary>
    public const int ServerStatusCheckDelayMs = 1000;

    /// <summary>
    /// Truncation length for logs (characters)
    /// </summary>
    public const int LogTruncationLength = 200;

    /// <summary>
    /// Timeout for waiting on the server install check (5 sec)
    /// </summary>
    public const int ServerInstallCheckTimeoutMs = 5000;

    /// <summary>
    /// Base delay for retries (1 sec)
    /// </summary>
    public const int RetryBaseDelayMs = 1000;

    #endregion

    #region RAM limits (MB)

    /// <summary>
    /// Minimum RAM value (256 MB)
    /// </summary>
    public const int MinRamMb = 256;

    /// <summary>
    /// Maximum RAM value (64 GB)
    /// </summary>
    public const int MaxRamMb = 65536;

    /// <summary>
    /// Default RAM (4 GB)
    /// </summary>
    public const int DefaultRamMinMb = 1024;

    /// <summary>
    /// Default RAM (4 GB)
    /// </summary>
    public const int DefaultRamMaxMb = 4096;

    #endregion

    #region Path limits

    /// <summary>
    /// Maximum path length (260 characters on Windows)
    /// </summary>
    public const int MaxPathLength = 260;

    /// <summary>
    /// Base servers directory (relative to the application directory)
    /// </summary>
    public static string ServersPath => Path.Combine(AppContext.BaseDirectory, "Servers");

    /// <summary>
    /// Directory for temporary downloads (installers, updates) next to the application
    /// </summary>
    public static string DownloadsPath => Path.Combine(AppContext.BaseDirectory, "downloads");

    #endregion

    #region Settings limits

    /// <summary>
    /// Maximum server name length (file system limitation)
    /// </summary>
    public const int MaxServerNameLength = 50;

    /// <summary>
    /// Maximum auto-restart delay (3600 sec = 1 hour)
    /// </summary>
    public const int MaxAutoRestartDelaySec = 3600;

    /// <summary>
    /// Default server port (25565)
    /// </summary>
    public const int DefaultServerPort = 25565;

    #endregion

    #region UI delays (ms)

    /// <summary>
    /// InfoBar auto-close delay (5 sec)
    /// </summary>
    public const int InfoBarAutoCloseDelayMs = 5000;

    #endregion

    #region Log limits

    /// <summary>
    /// Maximum number of lines in the log (1000)
    /// </summary>
    public const int MaxLogLines = 1000;

    #endregion

    #region Network settings

    /// <summary>
    /// Network compression threshold (256 bytes)
    /// </summary>
    public const int NetworkCompressionThreshold = 256;

    /// <summary>
    /// Maximum server tick time (60000 ms = 1 min)
    /// </summary>
    public const int MaxTickTimeMs = 60000;

    #endregion
}
