using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Резервное копирование модов перед обновлением (в папку сервера backups/mods).
/// </summary>
public static class ModBackup
{
    /// <summary>
    /// Путь к папке бэкапов модов сервера.
    /// </summary>
    public static string GetBackupDirectory(string serverPath)
    {
        return Path.Combine(serverPath, "backups", "mods");
    }

    /// <summary>
    /// Возвращает папку для бэкапов модов сервера и создаёт её при необходимости.
    /// </summary>
    public static string CreateBackupDirectory(string serverPath)
    {
        var dir = GetBackupDirectory(serverPath);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
