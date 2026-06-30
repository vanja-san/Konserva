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
  public void SaveSettings(string? name, string? ramMinStr, string? ramMaxStr,
      bool? autoRestart, string? autoRestartDelayStr,
      bool javaAutoSelect, string? javaId,
      string jvmArgs)
  {
    if (_server == null)
      return;

    // Название
    if (!string.IsNullOrEmpty(name) && name != _server.Name)
    {
      _server.Name = name;
      ServerName = name;
    }

    // RAM
    if (int.TryParse(ramMinStr, out var ramMin) && ramMin >= Constants.MinRamMb && ramMin != _server.Settings.RamMin)
      _server.Settings.RamMin = ramMin;

    if (int.TryParse(ramMaxStr, out var ramMax) && ramMax >= ramMin && ramMax != _server.Settings.RamMax)
      _server.Settings.RamMax = ramMax;

    // Авто-рестарт
    if (autoRestart.HasValue && autoRestart.Value != _server.Settings.AutoRestart)
      _server.Settings.AutoRestart = autoRestart.Value;

    // Задержка авто-рестарта
    if (int.TryParse(autoRestartDelayStr, out var delay) && delay >= 0 && delay != _server.Settings.AutoRestartDelay)
      _server.Settings.AutoRestartDelay = delay;

    // Java
    if (javaAutoSelect != _server.Settings.JavaAutoSelect)
      _server.Settings.JavaAutoSelect = javaAutoSelect;

    if (!javaAutoSelect && javaId != _server.Settings.JavaId)
      _server.Settings.JavaId = javaId;
    else if (javaAutoSelect && _server.Settings.JavaId != null)
      _server.Settings.JavaId = null;

    // JVM аргументы
    _server.Settings.JvmArgsText = jvmArgs;

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
        _serverManager.StopServer(_serverId!);
      }
      else
      {
        _server.ErrorDialogShown = false;
        _serverManager.StartServer(_serverId!);
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
      var modsDir = Path.Combine(_server.Path, "mods");
      if (!Directory.Exists(modsDir))
      {
        Mods = new();
        ModsVisible = false;
        return;
      }

      var mods = new List<ModItem>();
      foreach (var pattern in new[] { "*.jar", "*.jar.disabled" })
      {
        foreach (var path in Directory.GetFiles(modsDir, pattern))
        {
          var fileName = Path.GetFileName(path);
          var isDisabled = fileName.EndsWith(".disabled");
          var cleanName = isDisabled ? fileName.Replace(".jar.disabled", ".jar") : fileName;
          mods.Add(new ModItem
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

      Mods = new System.Collections.ObjectModel.ObservableCollection<ModItem>(mods.OrderBy(m => m.Name));
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
      var pluginsDir = Path.Combine(_server.Path, "plugins");
      if (!Directory.Exists(pluginsDir))
      {
        Plugins = new();
        PluginsVisible = false;
        return;
      }

      var plugins = new List<PluginItem>();
      foreach (var pattern in new[] { "*.jar", "*.jar.disabled" })
      {
        foreach (var path in Directory.GetFiles(pluginsDir, pattern))
        {
          var fileName = Path.GetFileName(path);
          var isDisabled = fileName.EndsWith(".disabled");
          var cleanName = isDisabled ? fileName.Replace(".jar.disabled", ".jar") : fileName;
          plugins.Add(new PluginItem
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

      Plugins = new System.Collections.ObjectModel.ObservableCollection<PluginItem>(plugins.OrderBy(p => p.Name));
      PluginsVisible = Plugins.Count > 0;
    }
    catch (Exception ex)
    {
      Logger.Warning($"LoadPlugins error: {ex.Message}", "ServerDetailViewModel");
    }
  }

  public async Task ToggleModAsync(ModItem mod)
  {
    try
    {
      if (mod.Enabled)
      {
        var disabledPath = mod.FilePath + ".disabled";
        if (File.Exists(mod.FilePath))
        {
          File.Move(mod.FilePath, disabledPath);
          mod.FilePath = disabledPath;
          mod.Enabled = false;
        }
      }
      else
      {
        if (File.Exists(mod.FilePath))
        {
          var enabledPath = mod.FilePath.Replace(".jar.disabled", ".jar");
          File.Move(mod.FilePath, enabledPath);
          mod.FilePath = enabledPath;
          mod.Enabled = true;
        }
      }
      LoadMods();
    }
    catch (Exception ex)
    {
      Logger.Error($"ToggleMod error: {ex.Message}", ex, "ServerDetailViewModel");
      await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_ModToggleError")}: {ex.Message}");
    }
  }

  public async Task TogglePluginAsync(PluginItem plugin)
  {
    try
    {
      if (plugin.Enabled)
      {
        var disabledPath = plugin.FilePath + ".disabled";
        if (File.Exists(plugin.FilePath))
        {
          File.Move(plugin.FilePath, disabledPath);
          plugin.FilePath = disabledPath;
          plugin.Enabled = false;
        }
      }
      else
      {
        if (File.Exists(plugin.FilePath))
        {
          var enabledPath = plugin.FilePath.Replace(".jar.disabled", ".jar");
          File.Move(plugin.FilePath, enabledPath);
          plugin.FilePath = enabledPath;
          plugin.Enabled = true;
        }
      }
      LoadPlugins();
    }
    catch (Exception ex)
    {
      Logger.Error($"TogglePlugin error: {ex.Message}", ex, "ServerDetailViewModel");
      await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_PluginToggleError")}: {ex.Message}");
    }
  }

  public void ToggleAllMods()
  {
    if (_server == null || Mods.Count == 0) return;

    var anyEnabled = Mods.Any(m => m.Enabled);
    var targetEnabled = !anyEnabled;

    foreach (var mod in Mods)
    {
      if (mod.Enabled == targetEnabled) continue;
      try
      {
        if (targetEnabled)
        {
          var enabledPath = mod.FilePath.Replace(".jar.disabled", ".jar");
          if (File.Exists(mod.FilePath))
          {
            File.Move(mod.FilePath, enabledPath);
            mod.FilePath = enabledPath;
            mod.Enabled = true;
          }
        }
        else
        {
          var disabledPath = mod.FilePath + ".disabled";
          if (File.Exists(mod.FilePath))
          {
            File.Move(mod.FilePath, disabledPath);
            mod.FilePath = disabledPath;
            mod.Enabled = false;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"ToggleAllMods error: {ex.Message}", ex, "ServerDetailViewModel");
      }
    }
    LoadMods();
  }

  public void ToggleAllPlugins()
  {
    if (_server == null || Plugins.Count == 0) return;

    var anyEnabled = Plugins.Any(p => p.Enabled);
    var targetEnabled = !anyEnabled;

    foreach (var plugin in Plugins)
    {
      if (plugin.Enabled == targetEnabled) continue;
      try
      {
        if (targetEnabled)
        {
          var enabledPath = plugin.FilePath.Replace(".jar.disabled", ".jar");
          if (File.Exists(plugin.FilePath))
          {
            File.Move(plugin.FilePath, enabledPath);
            plugin.FilePath = enabledPath;
            plugin.Enabled = true;
          }
        }
        else
        {
          var disabledPath = plugin.FilePath + ".disabled";
          if (File.Exists(plugin.FilePath))
          {
            File.Move(plugin.FilePath, disabledPath);
            plugin.FilePath = disabledPath;
            plugin.Enabled = false;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"ToggleAllPlugins error: {ex.Message}", ex, "ServerDetailViewModel");
      }
    }
    LoadPlugins();
  }

  public async Task DeleteModAsync(ModItem mod)
  {
    try
    {
      if (File.Exists(mod.FilePath))
      {
        File.Delete(mod.FilePath);
        LoadMods();
      }
    }
    catch (Exception ex)
    {
      Logger.Error($"DeleteMod error: {ex.Message}", ex, "ServerDetailViewModel");
      await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_ModDeleteError")}: {ex.Message}");
    }
  }

  public async Task DeletePluginAsync(PluginItem plugin)
  {
    try
    {
      if (File.Exists(plugin.FilePath))
      {
        File.Delete(plugin.FilePath);
        LoadPlugins();
      }
    }
    catch (Exception ex)
    {
      Logger.Error($"DeletePlugin error: {ex.Message}", ex, "ServerDetailViewModel");
      await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_PluginDeleteError")}: {ex.Message}");
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

  public bool CheckAllModsDisabled()
  {
    return Mods.Count > 0 && Mods.All(m => !m.Enabled);
  }

  public bool CheckAllPluginsDisabled()
  {
    return Plugins.Count > 0 && Plugins.All(p => !p.Enabled);
  }


}
