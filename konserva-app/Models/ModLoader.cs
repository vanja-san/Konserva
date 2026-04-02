namespace Konserva.Models;

/// <summary>
/// Тип модлоадера
/// </summary>
public enum ModLoaderType
{
    Vanilla,
    Forge,
    NeoForge,
    Fabric,
    Quilt,
    Paper,
    Purpur
}

/// <summary>
/// Конфигурация модлоадера
/// </summary>
public class ModLoader
{
    public ModLoaderType Type { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? LoaderVersion { get; set; }

    /// <summary>
    /// Проверка: используется ли кастомный модлоадер (не Vanilla)
    /// </summary>
    public bool IsModded => Type != ModLoaderType.Vanilla;

    /// <summary>
    /// Полное название модлоадера
    /// </summary>
    public string FullName => IsModded ? $"{Type} {LoaderVersion ?? Version}" : "Vanilla";

    /// <summary>
    /// Клонирование модлоадера
    /// </summary>
    public ModLoader Clone() => new()
    {
        Type = Type,
        Version = Version,
        LoaderVersion = LoaderVersion
    };
}
