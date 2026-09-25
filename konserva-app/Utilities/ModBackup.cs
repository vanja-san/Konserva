using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Резервное копирование модов перед обновлением (в общую папку Backups/Mods).
/// </summary>
public static class ModBackup
{
    /// <summary>
    /// Общая папка для резервных копий модов (без разделения по серверам/версиям).
    /// </summary>
    public static string ModsBackupPath => Path.Combine(ServerBackup.BackupsPath, "Mods");

    /// <summary>
    /// Возвращает общую папку для бэкапов модов и создаёт её при необходимости.
    /// </summary>
    public static string CreateBackupDirectory()
    {
        Directory.CreateDirectory(ModsBackupPath);
        return ModsBackupPath;
    }
}
