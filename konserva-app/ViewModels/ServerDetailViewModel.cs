using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace Konserva.ViewModels;

/// <summary>
/// ViewModel для страницы деталей сервера
/// </summary>
public partial class ServerDetailViewModel : ObservableObject
{
    private readonly IServerManager _serverManager;
    private readonly IConfigService _configService;
    private readonly IPortForwardingService? _portForwardingService;

    private bool _isBusy;

    public ServerDetailViewModel(
        IServerManager serverManager,
        IConfigService configService,
        IPortForwardingService? portForwardingService = null)
    {
        _serverManager = serverManager;
        _configService = configService;
        _portForwardingService = portForwardingService;
    }

    // ─── Свойства ───────────────────────────────────────────────────

    private string? _serverId;
    public string? ServerId
    {
        get => _serverId;
        set
        {
            if (_serverId != value)
            {
                _serverId = value;
                LoadServer();
            }
        }
    }

    private Server? _server;
    public Server? Server
    {
        get => _server;
        private set => SetProperty(ref _server, value);
    }

    private McServerProcess? _process;
    public McServerProcess? Process
    {
        get => _process;
        private set => SetProperty(ref _process, value);
    }

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _serverInfo = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    public bool IsBusy => _isBusy;

    // ─── Настройки ──────────────────────────────────────────────────

    [ObservableProperty]
    private string _settingsName = string.Empty;

    [ObservableProperty]
    private int _settingsRamMin;

    [ObservableProperty]
    private int _settingsRamMax;

    [ObservableProperty]
    private bool _settingsAutoRestart;

    [ObservableProperty]
    private int _settingsAutoRestartDelay;

    [ObservableProperty]
    private bool _settingsJavaAutoSelect = true;

    [ObservableProperty]
    private bool _settingsEnableUpnp;

    [ObservableProperty]
    private string _settingsJvmArgs = string.Empty;

    [ObservableProperty]
    private string _upnpAddress = string.Empty;

    [ObservableProperty]
    private bool _upnpAddressVisible;

    /// <summary>
    /// Текст ошибки переименования папки (null если успешно)
    /// </summary>
    public string? LastRenameError { get; private set; }

    // ─── Mods / Plugins ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModsCount))]
    private System.Collections.ObjectModel.ObservableCollection<ModItem> _mods = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PluginsCount))]
    private System.Collections.ObjectModel.ObservableCollection<PluginItem> _plugins = new();

    [ObservableProperty]
    private bool _modsVisible;

    [ObservableProperty]
    private bool _pluginsVisible;

    public string ModsCount => Mods.Count > 0 ? Mods.Count.ToString() : string.Empty;

    public string PluginsCount => Plugins.Count > 0 ? Plugins.Count.ToString() : string.Empty;

    // ─── Загрузка сервера ───────────────────────────────────────────

    public void LoadServer()
    {
        if (_serverId == null)
            return;

        var server = _serverManager.GetServer(_serverId);
        var process = _serverManager.GetProcess(_serverId);

        Server = server;
        Process = process;

        if (server == null)
            return;

        ServerName = server.Name;
        ServerInfo = server.Description;
        IsRunning = server.IsRunning;

        // Настройки
        SettingsName = server.Name;
        SettingsRamMin = server.Settings.RamMin;
        SettingsRamMax = server.Settings.RamMax;
        SettingsAutoRestart = server.Settings.AutoRestart;
        SettingsAutoRestartDelay = server.Settings.AutoRestartDelay;
        SettingsJavaAutoSelect = server.Settings.JavaAutoSelect;
        SettingsEnableUpnp = server.Settings.EnableUpnp;
        SettingsJvmArgs = server.Settings.JvmArgsText;

        UpdateServerAddressDisplay();
    }

    // ─── Сохранение настроек ────────────────────────────────────────

    /// <summary>
    /// Автосохранение настроек сервера
    /// </summary>
    public void SaveSettings(ServerSettingsRequest request)
    {
        if (_server == null)
            return;

        // Название + переименование папки
        if (!string.IsNullOrEmpty(request.Name) && request.Name != _server.Name)
        {
            var oldName = _server.Name;
            _server.Name = request.Name;
            ServerName = request.Name;

            // Если сервер не запущен — переименовываем папку
            if (!_server.IsRunning)
            {
                LastRenameError = null;
                if (!_serverManager.RenameServerFolder(_server.Id, request.Name, out var error))
                {
                    LastRenameError = error;
                    // Откатываем имя обратно
                    _server.Name = oldName;
                    ServerName = oldName;
                }
            }
        }
        else
        {
            LastRenameError = null;
        }

        // RAM
        if (int.TryParse(request.RamMinStr, out var ramMin) && ramMin >= Constants.MinRamMb && ramMin != _server.Settings.RamMin)
            _server.Settings.RamMin = ramMin;

        if (int.TryParse(request.RamMaxStr, out var ramMax) && ramMax >= ramMin && ramMax != _server.Settings.RamMax)
            _server.Settings.RamMax = ramMax;

        // Авто-рестарт
        if (request.AutoRestart.HasValue && request.AutoRestart.Value != _server.Settings.AutoRestart)
            _server.Settings.AutoRestart = request.AutoRestart.Value;

        // Задержка авто-рестарта
        if (int.TryParse(request.AutoRestartDelayStr, out var delay) && delay >= 0 && delay != _server.Settings.AutoRestartDelay)
            _server.Settings.AutoRestartDelay = delay;

        // Java
        if (request.JavaAutoSelect != _server.Settings.JavaAutoSelect)
            _server.Settings.JavaAutoSelect = request.JavaAutoSelect;

        if (!request.JavaAutoSelect && request.JavaId != _server.Settings.JavaId)
            _server.Settings.JavaId = request.JavaId;
        else if (request.JavaAutoSelect && _server.Settings.JavaId != null)
            _server.Settings.JavaId = null;

        // JVM аргументы
        _server.Settings.JvmArgsText = request.JvmArgs;

        _serverManager.UpdateServer(_server);
    }

    /// <summary>
    /// Сохранение настройки UPnP
    /// </summary>
    public void SaveUpnpSetting(bool enable)
    {
        if (_server == null) return;

        if (enable != _server.Settings.EnableUpnp)
        {
            _server.Settings.EnableUpnp = enable;
            _serverManager.UpdateServer(_server);
        }
    }

    /// <summary>
    /// Сохранение порта из редактора свойств
    /// </summary>
    public void SavePort(int newPort)
    {
        if (_server == null) return;

        if (_server.Port != newPort)
        {
            _server.Port = newPort;
            _serverManager.UpdateServer(_server);
        }
    }

    // ─── Запуск / Остановка ─────────────────────────────────────────

    [RelayCommand]
    private async Task StartStopAsync()
    {
        if (_server == null || _isBusy)
            return;

        _isBusy = true;
        try
        {
            if (_server.IsRunning)
            {
                await _serverManager.StopServerAsync(_serverId!);
            }
            else
            {
                _server.ErrorDialogShown = false;
                await _serverManager.StartServerAsync(_serverId!);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"StartStop error: {ex.Message}", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_OperationError")}: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    // ─── Процесс ────────────────────────────────────────────────────

    /// <summary>
    /// Обновляет процесс из менеджера и возвращает его
    /// </summary>
    public McServerProcess? RefreshProcess()
    {
        if (_serverId == null) return null;

        Process = _serverManager.GetProcess(_serverId);
        return Process;
    }

    /// <summary>
    /// Обновляет статус сервера
    /// </summary>
    public void UpdateServerStatus(ServerStatus status)
    {
        if (status == ServerStatus.Running && _server != null)
            _server.ResetErrorDialog();

        if (status is ServerStatus.Stopped or ServerStatus.Error)
            _isBusy = false;

        IsRunning = status == ServerStatus.Running;
    }

    // ─── Java ───────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает список Java для ComboBox
    /// </summary>
    public List<(string? Id, string Display)> GetJavaList()
    {
        var config = _configService.GetConfig();
        var list = new List<(string? Id, string Display)>();

        var defaultJava = config.GetDefaultJava();
        var defaultText = $"{LocalizationManager.Get("ServerDetail_JavaDefault")} " +
            $"({(defaultJava != null ? defaultJava.DisplayName : LocalizationManager.Get("ServerDetail_JavaNotSelected"))})";
        list.Add((null, defaultText));

        foreach (var java in config.JavaInstallations.Where(j => j.Exists))
        {
            list.Add((java.Id, java.DisplayName));
        }

        return list;
    }

    /// <summary>
    /// Возвращает сохранённый JavaId сервера
    /// </summary>
    public string? GetSelectedJavaId()
    {
        return _server?.Settings.JavaId;
    }

    // ─── Mods / Plugins ─────────────────────────────────────────────

    public void LoadMods()
    {
        if (_server == null) return;
        try
        {
            var items = ScanItemFiles<ModItem>("mods");
            Mods = new(items);
            ModsVisible = Mods.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.Warning($"LoadMods error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    public void LoadPlugins()
    {
        if (_server == null) return;
        try
        {
            var items = ScanItemFiles<PluginItem>("plugins");
            Plugins = new(items);
            PluginsVisible = Plugins.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.Warning($"LoadPlugins error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    public async Task ToggleModAsync(ModItem mod) =>
        await ToggleItemInternal(mod, LoadMods, "ServerDetail_ModToggleError");

    public async Task TogglePluginAsync(PluginItem plugin) =>
        await ToggleItemInternal(plugin, LoadPlugins, "ServerDetail_PluginToggleError");

    public void ToggleAllMods() =>
        ToggleAllItemsInternal(Mods, LoadMods);

    public void ToggleAllPlugins() =>
        ToggleAllItemsInternal(Plugins, LoadPlugins);

    public async Task DeleteModAsync(ModItem mod) =>
        await DeleteItemInternal(mod, LoadMods, "ServerDetail_ModDeleteError");

    public async Task DeletePluginAsync(PluginItem plugin) =>
        await DeleteItemInternal(plugin, LoadPlugins, "ServerDetail_PluginDeleteError");

    // ─── Generic helpers ────────────────────────────────────────────

    private List<T> ScanItemFiles<T>(string subDir) where T : IItemEntry, new()
    {
        var dir = Path.Combine(_server!.Path, subDir);
        if (!Directory.Exists(dir)) return new();

        var items = new List<T>();
        foreach (var pattern in new[] { "*.jar", "*.jar.disabled" })
        {
            foreach (var path in Directory.GetFiles(dir, pattern))
            {
                var fileName = Path.GetFileName(path);
                var isDisabled = fileName.EndsWith(".disabled");
                var cleanName = isDisabled ? fileName.Replace(".jar.disabled", ".jar") : fileName;
                items.Add(new T
                {
                    Name = Path.GetFileNameWithoutExtension(cleanName),
                    Version = ParseVersion(cleanName),
                    FileName = cleanName,
                    FilePath = path,
                    FileSize = new FileInfo(path).Length,
                    Enabled = !isDisabled
                });
            }
        }
        return [.. items.OrderBy(m => m.Name)];
    }

    private async Task ToggleItemInternal<T>(T item, Action reload, string errorKey) where T : IItemEntry
    {
        try
        {
            if (item.Enabled)
            {
                var disabledPath = item.FilePath + ".disabled";
                if (File.Exists(item.FilePath))
                {
                    File.Move(item.FilePath, disabledPath);
                    item.FilePath = disabledPath;
                    item.Enabled = false;
                }
            }
            else
            {
                if (File.Exists(item.FilePath))
                {
                    var enabledPath = item.FilePath.Replace(".jar.disabled", ".jar");
                    File.Move(item.FilePath, enabledPath);
                    item.FilePath = enabledPath;
                    item.Enabled = true;
                }
            }
            reload();
        }
        catch (Exception ex)
        {
            Logger.Error($"ToggleItem error: {ex.Message}", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get(errorKey)}: {ex.Message}");
        }
    }

    private void ToggleAllItemsInternal<T>(IList<T> items, Action reload) where T : IItemEntry
    {
        if (_server == null || items.Count == 0) return;

        var targetEnabled = !items.Any(m => m.Enabled);

        foreach (var item in items)
        {
            if (item.Enabled == targetEnabled) continue;
            try
            {
                if (targetEnabled)
                {
                    var enabledPath = item.FilePath.Replace(".jar.disabled", ".jar");
                    if (File.Exists(item.FilePath))
                    {
                        File.Move(item.FilePath, enabledPath);
                        item.FilePath = enabledPath;
                        item.Enabled = true;
                    }
                }
                else
                {
                    var disabledPath = item.FilePath + ".disabled";
                    if (File.Exists(item.FilePath))
                    {
                        File.Move(item.FilePath, disabledPath);
                        item.FilePath = disabledPath;
                        item.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ToggleAllItems error: {ex.Message}", ex, "ServerDetailViewModel");
            }
        }
        reload();
    }

    private async Task DeleteItemInternal<T>(T item, Action reload, string errorKey) where T : IItemEntry
    {
        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
                reload();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"DeleteItem error: {ex.Message}", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get(errorKey)}: {ex.Message}");
        }
    }

    public async Task DeleteServerAsync()
    {
        if (_server == null || _serverId == null) return;

        try
        {
            await _serverManager.DeleteServerAsync(_serverId);
        }
        catch (Exception ex)
        {
            Logger.Error($"DeleteServer error: {ex.Message}", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_DeleteServerError")}: {ex.Message}");
        }
    }

    // ─── UPnP ───────────────────────────────────────────────────────

    public void UpdateServerAddressDisplay()
    {
        if (_server == null) return;

        var port = _server.Port;
        var externalIp = _portForwardingService?.TryGetCachedExternalIp();

        if (externalIp != null)
        {
            UpnpAddress = $"{externalIp}:{port}";
            UpnpAddressVisible = true;
        }
        else
        {
            UpnpAddress = string.Empty;
            UpnpAddressVisible = false;
        }
    }

    public async Task<bool> CheckUpnpAvailabilityAsync()
    {
        if (_portForwardingService == null) return false;
        return await _portForwardingService.IsAvailableAsync();
    }

    public async Task<bool> CheckPortMappingAsync(int port)
    {
        if (_portForwardingService == null) return false;
        return await _portForwardingService.CheckMappingAsync(port);
    }

    public async Task<string?> GetExternalIpAsync()
    {
        if (_portForwardingService == null) return null;
        return await _portForwardingService.GetExternalIpAsync();
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static string ParseVersion(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        return parts.Length > 1 ? parts[^1] : LocalizationManager.Get("Common_Unknown");
    }

    public bool CheckAllModsDisabled() => CheckAllDisabled(Mods);

    public bool CheckAllPluginsDisabled() => CheckAllDisabled(Plugins);

    private static bool CheckAllDisabled<T>(IList<T> items) where T : IItemEntry =>
        items.Count > 0 && items.All(m => !m.Enabled);
}
