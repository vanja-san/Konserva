using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Проверка обновлений модов через репозиторий (v1 — Modrinth).
/// </summary>
public interface IModUpdateService
{
    /// <summary>
    /// Проверяет обновления для указанных jar-файлов.
    /// Возвращает словарь: путь к локальному файлу → найденное обновление.
    /// Файлы без доступного обновления в результат не попадают.
    /// </summary>
    /// <param name="jarPaths">Пути к локальным jar-файлам модов.</param>
    /// <param name="loader">Загрузчик сервера.</param>
    /// <param name="gameVersion">Версия Minecraft сервера.</param>
    /// <param name="channel">Канал обновлений (release/beta/alpha).</param>
    /// <param name="progress">Отчёт о прогрессе (имя обрабатываемого файла).</param>
    /// <param name="precomputedHashes">Уже посчитанные SHA-512: путь → хеш. Позволяет не хешировать файлы повторно.</param>
    Task<IReadOnlyDictionary<string, ModUpdateInfo>> CheckForUpdatesAsync(
        IReadOnlyCollection<string> jarPaths,
        ModLoaderType loader,
        string gameVersion,
        ModUpdateChannel channel,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null);

    /// <summary>
    /// Скачивает новую версию, проверяет её SHA-512, делает резервную копию
    /// старого файла и заменяет его на месте (имя и состояние .disabled сохраняются).
    /// </summary>
    /// <param name="update">Найденное обновление.</param>
    /// <param name="backupDirectory">Папка для резервных копий (обычно из ModBackup).</param>
    /// <param name="progress">Отчёт о прогрессе (текст этапа).</param>
    Task<ModUpdateApplyResult> ApplyUpdateAsync(
        ModUpdateInfo update,
        string backupDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Реальные название и версия для указанных jar-файлов по данным репозитория.
    /// Возвращает словарь: путь к локальному файлу → название и версия проекта.
    /// Файлы, не найденные в репозитории, в результат не попадают.
    /// </summary>
    /// <param name="jarPaths">Пути к локальным jar-файлам модов.</param>
    /// <param name="progress">Отчёт о прогрессе (имя обрабатываемого файла).</param>
    /// <param name="precomputedHashes">Уже посчитанные SHA-512: путь → хеш. Позволяет не хешировать файлы повторно.</param>
    Task<IReadOnlyDictionary<string, ModMetadata>> ResolveModMetadataAsync(
        IReadOnlyCollection<string> jarPaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null);
}
