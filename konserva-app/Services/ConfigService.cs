using Konserva.Models;
using Konserva.Utilities;
using System.IO;

namespace Konserva.Services;

public class ConfigService : IConfigService, IDisposable
{
    private readonly FileBasedStore<AppConfig> _store;
    private bool _disposed;

    /// <summary>
    /// Основной конструктор. Путь к файлу конфигурации: BaseDirectory/config.json
    /// </summary>
    public ConfigService()
        : this(Path.Combine(AppContext.BaseDirectory, "config.json"))
    {
        var exeDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(Path.Combine(exeDir, "Servers"));
        Directory.CreateDirectory(Path.Combine(exeDir, "Logs"));
    }

    /// <summary>
    /// Конструктор с указанием пути (используется в тестах)
    /// </summary>
    internal ConfigService(string configPath)
    {
        _store = new FileBasedStore<AppConfig>(configPath);
    }

    public AppConfig GetConfig()
    {
        var config = _store.Load();
        if (config != null)
            return config;

        config = new AppConfig();
        _store.Save(config);
        return config;
    }

    public async Task<AppConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var config = await _store.LoadAsync(ct);
        if (config != null)
            return config;

        config = new AppConfig();
        await _store.SaveAsync(config, ct);
        return config;
    }

    public void SaveConfig(AppConfig config)
        => _store.Save(config);

    public async Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
        => await _store.SaveAsync(config, ct);

    public void UpdateConfig(Action<AppConfig> updateAction)
    {
        var config = GetConfig();
        updateAction(config);
        SaveConfig(config);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}