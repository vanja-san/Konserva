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
/// Main application window
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
    private MenuItem? _trayOpenMenuItem;
    private MenuItem? _trayExitMenuItem;
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

        // Tray initialization (after InitializeComponent)
        InitTray();
    }

    /// <summary>
    /// Initializes the tray events.
    /// </summary>
    private void InitTray()
    {
        try
        {
            _trayOpenMenuItem = new MenuItem { Header = LocalizationManager.Get("Tray_Open") };
            _trayStatusMenuItem = new MenuItem
            {
                Header = LocalizationManager.Get("Tray_Status", 0, 0),
                IsEnabled = false
            };
            _trayExitMenuItem = new MenuItem { Header = LocalizationManager.Get("Tray_Exit") };

            var showItem = _trayOpenMenuItem;
            var exitItem = _trayExitMenuItem;

            showItem.Click += (_, _) =>
            {
                ShowInTaskbar = true;
                WindowState = WindowState.Normal;
                Activate();
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

            // We create the NotifyIcon in code — without XAML, so that OnRender does not trigger
            // automatic registration in the tray. We manage Register/Unregister ourselves.
            _trayIcon = new NotifyIcon
            {
                Icon = new BitmapImage(
                    new Uri("pack://application:,,,/Assets/app-icon.ico")),
                TooltipText = "Konserva — Minecraft Server Manager",
                MenuOnRightClick = true,
                FocusOnLeftClick = false,
                Menu = contextMenu
            };

#pragma warning disable CS8622
            _trayIcon.LeftClick += OnTrayLeftClick;
#pragma warning restore CS8622

            // Apply the initial tray icon visibility
            UpdateTrayIconVisibility();
        }
        catch (Exception ex)
        {
            Logger.Warning($"InitTray error: {ex.Message}", "MainWindow");
        }
    }

    private void OnTrayLeftClick(NotifyIcon sender, RoutedEventArgs e)
    {
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _ = Focus();
    }

    /// <summary>
    /// Service used to display ContentDialog
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
        // Apply the theme from the config
        var config = _config.GetConfig();
        ApplyTheme(config.Theme ?? "System");

        // Watch the system theme so it auto-refreshes when Windows switches
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        AutoDetectJava();
        StartStatusBarTimer();
        StartUpdateCheckLoop();

        // Initial title (before the first navigation)
        WindowTitleText.Text = LocalizationManager.Get("MainWindow_Header");

        // Subscribe to language changes to update the window title
        LocalizationManager.LanguageChanged += OnLanguageChanged;

        // Subscribe to the navigation event to update the "Back" button and the title
        ContentFrame.Navigated += ContentFrame_Navigated;

        // Navigate to Servers page by default
        ContentFrame.Navigate(new Pages.ServersPage());

        // Initialize SnackbarService (the visual tree is already loaded)
        _ = SnackbarService;
    }

    /// <summary>
    /// Navigation event handler — updates the "Back" button and the TitleBar title
    /// </summary>
    private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (BackButton != null)
        {
            BackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        }

        // Hide the status bar on pages that do not need it (server creation, settings)
        StatusBarPanel.Visibility = e.Content is Pages.CreateServerPage or Pages.SettingsPage
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Update the window title: "Konserva Manager — <page title>"
        var mainTitle = LocalizationManager.Get("MainWindow_Header");
        WindowTitleText.Text = e.Content is System.Windows.Controls.Page page && !string.IsNullOrEmpty(page.Title?.ToString())
            ? $"{mainTitle} — {page.Title}"
            : mainTitle;

        // Page fade-in animation — start it once the UI is ready for interaction
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
    /// Updates the window title when the language changes.
    /// Uses Dispatcher.InvokeAsync so that pages can update their Title first
    /// (through their LanguageChanged subscription), and only afterwards
    /// MainWindow reads the up-to-date page.Title.
    /// </summary>
    private void OnLanguageChanged(string culture)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            var mainTitle = LocalizationManager.Get("MainWindow_Header");
            WindowTitleText.Text = ContentFrame.Content is System.Windows.Controls.Page page && !string.IsNullOrEmpty(page.Title?.ToString())
                ? $"{mainTitle} — {page.Title}"
                : mainTitle;

            // The tray context menu is created in code, so its labels
            // are not updated via {loc:Loc} — we set them manually.
            if (_trayOpenMenuItem != null)
                _trayOpenMenuItem.Header = LocalizationManager.Get("Tray_Open");

            if (_trayExitMenuItem != null)
                _trayExitMenuItem.Header = LocalizationManager.Get("Tray_Exit");

            UpdateTrayStatus();
        });
    }

    /// <summary>
    /// Starts the status bar refresh
    /// </summary>
    private void StartStatusBarTimer()
    {
        _statusBarCts?.Cancel();
        _statusBarCts = new CancellationTokenSource();
        StatusBarLoopAsync(_statusBarCts.Token).SafeFireAndForget(errorMessage: "StatusBar loop failed");
    }

    /// <summary>
    /// Stops the status bar refresh
    /// </summary>
    private void StopStatusBarTimer()
    {
        _statusBarCts?.Cancel();
        _statusBarCts?.Dispose();
        _statusBarCts = null;
    }

    /// <summary>
    /// Status bar refresh loop, every 5 seconds
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
            // Expected cancellation
        }
    }

    /// <summary>
    /// Auto-detection of installed Java.
    /// On every launch it scans the system and adds only new installations (matched by path).
    /// </summary>
    private void AutoDetectJava()
    {
        var config = _config.GetConfig();

        // No Java in the config yet — first launch, do the full setup
        if (config.JavaInstallations.Count == 0)
        {
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
        else
        {
            // Java is already there — just add the new ones (without overwriting existing ones)
            _javaService.ScanAndAddJava();
            UpdateStatusBar();
        }
    }

    /// <summary>
    /// Navigation to the settings page
    /// </summary>
    public void NavigateToSettings()
    {
        if (ContentFrame.Content is Pages.SettingsPage)
            return;

        ContentFrame.Navigate(new Pages.SettingsPage());
    }

    /// <summary>
    /// Navigation to the server creation page
    /// </summary>
    public void NavigateToCreateServer()
    {
        if (ContentFrame.Content is Pages.CreateServerPage)
            return;

        ContentFrame.Navigate(new Pages.CreateServerPage());
    }

    /// <summary>
    /// Status bar refresh
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
    /// When the window is minimized — go to the tray depending on the mode.
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            var config = _config.GetConfig();
            var mode = Enum.TryParse<MinimizeToTrayMode>(config.MinimizeToTrayMode, out var m) ? m : MinimizeToTrayMode.OnClose;
            if (mode is MinimizeToTrayMode.OnMinimize or MinimizeToTrayMode.Always)
            {
                ShowInTaskbar = false;
            }
        }
        else
        {
            ShowInTaskbar = true;
        }

        UpdateTrayIconVisibility();
    }

    /// <summary>
    /// Updates the tray icon visibility depending on the settings and the window state.
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

            if (isTrayActive && WindowState == WindowState.Minimized)
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
        // If the app is really shutting down (from the Exit menu), do not cancel
        if (_isExiting)
            return;

        // Minimize to the tray on close depending on the mode
        var config = _config.GetConfig();
        var mode = Enum.TryParse<MinimizeToTrayMode>(config.MinimizeToTrayMode, out var m) ? m : MinimizeToTrayMode.OnClose;
        if (mode is MinimizeToTrayMode.OnClose or MinimizeToTrayMode.Always)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            UpdateTrayIconVisibility();
        }
    }

    /// <summary>
    /// Updates the tray status (server count, tooltip).
    /// </summary>
    private void UpdateTrayStatus()
    {
        if (_trayIcon == null || _trayStatusMenuItem == null)
            return;

        try
        {
            var (total, running, _) = _serverManager.GetStats();

            _trayStatusMenuItem.Header = LocalizationManager.Get("Tray_Status", total, running);

            _trayIcon.TooltipText = running > 0
                ? $"Konserva — {running}/{total} {LocalizationManager.Get("StatusBar_Running").ToLowerInvariant()}"
                : "Konserva — Minecraft Server Manager";
        }
        catch (Exception ex)
        {
            Logger.Warning($"UpdateTrayStatus error: {ex.Message}", "MainWindow");
        }
    }

    /// <summary>
    /// Navigation to the server details (adds to the navigation history)
    /// </summary>
    public void NavigateToServer(string serverId)
    {
        ContentFrame.Navigate(new Pages.ServerDetailPage(serverId));
    }

    /// <summary>
    /// Command used for navigation from XAML
    /// </summary>
    public static void NavigateToServerCommand(object parameter)
    {
        if (parameter is string serverId)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToServer(serverId);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        // Check whether the settings page is already open
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
    /// Applies the application theme via WPF-UI
    /// (swaps the WPF-UI::ThemesDictionary dictionaries in place; the Violeta dictionaries are static)
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
        LocalizationManager.LanguageChanged -= OnLanguageChanged;

        // Remove the icon from the tray
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
        _ = Dispatcher.InvokeAsync(() =>
        {
            // The notification itself shows "vX → vY", so we hide the static version
            // so the version is not printed twice (v1.0v1.0 → v1.1).
            VersionText.Visibility = Visibility.Collapsed;
            UpdateNotificationControl.Show(updateInfo);
        });
    }

    private void OnUpdateCheckStarted()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            UpdateProgressRing.ShowIndeterminate();
            UpdateCheckmarkIcon.Visibility = Visibility.Collapsed;
        });
    }

    private void OnUpdateCheckCompleted(UpdateInfo updateInfo)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            UpdateProgressRing.HideIndeterminate();

            if (!updateInfo.IsAvailable)
            {
                UpdateCheckmarkIcon.Visibility = Visibility.Visible;

                // Auto-hide checkmark after 5 seconds
                await Task.Delay(5000);
                UpdateCheckmarkIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateCheckmarkIcon.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>
    /// Starts the background auto-update check loop.
    /// </summary>
    public void StartUpdateCheckLoop()
    {
        _updateService.Start();
    }

    /// <summary>
    /// Forced update check (called from SettingsPage).
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
}
