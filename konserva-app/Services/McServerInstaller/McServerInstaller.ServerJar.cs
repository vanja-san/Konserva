using System.IO;
using System.IO.Compression;

namespace Konserva.Services;

/// <summary>
/// Locating the server jar file and validating its manifest.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Checks whether the jar file has a Main-Class in its manifest.
    /// This is a mandatory condition for launching via java -jar.
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
    /// Find the executable server jar file to launch via -jar.
    /// Modern Forge (47.x+) and NeoForge return an empty string — they use @args files.
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

        // Legacy Forge (forge-*.jar with a Main-Class in the manifest)
        var forgeJars = Directory.GetFiles(serverPath, "forge-*.jar");
        if (forgeJars.Length > 0 && HasMainClass(forgeJars[0]))
            return forgeJars[0];

        // Legacy Forge shim (alternative format)
        var shimJars = Directory.GetFiles(serverPath, "*-shim.jar");
        if (shimJars.Length > 0 && HasMainClass(shimJars[0]))
            return shimJars[0];

        // Modern Forge/NeoForge use @args files instead of -jar
        return string.Empty;
    }
}
