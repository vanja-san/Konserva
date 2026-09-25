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
    private readonly IModUpdateService? _modUpdateService;

    private bool _isBusy;

    private readonly Dictionary<string, ModUpdateInfo> _pendingUpdates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Хеши SHA-512 по путям файлов, посчитанные за текущую сессию.
    /// Позволяет не хешировать один и тот же файл дважды за одно открытие вкладки
    /// (названия + проверка обновлений). На диск хеши попадают через
    /// ModMetadataCacheEntry.Sha512.
    /// </summary>
    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Кэш реальных названий и версий модов из Modrinth.
    /// Ключ — нормализованное имя файла (без .disabled), чтобы после перезагрузок
    /// не показывать имена файлов и не дёргать сеть при каждом действии.
    /// </summary>
    private readonly Dictionary<string, ModMetadataCacheEntry> _modTitlesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Закэшированные на диске обновления: имя файла → данные (валидны для файла указанного размера).
    /// Позволяет показывать найденные обновления сразу после перезапуска приложения без сети.
    /// </summary>
    private readonly Dictionary<string, ModUpdateCacheEntry> _cachedUpdates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Порог актуальности авто-проверки обновлений: пока файлы не менялись и проверка была
    /// меньше часа назад — сеть не дёргаем.
    /// </summary>
    private static readonly TimeSpan ModUpdateAutoCheckTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Диск-кэш метаданных модов (названия, версии, инвентаризация файлов, обновления).
    /// Keyed by server id under Constants.ServersPath\mods_cache.
    /// </summary>
    private FileBasedStore<ModsMetadataCacheData>? _modsCacheStore;
    private bool _modsCacheLoaded;

    /// <summary>Время последней реальной проверки обновлений (UTC).</summary>
    private DateTime _lastModsUpdateCheckUtc;

    public ServerDetailViewModel(
        IServerManager serverManager,
        IConfigService configService,
        IPortForwardingService? portForwardingService = null,
        IModUpdateService? modUpdateService = null)
    {
        _serverManager = serverManager;
        _configService = configService;
        _portForwardingService = portForwardingService;
        _modUpdateService = modUpdateService;
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
    private bool _settingsEnableUpdateNotification = true;

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

    // ─── Mod Updates ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isCheckingModUpdates;

    [ObservableProperty]
    private bool _isUpdatingMods;

    [ObservableProperty]
    private bool _hasModUpdates;

    public int ModUpdatesCount => _pendingUpdates.Count;

    [ObservableProperty]
    private string _modUpdateStatusText = string.Empty;

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

        EnsureModsCacheLoaded();

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
        SettingsEnableUpdateNotification = server.Settings.EnableUpdateNotification;
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
    /// Сохранение настройки уведомлений об обновлениях загрузчика
    /// </summary>
    public void SaveUpdateNotificationSetting(bool enable)
    {
        if (_server == null) return;

        if (enable != _server.Settings.EnableUpdateNotification)
        {
            _server.Settings.EnableUpdateNotification = enable;
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
    /// Останавливает сервер, если он запущен. Используется перед обновлением загрузчика.
    /// Возвращает false, если остановка не удалась.
    /// </summary>
    public async Task<bool> StopServerIfRunningAsync()
    {
        if (_server == null || _serverId == null || !_server.IsRunning)
            return true;

        try
        {
            await _serverManager.StopServerAsync(_serverId);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"StopServerIfRunning error: {ex.Message}", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_OperationError")}: {ex.Message}");
            return false;
        }
    }

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
            ApplyCachedTitles(items);
            ApplyCachedUpdates(items);
            RefreshModUpdateStatus(items);
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

    // ─── Mod Updates ──────────────────────────────────────────────────

    public async Task CheckModUpdatesAsync(bool silent = false)
    {
        if (_server == null || _isBusy || IsCheckingModUpdates || _modUpdateService == null) return;

        if (IsRunning)
        {
            if (!silent)
                await UiHelper.ShowInfo(LocalizationManager.Get("ServerDetail_Mods_ServerMustBeStopped"));
            return;
        }

        if (!ModrinthLoaderMap.SupportsModUpdates(_server.ModLoader.Type))
        {
            if (!silent)
                await UiHelper.ShowInfo(LocalizationManager.Get("ServerDetail_Mods_UpdateNotSupported"));
            return;
        }

        IsCheckingModUpdates = true;
        _pendingUpdates.Clear();
        foreach (var mod in Mods)
        {
            mod.UpdateAvailable = false;
            mod.LatestVersion = null;
        }
        ModUpdateStatusText = LocalizationManager.Get("ServerDetail_Mods_CheckingUpdates");

        try
        {
            var paths = Mods.Select(m => m.FilePath).ToList();
            var hashes = await BuildModHashMapAsync(Mods).ConfigureAwait(false);
            var updates = await _modUpdateService.CheckForUpdatesAsync(
                paths, _server.ModLoader.Type, _server.McVersion, ModUpdateChannel.Release,
                progress: null, precomputedHashes: hashes);

            foreach (var mod in Mods)
            {
                if (updates.TryGetValue(mod.FilePath, out var info))
                {
                    _pendingUpdates[mod.FilePath] = info;
                    mod.UpdateAvailable = true;
                    mod.LatestVersion = info.LatestVersion;
                }
            }

            HasModUpdates = _pendingUpdates.Count > 0;
            ModUpdateStatusText = _pendingUpdates.Count > 0
                ? string.Format(LocalizationManager.Get("ServerDetail_Mods_UpdatesFound"), _pendingUpdates.Count)
                : string.Empty;

            _lastModsUpdateCheckUtc = DateTime.UtcNow;
            PersistModsCache();
        }
        catch (Exception ex)
        {
            Logger.Error("CheckModUpdates error", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_Mods_CheckUpdatesError")}: {ex.Message}");
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    public async Task ApplyAllModsUpdatesAsync()
    {
        if (_server == null || _pendingUpdates.Count == 0 || _modUpdateService == null) return;

        if (IsRunning)
        {
            var stopped = await StopServerIfRunningAsync();
            if (!stopped) return;
        }

        IsUpdatingMods = true;
        try
        {
            var backupDir = ModBackup.CreateBackupDirectory(_server.Name);
            var successCount = 0;
            var total = _pendingUpdates.Count;
            var processed = 0;

            foreach (var (path, update) in _pendingUpdates)
            {
                processed++;
                ModUpdateStatusText = string.Format(
                    LocalizationManager.Get("ServerDetail_Mods_UpdatingProgress"),
                    processed, total, update.DownloadFileName);

                var result = await _modUpdateService.ApplyUpdateAsync(update, backupDir);

                if (result.Success)
                {
                    successCount++;
                    SeedTitleCacheAfterUpdate(CleanFileNameKey(Path.GetFileName(path)), update, result.NewFileName);
                }
                else
                {
                    Logger.Warning($"Failed to update {Path.GetFileName(path)}: {result.Error}", "ServerDetailViewModel");
                }
            }

            ModUpdateStatusText = string.Format(
                LocalizationManager.Get("ServerDetail_Mods_UpdateComplete"), successCount);
            _pendingUpdates.Clear();
            HasModUpdates = false;
            LoadMods();
        }
        catch (Exception ex)
        {
            Logger.Error("ApplyAllModsUpdates error", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_Mods_UpdateError")}: {ex.Message}");
        }
        finally
        {
            IsUpdatingMods = false;
        }
    }

    public async Task<ModUpdateApplyResult?> ApplyModUpdateAsync(ModItem mod)
    {
        if (_server == null || !_pendingUpdates.TryGetValue(mod.FilePath, out var update) || _modUpdateService == null) return null;

        IsUpdatingMods = true;
        try
        {
            var backupDir = ModBackup.CreateBackupDirectory(_server.Name);
            ModUpdateStatusText = LocalizationManager.Get("ServerDetail_Mods_Updating") + " " + update.DownloadFileName;

            var result = await _modUpdateService.ApplyUpdateAsync(update, backupDir);

            if (result.Success)
            {
                _pendingUpdates.Remove(mod.FilePath);
                HasModUpdates = _pendingUpdates.Count > 0;
                ModUpdateStatusText = string.Empty;

                SeedTitleCacheAfterUpdate(mod.FileName, update, result.NewFileName);

                LoadMods();
            }
            else
            {
                await UiHelper.ShowWarning($"Ошибка: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("ApplyModUpdate error", ex, "ServerDetailViewModel");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_Mods_UpdateError")}: {ex.Message}");
            return null;
        }
        finally
        {
            IsUpdatingMods = false;
        }
    }

    /// <summary>
    /// Приводит имя файла мода к ключу кэша названий: убирает суффикс ".disabled",
    /// чтобы и ".jar", и ".jar.disabled" указывали на одну запись (см. ScanItemFiles).
    /// </summary>
    private static string CleanFileNameKey(string fileName) =>
        fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;

    /// <summary>
    /// Переносит уже известное название мода на файл с новой версией, чтобы после
    /// обновления список не мигал «имя файла → название с Modrinth»: новый файл сразу
    /// получает title/версию из кэша, а сетевой ResolveModTitlesAsync лишь подтвердит.
    /// </summary>
    private void SeedTitleCacheAfterUpdate(string oldFileName, ModUpdateInfo update, string? newFileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newFileName))
                return;

            if (!_modTitlesCache.TryGetValue(oldFileName, out var oldEntry)
                || string.IsNullOrWhiteSpace(oldEntry.Title))
            {
                return;
            }

            // Ключи кэша хранятся без ".disabled" (см. ScanItemFiles), приводим имя к тому же виду
            var cacheKey = CleanFileNameKey(newFileName);

            var directory = Path.GetDirectoryName(update.FilePath);
            if (string.IsNullOrEmpty(directory))
                return;

            var newPath = Path.Combine(directory, newFileName);
            if (!File.Exists(newPath))
                return;

            _modTitlesCache[cacheKey] = new ModMetadataCacheEntry
            {
                FileSize = new FileInfo(newPath).Length,
                Title = oldEntry.Title,
                Version = update.LatestVersion,
                Sha512 = update.LatestHash
            };

            _fileHashes[newPath] = update.LatestHash;
            PersistModsCache();
        }
        catch (Exception ex)
        {
            Logger.Warning($"SeedTitleCacheAfterUpdate error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    private void RefreshModUpdateStatus(IEnumerable<ModItem> items)
    {
        foreach (var mod in items)
        {
            if (_pendingUpdates.TryGetValue(mod.FilePath, out var info))
            {
                mod.UpdateAvailable = true;
                mod.LatestVersion = info.LatestVersion;
            }
        }
    }

    public async Task ResolveModTitlesAsync()
    {
        if (_server == null || _modUpdateService == null) return;

        try
        {
            ApplyCachedTitles(Mods);

            var uncachedMods = Mods
                .Where(m => !_modTitlesCache.TryGetValue(m.FileName, out var cached)
                            || cached.FileSize != m.FileSize)
                .ToList();

            if (uncachedMods.Count == 0)
                return;

            var hashes = await BuildModHashMapAsync(uncachedMods).ConfigureAwait(false);

            var metadata = await _modUpdateService.ResolveModMetadataAsync(
                [.. uncachedMods.Select(m => m.FilePath)],
                progress: null,
                precomputedHashes: hashes).ConfigureAwait(false);

            foreach (var mod in uncachedMods)
            {
                if (metadata.TryGetValue(mod.FilePath, out var info))
                {
                    if (!string.IsNullOrWhiteSpace(info.Title))
                        mod.Name = info.Title;
                    if (!string.IsNullOrWhiteSpace(info.Version))
                        mod.Version = info.Version;

                    _modTitlesCache[mod.FileName] = new ModMetadataCacheEntry
                    {
                        FileSize = mod.FileSize,
                        Title = string.IsNullOrWhiteSpace(info.Title) ? mod.Name : info.Title,
                        Version = string.IsNullOrWhiteSpace(info.Version) ? mod.Version : info.Version,
                        Sha512 = hashes.TryGetValue(mod.FilePath, out var hash) ? hash : string.Empty
                    };
                }
            }

            PersistModsCache();
        }
        catch (Exception ex)
        {
            Logger.Warning($"ResolveModTitles error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    private void ApplyCachedTitles(IEnumerable<ModItem> mods)
    {
        foreach (var mod in mods)
        {
            if (_modTitlesCache.TryGetValue(mod.FileName, out var entry) && entry.FileSize == mod.FileSize)
            {
                mod.Name = entry.Title;
                mod.Version = entry.Version;
            }
        }
    }

    /// <summary>
    /// SHA-512 локальных jar-файлов с переиспользованием кэшей: сначала сессионный
    /// _fileHashes, затем персистентный ModMetadataCacheEntry.Sha512 (по имени файла
    /// с совпавшим размером), и только недостающее хешируется параллельно.
    /// Возвращает карту: путь → хеш. Хеши оседают в _fileHashes (и в кэш названий,
    /// если для файла уже есть запись), чтобы каждый файл за сессию хешировался один раз.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildModHashMapAsync(
        IEnumerable<ModItem> mods,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var toHash = new List<ModItem>();

        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FilePath) || string.IsNullOrWhiteSpace(mod.FileName))
                continue;
            if (!File.Exists(mod.FilePath))
                continue;

            if (_fileHashes.TryGetValue(mod.FilePath, out var known))
            {
                result[mod.FilePath] = known;
                continue;
            }

            if (_modTitlesCache.TryGetValue(mod.FileName, out var entry)
                && entry.FileSize == mod.FileSize
                && !string.IsNullOrWhiteSpace(entry.Sha512))
            {
                _fileHashes[mod.FilePath] = entry.Sha512;
                result[mod.FilePath] = entry.Sha512;
                continue;
            }

            toHash.Add(mod);
        }

        if (toHash.Count > 0)
        {
            await Parallel.ForEachAsync(
                toHash,
                new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
                async (mod, token) =>
                {
                    var hash = await FileHash.Sha512Async(mod.FilePath, token).ConfigureAwait(false);

                    lock (_fileHashes)
                    {
                        _fileHashes[mod.FilePath] = hash;
                    }

                    lock (result)
                    {
                        result[mod.FilePath] = hash;
                    }

                    if (_modTitlesCache.TryGetValue(mod.FileName, out var entry))
                    {
                        lock (_modTitlesCache)
                        {
                            if (entry.FileSize == mod.FileSize)
                                entry.Sha512 = hash;
                        }
                    }
                }).ConfigureAwait(false);
        }

        return result;
    }

    // ─── Disk cache helpers ─────────────────────────────────────────

    private string? GetModsCachePath()
    {
        if (_server == null) return null;
        return Path.Combine(Constants.ServersPath, "mods_cache", $"{_server.Id}.json");
    }

    private void EnsureModsCacheLoaded()
    {
        if (_modsCacheLoaded || _server == null) return;
        _modsCacheLoaded = true;

        try
        {
            var cachePath = GetModsCachePath();
            if (cachePath == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            _modsCacheStore = new FileBasedStore<ModsMetadataCacheData>(cachePath);

            var data = _modsCacheStore.Load();
            if (data == null) return;

            _lastModsUpdateCheckUtc = data.LastUpdatesCheckUtc;
            _modTitlesCache.Clear();
            _cachedUpdates.Clear();

            foreach (var (fileName, entry) in data.Titles)
            {
                if (!string.IsNullOrEmpty(fileName) && entry != null)
                    _modTitlesCache[fileName] = entry;
            }

            foreach (var (fileName, entry) in data.Updates)
            {
                if (!string.IsNullOrEmpty(fileName) && entry?.Update != null)
                    _cachedUpdates[fileName] = entry;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"ModsCache load error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    /// <summary>
    /// Восстанавливает найденные ранее обновления из кэша для файлов, чей размер не изменился.
    /// Ключ кэша — имя файла, поэтому при перезапуске пути (в т.ч. с .disabled) исправляются под текущие.
    /// </summary>
    private void ApplyCachedUpdates(IEnumerable<ModItem> items)
    {
        _pendingUpdates.Clear();

        foreach (var mod in items)
        {
            if (_cachedUpdates.TryGetValue(mod.FileName, out var entry) && entry.FileSize == mod.FileSize)
            {
                var info = entry.Update;
                if (info == null) continue;

                info.FilePath = mod.FilePath;
                _pendingUpdates[mod.FilePath] = info;
                mod.UpdateAvailable = true;
                mod.LatestVersion = info.LatestVersion;
            }
        }

        HasModUpdates = _pendingUpdates.Count > 0;
    }

    /// <summary>
    /// Пишет на диск кэш названий/версий, инвентаризацию файлов и найденные обновления,
    /// чтобы после перезапуска приложения не дёргать сеть повторно.
    /// </summary>
    private void PersistModsCache()
    {
        if (_modsCacheStore == null || _server == null) return;

        try
        {
            _cachedUpdates.Clear();
            foreach (var mod in Mods)
            {
                if (_pendingUpdates.TryGetValue(mod.FilePath, out var info))
                    _cachedUpdates[mod.FileName] = new ModUpdateCacheEntry { FileSize = mod.FileSize, Update = info };
            }

            var data = new ModsMetadataCacheData
            {
                LastUpdatesCheckUtc = _lastModsUpdateCheckUtc,
                Titles = new(_modTitlesCache, StringComparer.OrdinalIgnoreCase),
                Updates = new(_cachedUpdates, StringComparer.OrdinalIgnoreCase)
            };

            foreach (var mod in Mods)
                data.ModFiles[mod.FileName] = mod.FileSize;

            _modsCacheStore.Save(data);
        }
        catch (Exception ex)
        {
            Logger.Warning($"ModsCache save error: {ex.Message}", "ServerDetailViewModel");
        }
    }

    /// <summary>
    /// Авто-проверка обновлений при открытии вкладки: без сети, если файлы не менялись
    /// и последняя проверка была меньше часа назад. Сервер при этом должен быть остановлен.
    /// </summary>
    public async Task MaybeCheckModUpdatesAutoAsync()
    {
        if (_server == null || _isBusy || IsCheckingModUpdates || _modUpdateService == null) return;
        if (IsRunning) return;
        if (!ModrinthLoaderMap.SupportsModUpdates(_server.ModLoader.Type)) return;
        if (Mods.Count == 0) return;

        try
        {
            EnsureModsCacheLoaded();
            if (!ShouldAutoCheckUpdates()) return;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Auto-check skipped: {ex.Message}", "ServerDetailViewModel");
            return;
        }

        await CheckModUpdatesAsync(silent: true);
    }

    private bool ShouldAutoCheckUpdates()
    {
        var data = _modsCacheStore?.Load();
        if (data == null) return true;

        if (data.ModFiles.Count != Mods.Count) return true;

        foreach (var mod in Mods)
        {
            if (!data.ModFiles.TryGetValue(mod.FileName, out var size) || size != mod.FileSize)
                return true;
        }

        var lastCheck = data.LastUpdatesCheckUtc != default ? data.LastUpdatesCheckUtc : _lastModsUpdateCheckUtc;
        if (lastCheck == default) return true;

        return DateTime.UtcNow - lastCheck > ModUpdateAutoCheckTtl;
    }

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
