using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Резервное копирование модов перед обновлением (в папку сервера backups/mods).
/// </summary>
public static class ModBackup
{
    /// <summary>
    /// Возвращает папку для бэкапов модов сервера и создаёт её при необходимости.
    /// </summary>
    public static string CreateBackupDirectory(string serverPath)
    {
        var dir = Path.Combine(serverPath, "backups", "mods");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
