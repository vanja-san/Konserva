using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using static Konserva.Models.ApiUrls;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Konserva.Pages;

/// <summary>
/// Страница создания нового сервера
/// </summary>
public partial class CreateServerPage : Page
{
    private readonly IConfigService? _configService;
    private readonly IMcVersionsApi _versionsApi;
    private readonly IServerInstaller _installer;
    private bool _isInitializing;
    private bool _isInstalling;
    private bool _isUpdating;
    private bool _isChangingSnapshots;
    private bool _isChangingModLoader;
    private CancellationTokenSource? _installCts;
    private bool _wasCancelled;
    private bool _isFindingCompatibleVersion;
    private string[] _allMcVersions = [];
    private HashSet<string> _paperVersions = [];
    private HashSet<string>? _neoForgeStableMcVersions;
    private HashSet<string>? _quiltSupportedVersions;
    private string? _lastLoadedModLoader;
    private string? _lastLoadedMcVersion;
    private bool _isLoadingInProgress;
    private string? _currentModLoader;
    private CancellationTokenSource? _loaderLoadingCts;

    public CreateServerPage(IConfigService? configService = null, IMcVersionsApi? versionsApi = null, IServerInstaller? installer = null)
    {
        _configService = configService;
        _versionsApi = versionsApi
            ?? (App.ServiceProvider?.GetService(typeof(IMcVersionsApi)) as IMcVersionsApi)
            ?? throw new InvalidOperationException("IMcVersionsApi not available");
        _installer = installer
            ?? (App.ServiceProvider?.GetService(typeof(IServerInstaller)) as IServerInstaller)
            ?? new McServerInstaller(new HttpClient());

        InitializeComponent();

        Title = LocalizationManager.Get("CreateServer_Title");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        try
        {
            Logger.Info("Loading CreateServerPage data...", "CreateServerPage");

            ShowSnapshotsBox.Checked += ShowSnapshotsBox_Changed;
            ShowSnapshotsBox.Unchecked += ShowSnapshotsBox_Changed;

            ModLoaderBox.SelectionChanged += ModLoaderBox_SelectionChanged;
            McVersionBox.SelectionChanged += McVersionBox_SelectionChanged;

            _allMcVersions = await _versionsApi.GetMcVersions();
            Logger.Info($"Loaded {_allMcVersions.Length} Minecraft versions", "CreateServerPage");

            ServerPathBox.Text = GetDefaultServerPath();
            Logger.Info($"Server path: {ServerPathBox.Text}", "CreateServerPage");

            await FilterMcVersionsAsync();
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerPage");
        }
        catch (Exception ex)
        {
            Logger.Error($"Page load error: {ex}", ex, "CreateServerPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Error_DialogLoad")}: {ex.Message}");
        }

        _isInitializing = false;
        Logger.Info("CreateServerPage loaded successfully", "CreateServerPage");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отменяем установку, если пользователь ушёл со страницы
        if (_isInstalling && _installCts != null)
        {
            _wasCancelled = true;
            _installCts.Cancel();
        }

        _installCts?.Dispose();
        _installCts = null;
    }

    /// <summary>
    /// Навигация назад к списку серверов
    /// </summary>
    private void NavigateBackToServers()
    {
        if (App.MainWindow?.ContentFrame.CanGoBack == true)
        {
            App.MainWindow.ContentFrame.GoBack();
        }
        else
        {
            App.MainWindow?.ContentFrame.Navigate(new ServersPage());
        }
    }

    // ======================== Фильтрация версий ========================

    private async Task FilterMcVersionsAsync(string? modLoaderOverride = null)
    {
        Logger.Info($"FilterMcVersionsAsync started: _isUpdating={_isUpdating}, modLoaderOverride={modLoaderOverride}", "CreateServerPage");

        if (_isUpdating || ShowSnapshotsBox == null || ModLoaderBox == null || McVersionBox == null)
        {
            Logger.Info($"FilterMcVersionsAsync skipped", "CreateServerPage");
            return;
        }

        _isUpdating = true;
        _isChangingModLoader = true;
        try
        {
            var showSnapshots = ShowSnapshotsBox.IsChecked ?? false;
            var selectedModLoader = modLoaderOverride ?? GetSelectedModLoader();

            Logger.Info($"FilterMcVersionsAsync: modLoader={selectedModLoader}, showSnapshots={showSnapshots}", "CreateServerPage");

            if (selectedModLoader == "Paper" && _paperVersions.Count == 0)
                await LoadPaperVersionsAsync();

            HashSet<string> supportedVersions;

            if (selectedModLoader == "NeoForge" && !showSnapshots)
            {
                if (_neoForgeStableMcVersions != null)
                {
                    supportedVersions = _neoForgeStableMcVersions.Count > 0
                        ? _neoForgeStableMcVersions
                        : [.. _allMcVersions.Where(v => TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
                }
                else
                {
                    supportedVersions = [.. _allMcVersions.Where(v => TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
                    _ = LoadNeoForgeStableVersionsAsync();
                }
            }
            else if (selectedModLoader == "NeoForge")
            {
                supportedVersions = [.. _allMcVersions.Where(v =>
                    TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
            }
            else if (selectedModLoader == "Quilt")
            {
                if (_quiltSupportedVersions == null)
                {
                    Logger.Info("Loading Quilt supported versions...", "CreateServerPage");
                    _quiltSupportedVersions = await GetQuiltSupportedVersionsAsync();
                    Logger.Info($"Loaded {_quiltSupportedVersions.Count} Quilt supported versions", "CreateServerPage");
                }
                supportedVersions = _quiltSupportedVersions;
            }
            else if (selectedModLoader == "Paper")
            {
                if (_paperVersions.Count == 0)
                    await LoadPaperVersionsAsync();
                supportedVersions = _paperVersions.Count > 0 ? _paperVersions : [.. _allMcVersions];
            }
            else
            {
                supportedVersions = [.. _allMcVersions];
            }

            var versions = _allMcVersions
                .Where(v => supportedVersions.Contains(v))
                .Where(v => showSnapshots || !IsSnapshot(v))
                .ToArray();

            Logger.Info($"Filtered MC versions for {selectedModLoader}: {versions.Length} (from {_allMcVersions.Length})", "CreateServerPage");

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

            if (!string.IsNullOrEmpty(currentMcVersion) && versions.Contains(currentMcVersion))
            {
                var matchingItem = McVersionBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content?.ToString() == currentMcVersion);
                if (matchingItem != null)
                {
                    McVersionBox.SelectedItem = matchingItem;
                }
            }
            else if (McVersionBox.Items.Count > 0)
            {
                McVersionBox.SelectedIndex = 0;
            }
            else
            {
                return;
            }


        }
        finally
        {
            _isUpdating = false;
            _isChangingModLoader = false;
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerPage");
        }

        if (!string.IsNullOrEmpty(_currentModLoader) && _currentModLoader is "Forge" or "NeoForge" or "Fabric" or "Quilt")
        {
            var selectedMcVersion = (McVersionBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(selectedMcVersion))
            {
                Logger.Info($"Calling LoadLoaderVersions manually: {_currentModLoader} for MC {selectedMcVersion}", "CreateServerPage");
                _ = LoadLoaderVersions(_currentModLoader, selectedMcVersion);
            }
        }
    }

    private async Task<HashSet<string>> GetQuiltSupportedVersionsAsync()
    {
        var supported = new HashSet<string>();

        var recentVersions = _allMcVersions
            .Where(v => TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))
            .Take(50)
            .ToArray();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        foreach (var version in recentVersions.Take(20))
        {
            try
            {
                var url = $"{QuiltVersionsLoader}/{version}";
                var response = await httpClient.GetStringAsync(url);

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var array = doc.RootElement.EnumerateArray();

                if (array.Any())
                {
                    supported.Add(version);
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 — версия не поддерживается
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to check Quilt support for version {version}: {ex.Message}", "CreateServerPage");
            }
        }

        return supported.Count > 0 ? supported : [.. _allMcVersions];
    }

    private async Task LoadPaperVersionsAsync()
    {
        try
        {
            var response = await _versionsApi.GetStringWithDecompressionAsync(
                PaperApi + "/projects/paper");
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            _paperVersions = [.. doc.RootElement.GetProperty("versions")
                .EnumerateObject()
                .SelectMany(g => g.Value.EnumerateArray())
                .Select(v => v.GetString()!)];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load Paper versions, using fallback: {ex.Message}", "CreateServerPage");
            _paperVersions = [.. _allMcVersions];
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
        catch
        {
            // Ignore parse errors
        }

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
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get config for default server path: {ex.Message}", "CreateServerPage");
        }

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
        var snapshotMarkers = new[] { "w", "-pre", "-rc", "-snapshot", "Pre-Release", " pre", "inf" };
        if (snapshotMarkers.Any(m => version.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return true;

        var snapshotPrefixes = new[] { "a", "b", "c", "rd" };
        if (snapshotPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (version.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsNeoForgeSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

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

    private static bool IsQuiltSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

        if (fullVersion.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-pre.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-rc.", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task<HashSet<string>> GetNeoForgeStableMcVersionsAsync()
    {
        var stableVersions = new HashSet<string>();

        try
        {
            var url = NeoForgeMetadata;

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await httpClient.GetStringAsync(url);

            var matches = NeoForgeVersionRegex().Matches(response);

            foreach (Match match in matches)
            {
                var fullVersion = match.Groups[1].Value;
                var versionPart = fullVersion.Split('-')[0];
                var parts = versionPart.Split('.');

                if (parts.Length >= 2)
                {
                    var mcVersion = $"1.{parts[0]}.{parts[1]}";

                    if (!IsNeoForgeSnapshot(fullVersion))
                    {
                        stableVersions.Add(mcVersion);
                    }
                }
            }
        }
        catch
        {
            // При ошибке возвращаем пустой список
        }

        return stableVersions;
    }

    private async Task LoadNeoForgeStableVersionsAsync()
    {
        try
        {
            var versions = await GetNeoForgeStableMcVersionsAsync();
            _neoForgeStableMcVersions = versions;

            if (GetSelectedModLoader() == "NeoForge" && !(ShowSnapshotsBox?.IsChecked ?? false))
            {
                _ = FilterMcVersionsAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load NeoForge stable versions: {ex.Message}", "CreateServerPage");
        }
    }

    private void ShowSnapshotsBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isChangingSnapshots || _isUpdating || McVersionBox == null || ModLoaderBox == null)
            return;

        _isChangingSnapshots = true;
        try
        {
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
        ServerNameError.Visibility = Visibility.Collapsed;

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
        try
        {
            if (_isChangingModLoader)
            {
                Logger.Info($"ModLoaderBox_SelectionChanged skipped: _isChangingModLoader=True", "CreateServerPage");
                return;
            }

            if (_isUpdating)
            {
                Logger.Info("ModLoaderBox_SelectionChanged skipped: _isUpdating=True", "CreateServerPage");
                return;
            }

            if (_isInitializing || LoaderVersionBox == null || McVersionBox == null)
            {
                Logger.Info($"ModLoaderBox_SelectionChanged skipped", "CreateServerPage");
                return;
            }

            if (ModLoaderBox.SelectedItem is not ComboBoxItem item)
            {
                Logger.Info("ModLoaderBox_SelectionChanged: no selected item", "CreateServerPage");
                return;
            }

            var tag = item.Tag?.ToString();
            Logger.Info($"ModLoaderBox_SelectionChanged: {tag}", "CreateServerPage");

            _loaderLoadingCts?.Cancel();
            _loaderLoadingCts?.Dispose();
            _loaderLoadingCts = null;
            _isLoadingInProgress = false;
            _lastLoadedModLoader = null;
            _lastLoadedMcVersion = null;
            _isFindingCompatibleVersion = false;

            _currentModLoader = tag;

            var isEnabled = tag is "Forge" or "NeoForge" or "Fabric" or "Quilt";
            LoaderVersionBox.IsEnabled = isEnabled;
            LoaderVersionBox.Items.Clear();

            await FilterMcVersionsAsync(tag);
        }
        catch (Exception ex)
        {
            Logger.Error($"ModLoaderBox_SelectionChanged error: {ex.Message}", ex, "CreateServerPage");
        }
    }

    // ======================== Загрузка версий загрузчика ========================

    private async Task LoadLoaderVersions(string modLoaderType, string mcVersion)
    {
        Logger.Info($"LoadLoaderVersions: {modLoaderType} for MC {mcVersion}", "CreateServerPage");

        _loaderLoadingCts?.Cancel();
        _loaderLoadingCts?.Dispose();
        _loaderLoadingCts = new CancellationTokenSource();
        var cts = _loaderLoadingCts;

        if (_isLoadingInProgress &&
            (_lastLoadedModLoader == modLoaderType && _lastLoadedMcVersion == mcVersion))
        {
            Logger.Info($"LoadLoaderVersions skipped: already loaded", "CreateServerPage");
            return;
        }

        if (_isFindingCompatibleVersion)
        {
            Logger.Info($"LoadLoaderVersions skipped: _isFindingCompatibleVersion=True", "CreateServerPage");
            return;
        }

        _isLoadingInProgress = true;

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

            if (cts.IsCancellationRequested)
            {
                Logger.Info($"LoadLoaderVersions cancelled after API call: {modLoaderType}", "CreateServerPage");
                return;
            }

            Logger.Info($"Loaded {versions.Length} {modLoaderType} versions", "CreateServerPage");

            _lastLoadedModLoader = modLoaderType;
            _lastLoadedMcVersion = mcVersion;

            var showSnapshots = ShowSnapshotsBox?.IsChecked ?? false;
            if (modLoaderType == "NeoForge" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !IsNeoForgeSnapshot(v))];
                Logger.Info($"Filtered NeoForge versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerPage");
            }

            if (modLoaderType == "Quilt" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !IsQuiltSnapshot(v))];
                Logger.Info($"Filtered Quilt versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerPage");
            }

            if (versions.Length == 0)
            {
                Logger.Warning($"No {modLoaderType} versions found for MC {mcVersion}", "CreateServerPage");
                return;
            }

            var firstVersion = versions[0];
            LoaderVersionBox.Items.Add(new ComboBoxItem
            {
                Content = firstVersion,
                IsSelected = true
            });
            Logger.Info($"Selected {modLoaderType} version: {firstVersion}", "CreateServerPage");

            foreach (var version in versions.Skip(1).Take(9))
            {
                LoaderVersionBox.Items.Add(new ComboBoxItem { Content = version });
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error loading {modLoaderType} versions: {ex.Message}", "CreateServerPage");
        }
        finally
        {
            _isLoadingInProgress = false;
        }
    }

    private async void McVersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isChangingSnapshots || _isFindingCompatibleVersion || _isChangingModLoader || McVersionBox == null || ModLoaderBox == null)
        {
            Logger.Info($"McVersionBox_SelectionChanged skipped", "CreateServerPage");
            return;
        }

        if (ModLoaderBox.SelectedItem is ComboBoxItem item &&
            McVersionBox.SelectedItem is ComboBoxItem mcItem)
        {
            var tag = _currentModLoader ?? item.Tag?.ToString();
            var mcVersion = mcItem.Content?.ToString();

            if (!string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(mcVersion) &&
                tag is "Forge" or "NeoForge" or "Fabric" or "Quilt")
            {
                LoaderVersionBox.Items.Clear();
                Logger.Info($"McVersionBox_SelectionChanged: calling LoadLoaderVersions for {tag}, MC: {mcVersion}", "CreateServerPage");
                _ = LoadLoaderVersions(tag, mcVersion);
            }
        }

    }

    // ======================== Путь к серверу ========================

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
        catch
        {
            // Suppress config load errors
        }

        var exeDir = AppContext.BaseDirectory;
        var serversDir = Path.Combine(exeDir, "Servers");
        Directory.CreateDirectory(serversDir);
        return serversDir;
    }

    // ======================== Импорт сервера ========================

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

                var existingServer = App.ServerManager.GetServers()
                    .FirstOrDefault(s => s.Path.Equals(serverPath, StringComparison.OrdinalIgnoreCase));

                if (existingServer != null)
                {
                    await UiHelper.ShowWarning(string.Format(LocalizationManager.Get("CreateServer_Import_Duplicate"), existingServer.Name));
                    return;
                }

                var jarFile = Directory.GetFiles(serverPath, "*.jar").FirstOrDefault();
                if (jarFile == null)
                {
                    await UiHelper.ShowWarning(LocalizationManager.Get("CreateServer_Import_NoJar"));
                    return;
                }

                var launchType = _installer.GetServerLaunchType(serverPath);
                var modLoader = launchType switch
                {
                    ServerLaunchType.Forge => new ModLoader { Type = ModLoaderType.Forge },
                    ServerLaunchType.NeoForge => new ModLoader { Type = ModLoaderType.NeoForge },
                    ServerLaunchType.Fabric => new ModLoader { Type = ModLoaderType.Fabric },
                    ServerLaunchType.Quilt => new ModLoader { Type = ModLoaderType.Quilt },
                    ServerLaunchType.Standard => new ModLoader { Type = ModLoaderType.Vanilla },
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
                    catch
                    {
                        // Suppress version.json parse errors
                    }
                }

                var serverName = Path.GetFileName(serverPath);
                _ = App.ServerManager.CreateServer(serverName, mcVersion, modLoader, serverPath);

                App.MainWindow?.ShowSnackbar(
                    LocalizationManager.Get("CreateServer_Import_Success_Title") ?? "Импорт завершён",
                    string.Format(LocalizationManager.Get("CreateServer_Import_Success"), serverName),
                    Wpf.Ui.Controls.ControlAppearance.Success);

                NavigateBackToServers();
            }
        }
        catch (Exception ex)
        {
            await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Import_Error")}: {ex.Message}");
        }
    }

    // ======================== Состояние установки ========================

    private void SetInstallingState(bool isInstalling)
    {
        _isInstalling = isInstalling;

        ServerNameBox.IsEnabled = !isInstalling;
        McVersionBox.IsEnabled = !isInstalling;
        ShowSnapshotsBox.IsEnabled = !isInstalling;
        ModLoaderBox.IsEnabled = !isInstalling;
        LoaderVersionBox.IsEnabled = !isInstalling;
        ServerPathBox.IsEnabled = !isInstalling;

        Dispatcher.Invoke(() =>
        {
            if (isInstalling)
            {
                ProgressText.Visibility = Visibility.Visible;
                ProgressText.Text = "Подготовка...";

                ActionOrCancelButton.Content = "Отмена";
                ActionOrCancelButton.Appearance = ControlAppearance.Danger;
                ActionOrCancelButton.IsEnabled = true;

                ImportButton.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = false;
            }
            else
            {
                ProgressText.Visibility = Visibility.Collapsed;

                ActionOrCancelButton.Content = LocalizationManager.Get("CreateServer_Create") ?? "Создать";
                ActionOrCancelButton.Appearance = ControlAppearance.Success;
                ActionOrCancelButton.IsEnabled = true;

                ImportButton.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = true;
            }
        });
    }

    private async void ActionOrCancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInstalling)
            {
                _wasCancelled = true;
                _installCts?.Cancel();
            }
            else
            {
                await Create_Click_Handler();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ActionOrCancel_Click error: {ex.Message}", ex, "CreateServerPage");
        }
    }

    // ======================== Создание сервера ========================

    private async Task Create_Click_Handler()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ServerNameBox.Text))
            {
                ServerNameError.Visibility = Visibility.Visible;
                ServerNameBox.Focus();
                return;
            }
            else
            {
                ServerNameError.Visibility = Visibility.Collapsed;
            }

            if (string.IsNullOrWhiteSpace(ServerPathBox.Text))
            {
                await UiHelper.ShowWarning(LocalizationManager.Get("CreateServer_Error_NoFolder"));
                return;
            }

            if (App.ServerManager == null)
            {
                await UiHelper.ShowError(LocalizationManager.Get("CreateServer_Error_NoServerManager"));
                return;
            }

            var serverName = ServerNameBox.Text.Trim();

            var existingServers = App.ServerManager.GetServers();
            if (existingServers.Any(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase)))
            {
                await UiHelper.ShowWarning(string.Format(LocalizationManager.Get("CreateServer_Error_DuplicateName"), serverName));
                ServerNameBox.Focus();
                return;
            }

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

            var loaderVersion = string.Empty;
            if (LoaderVersionBox.IsEnabled &&
                LoaderVersionBox.SelectedItem is ComboBoxItem loaderItem)
            {
                loaderVersion = loaderItem.Content?.ToString() ?? string.Empty;
            }

            var modLoader = new ModLoader
            {
                Type = modLoaderType,
                Version = mcVersion,
                LoaderVersion = loaderVersion
            };

            var server = App.ServerManager.CreateServer(serverName, mcVersion, modLoader, serverPath);

            SetInstallingState(true);

            _ = InstallServerInBackground(server, modLoaderType, mcVersion, loaderVersion, serverPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Create server failed: {ex.Message}", ex, "CreateServerPage");
            SetInstallingState(false);
            await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Error_CreateFailed")}: {ex.Message}");
        }
    }

    private void UpdateStatus(string statusText)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressText.Text = statusText;
        });
    }

    private async Task InstallServerInBackground(Server server, ModLoaderType modLoaderType,
        string mcVersion, string loaderVersion, string serverPath)
    {
        _wasCancelled = false;
        _installCts = new CancellationTokenSource();

        try
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Visibility = Visibility.Visible;
                ProgressText.Text = "Подготовка...";
            });

            server.InstallStatus = $"Установка {server.Name}...";
            App.ServerManager.UpdateServer(server);

            var progress = new Progress<string>(statusText =>
            {
                UpdateStatus(statusText);
                server.InstallStatus = statusText;
                App.ServerManager.UpdateServer(server);
            });

            var installResult = await _installer.InstallServer(
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
                if (!_wasCancelled)
                {
                    server.InstallStatus = $"Ошибка: {installResult.Error}";
                    server.Status = ServerStatus.Error;
                    Logger.Error($"Server install failed: {installResult.Error}");

                    await Dispatcher.InvokeAsync(async () =>
                    {
                        SetInstallingState(false);
                        await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Error_InstallFailed")}\n{installResult.Error}");
                    });
                }
                else
                {
                    server.InstallStatus = "Отменено";
                    server.Status = ServerStatus.Stopped;
                    Logger.Info("Server install cancelled by user");

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

                    await Dispatcher.InvokeAsync(async () =>
                    {
                        SetInstallingState(false);
                        await App.ServerManager.DeleteServerAsync(server.Id);
                    });
                }
            }
            else
            {
                server.InstallStatus = "Готов";
                server.Status = ServerStatus.Stopped;
                Logger.Info($"Server install completed: {server.Name}");

                if (installResult.BuildNumber.HasValue)
                {
                    server.ServerBuild = installResult.BuildNumber.Value;
                    Logger.Info($"Server build number saved: {server.ServerBuild}", "CreateServerPage");
                }

                Dispatcher.Invoke(() =>
                {
                    NavigateBackToServers();
                });
            }
        }
        catch (OperationCanceledException)
        {
            server.InstallStatus = "Отменено";
            server.Status = ServerStatus.Error;
            Logger.Info("Server install cancelled");

            await Dispatcher.InvokeAsync(() =>
            {
                SetInstallingState(false);
            });
        }
        catch (Exception ex)
        {
            server.InstallStatus = $"Ошибка: {ex.Message}";
            server.Status = ServerStatus.Error;
            Logger.Error($"Server install exception: {ex}");

            await Dispatcher.InvokeAsync(async () =>
            {
                SetInstallingState(false);
                await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Error_InstallFailed_Exception")}\n{ex.Message}");
            });
        }
        finally
        {
            _installCts?.Dispose();
            _installCts = null;
        }

        App.ServerManager.UpdateServer(server);
    }

    // ======================== Валидация пути ========================

    private sealed class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    private static PathValidationResult ValidateServerPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь к серверу не может быть пустым"
                };
            }

            var invalidPathChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidPathChars) >= 0)
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь содержит недопустимые символы"
                };
            }

            if (path.Length > 260)
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Путь слишком длинный (максимум 260 символов)"
                };
            }

            if (File.Exists(path))
            {
                return new PathValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Указанный путь является файлом, а не папкой"
                };
            }

            if (Directory.Exists(path))
            {
                var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();

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

                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    return new PathValidationResult
                    {
                        IsValid = true,
                        ErrorMessage = $"Папка не пуста ({files.Length} файлов). Убедитесь, что это правильное место для сервера."
                    };
                }
            }
            else
            {
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    return new PathValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Родительская папка не существует"
                    };
                }

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
            Logger.Error($"Path validation error: {ex.Message}", ex, "CreateServerPage");
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
