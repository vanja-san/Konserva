using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Backup of mods before an update (into the server's backups/mods folder).
/// </summary>
public static class ModBackup
{
    /// <summary>
    /// Path to the server's mod backup folder.
    /// </summary>
    public static string GetBackupDirectory(string serverPath)
    {
        return Path.Combine(serverPath, "backups", "mods");
    }

    /// <summary>
    /// Returns the server's mod backup folder and creates it if necessary.
    /// </summary>
    public static string CreateBackupDirectory(string serverPath)
    {
        var dir = GetBackupDirectory(serverPath);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
