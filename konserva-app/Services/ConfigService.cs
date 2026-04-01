using Konserva.Models;
using Konserva.Utilities;
using Newtonsoft.Json;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Сервис управления конфигурацией приложения
/// </summary>
public class ConfigService : IConfigService, IDisposable
{
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
        await Task.CompletedTask;
        lock (_lock)
        {
            _config = config;
            SaveConfigToFile(config);
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

            return JsonConvert.DeserializeObject<AppConfig>(json);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load config: {ex.Message}", "ConfigService");
            return null;
        }
    }

    private async Task<AppConfig?> LoadConfigFromFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_configPath))
        {
            var defaultConfig = new AppConfig();
            await SaveConfigToFileAsync(defaultConfig, ct);
            return defaultConfig;
        }

        try
        {
            await using var fileStream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var reader = new StreamReader(fileStream);
            var json = await reader.ReadToEndAsync(ct);

            return JsonConvert.DeserializeObject<AppConfig>(json);
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
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
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
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
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
        GC.SuppressFinalize(this);
    }
}