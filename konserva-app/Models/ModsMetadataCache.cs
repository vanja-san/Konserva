namespace Konserva.Models;

/// <summary>
/// Кэш метаданных модов (названия, версии и обновления), чтобы не ходить
/// в сеть при каждом открытии вкладки и после перезапуска приложения.
/// </summary>
public class ModsMetadataCacheData
{
    /// <summary>Время последней реальной проверки обновлений (UTC).</summary>
    public DateTime LastUpdatesCheckUtc { get; set; }

    /// <summary>Названия/версии модов: имя файла → метаданные.</summary>
    public Dictionary<string, ModMetadataCacheEntry> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Инвентаризация файлов на момент последней проверки: имя файла → размер.</summary>
    public Dictionary<string, long> ModFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Найденные обновления: имя файла → данные обновления (без версий — их содержит "Updates").</summary>
    public Dictionary<string, ModUpdateCacheEntry> Updates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Закэшированные название и версия мода, действительные для файла указанного размера.
/// </summary>
public class ModMetadataCacheEntry
{
    public long FileSize { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// SHA-512 локального файла (нижний регистр hex, формат Modrinth).
    /// Позволяет не пересчитывать хеши при каждом открытии вкладки.
    /// </summary>
    public string Sha512 { get; set; } = string.Empty;
}

/// <summary>
/// Закэшированное обновление мода, действительное для файла указанного размера.
/// </summary>
public class ModUpdateCacheEntry
{
    public long FileSize { get; set; }

    public ModUpdateInfo Update { get; set; } = new();
}