namespace Konserva.Models;

/// <summary>
/// Тип запуска сервера (определяет аргументы JVM и способ запуска).
/// </summary>
public enum ServerLaunchType
{
    /// <summary>Vanilla, Paper</summary>
    Standard,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}
