using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Konserva;

/// <summary>
/// Главное окно приложения
/// </summary>
public partial class MainWindow : FluentWindow, IDisposable
{
    private readonly IConfigService _config;
    private readonly IServerManager _serverManager;
    private readonly IJavaManagementService _javaService;
    private readonly IUpdateService _updateService;
    private IContentDialogService? _contentDialogService;
    private bool _disposed;
    private bool _isUpdatingStatusBar;
    private CancellationTokenSource? _statusBarCts;

    // Tray
    private NotifyIcon? _trayIcon;
    private MenuItem? _trayStatusMenuItem;
    private bool _isExiting;

    public MainWindow(IConfigService configService, IServerManager serverManager, IJavaManagementService javaService, IUpdateService updateService)
    {
        InitializeComponent();

        _config = configService;
        _serverManager = serverManager;
        _javaService = javaService;
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

        _updateService.UpdateAvailable += OnUpdateAvailable;
        _updateService.CheckStarted += OnUpdateCheckStarted;
        _updateService.CheckCompleted += OnUpdateCheckCompleted;

        _serverManager.OnServersChanged += UpdateStatusBar;
        _serverManager.OnServersChanged += UpdateTrayStatus;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        IsVisibleChanged += (_, _) => UpdateTrayIconVisibility();

        // Инициализация трея (после InitializeComponent)
        InitTray();
    }

    /// <summary>
    /// Инициализация событий трея.
    /// </summary>
    private void InitTray()
    {
        try
        {
            var showItem = new MenuItem { Header = "Открыть Konserva" };
            _trayStatusMenuItem = new MenuItem
            {
                Header = "Серверы: 0 | Запущено: 0",
                IsEnabled = false
            };
            var exitItem = new MenuItem { Header = "Выход" };

            showItem.Click += (_, _) =>
            {
                Show();
                Activate();
                WindowState = WindowState.Normal;
            };

            exitItem.Click += (_, _) =>
            {
                _isExiting = true;
                Close();
            };

            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(_trayStatusMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            // Создаём NotifyIcon в коде — без XAML, чтобы OnRender не вызвал
            // автоматическую регистрацию в трее. Управляем Register/Unregister сами.
            _trayIcon = new NotifyIcon
            {
                Icon = new BitmapImage(
                    new Uri("pack://application:,,,/Assets/app-icon.ico")),
                TooltipText = "Konserva — Minecraft Server Manager",
                MenuOnRightClick = true,
                FocusOnLeftClick = true,
                Menu = contextMenu
            };

#pragma warning disable CS8622
            _trayIcon.LeftClick += OnTrayLeftClick;
#pragma warning restore CS8622

            // Применяем начальную видимость иконки в трее
            UpdateTrayIconVisibility();
        }
        catch (Exception ex)
        {
            Logger.Warning($"InitTray error: {ex.Message}", "MainWindow");
        }
    }

    private void OnTrayLeftClick(NotifyIcon sender, RoutedEventArgs e)
    {
        Show();
        Activate();
        WindowState = WindowState.Normal;
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

        // Отслеживаем системную тему для авто-обновления при смене в Windows
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        AutoDetectJava();
        StartStatusBarTimer();
        StartUpdateCheckLoop();

        // Начальный заголовок (до первой навигации)
        WindowTitleText.Text = LocalizationManager.Get("MainWindow_Header");

        // Подписываемся на событие навигации для обновления кнопки "Назад" и заголовка
        ContentFrame.Navigated += ContentFrame_Navigated;

        // Navigate to Servers page by default
        ContentFrame.Navigate(new Pages.ServersPage());

        // Инициализируем SnackbarService (визуальное дерево уже загружено)
        _ = SnackbarService;
    }

    /// <summary>
    /// Обработчик события навигации — обновляет кнопку "Назад" и заголовок в TitleBar
    /// </summary>
    private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (BackButton != null)
        {
            BackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        }

        // Обновляем заголовок окна: "Konserva Manager — <page title>"
        var mainTitle = LocalizationManager.Get("MainWindow_Header");
        WindowTitleText.Text = e.Content is System.Windows.Controls.Page page && !string.IsNullOrEmpty(page.Title?.ToString())
            ? $"{mainTitle} — {page.Title}"
            : mainTitle;

        // Анимация появления страницы — стартуем когда UI готов к взаимодействию
        if (e.Content is FrameworkElement pageContent)
        {
            if (pageContent.IsLoaded)
            {
                Wpf.Ui.Animations.TransitionAnimationProvider.ApplyTransition(
                    pageContent, Wpf.Ui.Animations.Transition.FadeIn, 250);
            }
            else
            {
                pageContent.Loaded += OnPageLoadedForAnimation;
            }
        }
    }

    private static void OnPageLoadedForAnimation(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Loaded -= OnPageLoadedForAnimation;
            Wpf.Ui.Animations.TransitionAnimationProvider.ApplyTransition(
                element, Wpf.Ui.Animations.Transition.FadeIn, 250);
        }
    }

    /// <summary>
    /// Запуск обновления статусбара
    /// </summary>
    private void StartStatusBarTimer()
    {
        _statusBarCts?.Cancel();
        _statusBarCts = new CancellationTokenSource();
        StatusBarLoopAsync(_statusBarCts.Token).SafeFireAndForget(errorMessage: "StatusBar loop failed");
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
                // Dispatch to UI thread — PeriodicTimer callback runs on threadpool
                await Dispatcher.InvokeAsync(UpdateStatusBar);
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

        var foundJava = _javaService.FindInstalledJava();
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
        if (ContentFrame.Content is Pages.SettingsPage)
            return;

        ContentFrame.Navigate(new Pages.SettingsPage());
    }

    /// <summary>
    /// Навигация к странице создания сервера
    /// </summary>
    public void NavigateToCreateServer()
    {
        if (ContentFrame.Content is Pages.CreateServerPage)
            return;

        ContentFrame.Navigate(new Pages.CreateServerPage());
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

            // CPU
            var cpuUsage = _serverManager.GetTotalCpuUsage();
            StatusCpuUsage.Text = cpuUsage >= 10
                ? $"{cpuUsage:F0}%"
                : $"{cpuUsage:F1}%";

            // RAM
            var totalRamBytes = _serverManager.GetTotalMemoryUsage();
            var totalRamMB = totalRamBytes / (1024 * 1024);
            StatusMemoryUsage.Text = totalRamMB >= 1024
                ? $"{totalRamMB / 1024.0:0.#} GB"
                : $"{totalRamMB} MB";

        }
        finally
        {
            _isUpdatingStatusBar = false;
        }
    }

    // ===== Tray =====

    /// <summary>
    /// При сворачивании окна — в трей в зависимости от режима.
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            var config = _config.GetConfig();
            var mode = Enum.TryParse<MinimizeToTrayMode>(config.MinimizeToTrayMode, out var m) ? m : MinimizeToTrayMode.OnClose;
            if (mode is MinimizeToTrayMode.OnMinimize or MinimizeToTrayMode.Always)
            {
                Hide();
            }
        }

        UpdateTrayIconVisibility();
    }

    /// <summary>
    /// Обновляет видимость иконки в трее в зависимости от настроек и состояния окна.
    /// </summary>
    public void UpdateTrayIconVisibility()
    {
        if (_trayIcon == null)
            return;

        try
        {
            var config = _config.GetConfig();
            var mode = Enum.TryParse<MinimizeToTrayMode>(config.MinimizeToTrayMode, out var m) ? m : MinimizeToTrayMode.None;
            var isTrayActive = mode != MinimizeToTrayMode.None;

            // Иконка показывается только если трей активен И окно свёрнуто или скрыто
            if (isTrayActive && (WindowState == WindowState.Minimized || !IsVisible))
            {
                if (!_trayIcon.IsRegistered)
                    _trayIcon.Register();
            }
            else
            {
                if (_trayIcon.IsRegistered)
                    _trayIcon.Unregister();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"UpdateTrayIconVisibility error: {ex.Message}", "MainWindow");
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Если приложение действительно завершается (из Exit меню), не отменяем
        if (_isExiting)
            return;

        // Сворачиваем в трей при закрытии в зависимости от режима
        var config = _config.GetConfig();
        var mode = Enum.TryParse<MinimizeToTrayMode>(config.MinimizeToTrayMode, out var m) ? m : MinimizeToTrayMode.OnClose;
        if (mode is MinimizeToTrayMode.OnClose or MinimizeToTrayMode.Always)
        {
            e.Cancel = true;
            Hide();
            // После Hide обновляем видимость иконки
            UpdateTrayIconVisibility();
        }
    }

    /// <summary>
    /// Обновляет статус в трее (количество серверов, подсказка).
    /// </summary>
    private void UpdateTrayStatus()
    {
        if (_trayIcon == null || _trayStatusMenuItem == null)
            return;

        try
        {
            var (total, running, _) = _serverManager.GetStats();

            Func<string, string> localize = LocalizationManager.Get;
            var statusText = $"{localize("StatusBar_TotalServers")}: {total} | {localize("StatusBar_Running")}: {running}";
            _trayStatusMenuItem.Header = statusText;

            _trayIcon.TooltipText = running > 0
                ? $"Konserva — {running}/{total} {localize("StatusBar_Running").ToLowerInvariant()}"
                : "Konserva — Minecraft Server Manager";
        }
        catch (Exception ex)
        {
            Logger.Warning($"UpdateTrayStatus error: {ex.Message}", "MainWindow");
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
        if (parameter is string serverId)
        {
            Ioc.Default.GetService<MainWindow>()?.NavigateToServer(serverId);
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
    /// Применение темы приложения через WPF-UI
    /// </summary>
    public void ApplyTheme(string theme)
    {
        if (theme == "System")
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        }
        else
        {
            var wpfTheme = theme switch
            {
                "Dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
                "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
                _ => Wpf.Ui.Appearance.ApplicationTheme.Unknown
            };

            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfTheme);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopStatusBarTimer();
        _updateService.Stop();
        _updateService.UpdateAvailable -= OnUpdateAvailable;
        _updateService.CheckStarted -= OnUpdateCheckStarted;
        _updateService.CheckCompleted -= OnUpdateCheckCompleted;
        _serverManager.OnServersChanged -= UpdateStatusBar;
        _serverManager.OnServersChanged -= UpdateTrayStatus;

        // Удаляем иконку из трея
        try { _trayIcon?.Dispose(); } catch { /* ignored */ }

        // Dispose CTS
        _statusBarCts?.Cancel();
        _statusBarCts?.Dispose();
        _statusBarCts = null;

        _disposed = true;
    }

    // ===== Update checking =====

    private void OnUpdateAvailable(UpdateInfo updateInfo)
    {
        Dispatcher.Invoke(() => UpdateNotificationControl.Show(updateInfo));
    }

    private void OnUpdateCheckStarted()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProgressRing.Visibility = Visibility.Visible;
            UpdateCheckmarkIcon.Visibility = Visibility.Collapsed;
        });
    }

    private void OnUpdateCheckCompleted(UpdateInfo updateInfo)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProgressRing.Visibility = Visibility.Collapsed;
            UpdateCheckmarkIcon.Visibility = !updateInfo.IsAvailable
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Запускает фоновый цикл авто-проверки обновлений.
    /// </summary>
    public void StartUpdateCheckLoop()
    {
        _updateService.Start();
    }

    /// <summary>
    /// Принудительная проверка обновлений (вызывается из SettingsPage).
    /// </summary>
    public async Task<UpdateInfo> ForceCheckForUpdatesAsync()
    {
        return await _updateService.ForceCheckAsync();
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
                Padding = new Thickness(12, 10, 12, 10),
                MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            SnackbarPresenter.AddToQue(snackbar);
        });
    }

    /// <summary>
    /// Показывает минималистичный бадж «Настройки сохранены» справа сверху.
    /// </summary>
    public void ShowSaveBadge()
    {
        Dispatcher.Invoke(async () =>
        {
            if (SaveNotificationBadge == null) return;

            // Сначала скрываем, если уже показан (перезапускаем анимацию)
            SaveNotificationBadge.Visibility = Visibility.Visible;
            SaveNotificationBadge.Opacity = 0;

            // Плавное появление
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            SaveNotificationBadge.BeginAnimation(OpacityProperty, fadeIn);

            // Ждём 2 секунды
            await Task.Delay(2000);

            // Плавное исчезновение
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => SaveNotificationBadge.Visibility = Visibility.Collapsed;
            SaveNotificationBadge.BeginAnimation(OpacityProperty, fadeOut);
        });
    }
}

// Вместо TaskExtensions.FireAndForget используйте SafeFireAndForget из CommunityToolkit
