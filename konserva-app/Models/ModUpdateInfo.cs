namespace Konserva.Models;

/// <summary>
/// Найденное в репозитории обновление конкретного файла мода.
/// </summary>
public class ModUpdateInfo
{
    /// <summary>Путь к локальному jar-файлу.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>SHA-512 локального файла.</summary>
    public string CurrentHash { get; set; } = string.Empty;

    /// <summary>Версия, соответствующая локальному файлу (если найдена в репозитории).</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>Идентификатор версии в репозитории.</summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>Идентификатор проекта в репозитории.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Номер новой версии.</summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>URL для скачивания новой версии.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Имя файла новой версии.</summary>
    public string DownloadFileName { get; set; } = string.Empty;

    /// <summary>SHA-512 новой версии (для проверки после скачивания).</summary>
    public string LatestHash { get; set; } = string.Empty;

    /// <summary>Ссылка на страницу версии (для открытия в браузере).</summary>
    public string VersionPageUrl => $"https://modrinth.com/project/{ProjectId}/version/{VersionId}";
}
