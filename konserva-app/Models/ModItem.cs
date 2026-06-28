using Konserva.Utilities;

namespace Konserva.Models;

/// <summary>
/// Информация о моде
/// </summary>
public class ModItem : ObservableObject
{
    private bool _enabled = true;

    /// <summary>
    /// Имя мода (без расширения)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Версия мода
    /// </summary>
    public string Version { get; set; } = string.Empty;

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
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
}
