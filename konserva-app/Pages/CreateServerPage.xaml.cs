using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Controls;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Konserva.Pages;

/// <summary>
/// Страница создания нового сервера
/// </summary>
public partial class CreateServerPage : Page
{
    private readonly CreateServerViewModel _viewModel;
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
    private string? _lastLoadedModLoader;
    private string? _lastLoadedMcVersion;
    private bool _isLoadingInProgress;
    private CancellationTokenSource? _loaderLoadingCts;
    private readonly SelectionChangedEventHandler _loaderVersionChangedHandler; // for clean unsubscribe
    private readonly List<string> _installLog = [];
    private InstallLogWindow? _installLogWindow;

    public CreateServerPage()
    {
        _viewModel = Ioc.Default.GetService<CreateServerViewModel>()
            ?? new CreateServerViewModel(
                Ioc.Default.GetService<IConfigService>()!,
                Ioc.Default.GetService<IMcVersionsApi>()!,
                Ioc.Default.GetService<IServerInstaller>()!,
                Ioc.Default.GetService<IServerManager>()!);
        _configService = Ioc.Default.GetService<IConfigService>();
        _versionsApi = Ioc.Default.GetService<IMcVersionsApi>()!;
        _installer = Ioc.Default.GetService<IServerInstaller>()!;

        InitializeComponent();

        _loaderVersionChangedHandler = (_, _) => UpdateCreateButtonState();

        Title = LocalizationManager.Get("CreateServer_Title");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(string culture)
    {
        Title = LocalizationManager.Get("CreateServer_Title");
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
            LoaderVersionBox.SelectionChanged += _loaderVersionChangedHandler;

            // Инициализируем ViewModel (загружает и фильтрует версии)
            await _viewModel.InitializeAsync();

            Logger.Info($"Loaded {_viewModel.McVersionList.Length} Minecraft versions", "CreateServerPage");

            ServerPathBox.Text = _viewModel.GetDefaultServerPath();
            Logger.Info($"Server path: {ServerPathBox.Text}", "CreateServerPage");

            await FilterMcVersionsAsync();
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerPage");

            // ProgressRing запускается автоматически при отображении панели
        }
        catch (Exception ex)
        {
            Logger.Error($"Page load error: {ex}", ex, "CreateServerPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("CreateServer_Error_DialogLoad")}: {ex.Message}");
        }

        _isInitializing = false;
        UpdateCreateButtonState();
        Logger.Info("CreateServerPage loaded successfully", "CreateServerPage");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от событий UI
        ShowSnapshotsBox.Checked -= ShowSnapshotsBox_Changed;
        ShowSnapshotsBox.Unchecked -= ShowSnapshotsBox_Changed;
        ModLoaderBox.SelectionChanged -= ModLoaderBox_SelectionChanged;
        McVersionBox.SelectionChanged -= McVersionBox_SelectionChanged;
        LoaderVersionBox.SelectionChanged -= _loaderVersionChangedHandler;

        // ProgressRing останавливается автоматически при скрытии панели

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
        var mainWindow = Ioc.Default.GetService<MainWindow>();
        if (mainWindow?.ContentFrame.CanGoBack == true)
        {
            mainWindow.ContentFrame.GoBack();
        }
        else
        {
            mainWindow?.ContentFrame.Navigate(new ServersPage());
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
        LoaderProgressRing.Visibility = Visibility.Visible;
        try
        {
            var showSnapshots = ShowSnapshotsBox.IsChecked ?? false;
            var selectedModLoader = modLoaderOverride ?? _viewModel.SelectedModLoader;

            Logger.Info($"FilterMcVersionsAsync: modLoader={selectedModLoader}, showSnapshots={showSnapshots}", "CreateServerPage");

            if (selectedModLoader == "Paper" && _viewModel.GetPaperVersionsCount() == 0)
                await _viewModel.LoadPaperVersionsAsync();

            HashSet<string> supportedVersions;

            if (selectedModLoader == "NeoForge")
            {
                // NeoForge — MC 1.16+ (не поддерживает более старые версии)
                supportedVersions = [.. _viewModel.McVersionList.Where(v =>
                    McVersionHelper.TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
            }
            else if (selectedModLoader == "Quilt")
            {
                if (_viewModel.QuiltSupportedVersions == null)
                {
                    Logger.Info("Loading Quilt supported versions...", "CreateServerPage");
                    await _viewModel.LoadQuiltSupportedVersionsAsync();
                    Logger.Info($"Loaded {_viewModel.QuiltSupportedVersions?.Count ?? 0} Quilt supported versions", "CreateServerPage");
                }
                supportedVersions = _viewModel.QuiltSupportedVersions ?? [.. _viewModel.McVersionList];
            }
            else if (selectedModLoader == "Paper")
            {
                if (_viewModel.PaperVersions.Count == 0)
                    await _viewModel.LoadPaperVersionsAsync();
                supportedVersions = _viewModel.PaperVersions.Count > 0 ? _viewModel.PaperVersions : [.. _viewModel.McVersionList];
            }
            else
            {
                supportedVersions = [.. _viewModel.McVersionList];
            }

            var versions = _viewModel.McVersionList
                .Where(v => supportedVersions.Contains(v))
                .Where(v => showSnapshots || !McVersionHelper.IsSnapshot(v))
                .ToArray();

            Logger.Info($"Filtered MC versions for {selectedModLoader}: {versions.Length} (from {_viewModel.McVersionList.Length})", "CreateServerPage");

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

            // Если версии загрузчика не загружены или нужна проверка стабильности
            var modLoader = _viewModel.SelectedModLoader;
            if (!string.IsNullOrEmpty(modLoader) && modLoader is "Forge" or "NeoForge" or "Fabric" or "Quilt" or "Paper")
            {
                var selectedMcVersion = (McVersionBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                var showSnapshotsLocal = ShowSnapshotsBox?.IsChecked ?? false;

                // Для Quilt без снапшотов — проверяем, есть ли стабильные версии
                if (!string.IsNullOrEmpty(selectedMcVersion) &&
                    !showSnapshotsLocal && modLoader == "Quilt")
                {
                    var compatibleVersion = await FindLastCompatibleMcVersionAsync(modLoader, selectedMcVersion, showSnapshotsLocal);
                    if (compatibleVersion != null && compatibleVersion != selectedMcVersion)
                    {
                        Logger.Info($"Switching to compatible MC version for {modLoader}: {compatibleVersion}", "CreateServerPage");
                        var matchingItem = McVersionBox.Items.Cast<ComboBoxItem>()
                            .FirstOrDefault(item => item.Content?.ToString() == compatibleVersion);
                        if (matchingItem != null)
                        {
                            McVersionBox.SelectedItem = matchingItem;
                            selectedMcVersion = compatibleVersion;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedMcVersion))
                {
                    LoaderVersionBox.Items.Clear();
                    Logger.Info($"Calling LoadLoaderVersions manually: {modLoader} for MC {selectedMcVersion}", "CreateServerPage");
                    await LoadLoaderVersions(modLoader, selectedMcVersion);
                }
            }
        }
        finally
        {
            _isUpdating = false;
            _isChangingModLoader = false;
            LoaderProgressRing.Visibility = Visibility.Collapsed;
            Logger.Info("FilterMcVersionsAsync completed", "CreateServerPage");
        }
    }

    /// <summary>
    /// Ищет последнюю совместимую версию Minecraft для загрузчика (строгая фильтрация снапшотов)
    /// </summary>
    private async Task<string?> FindLastCompatibleMcVersionAsync(string modLoaderType, string currentMcVersion, bool showSnapshots)
    {
        // Проверяем сначала текущую версию
        string[] currentVersions = modLoaderType switch
        {
            "Forge" => await _versionsApi.GetForgeVersions(currentMcVersion),
            "NeoForge" => await _versionsApi.GetNeoForgeVersions(currentMcVersion),
            "Fabric" => await _versionsApi.GetFabricVersions(currentMcVersion),
            "Quilt" => await _versionsApi.GetQuiltVersions(currentMcVersion),
            "Paper" => await _versionsApi.GetPaperVersions(currentMcVersion),
            _ => []
        };

        // Строгая фильтрация: только стабильные версии считаются совместимыми
        if (modLoaderType == "NeoForge" && !showSnapshots)
            currentVersions = [.. currentVersions.Where(v => !McVersionHelper.IsNeoForgeSnapshot(v))];
        else if (modLoaderType == "Quilt" && !showSnapshots)
            currentVersions = [.. currentVersions.Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase))];
        else if (modLoaderType == "Paper" && !showSnapshots)
            currentVersions = [.. currentVersions.Where(v => !v.Contains("(ALPHA)", StringComparison.OrdinalIgnoreCase))];

        if (currentVersions.Length > 0 && currentVersions[0] != "latest")
            return currentMcVersion;

        // Текущая не подходит — ищем среди последних 10
        var mcVersions = _viewModel.McVersionList
            .Where(v => showSnapshots || !McVersionHelper.IsSnapshot(v))
            .Take(10)
            .ToList();

        foreach (var version in mcVersions)
        {
            if (version == currentMcVersion) continue;

            try
            {
                string[] versions = modLoaderType switch
                {
                    "Forge" => await _versionsApi.GetForgeVersions(version),
                    "NeoForge" => await _versionsApi.GetNeoForgeVersions(version),
                    "Fabric" => await _versionsApi.GetFabricVersions(version),
                    "Quilt" => await _versionsApi.GetQuiltVersions(version),
                    "Paper" => await _versionsApi.GetPaperVersions(version),
                    _ => []
                };

                if (modLoaderType == "NeoForge" && !showSnapshots)
                    versions = [.. versions.Where(v => !McVersionHelper.IsNeoForgeSnapshot(v))];
                else if (modLoaderType == "Quilt" && !showSnapshots)
                    versions = [.. versions.Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase))];
                else if (modLoaderType == "Paper" && !showSnapshots)
                    versions = [.. versions.Where(v => !v.Contains("(ALPHA)", StringComparison.OrdinalIgnoreCase))];

                if (versions.Length > 0 && versions[0] != "latest")
                    return version;
            }
            catch { /* skip on error */ }
        }

        return null;
    }

    /// <summary>
    /// Проверяет, все ли обязательные поля заполнены и возвращает список ошибок
    /// </summary>
    private List<string> GetValidationErrors()
    {
        // Синхронизируем данные с ViewModel
        _viewModel.ServerName = ServerNameBox.Text;
        _viewModel.ServerPath = ServerPathBox.Text;

        // Синхронизируем выбранную версию загрузчика из ComboBox в ViewModel
        if (LoaderVersionBox.SelectedItem is ComboBoxItem { Content: string loaderVersion })
            _viewModel.SelectedLoaderVersion = loaderVersion;

        return _viewModel.GetValidationErrors();
    }

    /// <summary>
    /// Обновляет состояние кнопки Создать и иконку предупреждения
    /// </summary>
    private void UpdateCreateButtonState()
    {
        if (_isInstalling)
            return;

        var errors = GetValidationErrors();
        var hasErrors = errors.Count > 0;

        ActionOrCancelButton.IsEnabled = !hasErrors;
        ValidationIcon.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;

        if (hasErrors)
        {
            ValidationIcon.ToolTip = string.Join("\n", errors.Select(e => $"• {e}"));
        }
        else
        {
            ValidationIcon.ToolTip = null;
        }

        // ─── Per-field валидация ────────────────────────────────────
        // ServerName: предупреждение показывается только после потери фокуса (ServerNameBox_LostFocus)
        if (!string.IsNullOrWhiteSpace(ServerNameBox.Text))
            ServerNameValidation.Visibility = Visibility.Collapsed;

        // ServerPath: read-only, предупреждение показываем если путь пуст
        if (string.IsNullOrWhiteSpace(ServerPathBox.Text))
        {
            ServerPathValidation.Text = LocalizationManager.Get("CreateServer_Validation_NoFolder");
            ServerPathValidation.Visibility = Visibility.Visible;
        }
        else
        {
            ServerPathValidation.Visibility = Visibility.Collapsed;
        }
    }

    // Статические методы TryParseMcVersion, IsSnapshot, IsNeoForgeSnapshot, IsQuiltSnapshot
    // вынесены в общий класс McVersionHelper в Utilities.
    // Используйте McVersionHelper.* вместо этих методов.



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
        if (_isInitializing)
            return;

        if (!string.IsNullOrWhiteSpace(ServerNameBox.Text))
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var serverNameClean = new string([.. ServerNameBox.Text.Where(c => !invalidChars.Contains(c))]);
            var exeDir = AppContext.BaseDirectory;
            var serversDir = Path.Combine(exeDir, "Servers");
            ServerPathBox.Text = Path.Combine(serversDir, serverNameClean);
        }

        UpdateCreateButtonState();
    }

    private void ServerNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (string.IsNullOrWhiteSpace(ServerNameBox.Text))
        {
            ServerNameValidation.Text = LocalizationManager.Get("CreateServer_Validation_NoName");
            ServerNameValidation.Visibility = Visibility.Visible;
        }
        else
        {
            ServerNameValidation.Visibility = Visibility.Collapsed;
        }
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

            if (tag is not null)
                _viewModel.SelectedModLoader = tag;
            _viewModel.IsFindingCompatibleVersion = false;

            var isEnabled = tag is "Forge" or "NeoForge" or "Fabric" or "Quilt" or "Paper";
            LoaderVersionBox.IsEnabled = isEnabled;
            LoaderVersionBox.Items.Clear();
            UpdateCreateButtonState();

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

        if (_viewModel.IsFindingCompatibleVersion)
        {
            Logger.Info($"LoadLoaderVersions skipped: IsFindingCompatibleVersion=True", "CreateServerPage");
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
                "Paper" => await _versionsApi.GetPaperVersions(mcVersion),
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
                versions = [.. versions.Where(v => !McVersionHelper.IsNeoForgeSnapshot(v))];
                Logger.Info($"Filtered NeoForge versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerPage");
            }

            if (modLoaderType == "Quilt" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !McVersionHelper.IsQuiltSnapshot(v))];
                Logger.Info($"Filtered Quilt versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerPage");
            }

            // Фильтруем ALPHA-сборки для Paper
            if (modLoaderType == "Paper" && !showSnapshots)
            {
                versions = [.. versions.Where(v => !v.Contains("(ALPHA)", StringComparison.OrdinalIgnoreCase))];
                Logger.Info($"Filtered Paper versions: {versions.Length} (showSnapshots={showSnapshots})", "CreateServerPage");
            }

            LoaderVersionBox.Items.Clear();

            if (versions.Length == 0)
            {
                Logger.Warning($"No {modLoaderType} versions found for MC {mcVersion}", "CreateServerPage");
                LoaderVersionBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationManager.Get("CreateServer_Not_Found"),
                    IsEnabled = false,
                    IsSelected = true
                });
                LoaderVersionBox.IsEnabled = false;

                // Синхронизируем с ViewModel для валидации
                _viewModel.LoaderVersions.Clear();
                _viewModel.SelectedLoaderVersion = string.Empty;
                return;
            }

            LoaderVersionBox.IsEnabled = true;

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

            // Синхронизируем с ViewModel для валидации
            _viewModel.LoaderVersions = new System.Collections.ObjectModel.ObservableCollection<string>(versions);
            _viewModel.SelectedLoaderVersion = firstVersion;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error loading {modLoaderType} versions: {ex.Message}", "CreateServerPage");
            LoaderVersionBox.Items.Clear();
            LoaderVersionBox.Items.Add(new ComboBoxItem
            {
                Content = LocalizationManager.Get("CreateServer_Not_Found"),
                IsEnabled = false,
                IsSelected = true
            });
            LoaderVersionBox.IsEnabled = false;

            // Синхронизируем с ViewModel для валидации
            _viewModel.LoaderVersions.Clear();
            _viewModel.SelectedLoaderVersion = string.Empty;
        }
        finally
        {
            _isLoadingInProgress = false;
            this.Invoke(() => UpdateCreateButtonState());
        }
    }

    private async void McVersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isChangingSnapshots || _viewModel.IsFindingCompatibleVersion || _isChangingModLoader || McVersionBox == null || ModLoaderBox == null)
        {
            Logger.Info($"McVersionBox_SelectionChanged skipped", "CreateServerPage");
            return;
        }

        if (ModLoaderBox.SelectedItem is ComboBoxItem item &&
            McVersionBox.SelectedItem is ComboBoxItem mcItem)
        {
            var tag = _viewModel.SelectedModLoader ?? item.Tag?.ToString();
            var mcVersion = mcItem.Content?.ToString();

            if (!string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(mcVersion) &&
                tag is "Forge" or "NeoForge" or "Fabric" or "Quilt" or "Paper")
            {
                LoaderVersionBox.Items.Clear();
                Logger.Info($"McVersionBox_SelectionChanged: calling LoadLoaderVersions for {tag}, MC: {mcVersion}", "CreateServerPage");
                _ = LoadLoaderVersions(tag, mcVersion);
            }
            else
            {
                UpdateCreateButtonState();
            }
        }
        else
        {
            UpdateCreateButtonState();
        }
    }

    // ======================== Путь к серверу ========================

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.Get("CreateServer_Browse_Title"),
            InitialDirectory = GetDefaultFolderPath()
        };

        if (dialog.ShowDialog() == true)
        {
            ServerPathBox.Text = dialog.FolderName;
            SaveServersPath(dialog.FolderName);
            UpdateCreateButtonState();
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
                Title = LocalizationManager.Get("CreateServer_Import_Title"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog() == true)
            {
                var serverPath = dialog.FolderName;

                var existingServer = Ioc.Default.GetService<IServerManager>()!.GetServers()
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

                var mcVersion = LocalizationManager.Get("Common_Unknown");
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
                _ = Ioc.Default.GetService<IServerManager>()!.CreateServer(serverName, mcVersion, modLoader, serverPath);

                Ioc.Default.GetService<MainWindow>()?.ShowSnackbar(
                    LocalizationManager.Get("CreateServer_Import_Success_Title"),
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
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = LocalizationManager.Get("Installer_Preparing");
                ShowLogButton.Visibility = Visibility.Visible;

                ActionOrCancelButton.Content = LocalizationManager.Get("CreateServer_Cancel");
                ActionOrCancelButton.Appearance = ControlAppearance.Danger;
                ActionOrCancelButton.IsEnabled = true;

                ImportButton.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = false;
            }
            else
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                ShowLogButton.Visibility = Visibility.Collapsed;

                ActionOrCancelButton.Content = LocalizationManager.Get("CreateServer_Create");
                ActionOrCancelButton.Appearance = ControlAppearance.Success;
                UpdateCreateButtonState();

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
                ServerNameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ServerPathBox.Text))
            {
                await UiHelper.ShowWarning(LocalizationManager.Get("CreateServer_Error_NoFolder"));
                return;
            }

            if (Ioc.Default.GetService<IServerManager>() == null)
            {
                await UiHelper.ShowError(LocalizationManager.Get("CreateServer_Error_NoServerManager"));
                return;
            }

            var serverName = ServerNameBox.Text.Trim();

            var existingServers = Ioc.Default.GetService<IServerManager>()!.GetServers();
            if (existingServers.Any(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase)))
            {
                await UiHelper.ShowWarning(string.Format(LocalizationManager.Get("CreateServer_Error_DuplicateName"), serverName));
                ServerNameBox.Focus();
                return;
            }

            var serverPath = ServerPathBox.Text.Trim();
            var pathValidationResult = _viewModel.ValidateServerPath(serverPath);
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

            var server = Ioc.Default.GetService<IServerManager>()!.CreateServer(serverName, mcVersion, modLoader, serverPath);

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

    private void ShowLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installLogWindow != null && _installLogWindow.IsVisible)
        {
            _installLogWindow.Activate();
            return;
        }

        _installLogWindow = new InstallLogWindow(_installLog)
        {
            Owner = Window.GetWindow(this)
        };
        _installLogWindow.Closed += (_, _) => _installLogWindow = null;
        _installLogWindow.Show();
    }

    private async Task InstallServerInBackground(Server server, ModLoaderType modLoaderType,
        string mcVersion, string loaderVersion, string serverPath)
    {
        _installLog.Clear();
        _installLogWindow = null;
        _wasCancelled = false;
        _installCts = new CancellationTokenSource();

        _installLog.Add($"[{DateTime.Now:HH:mm:ss}] {string.Format(LocalizationManager.Get("CreateServer_Installing_Progress"), server.Name)}");
        _installLog.Add($"[{DateTime.Now:HH:mm:ss}] {LocalizationManager.Get("Installer_DownloadingServer")}");

        try
        {
            Dispatcher.Invoke(() =>
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = LocalizationManager.Get("Installer_Preparing");
                ShowLogButton.Visibility = Visibility.Visible;
            });

            server.LastErrorMessage = string.Format(LocalizationManager.Get("CreateServer_Installing_Progress"), server.Name);
            Ioc.Default.GetService<IServerManager>()!.UpdateServer(server);

            // Используем Dispatcher напрямую вместо Progress<T>,
            // чтобы гарантированно доставлять обновления на UI-поток
            var uiDispatcher = Dispatcher;

            var installResult = await _installer.InstallServer(
                modLoaderType,
                mcVersion,
                loaderVersion,
                serverPath,
                server.Port,
                server.Settings.RamMin,
                server.Settings.RamMax,
                new DispatcherProgress<string>(statusText =>
                {
                    var timestamped = $"[{DateTime.Now:HH:mm:ss}] {statusText}";
                    Logger.Info($"Install progress: {statusText}", "CreateServerPage");
                    _installLog.Add(timestamped);
                    ProgressText.Text = statusText;
                    _installLogWindow?.AppendLog(timestamped);
                    server.LastErrorMessage = statusText;
                    Ioc.Default.GetService<IServerManager>()!.UpdateServer(server);
                }, uiDispatcher),
                _installCts.Token);

            if (!installResult.Success)
            {
                if (!_wasCancelled)
                {
                    server.LastErrorMessage = string.Format(LocalizationManager.Get("CreateServer_Install_Error"), installResult.Error);
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
                    server.LastErrorMessage = LocalizationManager.Get("CreateServer_Install_Cancelled");
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
                        await Ioc.Default.GetService<IServerManager>()!.DeleteServerAsync(server.Id);
                    });
                }
            }
            else
            {
                server.LastErrorMessage = LocalizationManager.Get("CreateServer_Install_Ready");
                server.Status = ServerStatus.Stopped;
                Logger.Info($"Server install completed: {server.Name}");

                if (installResult.BuildNumber.HasValue)
                {
                    server.ServerBuild = installResult.BuildNumber.Value;
                    Logger.Info($"Server build number saved: {server.ServerBuild}", "CreateServerPage");
                }

                // Задержка, чтобы пользователь увидел зелёное сообщение "Сервер успешно установлен!"
                await Dispatcher.InvokeAsync(async () =>
                {
                    // Красим текст прогресса в зелёный (Installer_Success уже показан)
                    if (ProgressText.TryFindResource("SystemFillColorSuccessBrush") is Brush successBrush)
                        ProgressText.Foreground = successBrush;

                    await Task.Delay(2000);

                    NavigateBackToServers();
                });
            }
        }
        catch (OperationCanceledException)
        {
            server.LastErrorMessage = LocalizationManager.Get("CreateServer_Install_Cancelled");
            server.Status = ServerStatus.Error;
            Logger.Info("Server install cancelled");

            await Dispatcher.InvokeAsync(() =>
            {
                SetInstallingState(false);
            });
        }
        catch (Exception ex)
        {
            server.LastErrorMessage = string.Format(LocalizationManager.Get("CreateServer_Install_Error"), ex.Message);
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

        Ioc.Default.GetService<IServerManager>()!.UpdateServer(server);
    }

}
