using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Репозиторий версий модов. v1 — только Modrinth (публичные эндпоинты, без API-ключа).
/// </summary>
public interface IModRepositoryApi
{
    /// <summary>
    /// Текущие (локальные) версии по SHA-512 хешам файлов.
    /// Нужны для date_published, чтобы отличить реальное обновление от даунгрейда.
    /// Ключ словаря — переданный хеш.
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthVersion>> GetCurrentVersionsAsync(
        IReadOnlyCollection<string> hashes,
        CancellationToken ct = default);

    /// <summary>
    /// Последние версии, совместимые с указанными лоадерами, версией игры и каналами.
    /// Файлы без совместимой версии в ответ не попадают. Ключ словаря — переданный хеш.
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthVersion>> GetLatestVersionsAsync(
        IReadOnlyCollection<string> hashes,
        IReadOnlyCollection<string> loaders,
        string gameVersion,
        IReadOnlyCollection<string> versionTypes,
        CancellationToken ct = default);

    /// <summary>
    /// Проекты Modrinth по их id (GET /v2/projects?ids=[...]).
    /// Возвращает словарь: id проекта → проект (нужно для реальных названий модов).
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthProject>> GetProjectsAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);
}
