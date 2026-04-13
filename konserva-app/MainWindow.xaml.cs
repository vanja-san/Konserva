using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Konserva;

/// <summary>
/// Главное окно приложения
/// </summary>
public partial class MainWindow : FluentWindow, IDisposable
{
    private static MainWindow? _instance;
    private readonly IConfigService _config;
    private readonly IServerManager _serverManager;
    private IContentDialogService? _contentDialogService;
    private bool _disposed;
    private bool _isUpdatingStatusBar;
    private CancellationTokenSource? _statusBarCts;

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();

        _config = App.ConfigService;
        _serverManager = App.ServerManager;

        _serverManager.OnServersChanged += UpdateStatusBar;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    /// <summary>
    /// Сервис для отображения ContentDialog
    /// </summary>
    public IContentDialogService ContentDialogService
    {
        get
        {
            if (_contentDialogService == null)
            {
                _contentDialogService = new ContentDialogService();
                _contentDialogService.SetDialogHost(ContentDialogPresenter);
            }
            return _contentDialogService;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) => InitializeMainWindow();
    private void MainWindow_Closed(object? sender, EventArgs e) => Dispose();

    private void InitializeMainWindow()
    {
        // Применяем тему из конфига
        var config = _config.GetConfig();
        ApplyTheme(config.Theme ?? "System");

        AutoDetectJava();
        StartStatusBarTimer();

        // Подписываемся на событие навигации для обновления кнопки "Назад"
        ContentFrame.Navigated += ContentFrame_Navigated;

        // Navigate to Servers page by default
        ContentFrame.Navigate(new Pages.ServersPage());

        // Инициализируем SnackbarService (визуальное дерево уже загружено)
        _ = SnackbarService;

        // Проверяем обновления
        CheckForUpdatesAsync().FireAndForget();
    }

    /// <summary>
    /// Обработчик события навигации - обновляет видимость кнопки "Назад"
    /// </summary>
    private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (BackButton != null)
        {
            BackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Текущий экземпляр окна
    /// </summary>
    public static MainWindow? Instance => _instance;

    /// <summary>
    /// Сервис ContentDialog для использования из UiHelper
    /// </summary>
    public static IContentDialogService? GetContentDialogService() => _instance?.ContentDialogService;

    /// <summary>
    /// Сервис конфигурации
    /// </summary>
    public static IConfigService Config => App.ConfigService;

    /// <summary>
    /// Менеджер серверов
    /// </summary>
    public static IServerManager ServerManager => App.ServerManager;

    /// <summary>
    /// Запуск обновления статусбара
    /// </summary>
    private void StartStatusBarTimer()
    {
        _statusBarCts?.Cancel();
        _statusBarCts = new CancellationTokenSource();
        _ = StatusBarLoopAsync(_statusBarCts.Token);
    }

    /// <summary>
    /// Остановка обновления статусбара
    /// </summary>
    private void StopStatusBarTimer()
    {
        _statusBarCts?.Cancel();
        _statusBarCts?.Dispose();
        _statusBarCts = null;
    }

    /// <summary>
    /// Цикл обновления статусбара каждые 5 секунд
    /// </summary>
    private async Task StatusBarLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                UpdateStatusBar();
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемая отмена
        }
    }

    /// <summary>
    /// Авто-поиск установленных Java
    /// </summary>
    private void AutoDetectJava()
    {
        var config = _config.GetConfig();

        if (config.JavaInstallations.Count > 0)
            return;

        var javaService = new JavaManagementService(_config);
        var foundJava = javaService.FindInstalledJava();
        foreach (var java in foundJava)
        {
            config.JavaInstallations.Add(java);
        }

        if (config.JavaInstallations.Count > 0 && string.IsNullOrEmpty(config.DefaultJavaId))
        {
            var firstJava = config.JavaInstallations.First();
            firstJava.IsDefault = true;
            config.DefaultJavaId = firstJava.Id;
        }

        _config.SaveConfig(config);
        UpdateStatusBar();
    }

    /// <summary>
    /// Навигация к странице настроек
    /// </summary>
    public void NavigateToSettings()
    {
        // Проверяем, не открыта ли уже страница настроек
        if (ContentFrame.Content is Pages.SettingsPage)
        {
            return;
        }

        ContentFrame.Navigate(new Pages.SettingsPage());
    }

    /// <summary>
    /// Обновление статусбара
    /// </summary>
    public void UpdateStatusBar()
    {
        if (_isUpdatingStatusBar || StatusTotalServers == null)
            return;

        _isUpdatingStatusBar = true;
        try
        {
            var (total, running, _) = _serverManager.GetStats();

            StatusTotalServers.Text = $"{total}";
            StatusRunningServers.Text = $"{running}";

            var totalRamBytes = _serverManager.GetTotalMemoryUsage();
            var totalRamMB = totalRamBytes / (1024 * 1024);
            StatusMemoryUsage.Text = totalRamMB >= 1024
                ? $"{totalRamMB / 1024.0:0.#} GB"
                : $"{totalRamMB} MB";

            var config = _config.GetConfig();
            StatusJava.Text = !string.IsNullOrEmpty(config.DefaultJavaId)
                ? config.JavaInstallations.FirstOrDefault(j => j.Id == config.DefaultJavaId) switch
                {
                    null => $"{config.JavaInstallations.Count} {LocalizationManager.Get("MainWindow_JavaVersions")}",
                    var java => java.DisplayName
                }
                : config.JavaInstallations.Count > 0
                    ? $"{config.JavaInstallations.Count} {LocalizationManager.Get("MainWindow_JavaVersions")}"
                    : LocalizationManager.Get("StatusBar_Java_NotConfigured");
        }
        finally
        {
            _isUpdatingStatusBar = false;
        }
    }

    /// <summary>
    /// Навигация к деталям сервера (добавляет в историю навигации)
    /// </summary>
    public void NavigateToServer(string serverId)
    {
        ContentFrame.Navigate(new Pages.ServerDetailPage(serverId));
    }

    /// <summary>
    /// Команда для навигации из XAML
    /// </summary>
    public static void NavigateToServerCommand(object parameter)
    {
        if (parameter is string serverId && _instance != null)
        {
            _instance.NavigateToServer(serverId);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        // Проверяем, не открыта ли уже страница настроек
        if (ContentFrame.Content is Pages.SettingsPage)
        {
            return;
        }

        ContentFrame.Navigate(new Pages.SettingsPage());
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    /// <summary>
    /// Применение темы приложения
    /// </summary>
    public void ApplyTheme(string theme)
    {

        // Сначала применяем тему через ApplicationThemeManager
        var wpfTheme = theme switch
        {
            "Dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
            "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Unknown // System
        };

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfTheme);

        // Для системной темы нужно явно обновить ресурсы
        if (theme == "System")
        {
            // Проверяем текущую системную тему и применяем соответствующую
            var isSystemDark = Microsoft.Win32.Registry.GetValue(
                "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                "AppsUseLightTheme",
                1
            ) is int value && value == 0;

            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(isSystemDark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        }
    }

    private async void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Dialogs.CreateServerDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                UpdateStatusBar();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Create server dialog error: {ex.Message}", ex, "MainWindow");
            await UiHelper.ShowError($"{LocalizationManager.Get("MainWindow_Error")}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopStatusBarTimer();
        _serverManager.OnServersChanged -= UpdateStatusBar;

        // Dispose CTS
        _statusBarCts?.Cancel();
        _statusBarCts?.Dispose();
        _statusBarCts = null;

        _disposed = true;
    }

    // ===== Update checking =====

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Проверяет наличие обновлений (с учётом интервала 24ч).
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var config = _config.GetConfig();
            if (!config.CheckUpdates)
                return;

            // Проверяем интервал
            if (config.LastUpdateCheck.HasValue)
            {
                var elapsed = DateTime.UtcNow - config.LastUpdateCheck.Value;
                if (elapsed < UpdateCheckInterval)
                    return;
            }

            var updateInfo = await UpdateChecker.CheckAsync();

            // Обновляем время проверки
            _config.UpdateConfig(c => c.LastUpdateCheck = DateTime.UtcNow);

            if (updateInfo.IsAvailable)
            {
                Dispatcher.Invoke(() => UpdateNotificationControl.Show(updateInfo));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Update check failed: {ex.Message}", ex, "MainWindow");
        }
    }

    /// <summary>
    /// Принудительная проверка обновлений (вызывается из SettingsPage).
    /// </summary>
    public async Task<UpdateInfo> ForceCheckForUpdatesAsync()
    {
        var updateInfo = await UpdateChecker.CheckAsync();

        if (updateInfo.IsAvailable)
        {
            Dispatcher.Invoke(() => UpdateNotificationControl.Show(updateInfo));
        }

        return updateInfo;
    }

    // ===== Java Error Snackbar =====

    private SnackbarService? _snackbarService;

    private SnackbarService SnackbarService
    {
        get
        {
            if (_snackbarService == null)
            {
                _snackbarService = new SnackbarService();
                _snackbarService.SetSnackbarPresenter(SnackbarPresenter);
            }
            return _snackbarService;
        }
    }

    /// <summary>
    /// Показывает Snackbar с ошибкой несовместимости Java.
    /// </summary>
    public void ShowJavaErrorSnackbar(Server server, string errorMessage, int requiredVersion, int foundVersion, List<JavaInstallation>? allJava = null)
    {
        var isServersPage = ContentFrame.Content is Pages.ServersPage;
        var isDetailPage = ContentFrame.Content is Pages.ServerDetailPage;

        Logger.Info($"[ShowJavaErrorSnackbar] server={server.Name}, required={requiredVersion}, found={foundVersion}, isServersPage={isServersPage}, isDetailPage={isDetailPage}", "MainWindow");

        string title, message;

        // Формируем строку с найденными Java версиями
        string javaVersionsText;
        if (allJava != null && allJava.Count > 0)
        {
            // Собираем уникальные версии Java через запятую
            var versions = allJava
                .Where(j => j.Exists)
                .Select(j => j.MajorVersion > 0 ? j.MajorVersion.ToString() : j.Version)
                .Distinct()
                .OrderBy(v => int.TryParse(v, out var n) ? n : 999);
            javaVersionsText = string.Join(", ", versions);
        }
        else
        {
            javaVersionsText = foundVersion > 0 ? foundVersion.ToString() : "—";
        }

        if (isServersPage)
        {
            title = server.Name;
            message = LocalizationManager.Get("Snackbar_JavaIncompatible_Message_Plural", server.McVersion, requiredVersion, javaVersionsText);
        }
        else if (isDetailPage)
        {
            title = LocalizationManager.Get("Snackbar_JavaIncompatible_Title");
            message = LocalizationManager.Get("Snackbar_JavaIncompatible_Message_Plural", server.McVersion, requiredVersion, javaVersionsText);
        }
        else
        {
            title = LocalizationManager.Get("Snackbar_JavaIncompatible_Title");
            message = errorMessage;
        }

        Dispatcher.Invoke(() =>
        {
            if (SnackbarPresenter == null)
            {
                Logger.Error("[ShowJavaErrorSnackbar] SnackbarPresenter is null!", null, "MainWindow");
                return;
            }

            Logger.Info($"[ShowJavaErrorSnackbar] Showing snackbar: title='{title}'", "MainWindow");

            var snackbar = new Snackbar(SnackbarPresenter)
            {
                Title = title,
                Content = message,
                Icon = new SymbolIcon(SymbolRegular.ErrorCircle24) { FontSize = 28 },
                Timeout = TimeSpan.FromSeconds(10),
                Appearance = ControlAppearance.Danger,
                Padding = new Thickness(12, 8, 12, 8),
                Height = 32
            };

            SnackbarPresenter.AddToQue(snackbar);
        });
    }

    /// <summary>
    /// Скрывает Snackbar с ошибкой Java.
    /// </summary>
    public void HideJavaErrorSnackbar()
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (SnackbarPresenter != null)
            {
                await SnackbarPresenter.HideCurrent();
            }
        });
    }

    /// <summary>
    /// Универсальный метод показа Snackbar (Success, Info, Warning, Danger)
    /// </summary>
    public void ShowSnackbar(string title, string message, ControlAppearance appearance = ControlAppearance.Info, int timeoutSeconds = 3)
    {
        Dispatcher.Invoke(() =>
        {
            if (SnackbarPresenter == null)
            {
                Logger.Warning("[ShowSnackbar] SnackbarPresenter is null!", "MainWindow");
                return;
            }

            var symbol = appearance switch
            {
                ControlAppearance.Success => SymbolRegular.CheckmarkCircle20,
                ControlAppearance.Caution => SymbolRegular.Warning20,
                ControlAppearance.Danger => SymbolRegular.ErrorCircle20,
                _ => SymbolRegular.Info20
            };

            var snackbar = new Snackbar(SnackbarPresenter)
            {
                Title = title,
                Content = message,
                Icon = new SymbolIcon(symbol) { FontSize = 20 },
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Appearance = appearance,
                Padding = new Thickness(12, 8, 12, 8),
                Height = 32
            };

            SnackbarPresenter.AddToQue(snackbar);
        });
    }
}

/// <summary>
/// Extension для fire-and-forget задач.
/// </summary>
internal static class TaskExtensions
{
    public static void FireAndForget(this Task task)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                Logger.Error($"FireAndForget error: {t.Exception.InnerException?.Message}", t.Exception.InnerException, "TaskExtensions");
            }
        }, TaskScheduler.Default);
    }
}
