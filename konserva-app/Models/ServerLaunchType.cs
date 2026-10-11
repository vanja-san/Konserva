namespace Konserva.Models;

/// <summary>
/// Server launch type (determines the JVM arguments and the launch method).
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
