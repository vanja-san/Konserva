using CommunityToolkit.Mvvm.ComponentModel;

namespace Konserva.Models;

/// <summary>
/// Информация о моде
/// </summary>
public partial class ModItem : ObservableObject, IItemEntry
{
    /// <summary>
    /// Имя мода (без расширения)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    private string _name = string.Empty;

    /// <summary>
    /// Версия мода
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private string _version = string.Empty;

    /// <summary>
    /// Показывать ли версию рядом с названием (не пустая)
    /// </summary>
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);

    /// <summary>
    /// Показывать ли строку версии (текущая или "текущая -> новая")
    /// </summary>
    public bool HasVersionDisplay => HasVersion || UpdateAvailable;

    /// <summary>
    /// Версия для отображения: текущая, либо "текущая -> новая" при доступном обновлении
    /// </summary>
    public string VersionDisplay => UpdateAvailable && LatestVersion != null
        ? (HasVersion ? $"{Version} -> {LatestVersion}" : LatestVersion)
        : Version;

    /// <summary>
    /// Имя файла (например "OptiFine.jar")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Полный путь к файлу (может быть .jar или .jar.disabled)
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Размер файла в байтах
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Включён ли мод (.jar) или отключён (.jar.disabled)
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// Доступно ли обновление для этого мода
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private bool _updateAvailable;

    /// <summary>
    /// Идёт ли сейчас обновление этого мода
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotUpdating))]
    private bool _isUpdating;

    /// <summary>
    /// НЕ идёт обновление и нет успешного результата (кнопка видна)
    /// </summary>
    public bool IsNotUpdating => !IsUpdating && !UpdateSucceeded;

    /// <summary>
    /// Успешно ли завершилось обновление (показать галочку)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotUpdating))]
    private bool _updateSucceeded;

    /// <summary>
    /// Номер доступной новой версии
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private string? _latestVersion;

    /// <summary>
    /// Тултип кнопки обновления: "Обновить до vX.X.X"
    /// </summary>
    public string? UpdateTooltip => LatestVersion != null
        ? $"{Localization.LocalizationManager.Get("ServerDetail_Mods_UpdateTo")} v{LatestVersion}"
        : null;
}
