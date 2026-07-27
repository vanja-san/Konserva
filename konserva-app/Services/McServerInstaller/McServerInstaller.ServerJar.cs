using System.IO;
using System.IO.Compression;

namespace Konserva.Services;

/// <summary>
/// Поиск jar-файла сервера и проверка манифеста.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Проверяет, есть ли в jar-файле Main-Class в манифесте.
    /// Это обязательное условие для запуска через java -jar.
    /// </summary>
    public static bool HasMainClass(string jarPath)
    {
        try
        {
            if (!File.Exists(jarPath))
                return false;

            using var stream = File.OpenRead(jarPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifestEntry == null)
                return false;

            using var reader = new StreamReader(manifestEntry.Open());
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Найти исполняемый jar-файл сервера для запуска через -jar.
    /// Modern Forge (47.x+) и NeoForge возвращают пустую строку — они используют @args файлы.
    /// </summary>
    public string FindServerJar(string serverPath)
    {
        // Vanilla / standard servers
        var serverJar = Path.Combine(serverPath, "server.jar");
        if (File.Exists(serverJar))
            return serverJar;

        // Fabric
        var fabricJar = Path.Combine(serverPath, "fabric-server-launch.jar");
        if (File.Exists(fabricJar))
            return fabricJar;

        // Quilt
        var quiltJars = Directory.GetFiles(serverPath, "quilt-server-*.jar");
        if (quiltJars.Length > 0)
            return quiltJars[0];

        // Paper
        var paperJar = Path.Combine(serverPath, "paper.jar");
        if (File.Exists(paperJar))
            return paperJar;

        // Legacy Forge (forge-*.jar с Main-Class в манифесте)
        var forgeJars = Directory.GetFiles(serverPath, "forge-*.jar");
        if (forgeJars.Length > 0 && HasMainClass(forgeJars[0]))
            return forgeJars[0];

        // Legacy Forge shim (альтернативный формат)
        var shimJars = Directory.GetFiles(serverPath, "*-shim.jar");
        if (shimJars.Length > 0 && HasMainClass(shimJars[0]))
            return shimJars[0];

        // Modern Forge/NeoForge используют @args файлы, а не -jar
        return string.Empty;
    }
}
