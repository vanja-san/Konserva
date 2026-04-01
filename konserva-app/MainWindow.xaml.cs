using Konserva.Services;
using Konserva.Utilities;
using System.Windows;
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
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
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
            var (total, running, stopped) = _serverManager.GetStats();

            StatusTotalServers.Text = $"{total}";
            StatusRunningServers.Text = $"{running}";

            var servers = _serverManager.GetServers();
            var totalRam = servers.Where(s => s.IsRunning).Sum(s => s.Settings.RamMax);
            StatusMemoryUsage.Text = totalRam >= 1024
                ? $"{totalRam / 1024.0:0.#} GB"
                : $"{totalRam} MB";

            var config = _config.GetConfig();
            StatusJava.Text = !string.IsNullOrEmpty(config.DefaultJavaId)
                ? config.JavaInstallations.FirstOrDefault(j => j.Id == config.DefaultJavaId) switch
                {
                    null => $"{config.JavaInstallations.Count} версий Java",
                    var java => java.DisplayName
                }
                : config.JavaInstallations.Count > 0
                    ? $"{config.JavaInstallations.Count} версий Java"
                    : "Java не настроена";
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
            await UiHelper.ShowError($"Ошибка: {ex.Message}");
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
        GC.SuppressFinalize(this);
    }
}
