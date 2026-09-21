using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Резервное копирование модов перед обновлением (в общую папку Backups/).
/// </summary>
public static class ModBackup
{
    /// <summary>
    /// Создаёт отдельную папку для бэкапа модов сервера и возвращает её путь.
    /// </summary>
    public static string CreateBackupDirectory(string serverName)
    {
        Directory.CreateDirectory(ServerBackup.BackupsPath);

        var safeName = string.Concat((serverName ?? string.Empty).Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "server";

        var dir = Path.Combine(
            ServerBackup.BackupsPath,
            $"{safeName}-mods-{DateTime.Now:yyyyMMdd-HHmmss-fff}");

        Directory.CreateDirectory(dir);
        return dir;
    }
}
