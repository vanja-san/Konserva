using System.Collections.ObjectModel;
using System.IO;

namespace Konserva.Models;

/// <summary>
/// Конфигурация приложения
/// </summary>
public class AppConfig
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";

    /// <summary>
    /// Список установленных Java
    /// </summary>
    public ObservableCollection<JavaInstallation> JavaInstallations { get; set; } = [];

    /// <summary>
    /// ID Java по умолчанию
    /// </summary>
    public string? DefaultJavaId { get; set; }

    /// <summary>
    /// Путь к Java по умолчанию (для обратной совместимости)
    /// </summary>
    public string DefaultJavaPath
    {
        get => string.IsNullOrEmpty(field) ? "java" : field;
        set => field = value;
    }

    public string ServersDirectory
    {
        get => string.IsNullOrEmpty(field) ? Path.Combine(AppContext.BaseDirectory, "Servers") : field;
        set => field = value;
    }

    public int DefaultRamMin { get; set; } = 1024;
    public int DefaultRamMax { get; set; } = 4096;
    public bool CheckUpdates { get; set; } = true;

    /// <summary>
    /// Источник загрузки: VanillaApi (официальный) или BMCLAPI (зеркало)
    /// </summary>
    public string DownloadSource { get; set; } = "VanillaApi";

    /// <summary>
    /// Последняя проверка обновлений (UTC).
    /// </summary>
    public DateTime? LastUpdateCheck { get; set; }

    public List<string> RecentServers { get; set; } = [];

    /// <summary>
    /// API Endpoints для внешних запросов
    /// </summary>
    public ApiEndpoints ApiEndpoints { get; set; } = new();

    /// <summary>
    /// Путь к конфигурационной папке (рядом с exe)
    /// </summary>
    public static string ConfigDirectory
    {
        get
        {
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// Получить Java по умолчанию
    /// </summary>
    public JavaInstallation? GetDefaultJava()
    {
        if (string.IsNullOrEmpty(DefaultJavaId))
        {
            return JavaInstallations.FirstOrDefault(j => j.IsDefault)
                ?? JavaInstallations.FirstOrDefault();
        }

        return JavaInstallations.FirstOrDefault(j => j.Id == DefaultJavaId);
    }

    /// <summary>
    /// Получить путь к Java по умолчанию
    /// </summary>
    public string GetDefaultJavaPath() => GetDefaultJava()?.Path ?? DefaultJavaPath;

    /// <summary>
    /// Получить API Endpoints
    /// </summary>
    public ApiEndpoints GetApiEndpoints() => ApiEndpoints ?? new ApiEndpoints();
}