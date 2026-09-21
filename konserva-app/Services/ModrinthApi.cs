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
            Logger.Warning($"Modrinth {operation}: {ex.Message}", "ModrinthApi");
            return Empty;
        }
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
