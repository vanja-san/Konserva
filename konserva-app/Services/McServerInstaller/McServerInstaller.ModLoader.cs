using Konserva.Models;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Определение типа модлоадера по файлам в папке сервера.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Получить тип запуска сервера
    /// </summary>
    public ServerLaunchType GetServerLaunchType(string serverPath)
    {
        if (File.Exists(Path.Combine(serverPath, "fabric-server-launch.jar")))
            return ServerLaunchType.Fabric;

        // Quilt может иметь разные имена файлов
        var quiltJars = Directory.GetFiles(serverPath, "quilt-server-*.jar");
        if (quiltJars.Length > 0)
            return ServerLaunchType.Quilt;

        // Forge (старый формат forge-*.jar или новый *-shim.jar)
        if (Directory.GetFiles(serverPath, "forge-*.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "*-shim.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "neoforge-*.jar").Length > 0)
            return ServerLaunchType.NeoForge;

        // NeoForge 21.x+ может не иметь jar в корне — проверяем маркер конфига запуска
        if (File.Exists(Path.Combine(serverPath, ".neoforge-launch.json")))
            return ServerLaunchType.NeoForge;

        // Vanilla, Paper используют стандартный запуск
        return ServerLaunchType.Standard;
    }
}
