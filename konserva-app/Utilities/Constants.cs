namespace Konserva.Utilities;

/// <summary>
/// Общие константы приложения
/// </summary>
public static class Constants
{
    #region Общие

    /// <summary>
    /// Символы новой строки для Split
    /// </summary>
    public static readonly char[] NewLineChars = { '\r', '\n' };

    #endregion

    #region Базовые константы

    /// <summary>
    /// Миллисекунд в одной секунде
    /// </summary>
    public const int MsPerSecond = 1000;

    #endregion

    #region Таймауты и задержки (мс)

    /// <summary>
    /// Таймаут ожидания проверки Java (10 сек)
    /// </summary>
    public const int JavaCheckTimeoutMs = 10000;

    /// <summary>
    /// Таймаут ожидания проверки пути Java (5 сек)
    /// </summary>
    public const int JavaPathCheckTimeoutMs = 5000;

    /// <summary>
    /// Таймаут ожидания остановки сервера (60 сек)
    /// </summary>
    public const int ServerStopTimeoutMs = 60000;

    /// <summary>
    /// Задержка между проверками статуса сервера (1 сек)
    /// </summary>
    public const int ServerStatusCheckDelayMs = 1000;

    /// <summary>
    /// Таймаут ожидания установки сервера (5 сек)
    /// </summary>
    public const int ServerInstallCheckTimeoutMs = 5000;

    /// <summary>
    /// Базовая задержка для повторных попыток (1 сек)
    /// </summary>
    public const int RetryBaseDelayMs = 1000;

    #endregion

    #region Ограничения RAM (MB)

    /// <summary>
    /// Минимальное значение RAM (256 MB)
    /// </summary>
    public const int MinRamMb = 256;

    /// <summary>
    /// Максимальное значение RAM (64 GB)
    /// </summary>
    public const int MaxRamMb = 65536;

    /// <summary>
    /// RAM по умолчанию (4 GB)
    /// </summary>
    public const int DefaultRamMinMb = 1024;

    /// <summary>
    /// RAM по умолчанию (4 GB)
    /// </summary>
    public const int DefaultRamMaxMb = 4096;

    #endregion

    #region Ограничения путей

    /// <summary>
    /// Максимальная длина пути (260 символов для Windows)
    /// </summary>
    public const int MaxPathLength = 260;

    #endregion

    #region Ограничения настроек

    /// <summary>
    /// Максимальная задержка авто-рестарта (3600 сек = 1 час)
    /// </summary>
    public const int MaxAutoRestartDelaySec = 3600;

    /// <summary>
    /// Порт сервера по умолчанию (25565)
    /// </summary>
    public const int DefaultServerPort = 25565;

    #endregion

    #region Задержки UI (мс)

    /// <summary>
    /// Задержка авто-закрытия InfoBar (5 сек)
    /// </summary>
    public const int InfoBarAutoCloseDelayMs = 5000;

    #endregion

    #region Ограничения логов

    /// <summary>
    /// Максимальное количество строк в логе (1000)
    /// </summary>
    public const int MaxLogLines = 1000;

    #endregion

    #region Сетевые настройки

    /// <summary>
    /// Порог компрессии сети (256 байт)
    /// </summary>
    public const int NetworkCompressionThreshold = 256;

    /// <summary>
    /// Максимальное время тика сервера (60000 мс = 1 мин)
    /// </summary>
    public const int MaxTickTimeMs = 60000;

    #endregion
}
