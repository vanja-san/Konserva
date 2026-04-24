using Konserva.Models;
using Konserva.Utilities;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Konserva.Services;

public class ConfigService : IConfigService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _configPath;
    private readonly Lock _lock = new();
    private AppConfig? _config;
    private bool _disposed;

    public ConfigService()
    {
        var exeDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(exeDir, "config.json");

        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(Path.Combine(exeDir, "Servers"));
        Directory.CreateDirectory(Path.Combine(exeDir, "Logs"));
    }

    public AppConfig GetConfig()
    {
        if (_config != null)
            return _config;

        lock (_lock)
        {
            return _config ??= LoadConfigFromFile() ?? new AppConfig();
        }
    }

    public async Task<AppConfig> GetConfigAsync(CancellationToken ct = default)
    {
        if (_config != null)
            return _config;

        await Task.CompletedTask;
        lock (_lock)
        {
            return _config ??= LoadConfigFromFile() ?? new AppConfig();
        }
    }

    public void SaveConfig(AppConfig config)
    {
        lock (_lock)
        {
            _config = config;
            SaveConfigToFile(config);
        }
    }

    public async Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
    {
        string json;
        lock (_lock)
        {
            _config = config;
            json = JsonSerializer.Serialize(config, JsonOptions);
        }

        try
        {
            await using var fileStream = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var writer = new StreamWriter(fileStream);
            await writer.WriteAsync(json.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save config: {ex.Message}", ex, "ConfigService");
        }
    }

    public void UpdateConfig(Action<AppConfig> updateAction)
    {
        var config = GetConfig();
        updateAction(config);
        SaveConfig(config);
    }

    private AppConfig? LoadConfigFromFile()
    {
        if (!File.Exists(_configPath))
        {
            var defaultConfig = new AppConfig();
            SaveConfigToFile(defaultConfig);
            return defaultConfig;
        }

        try
        {
            using var fileStream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var json = reader.ReadToEnd();

            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load config: {ex.Message}", "ConfigService");
            return null;
        }
    }

    private void SaveConfigToFile(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            using var fileStream = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fileStream);
            writer.Write(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save config: {ex.Message}", ex, "ConfigService");
        }
    }

    private async Task SaveConfigToFileAsync(AppConfig config, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await using var fileStream = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var writer = new StreamWriter(fileStream);
            await writer.WriteAsync(json.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save config: {ex.Message}", ex, "ConfigService");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}