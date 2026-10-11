using Konserva.Models;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Detecting the mod loader type from the files in the server folder.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get the server launch type
    /// </summary>
    public ServerLaunchType GetServerLaunchType(string serverPath)
    {
        if (File.Exists(Path.Combine(serverPath, "fabric-server-launch.jar")))
            return ServerLaunchType.Fabric;

        // Quilt may use different file names
        var quiltJars = Directory.GetFiles(serverPath, "quilt-server-*.jar");
        if (quiltJars.Length > 0)
            return ServerLaunchType.Quilt;

        // Forge (old forge-*.jar format or the newer *-shim.jar)
        if (Directory.GetFiles(serverPath, "forge-*.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "*-shim.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "neoforge-*.jar").Length > 0)
            return ServerLaunchType.NeoForge;

        // NeoForge 21.x+ may have no jar in the root — check the launch config marker
        if (File.Exists(Path.Combine(serverPath, ".neoforge-launch.json")))
            return ServerLaunchType.NeoForge;

        // Vanilla and Paper use the standard launch
        return ServerLaunchType.Standard;
    }
}
