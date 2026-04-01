namespace Konserva.Models;

/// <summary>
/// Информация об установленной Java
/// </summary>
public class JavaInstallation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? AddedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Отображаемое имя
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(Name)
        ? $"Java {MajorVersion} ({Version})"
        : $"{Name} (Java {MajorVersion})";

    /// <summary>
    /// Проверка: существует ли файл Java
    /// </summary>
    public bool Exists => System.IO.File.Exists(Path) || System.IO.File.Exists(Path + ".exe");

    /// <summary>
    /// Создание копии
    /// </summary>
    public JavaInstallation Clone() => new()
    {
        Name = Name,
        Path = Path,
        Version = Version,
        MajorVersion = MajorVersion,
        IsDefault = IsDefault,
        AddedDate = AddedDate
    };
}