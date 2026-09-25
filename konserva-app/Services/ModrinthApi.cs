using Konserva.Models;
using Konserva.Utilities;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Konserva.Services;

/// <summary>
/// Клиент Modrinth API v2 для проверки обновлений модов по SHA-512 хешам файлов.
/// Использует только публичные эндпоинты — API-ключ не требуется.
/// </summary>
public sealed class ModrinthApi : IModRepositoryApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public ModrinthApi(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public Task<IReadOnlyDictionary<string, ModrinthVersion>> GetCurrentVersionsAsync(
        IReadOnlyCollection<string> hashes,
        CancellationToken ct = default)
    {
        if (hashes.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, ModrinthVersion>>(Empty);

        var body = new CurrentVersionsRequest { Hashes = hashes };
        return PostAsync($"{ApiUrls.ModrinthApiBase}/version_files", body, nameof(GetCurrentVersionsAsync), ct);
    }

    public Task<IReadOnlyDictionary<string, ModrinthVersion>> GetLatestVersionsAsync(
        IReadOnlyCollection<string> hashes,
        IReadOnlyCollection<string> loaders,
        string gameVersion,
        IReadOnlyCollection<string> versionTypes,
        CancellationToken ct = default)
    {
        if (hashes.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, ModrinthVersion>>(Empty);

        var body = new LatestVersionsRequest
        {
            Hashes = hashes,
            Loaders = loaders,
            GameVersions = [gameVersion],
            VersionTypes = versionTypes
        };
        return PostAsync($"{ApiUrls.ModrinthApiBase}/version_files/update", body, nameof(GetLatestVersionsAsync), ct);
    }

    private static IReadOnlyDictionary<string, ModrinthVersion> Empty =>
        new Dictionary<string, ModrinthVersion>();

    private async Task<IReadOnlyDictionary<string, ModrinthVersion>> PostAsync<TBody>(
        string url,
        TBody body,
        string operation,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);

                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    Logger.Warning("Modrinth API v2 недоступен (410 Gone)", "ModrinthApi");
                    return Empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    // Повторяем только транзиентные 5xx, остальные ошибки бессмысленно ретраить
                    if ((int)response.StatusCode >= 500 && attempt == 1)
                    {
                        Logger.Warning($"Modrinth {operation}: HTTP {(int)response.StatusCode}, повторная попытка", "ModrinthApi");
                        await Task.Delay(500, ct).ConfigureAwait(false);
                        continue;
                    }

                    Logger.Warning($"Modrinth {operation}: HTTP {(int)response.StatusCode}", "ModrinthApi");
                    return Empty;
                }

                var result = await response.Content
                    .ReadFromJsonAsync<Dictionary<string, ModrinthVersion>>(JsonOptions, ct)
                    .ConfigureAwait(false);

                return result ?? Empty;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == 1)
                {
                    Logger.Warning($"Modrinth {operation}: {ex.Message}, повторная попытка", "ModrinthApi");
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    continue;
                }

                Logger.Warning($"Modrinth {operation}: {ex.Message}", "ModrinthApi");
                return Empty;
            }
        }

        return Empty;
    }

    public async Task<IReadOnlyDictionary<string, ModrinthProject>> GetProjectsAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ModrinthProject>();

        if (projectIds.Count == 0)
            return result;

        var uniqueIds = projectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var chunk in ChunkIds(uniqueIds, 100))
        {
            ct.ThrowIfCancellationRequested();

            var encodedIds = Uri.EscapeDataString(JsonSerializer.Serialize(chunk));
            var url = $"{ApiUrls.ModrinthApiBase}/projects?ids={encodedIds}";

            try
            {
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    Logger.Warning("Modrinth API v2 недоступен (410 Gone)", "ModrinthApi");
                    result.Clear();
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Modrinth {nameof(GetProjectsAsync)}: HTTP {(int)response.StatusCode}", "ModrinthApi");
                    continue;
                }

                var projects = await response.Content
                    .ReadFromJsonAsync<List<ModrinthProject>>(JsonOptions, ct)
                    .ConfigureAwait(false);

                if (projects is null)
                    continue;

                foreach (var project in projects)
                {
                    if (!string.IsNullOrWhiteSpace(project.Id))
                        result[project.Id] = project;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Modrinth {nameof(GetProjectsAsync)}: {ex.Message}", "ModrinthApi");
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetProjectVersionsAsync(
        string projectId,
        IReadOnlyCollection<string> loaders,
        IReadOnlyCollection<string> gameVersions,
        IReadOnlyCollection<string> versionTypes,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var query = new List<string>();
        if (loaders.Count > 0)
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(loaders))}");
        if (gameVersions.Count > 0)
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(gameVersions))}");
        if (versionTypes.Count > 0)
            query.Add($"version_type={string.Join(",", versionTypes)}");

        var url = $"{ApiUrls.ModrinthApiBase}/project/{Uri.EscapeDataString(projectId)}/version";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                Logger.Warning("Modrinth API v2 недоступен (410 Gone)", "ModrinthApi");
                return [];
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Info($"Проект {projectId} не найден (404)", "ModrinthApi");
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Modrinth {nameof(GetProjectVersionsAsync)}: HTTP {(int)response.StatusCode}", "ModrinthApi");
                return [];
            }

            var versions = await response.Content
                .ReadFromJsonAsync<List<ModrinthVersion>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return versions ?? [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Modrinth {nameof(GetProjectVersionsAsync)}: {ex.Message}", "ModrinthApi");
            return [];
        }
    }

    private static IEnumerable<IReadOnlyCollection<string>> ChunkIds(IReadOnlyList<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToArray();
    }

    private sealed class CurrentVersionsRequest
    {
        [JsonPropertyName("hashes")]
        public IReadOnlyCollection<string> Hashes { get; init; } = [];

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; init; } = "sha512";
    }

    private sealed class LatestVersionsRequest
    {
        [JsonPropertyName("hashes")]
        public IReadOnlyCollection<string> Hashes { get; init; } = [];

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; init; } = "sha512";

        [JsonPropertyName("loaders")]
        public IReadOnlyCollection<string> Loaders { get; init; } = [];

        [JsonPropertyName("game_versions")]
        public IReadOnlyCollection<string> GameVersions { get; init; } = [];

        [JsonPropertyName("version_types")]
        public IReadOnlyCollection<string> VersionTypes { get; init; } = [];
    }
}
