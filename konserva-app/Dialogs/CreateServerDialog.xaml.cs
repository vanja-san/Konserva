using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace Konserva.Dialogs;

/// <summary>
/// Диалог создания нового сервера
/// </summary>
public partial class CreateServerDialog : FluentWindow
{
    private readonly IConfigService? _configService;
    private readonly IMcVersionsApi _versionsApi;
    private bool _isInitializing;
    private bool _isInstalling;
    private bool _isUpdating;
    private bool _isChangingSnapshots;
    private bool _isChangingModLoader; // Флаг для защиты от повторного вызова ModLoaderBox_SelectionChanged
    private CancellationTokenSource? _installCts;
    private bool _wasCancelled; // Флаг отмены пользователем
    private bool _isFindingCompatibleVersion;
    private string[] _allMcVersions = [];
    private HashSet<string> _paperVersions = [];
    private HashSet<string> _purpurVersions = [];
    private HashSet<string>? _neoForgeStableMcVersions;
    private HashSet<string>? _quiltSupportedVersions;

    // Кэш для избежания повторной загрузки версий загрузчиков
    private string? _lastLoadedModLoader;
    private string? _lastLoadedMcVersion;
    private bool _isLoadingInProgress;
    private string? _currentModLoader; // Текущий выбранный модлоадер

    public CreateServerDialog(IConfigService? configService = null, IMcVersionsApi? versionsApi = null)
    {
        _configService = configService;
        _versionsApi = versionsApi
            ?? (App.ServiceProvider?.GetService(typeof(IMcVersionsApi)) as IMcVersionsApi)
            ?? new McVersionsApi();

        InitializeComponent();

        // Подписываемся после InitializeComponent, чтобы избежать ошибок
        Loaded += OnLoaded;
        SourceInitialized += CreateServerDialog_SourceInitialized;
    }

    private void CreateServerDialog_SourceInitialized(object? sender, EventArgs e)
    {
        // Добавляем хук для обработки сообщений окна
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_SETCURSOR = 0x0020;
        const int IDC_ARROW = 32512;

        if (msg == WM_NCHITTEST)
        {
            // Всегда возвращаем HTCLIENT - отключаем все зоны изменения размера
            handled = true;
            return new IntPtr(HTCLIENT);
        }

        if (msg == WM_SETCURSOR)
        {
            // Принудительно устанавливаем обычный курсор
            handled = true;
            SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(IDC_ARROW)));
            return new IntPtr(1);
        }

        if (msg == WM_GETMINMAXINFO)
        {
            // Устанавливаем минимальный размер
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.x = 480;
            mmi.ptMinTrackSize.y = 500;
            mmi.ptMaxTrackSize.x = 480;
            mmi.ptMaxTrackSize.y = 500;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        try
        {
            Logger.Info("Loading dialog data...", "CreateServerDialog");

            // Подписываемся на события
            ShowSnapshotsBox.Checked += ShowSnapshotsBox_Changed;
            ShowSnapshotsBox.Unchecked += ShowSnapshotsBox_Changed;

            // Подписываемся на изменение выбора модлоадера и версии Minecraft
            ModLoaderBox.SelectionChanged += ModLoaderBox_SelectionChanged;
            McVersionBox.SelectionChanged += McVersionBox_SelectionChanged;

            // Загружаем версии Minecraft (асинхронно, чтобы не блокировать UI)
            _allMcVersions = await _versionsApi.GetMcVersions();
            Logger.Info($"Loaded {_allMcVersions.Length} Minecraft versions", "CreateServerDialog");

            // Путь по умолчанию
            ServerPathBox.Text = GetDefaultServerPath();
            Logger.Info($"Server path: {ServerPathBox.Text}", "CreateServerDialog");

            // Заполняем ComboBox версий Minecraft
            await FilterMcVersionsAsync();
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerDialog");
        }
        catch (Exception ex)
        {
            Logger.Error($"Dialog load error: {ex}", ex, "CreateServerDialog");
            await UiHelper.ShowError($"Ошибка загрузки диалога: {ex.Message}");
        }

        _isInitializing = false;
        Logger.Info("Dialog loaded successfully", "CreateServerDialog");
    }

    /// <summary>
    /// Фильтрация версий Minecraft с учётом выбранного загрузчика
    /// </summary>
    private async Task FilterMcVersionsAsync(string? modLoaderOverride = null)
    {
        Logger.Info($"FilterMcVersionsAsync started: _isUpdating={_isUpdating}, modLoaderOverride={modLoaderOverride}", "CreateServerDialog");

        // Защита от повторного входа
        if (_isUpdating || ShowSnapshotsBox == null || ModLoaderBox == null || McVersionBox == null)
        {
            Logger.Info($"FilterMcVersionsAsync skipped: _isUpdating={_isUpdating}, ShowSnapshotsBox={ShowSnapshotsBox != null}, ModLoaderBox={ModLoaderBox != null}, McVersionBox={McVersionBox != null}", "CreateServerDialog");
            return;
        }

        _isUpdating = true;
        _isChangingModLoader = true; // Блокируем ModLoaderBox_SelectionChanged
        try
        {
            var showSnapshots = ShowSnapshotsBox.IsChecked ?? false;
            // Получаем текущий выбранный загрузчик или переданный
            var selectedModLoader = modLoaderOverride ?? GetSelectedModLoader();

            Logger.Info($"FilterMcVersionsAsync: modLoader={selectedModLoader}, showSnapshots={showSnapshots}", "CreateServerDialog");

            // Загружаем списки Paper/Purpur один раз
            if (selectedModLoader == "Paper" && _paperVersions.Count == 0)
                await LoadPaperVersionsAsync();
            else if (selectedModLoader == "Purpur" && _purpurVersions.Count == 0)
                await LoadPurpurVersionsAsync();

            // Определяем поддерживаемые версии для выбранного загрузчика
            HashSet<string> supportedVersions;

            if (selectedModLoader == "NeoForge" && !showSnapshots)
            {
                // Для NeoForge при скрытии снимков — только стабильные версии
                var stableMcVersions = _neoForgeStableMcVersions ?? await GetNeoForgeStableMcVersionsAsync();
                _neoForgeStableMcVersions = stableMcVersions;

                supportedVersions = stableMcVersions.Count > 0
                    ? stableMcVersions
                    : [.. _allMcVersions.Where(v => TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
            }
            else if (selectedModLoader == "Quilt")
            {
                // Для Quilt используем список поддерживаемых версий
                if (_quiltSupportedVersions == null)
                {
                    Logger.Info("Loading Quilt supported versions...", "CreateServerDialog");
                    _quiltSupportedVersions = await GetQuiltSupportedVersionsAsync();
                    Logger.Info($"Loaded {_quiltSupportedVersions.Count} Quilt supported versions", "CreateServerDialog");
                }

                supportedVersions = _quiltSupportedVersions;
            }
            else
            {
                supportedVersions = selectedModLoader switch
                {
                    "Paper" => _paperVersions.Count > 0 ? _paperVersions : [.. _allMcVersions],
                    "Purpur" => _purpurVersions.Count > 0 ? _purpurVersions : [.. _allMcVersions],
                    "NeoForge" => [.. _allMcVersions.Where(v =>
                        TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))],
                    // Forge, Fabric, Vanilla — версии Minecraft (не NeoForge)
                    // NeoForge версии имеют 3+ части (26.1.0, 21.10.64), Minecraft — 2 части (1.21.11, 26.1)
                    _ => [.. _allMcVersions.Where(v =>
                    {
                        var parts = v.Split('.');
                        // Minecraft: 2 части (1.21.11, 26.1) или 3 части с префиксом 1.x.x
                        // NeoForge: 3+ части без префикса 1. (26.1.0, 21.10.64)
                        if (parts.Length < 2) return false;
                        if (parts.Length == 2) return true; // 26.1, 1.21 и т.д.
                        if (parts[0] == "1") return true; // 1.21.11, 1.20.4 и т.д.
                        // 3+ части, не начинающиеся с 1 — это NeoForge (26.1.0, 21.10.64)
                        return false;
                    })]
                };
            }

            var versions = _allMcVersions
                .Where(v => supportedVersions.Contains(v))
                .Where(v => showSnapshots || !IsSnapshot(v))
                .ToArray();

            Logger.Info($"Filtered MC versions for {selectedModLoader}: {versions.Length} (from {_allMcVersions.Length})", "CreateServerDialog");

            // Сохраняем текущую выбранную версию
            var currentMcVersion = McVersionBox.SelectedItem is ComboBoxItem currentItem ? currentItem.Content?.ToString() : null;

            McVersionBox.Items.Clear();
            foreach (var version in versions)
            {
                McVersionBox.Items.Add(new ComboBoxItem
                {
                    Content = version,
                    Tag = version
                });
            }

            // Восстанавливаем выбранную версию, если она всё ещё доступна
            if (!string.IsNullOrEmpty(currentMcVersion) && versions.Contains(currentMcVersion))
            {
                var matchingItem = McVersionBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content?.ToString() == currentMcVersion);
                if (matchingItem != null)
                {
                    McVersionBox.SelectedItem = matchingItem;
                    var selectedModLoaderTag = modLoaderOverride ?? GetSelectedModLoader();
                    Logger.Info($"Selected MC version: {currentMcVersion}, modLoader: {selectedModLoaderTag}", "CreateServerDialog");
                }
            }
            else if (McVersionBox.Items.Count > 0)
            {
                McVersionBox.SelectedIndex = 0;
                var selectedModLoaderTag = modLoaderOverride ?? GetSelectedModLoader();
                var firstMcVersion = (McVersionBox.Items[0] as ComboBoxItem)?.Content?.ToString();
                Logger.Info($"First MC version: {firstMcVersion}, modLoader: {selectedModLoaderTag}", "CreateServerDialog");
                Logger.Info($"Total MC versions in list: {McVersionBox.Items.Count}", "CreateServerDialog");
                Logger.Info($"All MC versions (first 10): {string.Join(", ", McVersionBox.Items.Cast<ComboBoxItem>().Take(10).Select(i => i.Content?.ToString()))}", "CreateServerDialog");
            }
            else
            {
                Logger.Warning($"NO MC VERSIONS IN LIST! modLoader={selectedModLoader}", "CreateServerDialog");
            }
        }
        finally
        {
            _isUpdating = false;
            _isChangingModLoader = false; // Разблокируем ModLoaderBox_SelectionChanged
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerDialog");
        }

        // Вызываем McVersionBox_SelectionChanged вручную для загрузки версий загрузчика
        // (автоматический вызов был заблокирован через _isChangingModLoader)
        if (!string.IsNullOrEmpty(_currentModLoader) && _currentModLoader is "Forge" or "NeoForge" or "Fabric" or "Quilt")
        {
            var selectedMcVersion = (McVersionBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(selectedMcVersion))
            {
                Logger.Info($"Calling McVersionBox_SelectionChanged manually: {_currentModLoader} for MC {selectedMcVersion}", "CreateServerDialog");
                McVersionBox_SelectionChanged(McVersionBox, new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
            }
        }
    }

    /// <summary>
    /// Получает список версий Minecraft, поддерживаемых Quilt
    /// </summary>
    private async Task<HashSet<string>> GetQuiltSupportedVersionsAsync()
    {
        var supported = new HashSet<string>();

        // Проверяем последние 50 версий (быстро)
        var recentVersions = _allMcVersions
            .Where(v => TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))
            .Take(50)
            .ToArray();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        foreach (var version in recentVersions.Take(20)) // Проверяем только первые 20 для скорости
        {
            try
            {
                var url = $"https://meta.quiltmc.org/v3/versions/loader/{version}";
                var response = await httpClient.GetStringAsync(url);

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var array = doc.RootElement.EnumerateArray();

                // Если есть хотя бы один loader — версия поддерживается
                if (array.Any())
                {
                    supported.Add(version);
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 — версия не поддерживается, пропускаем
            }
            catch (Exception)
            {
                // Игнорируем другие ошибки
            }
        }

        return supported.Count > 0 ? supported : [.. _allMcVersions];
    }

    private async Task LoadPaperVersionsAsync()
    {
        try
        {
            var response = await _versionsApi.GetStringWithDecompressionAsync(
                "https://api.papermc.io/v2/projects/paper");
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            _paperVersions = [.. doc.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetString()!)];
        }
        catch
        {
            _paperVersions = [.. _allMcVersions];
        }
    }

    private async Task LoadPurpurVersionsAsync()
    {
        try
        {
            var response = await _versionsApi.GetStringWithDecompressionAsync(
                "https://api.purpurmc.org/v2/purpur");
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            _purpurVersions = [.. doc.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetString()!)];
        }
        catch
        {
            _purpurVersions = [.. _allMcVersions];
        }
    }

    private static bool TryParseMcVersion(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        try
        {
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                major = int.Parse(parts[0]);
                minor = int.Parse(parts[1]);
                return true;
            }
        }
        catch { }

        return false;
    }

    private string GetDefaultServerPath()
    {
        try
        {
            var config = _configService?.GetConfig();
            if (config != null && !string.IsNullOrEmpty(config.ServersDirectory))
            {
                Directory.CreateDirectory(config.ServersDirectory);
                return config.ServersDirectory;
            }
        }
        catch { }

        var exeDir = AppContext.BaseDirectory;
        var serversDir = Path.Combine(exeDir, "Servers");
        Directory.CreateDirectory(serversDir);
        return serversDir;
    }

    private string GetSelectedModLoader()
    {
        if (ModLoaderBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "Vanilla";
        return "Vanilla";
    }

    private static bool IsSnapshot(string version)
    {
        // Признаки снимков
        var snapshotMarkers = new[] { "w", "-pre", "-rc", "-snapshot", "Pre-Release", " pre", "inf" };
        if (snapshotMarkers.Any(m => version.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Снапшоты старых версий
        var snapshotPrefixes = new[] { "a", "b", "c", "rd" };
        if (snapshotPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Quilt beta версии
        if (version.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Проверка: является ли версия NeoForge снимком (нестабильной)
    /// Формат: MAJOR.MINOR.PATCH[-beta/-alpha.x+pre-x/-alpha.x+snapshot-x]
    /// Примеры: 21.10.64 (стабильная), 21.11.0-beta (beta), 26.1.0.0-alpha.1+snapshot-1 (alpha)
    /// </summary>
    private static bool IsNeoForgeSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

        // Проверяем наличие суффиксов
        if (fullVersion.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-alpha.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+snapshot", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+pre", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Проверка: является ли версия Quilt снимком
    /// Формат: X.Y.Z[-beta.X/-pre.X/-rc.X]
    /// Примеры: 0.12.0 (стабильная), 0.13.0-beta.1 (beta), 0.14.0-pre.2 (pre-release)
    /// </summary>
    private static bool IsQuiltSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

        // Проверяем суффиксы: -beta, -pre, -rc
        if (fullVersion.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-pre.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-rc.", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Получает версии Minecraft, для которых есть стабильные выпуски NeoForge
    /// Формат версии NeoForge: MAJOR.MINOR.PATCH или MAJOR.MINOR.PATCH-beta
    /// Пример: 21.10.64 -> MC 1.21.10, 21.11.0-beta -> MC 1.21.11 (beta)
    /// </summary>
    private static async Task<HashSet<string>> GetNeoForgeStableMcVersionsAsync()
    {
        var stableVersions = new HashSet<string>();

        try
        {
            var url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await httpClient.GetStringAsync(url);

            var matches = NeoForgeVersionRegex().Matches(response);

            foreach (Match match in matches)
            {
                var fullVersion = match.Groups[1].Value;

                // Пример: 21.10.64 или 21.11.0-beta или 26.1.0.0-alpha.1+snapshot-1
                // Извлекаем MAJOR.MINOR из первой части (до дефиса)
                var versionPart = fullVersion.Split('-')[0]; // "21.10.64" или "26.1.0.0"
                var parts = versionPart.Split('.');

                if (parts.Length >= 2)
                {
                    // Формируем версию Minecraft: 1.MAJOR.MINOR
                    // 21.10 -> 1.21.10, 26.1 -> 1.26.1
                    var mcVersion = $"1.{parts[0]}.{parts[1]}";

                    // Добавляем только стабильные версии (не снимки)
                    if (!IsNeoForgeSnapshot(fullVersion))
                    {
                        stableVersions.Add(mcVersion);
                    }
                }
            }
        }
        catch
        {
            // При ошибке возвращаем пустой список, потом будет fallback
        }

        return stableVersions;
    }

    private void ShowSnapshotsBox_Changed(object? sender, RoutedEventArgs e)
    {
        // Защита от вызова во время инициализации и повторных входов
        if (_isInitializing || _isChangingSnapshots || _isUpdating || McVersionBox == null || ModLoaderBox == null)
            return;

        _isChangingSnapshots = true;
        try
        {
            // Сбрасываем кэш для перезагрузки версий
            _lastLoadedModLoader = null;
            _lastLoadedMcVersion = null;

            _ = FilterMcVersionsAsync();
        }
        finally
        {
            _isChangingSnapshots = false;
        }
    }

    private void ServerNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || string.IsNullOrWhiteSpace(ServerNameBox.Text))
            return;

        var invalidChars = Path.GetInvalidFileNameChars();
        var serverNameClean = new string([.. ServerNameBox.Text.Where(c => !invalidChars.Contains(c))]);
        var exeDir = AppContext.BaseDirectory;
        var serversDir = Path.Combine(exeDir, "Servers");
        ServerPathBox.Text = Path.Combine(serversDir, serverNameClean);
    }

    private async void ModLoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Защита от повторного вызова
        if (_isChangingModLoader)
        {
            Logger.Info($"ModLoaderBox_SelectionChanged skipped: _isChangingModLoader=True", "CreateServerDialog");
            return;
        }

        // Если идёт обновление или ещё не инициализировано — пропускаем
        if (_isUpdating)
        {
            Logger.Info("ModLoaderBox_SelectionChanged skipped: _isUpdating=True", "CreateServerDialog");
            return;
        }

        if (_isInitializing || LoaderVersionBox == null || McVersionBox == null)
        {
            Logger.Info($"ModLoaderBox_SelectionChanged skipped: _isInitializing={_isInitializing}, LoaderVersionBox={LoaderVersionBox != null}, McVersionBox={McVersionBox != null}", "CreateServerDialog");
            return;
        }

        if (ModLoaderBox.SelectedItem is not ComboBoxItem item)
        {
            Logger.Info("ModLoaderBox_SelectionChanged: no selected item", "CreateServerDialog");
            return;
        }

        var tag = item.Tag?.ToString();
        Logger.Info($"ModLoaderBox_SelectionChanged: {tag}", "CreateServerDialog");

        // Сохраняем текущий модлоадер
        _currentModLoader = tag;

        var isEnabled = tag is "Forge" or "NeoForge" or "Fabric" or "Quilt";
        LoaderVersionBox.IsEnabled = isEnabled;

        // Очищаем список версий загрузчика и сбрасываем кэш
        LoaderVersionBox.Items.Clear();
        _lastLoadedModLoader = null;
        _lastLoadedMcVersion = null;

        // Перефильтруем версии Minecraft для нового загрузчика
        await FilterMcVersionsAsync(tag);
    }

    /// <summary>
    /// Загружает версии загрузчика для указанной версии Minecraft
    /// </summary>
    private async Task LoadLoaderVersions(string modLoaderType, string mcVersion)
    {
        Logger.Info($"LoadLoaderVersions: {modLoaderType} for MC {mcVersion}", "CreateServerDialog");

        // Защита от повторной загрузки для тех же параметров
        if (_isLoadingInProgress ||
            (_lastLoadedModLoader == modLoaderType && _lastLoadedMcVersion == mcVersion))
        {
            Logger.Info($"LoadLoaderVersions skipped: isLoading={_isLoadingInProgress}, lastModLoader={_lastLoadedModLoader}, lastMcVersion={_lastLoadedMcVersion}", "CreateServerDialog");
            return;
        }

        // Защита от параллельного поиска совместимой версии
        if (_isFindingCompatibleVersion)
        {
            Logger.Info($"LoadLoaderVersions skipped: _isFindingCompatibleVersion=True", "CreateServerDialog");
            return;
        }

        // Устанавливаем флаг загрузки и сохраняем кэш
        _isLoadingInProgress = true;
        _lastLoadedModLoader = modLoaderType;
        _lastLoadedMcVersion = mcVersion;

        try
        {
            string[] versions = modLoaderType switch
            {
                "Forge" => await _versionsApi.GetForgeVersions(mcVersion),
                "NeoForge" => await _versionsApi.GetNeoForgeVersions(mcVersion),
                "Fabric" => await _versionsApi.GetFabricVersions(mcVersion),
                "Quilt" => await _versionsApi.GetQuiltVersions(mcVersion),
                _ => []
            };

            Logger.Info($"Loaded {versions.Length} {modLoaderType} versions", "CreateServerDialog");

            // Фильтруем снимки для NeoForge
            var showSnapshots = ShowSnapshotsBox?.IsChecked ?? false;
            if (modLoaderType == "NeoForge" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !IsNeoForgeSnapshot(v))];
                Logger.Info($"Filtered NeoForge versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerDialog");
            }

            // Фильтруем снимки для Quilt
            if (modLoaderType == "Quilt" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !IsQuiltSnapshot(v))];
                Logger.Info($"Filtered Quilt versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerDialog");
            }

            // Если версий нет, пробуем найти совместимую версию Minecraft
            if ((versions.Length == 0 || (versions.Length == 1 && versions[0] == "latest"))
                && !_isFindingCompatibleVersion)
            {
                _isFindingCompatibleVersion = true;
                try
                {
                    var compatibleMcVersion = await FindLastCompatibleMcVersionAsync(modLoaderType, mcVersion, showSnapshots);

                    if (compatibleMcVersion != null && compatibleMcVersion != mcVersion)
                    {
                        // Нашли совместимую версию — выбираем её в списке
                        var compatibleItem = McVersionBox.Items.Cast<ComboBoxItem>()
                            .FirstOrDefault(item => item.Content?.ToString() == compatibleMcVersion);

                        if (compatibleItem != null)
                        {
                            // Сбрасываем флаг перед повторной загрузкой
                            _isFindingCompatibleVersion = false;

                            McVersionBox.SelectedItem = compatibleItem;
                            // Рекурсивно загружаем версии для найденной версии
                            _ = LoadLoaderVersions(modLoaderType, compatibleMcVersion);
                            return;
                        }
                    }
                }
                finally
                {
                    // Сбрасываем флаг, если он ещё установлен
                    if (_isFindingCompatibleVersion)
                        _isFindingCompatibleVersion = false;
                }
            }

            if (versions.Length > 0)
            {
                // Сразу выбираем первую версию (стабильную или тестовую в зависимости от чекбокса)
                var firstVersion = versions[0];
                LoaderVersionBox.Items.Add(new ComboBoxItem
                {
                    Content = firstVersion,
                    IsSelected = true
                });
                Logger.Info($"Selected {modLoaderType} version: {firstVersion}", "CreateServerDialog");

                // Добавляем остальные версии (до 50)
                foreach (var version in versions.Skip(1).Take(49))
                {
                    LoaderVersionBox.Items.Add(new ComboBoxItem { Content = version });
                }
            }
            else
            {
                // Если версий нет, используем "latest" как запасной вариант
                LoaderVersionBox.Items.Add(new ComboBoxItem
                {
                    Content = "latest",
                    IsSelected = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load {modLoaderType} versions: {ex.Message}", ex, "CreateServerDialog");

            LoaderVersionBox.Items.Add(new ComboBoxItem
            {
                Content = "latest",
                IsSelected = true
            });
        }
        finally
        {
            // Снимаем флаг загрузки
            _isLoadingInProgress = false;
        }
    }

    /// <summary>
    /// Ищет последнюю совместимую версию Minecraft для загрузчика
    /// </summary>
    private async Task<string?> FindLastCompatibleMcVersionAsync(string modLoaderType, string _, bool showSnapshots)
    {
        // Проверяем последние 10 версий Minecraft (наиболее новые)
        var mcVersions = _allMcVersions
            .Where(v => showSnapshots || !IsSnapshot(v))
            .ToList();

        int checkCount = Math.Min(10, mcVersions.Count);

        for (int i = 0; i < checkCount; i++)
        {
            var version = mcVersions[i]; // идём от самой новой к старой
            try
            {
                string[] versions = modLoaderType switch
                {
                    "Forge" => await _versionsApi.GetForgeVersions(version),
                    "NeoForge" => await _versionsApi.GetNeoForgeVersions(version),
                    "Fabric" => await _versionsApi.GetFabricVersions(version),
                    "Quilt" => await _versionsApi.GetQuiltVersions(version),
                    _ => []
                };

                // Фильтруем снимки для NeoForge/Quilt
                if (modLoaderType == "NeoForge" && !showSnapshots)
                {
                    versions = [.. versions.Where(v => !IsNeoForgeSnapshot(v))];
                }
                else if (modLoaderType == "Quilt" && !showSnapshots)
                {
                    versions = [.. versions.Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase))];
                }

                if (versions.Length > 0 && versions[0] != "latest")
                {
                    return version;
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 — версия не поддерживается, пропускаем
            }
            catch
            {
                // Игнорируем остальные ошибки
            }
        }

        return null;
    }

    private void McVersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Защита от вызова во время инициализации, обновления или смены модлоадера
        if (_isInitializing || _isChangingSnapshots || _isFindingCompatibleVersion || _isChangingModLoader || McVersionBox == null || ModLoaderBox == null)
        {
            Logger.Info($"McVersionBox_SelectionChanged skipped: _isInitializing={_isInitializing}, _isChangingSnapshots={_isChangingSnapshots}, _isFindingCompatibleVersion={_isFindingCompatibleVersion}, _isChangingModLoader={_isChangingModLoader}", "CreateServerDialog");
            return;
        }

        // При смене версии Minecraft перезагружаем версии загрузчика
        if (ModLoaderBox.SelectedItem is ComboBoxItem item &&
            McVersionBox.SelectedItem is ComboBoxItem mcItem)
        {
            // Используем сохранённый модлоадер, а не текущий выбранный (чтобы избежать гонки)
            var tag = _currentModLoader ?? item.Tag?.ToString();
            var mcVersion = mcItem.Content?.ToString();

            if (!string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(mcVersion) &&
                tag is "Forge" or "NeoForge" or "Fabric" or "Quilt")
            {
                // Очищаем старые версии и загружаем новые
                LoaderVersionBox.Items.Clear();
                Logger.Info($"McVersionBox_SelectionChanged: calling LoadLoaderVersions for {tag}, MC: {mcVersion}", "CreateServerDialog");
                _ = LoadLoaderVersions(tag, mcVersion);
            }
        }
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сервера",
            InitialDirectory = GetDefaultFolderPath()
        };

        if (dialog.ShowDialog() == true)
        {
            ServerPathBox.Text = dialog.FolderName;
            SaveServersPath(dialog.FolderName);
        }
    }

    private void SaveServersPath(string path)
    {
        try
        {
            var config = _configService?.GetConfig();
            if (config != null)
            {
                config.ServersDirectory = path;
                _configService?.SaveConfig(config);
            }
        }
        catch
        {
            // Игнорируем ошибки сохранения
        }
    }

    private string GetDefaultFolderPath()
    {
        try
        {
            var config = _configService?.GetConfig();
            if (config != null && !string.IsNullOrEmpty(config.ServersDirectory))
            {
                return config.ServersDirectory;
            }
        }
        catch { }

        var exeDir = AppContext.BaseDirectory;
        var serversDir = Path.Combine(exeDir, "Servers");
        Directory.CreateDirectory(serversDir);
        return serversDir;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите папку с сервером",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog() == true)
            {
                var serverPath = dialog.FolderName;

                var existingServer = MainWindow.ServerManager.GetServers()
                    .FirstOrDefault(s => s.Path.Equals(serverPath, StringComparison.OrdinalIgnoreCase));

                if (existingServer != null)
                {
                    await UiHelper.ShowWarning($"Сервер из этой папки уже импортирован:\n{existingServer.Name}");
                    return;
                }

                var jarFile = Directory.GetFiles(serverPath, "*.jar").FirstOrDefault();
                if (jarFile == null)
                {
                    await UiHelper.ShowWarning("В выбранной папке не найден JAR файл сервера.");
                    return;
                }

                var launchType = McServerInstaller.GetServerLaunchType(serverPath);
                var modLoader = launchType switch
                {
                    McServerInstaller.ServerLaunchType.Forge => new ModLoader { Type = ModLoaderType.Forge },
                    McServerInstaller.ServerLaunchType.NeoForge => new ModLoader { Type = ModLoaderType.NeoForge },
                    McServerInstaller.ServerLaunchType.Fabric => new ModLoader { Type = ModLoaderType.Fabric },
                    McServerInstaller.ServerLaunchType.Quilt => new ModLoader { Type = ModLoaderType.Quilt },
                    McServerInstaller.ServerLaunchType.Standard => new ModLoader { Type = ModLoaderType.Vanilla },
                    _ => new ModLoader { Type = ModLoaderType.Vanilla }
                };

                var mcVersion = "Неизвестно";
                var versionFile = Path.Combine(serverPath, "version.json");
                if (File.Exists(versionFile))
                {
                    try
                    {
                        var json = File.ReadAllText(versionFile);
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                        if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                        {
                            mcVersion = idElement.GetString() ?? "Unknown";
                        }
                    }
                    catch { }
                }

                var serverName = Path.GetFileName(serverPath);
                _ = MainWindow.ServerManager.CreateServer(serverName, mcVersion, modLoader, serverPath);

                await UiHelper.ShowInfo($"Сервер \"{serverName}\" успешно импортирован!");

                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            await UiHelper.ShowError($"Ошибка импорта сервера: {ex.Message}");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetInstallingState(bool isInstalling)
    {
        _isInstalling = isInstalling;

        ServerNameBox.IsEnabled = !isInstalling;
        McVersionBox.IsEnabled = !isInstalling;
        ShowSnapshotsBox.IsEnabled = !isInstalling;
        ModLoaderBox.IsEnabled = !isInstalling;
        LoaderVersionBox.IsEnabled = !isInstalling;
        ServerPathBox.IsEnabled = !isInstalling;

        this.Invoke(() =>
        {
            if (isInstalling)
            {
                // Показываем прогресс
                ProgressGrid.Visibility = Visibility.Visible;
                ProgressPanel.Value = 0;
                ProgressText.Text = "Подготовка...";
                ProgressPercentText.Text = "0%";

                // Меняем кнопку на "Отмена"
                ActionOrCancelButton.Content = "Отмена";
                ActionOrCancelButton.Appearance = ControlAppearance.Danger;
                ActionOrCancelButton.IsEnabled = true; // Кнопка должна быть активна!

                // Скрываем импорт
                ImportButton.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = false;
            }
            else
            {
                // Скрываем прогресс
                ProgressGrid.Visibility = Visibility.Collapsed;

                // Возвращаем кнопку "Создать"
                ActionOrCancelButton.Content = LocalizationManager.Get("CreateServer_Create") ?? "Создать";
                ActionOrCancelButton.Appearance = ControlAppearance.Success;
                ActionOrCancelButton.IsEnabled = true;

                // Показываем импорт
                ImportButton.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = true;
            }
        });

        Title = isInstalling ? "Установка сервера..." : "Создание сервера";
    }

    /// <summary>
    /// Обработчик кнопки Создать/Отмена
    /// </summary>
    private async void ActionOrCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling)
        {
            // Отмена установки
            _wasCancelled = true;
            _installCts?.Cancel();
        }
        else
        {
            // Создание сервера
            await Create_Click_Handler();
        }
    }

    /// <summary>
    /// Логика создания сервера (из Create_Click)
    /// </summary>
    private async Task Create_Click_Handler()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ServerNameBox.Text))
            {
                await UiHelper.ShowWarning("Введите имя сервера");
                ServerNameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ServerPathBox.Text))
            {
                await UiHelper.ShowWarning("Выберите папку для сервера");
                return;
            }

            if (MainWindow.ServerManager == null)
            {
                await UiHelper.ShowError("ServerManager не инициализирован!");
                return;
            }

            var serverName = ServerNameBox.Text.Trim();

            // Проверяем, существует ли сервер с таким именем
            var existingServers = MainWindow.ServerManager.GetServers();
            if (existingServers.Any(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase)))
            {
                await UiHelper.ShowWarning($"Сервер с именем \"{serverName}\" уже существует. Пожалуйста, выберите другое имя.");
                ServerNameBox.Focus();
                return;
            }

            // Валидация пути к серверу
            var serverPath = ServerPathBox.Text.Trim();
            var pathValidationResult = ValidateServerPath(serverPath);
            if (!pathValidationResult.IsValid)
            {
                await UiHelper.ShowWarning(pathValidationResult.ErrorMessage);
                return;
            }

            var mcVersion = McVersionBox.SelectedItem is ComboBoxItem mcItem
                ? mcItem.Content?.ToString() ?? "1.20.4"
                : "1.20.4";

            var modLoaderType = ModLoaderType.Vanilla;
            if (ModLoaderBox.SelectedItem is ComboBoxItem modLoaderItem && modLoaderItem.Tag != null)
            {
                var modLoaderTypeName = modLoaderItem.Tag.ToString()!;
                Enum.TryParse(modLoaderTypeName, true, out modLoaderType);
            }

            var loaderVersion = "latest";
            if (LoaderVersionBox.IsEnabled &&
                LoaderVersionBox.SelectedItem is ComboBoxItem loaderItem)
            {
                loaderVersion = loaderItem.Content?.ToString() ?? "latest";
            }

            var modLoader = new ModLoader
            {
                Type = modLoaderType,
                Version = mcVersion,
                LoaderVersion = loaderVersion
            };

            var server = MainWindow.ServerManager.CreateServer(serverName, mcVersion, modLoader, serverPath);

            SetInstallingState(true);

            _ = InstallServerInBackground(server, modLoaderType, mcVersion, loaderVersion, serverPath);
        }
        catch (Exception ex)
        {
            SetInstallingState(false);
            await UiHelper.ShowError($"Ошибка при создании сервера: {ex.Message}");
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isInstalling && _installCts != null)
        {
            _installCts.Cancel();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Плавно обновляет прогресс
    /// </summary>
    private void UpdateProgress(double value, string statusText)
    {
        this.Invoke(() =>
        {
            ProgressPanel.Value = value;
            ProgressText.Text = statusText;
            ProgressPercentText.Text = $"{value:F0}%";
        });
    }

    private async Task InstallServerInBackground(Server server, ModLoaderType modLoaderType,
        string mcVersion, string loaderVersion, string serverPath)
    {
        _wasCancelled = false; // Сбрасываем флаг отмены
        _installCts = new CancellationTokenSource();

        try
        {
            // Явно показываем прогресс в начале
            this.Invoke(() =>
            {
                ProgressGrid.Visibility = Visibility.Visible;
                ProgressPanel.Value = 0;
                ProgressText.Text = "Подготовка...";
                ProgressPercentText.Text = "0%";
            });

            server.InstallStatus = $"Установка {server.Name}...";
            MainWindow.ServerManager.UpdateServer(server);

            // Создаём прогресс с обновлением UI
            var progress = new Progress<double>(percent =>
            {
                // Определяем текст задачи в зависимости от процента
                string statusText = percent switch
                {
                    < 5 => "Получение данных...",
                    < 15 => "Загрузка файлов...",
                    < 30 => "Проверка совместимости...",
                    < 50 => "Установка модлоадера...",
                    < 70 => "Загрузка библиотек...",
                    < 85 => "Настройка сервера...",
                    < 95 => "Финализация...",
                    _ => "Завершение..."
                };

                UpdateProgress(percent, statusText);

                server.InstallStatus = $"{statusText} {percent:F0}%";
                MainWindow.ServerManager.UpdateServer(server);
            });

            var installResult = await McServerInstaller.InstallServer(
                modLoaderType,
                mcVersion,
                loaderVersion,
                serverPath,
                server.Port,
                server.Settings.RamMin,
                server.Settings.RamMax,
                progress,
                _installCts.Token);

            if (!installResult.Success)
            {
                // Не показываем ошибку если отмена пользователем
                if (!_wasCancelled)
                {
                    server.InstallStatus = $"Ошибка: {installResult.Error}";
                    server.Status = ServerStatus.Error;
                    Logger.Error($"Server install failed: {installResult.Error}");

                    await this.InvokeAsync(async () =>
                    {
                        SetInstallingState(false);
                        await UiHelper.ShowError($"Не удалось установить сервер:\n{installResult.Error}");
                    });
                }
                else
                {
                    server.InstallStatus = "Отменено";
                    server.Status = ServerStatus.Stopped;
                    Logger.Info("Server install cancelled by user");

                    // Удаляем папку недосозданного сервера
                    try
                    {
                        if (!string.IsNullOrEmpty(server.Path) && System.IO.Directory.Exists(server.Path))
                        {
                            System.IO.Directory.Delete(server.Path, true);
                            Logger.Info($"Deleted incomplete server folder: {server.Path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to delete server folder on cancel: {ex.Message}");
                    }

                    await this.InvokeAsync(async () =>
                    {
                        SetInstallingState(false);
                        // Удаляем сервер из списка
                        await MainWindow.ServerManager.DeleteServerAsync(server.Id);
                    });
                }
            }
            else
            {
                server.InstallStatus = "Готов";
                server.Status = ServerStatus.Stopped;
                Logger.Info($"Server install completed: {server.Name}");

                await this.InvokeAsync(() =>
                {
                    UpdateProgress(100, "Готово!");
                    DialogResult = true;
                    Close();
                });
            }
        }
        catch (OperationCanceledException)
        {
            server.InstallStatus = "Отменено";
            server.Status = ServerStatus.Error;
            Logger.Info("Server install cancelled");

            await this.InvokeAsync(() =>
            {
                SetInstallingState(false);
            });
        }
        catch (Exception ex)
        {
            server.InstallStatus = $"Ошибка: {ex.Message}";
            server.Status = ServerStatus.Error;
            Logger.Error($"Server install exception: {ex}");

            await this.InvokeAsync(async () =>
            {
                SetInstallingState(false);
                await UiHelper.ShowError($"Ошибка при установке сервера:\n{ex.Message}");
            });
        }
        finally
        {
            _installCts?.Dispose();
            _installCts = null;
        }

        MainWindow.ServerManager.UpdateServer(server);
    }

    /// <summary>
    /// Результат валидации пути
    /// </summary>
    private sealed class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Валидация пути к серверу
    /// </summary>
    private PathValidationResult ValidateServerPath(string path)
    {
        try
        {
            // Проверка на пустой путь
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь к серверу не может быть пустым"
                };
            }

            // Проверка на недопустимые символы в пути
            var invalidPathChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidPathChars) >= 0)
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь содержит недопустимые символы"
                };
            }

            // Проверка на слишком длинный путь (максимум 260 символов для Windows)
            if (path.Length > 260)
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь слишком длинный (максимум 260 символов)"
                };
            }

            // Если папка существует - проверяем, не является ли она файлом
            if (File.Exists(path))
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Указанный путь является файлом, а не папкой"
                };
            }

            // Если папка существует - проверяем, не является ли она системной
            if (Directory.Exists(path))
            {
                var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
                
                // Запрещаем создание серверов в системных папках
                var systemPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant(),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant(),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)?.ToLowerInvariant() ?? ""
                };

                if (systemPaths.Any(sysPath => !string.IsNullOrEmpty(sysPath) && normalizedPath.StartsWith(sysPath)))
                {
                    return new PathValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Нельзя создавать серверы в системных папках Windows"
                    };
                }

                // Проверяем, пуста ли папка (если не пуста - предупреждаем)
                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    return new PathValidationResult
                    {
                        IsValid = true, // Не ошибка, но предупреждение
                        ErrorMessage = $"Папка не пуста ({files.Length} файлов). Убедитесь, что это правильное место для сервера."
                    };
                }
            }
            else
            {
                // Папка не существует - проверяем, можем ли её создать
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    return new PathValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Родительская папка не существует"
                    };
                }

                // Проверяем, есть ли права на создание папки
                try
                {
                    var tempPath = Path.Combine(path, ".konserva_test");
                    Directory.CreateDirectory(tempPath);
                    Directory.Delete(tempPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return new PathValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Недостаточно прав для создания папки"
                    };
                }
            }

            return new PathValidationResult
            {
                IsValid = true,
                ErrorMessage = string.Empty
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Path validation error: {ex.Message}", ex, "CreateServerDialog");
            return new PathValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Ошибка валидации пути: {ex.Message}"
            };
        }
    }

    [GeneratedRegex(@"<version>([^<]+)</version>")]
    private static partial Regex NeoForgeVersionRegex();
}