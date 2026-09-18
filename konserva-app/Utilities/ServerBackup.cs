using Konserva.Models;
using System.IO;

namespace Konserva.Utilities;

/// <summary>
/// Вспомогательные операции для обновления загрузчика сервера:
/// резервное копирование папки и удаление файлов, которые установщик
/// пересоздаёт заново (мир, конфиги, моды и плагины не затрагиваются).
/// </summary>
public static class ServerBackup
{
    /// <summary>
    /// Папка резервных копий рядом с исполняемым файлом приложения
    /// </summary>
    public static string BackupsPath => Path.Combine(AppContext.BaseDirectory, "Backups");

    /// <summary>
    /// Полное копирование папки сервера в папку резервных копий.
    /// Возвращает путь созданной копии.
    /// </summary>
    public static string CreateBackupFolder(string serverPath, string serverName)
    {
        Directory.CreateDirectory(BackupsPath);

        var safeName = string.Concat(serverName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "server";

        var dest = Path.Combine(BackupsPath, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}");
        CopyDirectory(new DirectoryInfo(serverPath), new DirectoryInfo(dest));
        return dest;
    }

    /// <summary>
    /// Удаляет файлы и папки, которые установщик загрузчика пересоздаст сам:
    /// сам jar загрузчика, libraries/, скрипты запуска и служебные файлы.
    /// Возвращает список удалённых путей (относительно папки сервера).
    /// </summary>
    public static IReadOnlyList<string> RemoveLoaderFiles(string serverPath, ModLoaderType loaderType)
    {
        var removed = new List<string>();

        void TryDelete(string relativePath)
        {
            var full = Path.Combine(serverPath, relativePath);
            try
            {
                if (Directory.Exists(full))
                {
                    Directory.Delete(full, recursive: true);
                    removed.Add(relativePath + Path.DirectorySeparatorChar);
                }
                else if (File.Exists(full))
                {
                    File.Delete(full);
                    removed.Add(relativePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"ServerBackup.RemoveLoaderFiles: can't delete {relativePath}: {ex.Message}");
            }
        }

        void TryDeletePattern(string pattern)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(serverPath, pattern))
                    TryDelete(Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Logger.Warning($"ServerBackup.RemoveLoaderFiles: can't enumerate {pattern}: {ex.Message}");
            }
        }

        // Общие файлы, которые всегда пересоздаются установщиком
        TryDelete("eula.txt");
        TryDelete("installer.log");
        TryDelete("run.bat");
        TryDelete("run.sh");
        TryDelete(".neoforge-launch.json");
        TryDelete("libraries");

        // Файлы, специфичные для загрузчика
        switch (loaderType)
        {
            case ModLoaderType.Vanilla:
            case ModLoaderType.Paper:
                TryDeletePattern("server.jar");
                break;
            case ModLoaderType.Fabric:
                TryDeletePattern("fabric-server-launch.jar");
                break;
            case ModLoaderType.Quilt:
                TryDeletePattern("quilt-server-*.jar");
                break;
            case ModLoaderType.Forge:
            case ModLoaderType.NeoForge:
                TryDeletePattern("forge-*.jar");
                TryDeletePattern("neoforge-*.jar");
                break;
        }

        return removed;
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
    {
        if (!source.Exists)
            return;

        target.Create();

        foreach (var file in source.EnumerateFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name), overwrite: true);

        foreach (var dir in source.EnumerateDirectories())
            CopyDirectory(dir, target.CreateSubdirectory(dir.Name));
    }
}