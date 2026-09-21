using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Controls.Sections;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using ContentDialogResult = Wpf.Ui.Controls.ContentDialogResult;

namespace Konserva.Pages;

/// <summary>
/// Страница деталей сервера - консоль, моды, плагины, свойства, настройки
/// </summary>
public partial class ServerDetailPage : Page, IDisposable
{
    private readonly ServerDetailViewModel _viewModel;
    private readonly IModLoaderService _modLoaderService;
    private string? _serverId;
    private Server? _server;
    private McServerProcess? _process;
    private bool _disposed;
    private bool _isBusy;
    private CancellationTokenSource? _statusCts;
    private CancellationTokenSource? _errorResetCts;

    private ServerConsoleSection? _consoleSection;
    private ServerModsSection? _modsSection;
    private ServerPluginsSection? _pluginsSection;
    private ServerSettingsSection? _settingsSection;

    private static readonly Brush SuccessBrush = GetThemeBrush("SystemFillColorSuccessBrush");
    private static readonly Brush WarningBrush = GetThemeBrush("SystemFillColorCautionBrush");
    private static readonly Brush ErrorBrush = GetThemeBrush("SystemFillColorCriticalBrush");

    private static Brush GetThemeBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush
        ?? new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));

    public ServerDetailPage(string? serverId = null)
    {
        _serverId = serverId;
        _viewModel = Ioc.Default.GetService<ServerDetailViewModel>()
            ?? new ServerDetailViewModel(
                Ioc.Default.GetService<IServerManager>()!,
                Ioc.Default.GetService<IConfigService>()!,
                Ioc.Default.GetService<IPortForwardingService>());

        _modLoaderService = Ioc.Default.GetService<IModLoaderService>()!;

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Передаём serverId в ViewModel (устанавливается через конструктор или DataContext)
        _viewModel.ServerId = _serverId;

        // Подписываемся на событие ошибки запуска
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError += OnServerStartError;

        EnsureConsoleSection();

        StartStatusTimer();
        LoadServer();

        // Синхронизируем порт с моделью сервера при сохранении свойств
        PropertiesEditor.PropertiesSaved += OnPropertiesSaved;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от события ошибки запуска
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError -= OnServerStartError;
        PropertiesEditor.PropertiesSaved -= OnPropertiesSaved;
        StopStatusTimer();
        Dispose();
    }

    private void OnPropertiesSaved(object? sender, EventArgs e)
    {
        if (_server == null)
            return;

        var newPort = PropertiesEditor.CurrentPort;
        _viewModel.SavePort(newPort);
    }

    /// <summary>
    /// Запуск таймера обновления статуса
    /// </summary>
    private void StartStatusTimer()
    {
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        _ = StatusLoopAsync(_statusCts.Token);
    }

    /// <summary>
    /// Остановка таймера обновления статуса
    /// </summary>
    private void StopStatusTimer()
    {
        _statusCts?.Cancel();
        _statusCts?.Dispose();
        _statusCts = null;
    }

    /// <summary>
    /// Цикл обновления статуса каждые 3 секунды
    /// </summary>
    private async Task StatusLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);

                if (_server != null)
                {
                    UpdateStatus(_server.Status);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемая отмена
        }
    }

    private void LoadServer()
    {
        if (_serverId == null)
            return;

        // Загружаем данные через ViewModel
        _viewModel.ServerId = _serverId;
        _server = _viewModel.Server;
        _process = _viewModel.Process;

        if (_server == null)
            return;

        // Подключаемся к процессу сервера (если он есть)
        ConnectToProcess();

        // Заполняем заголовок
        ServerNameText.Text = _viewModel.ServerName;
        ServerInfoText.Text = _viewModel.ServerInfo;

        // Лёгкая проверка обновления загрузчика для уведомления в заголовке
        _ = CheckLoaderUpdateAvailabilityAsync();

        UpdateStatus(_server.Status);

        // Выделяем первую кнопку (Консоль) при загрузке
        ResetNavigationButtons();
        if (ConsoleNavButton != null)
        {
            ConsoleNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
            ConsoleNavIcon.Filled = true;
        }
    }

    /// <summary>
    /// Получает актуальный процесс сервера из менеджера и подписывается
    /// на его события. Загружает уже накопленные логи.
    /// Возвращает true, если процесс найден и подключение выполнено.
    /// </summary>
    private bool ConnectToProcess()
    {
        if (_serverId == null)
            return false;

        var manager = Ioc.Default.GetService<IServerManager>();
        if (manager == null)
            return false;

        var newProcess = manager.GetProcess(_serverId);

        // Если это тот же процесс, к которому уже подключены — не перезагружаем консоль.
        // Иначе LoadExistingLogs полностью пересоздаёт документ (Document.Text = ...),
        // сбрасывая ручную прокрутку пользователя при каждом цикле опроса (статусы Error/Stopped).
        if (ReferenceEquals(newProcess, _process) && newProcess != null)
            return true;

        // Отписываемся от старого процесса (если был)
        UnsubscribeFromProcess();
        _process = null;

        _process = newProcess;
        if (_process == null)
            return false;

        // Загружаем существующие логи
        _consoleSection?.LoadExistingLogs(_process.GetLogs());

        // Подписываемся на события процесса
        _process.OnLog += UpdateLog;
        _process.OnStatusChanged += UpdateStatus;
        _process.OnPlayersChanged += UpdatePlayers;

        return true;
    }

    private void UpdateLog(string line)
    {
        _consoleSection?.AppendLog(line);
    }

    private async void UpdateStatus(ServerStatus status)
    {
        try
        {
            // Если сервер успешно запущен — сбрасываем флаг ошибки
            if (status == ServerStatus.Running && _server != null)
            {
                _server.ResetErrorDialog();
            }

            // Если процесс не актуален — переподключаемся к свежему.
            // Покрывает и ошибки запуска: даже если сервер упал до поллинга,
            // подключаемся и загружаем уже накопленные логи.
            if (status is not ServerStatus.Stopped && _process is null or { Status: ServerStatus.Stopped or ServerStatus.Error })
            {
                ConnectToProcess();
            }

            // Для остановленных или ошибочных снимаем флаг занятости
            if (status is ServerStatus.Stopped or ServerStatus.Error)
            {
                _isBusy = false;
            }

            // Обновляем доступность настроек (баннер и контролы раздела настроек)
            _settingsSection?.UpdateSettingsAvailability();

            await this.InvokeAsync(() =>
            {
                // Определяем настройки для каждого статуса
                SymbolRegular icon;
                string toolTip, text;
                ControlAppearance appearance;
                bool isTransitioning;

                switch (status)
                {
                    case ServerStatus.Running:
                        icon = SymbolRegular.Stop20;
                        toolTip = LocalizationManager.Get("ServerDetail_Stop");
                        text = LocalizationManager.Get("ServerDetail_Stop");
                        appearance = ControlAppearance.Danger;
                        isTransitioning = false;
                        break;
                    case ServerStatus.Starting:
                        icon = SymbolRegular.ArrowRepeat120;
                        toolTip = LocalizationManager.Get("ServerDetail_Starting");
                        text = LocalizationManager.Get("ServerDetail_Starting");
                        appearance = ControlAppearance.Caution;
                        isTransitioning = true;
                        break;
                    case ServerStatus.Stopping:
                        icon = SymbolRegular.ArrowRepeat120;
                        toolTip = LocalizationManager.Get("ServerDetail_Stopping");
                        text = LocalizationManager.Get("ServerDetail_Stopping");
                        appearance = ControlAppearance.Caution;
                        isTransitioning = true;
                        break;
                    case ServerStatus.Error:
                        icon = SymbolRegular.Play20;
                        toolTip = LocalizationManager.Get("ServerDetail_Start");
                        text = LocalizationManager.Get("ServerStatus_Error");
                        appearance = ControlAppearance.Danger;
                        isTransitioning = false;
                        break;
                    default: // Stopped
                        icon = SymbolRegular.Play20;
                        toolTip = LocalizationManager.Get("ServerDetail_Start");
                        text = LocalizationManager.Get("ServerDetail_Start");
                        appearance = ControlAppearance.Primary;
                        isTransitioning = false;
                        break;
                }

                // Иконка, текст, цвет кнопки
                StartStopIcon.Symbol = icon;
                StartStopIcon.Visibility = isTransitioning ? Visibility.Collapsed : Visibility.Visible;
                StartStopPulse.Visibility = isTransitioning ? Visibility.Visible : Visibility.Collapsed;
                StartStopText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
                StartStopButton.ToolTip = toolTip;
                StartStopButton.Appearance = appearance;
                StartStopButton.IsEnabled = !isTransitioning;
                StartStopText.Text = text;

                // Останавливаем старую пульсацию
                StartStopPulse.BeginAnimation(UIElement.OpacityProperty, null);
                StartStopButton.BeginAnimation(UIElement.OpacityProperty, null);
                StartStopButton.Opacity = 1.0;

                // Пульсация для переходных состояний (кружок + кнопка)
                if (isTransitioning)
                {
                    var pulseSb = new Storyboard
                    {
                        RepeatBehavior = RepeatBehavior.Forever,
                        AutoReverse = true
                    };
                    var anim = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.2,
                        Duration = TimeSpan.FromSeconds(0.7)
                    };

                    // Пульсация кружка
                    var ellipseAnim = anim.Clone();
                    Storyboard.SetTarget(ellipseAnim, StartStopPulse);
                    Storyboard.SetTargetProperty(ellipseAnim, new PropertyPath("Opacity"));
                    pulseSb.Children.Add(ellipseAnim);

                    // Пульсация кнопки
                    var btnAnim = anim.Clone();
                    Storyboard.SetTarget(btnAnim, StartStopButton);
                    Storyboard.SetTargetProperty(btnAnim, new PropertyPath("Opacity"));
                    pulseSb.Children.Add(btnAnim);

                    pulseSb.Begin();
                    StartStopPulse.Opacity = 1.0;
                }

                // Автосброс Error через 10 секунд
                _errorResetCts?.Cancel();
                _errorResetCts?.Dispose();
                _errorResetCts = null;

                if (status == ServerStatus.Error)
                {
                    _errorResetCts = new CancellationTokenSource();
                    var ct = _errorResetCts.Token;
                    var capturedServer = _server;
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), ct);
                            await this.InvokeAsync(() =>
                            {
                                if (capturedServer != null)
                                    capturedServer.Status = ServerStatus.Stopped;
                                UpdateStatus(ServerStatus.Stopped);
                            });
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }, ct).SafeFireAndForget(errorMessage: "Error auto-reset failed");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"[UpdateStatus] Error: {ex.Message}", "ServerDetailPage");
        }
    }

    private async void UpdatePlayers(int players)
    {
        try
        {
            Logger.Info($"Players online: {players}", "ServerDetailPage");

            await this.InvokeAsync(() =>
            {
                // TODO: Обновлять UI элемент с количеством игроков, когда он будет добавлен
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"[UpdatePlayers] Error: {ex.Message}", "ServerDetailPage");
        }
    }

    /// <summary>
    /// Отписка от событий процесса для предотвращения утечек
    /// </summary>
    private void UnsubscribeFromProcess()
    {
        if (_process != null)
        {
            _process.OnLog -= UpdateLog;
            _process.OnStatusChanged -= UpdateStatus;
            _process.OnPlayersChanged -= UpdatePlayers;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true)
        {
            NavigationService.GoBack();
        }
        else
        {
            Ioc.Default.GetService<MainWindow>()?.ContentFrame?.Navigate(new Pages.ServersPage());
        }
    }

    /// <summary>
    /// Обработчик ошибки запуска сервера
    /// </summary>
    private void OnServerStartError(Server server, string errorMessage)
    {
        // Проверяем, что это наш сервер
        if (server.Id != _serverId)
        {
            Logger.Info($"[OnServerStartError] Server ID mismatch: expected {_serverId}, got {server.Id}", "ServerDetailPage");
            return;
        }

        Logger.Info($"[OnServerStartError] Received error for server {server.Name}: {errorMessage}", "ServerDetailPage");
        JavaManagementService.HandleServerStartError(server, errorMessage);
    }

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || _isBusy)
            return;

        _isBusy = true;
        try
        {
            await _viewModel.StartStopCommand.ExecuteAsync(null);

            // Сразу подключаемся к новому процессу, чтобы консоль
            // отображалась мгновенно (в т.ч. при ошибке запуска)
            ConnectToProcess();
        }
        catch (Exception ex)
        {
            Logger.Error($"StartStop_Click: Error managing server {_server.Name}: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_OperationError")}: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        UiHelper.OpenFolder(_server.Path);
    }

    /// <summary>
    /// Переключение между разделами сервера
    /// </summary>
    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button navButton)
            return;

        var tag = navButton.Tag?.ToString();
        if (string.IsNullOrEmpty(tag))
            return;

        ShowSection(tag);
    }

    /// <summary>
    /// Переключает видимый раздел сервера по его имени
    /// (Console / Mods / Plugins / Properties / Settings)
    /// </summary>
    private void ShowSection(string tag)
    {
        // Сбрасываем выделение всех кнопок
        ResetNavigationButtons();

        // Показываем нужную панель
        ConsoleHost.Visibility = tag == "Console" ? Visibility.Visible : Visibility.Collapsed;
        ModsHost.Visibility = tag == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        PluginsHost.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        PropertiesView.Visibility = tag == "Properties" ? Visibility.Visible : Visibility.Collapsed;
        SettingsHost.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        // Выделяем активную кнопку (если найдена)
        switch (tag)
        {
            case "Console":
                ConsoleNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
                break;
            case "Mods":
                ModsNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
                break;
            case "Plugins":
                PluginsNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
                break;
            case "Properties":
                PropertiesNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
                break;
            case "Settings":
                SettingsNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
                break;
        }

        // Устанавливаем Filled=True для активной иконки
        SetIconFilled(tag, true);

        // Создаём нужную секцию (лениво) и загружаем данные
        switch (tag)
        {
            case "Mods":
                EnsureModsSection();
                _modsSection?.Load();
                break;
            case "Plugins":
                EnsurePluginsSection();
                _pluginsSection?.Load();
                break;
            case "Properties":
                LoadProperties();
                break;
            case "Settings":
                EnsureSettingsSection();
                break;
        }
    }

    /// <summary>
    /// Сброс выделения всех кнопок навигации
    /// </summary>
    private void ResetNavigationButtons()
    {
        var transparentBrush = TryFindResource("ControlFillColorTransparentBrush") as Brush ?? Brushes.Transparent;

        ConsoleNavButton.Background = transparentBrush;
        ModsNavButton.Background = transparentBrush;
        PluginsNavButton.Background = transparentBrush;
        PropertiesNavButton.Background = transparentBrush;
        SettingsNavButton.Background = transparentBrush;

        // Сбрасываем Filled у всех иконок
        SetIconFilled("Console", false);
        SetIconFilled("Mods", false);
        SetIconFilled("Plugins", false);
        SetIconFilled("Properties", false);
        SetIconFilled("Settings", false);
    }

    /// <summary>
    /// Установка свойства Filled для иконки навигации
    /// </summary>
    private void SetIconFilled(string tag, bool filled)
    {
        var icon = tag switch
        {
            "Console" => ConsoleNavIcon,
            "Mods" => ModsNavIcon,
            "Plugins" => PluginsNavIcon,
            "Properties" => PropertiesNavIcon,
            "Settings" => SettingsNavIcon,
            _ => null
        };

        if (icon != null)
            icon.Filled = filled;
    }

    /// <summary>
    /// Создаёт секцию консоли при первом показе страницы.
    /// </summary>
    private void EnsureConsoleSection()
    {
        if (_consoleSection != null)
            return;

        _consoleSection = new ServerConsoleSection();
        _consoleSection.SetServerId(_serverId);
        _consoleSection.ApplyConsoleSettings();
        ConsoleHost.Children.Add(_consoleSection);
    }

    /// <summary>
    /// Создаёт секцию модов при первом открытии раздела.
    /// </summary>
    private void EnsureModsSection()
    {
        if (_modsSection != null)
            return;

        _modsSection = new ServerModsSection();
        _modsSection.Initialize(_viewModel, _server);
        ModsHost.Children.Add(_modsSection);
    }

    /// <summary>
    /// Создаёт секцию плагинов при первом открытии раздела.
    /// </summary>
    private void EnsurePluginsSection()
    {
        if (_pluginsSection != null)
            return;

        _pluginsSection = new ServerPluginsSection();
        _pluginsSection.Initialize(_viewModel, _server);
        PluginsHost.Children.Add(_pluginsSection);
    }

    /// <summary>
    /// Создаёт секцию настроек при первом открытии раздела.
    /// </summary>
    private void EnsureSettingsSection()
    {
        if (_settingsSection != null)
            return;

        _settingsSection = new ServerSettingsSection();
        _settingsSection.ServerRenamed += OnSectionServerRenamed;
        _settingsSection.UpdateAvailabilityChanged += OnSectionUpdateAvailabilityChanged;
        _settingsSection.Initialize(_viewModel, _server);
        SettingsHost.Children.Add(_settingsSection);
    }

    /// <summary>
    /// Обновляем заголовок страницы при переименовании сервера в настройках.
    /// </summary>
    private void OnSectionServerRenamed(string name)
    {
        this.Invoke(() => ServerNameText.Text = name);
    }

    /// <summary>
    /// Показываем/скрываем уведомление об обновлении загрузчика в заголовке.
    /// </summary>
    private void OnSectionUpdateAvailabilityChanged(bool hasUpdate, string? latestVersion)
    {
        UpdateAvailableButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        if (hasUpdate && !string.IsNullOrEmpty(latestVersion))
        {
            UpdateAvailableButton.ToolTip = string.Format(
                LocalizationManager.Get("ServerDetail_UpdateLoader_Available"), latestVersion);
        }
    }

    /// <summary>
    /// Лёгкая проверка доступности обновления загрузчика для уведомления
    /// в заголовке (полная форма со списком версий живёт в разделе настроек).
    /// </summary>
    private async Task CheckLoaderUpdateAvailabilityAsync()
    {
        if (_server == null || _modLoaderService == null)
            return;

        try
        {
            var loaderType = _server.ModLoader.Type;
            var updatable = loaderType is ModLoaderType.Forge or ModLoaderType.NeoForge
                or ModLoaderType.Fabric or ModLoaderType.Quilt or ModLoaderType.Paper;
            if (!updatable || !_viewModel.SettingsEnableUpdateNotification)
            {
                UpdateAvailableButton.Visibility = Visibility.Collapsed;
                return;
            }

            var current = string.IsNullOrWhiteSpace(_server.ModLoader.LoaderVersion)
                ? null
                : _server.ModLoader.LoaderVersion;
            if (current == null)
            {
                UpdateAvailableButton.Visibility = Visibility.Collapsed;
                return;
            }

            var versions = await _modLoaderService.GetLoaderVersionsAsync(
                loaderType.ToString(), _server.McVersion, showSnapshots: false);

            var hasUpdate = versions.Any(v => VersionCompare.CompareLoaderVersions(v, current) > 0);

            UpdateAvailableButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Warning($"CheckLoaderUpdateAvailability error: {ex.Message}", "ServerDetailPage");
        }
    }

    /// <summary>
    /// Переход к обновлению загрузчика: открываем раздел настроек и карточку обновления.
    /// </summary>
    private void UpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        ShowSection("Settings");
        _settingsSection?.ShowUpdateCard();
    }

    /// <summary>
    /// Загрузка server.properties в редактор.
    /// </summary>
    private void LoadProperties()
    {
        if (_server == null)
            return;

        try
        {
            var propertiesPath = Path.Combine(_server.Path, "server.properties");
            PropertiesEditor.Load(propertiesPath);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load properties: {ex.Message}", "ServerDetailPage");
            ShowErrorSafe($"{LocalizationManager.Get("ServerDetail_PropsLoadError")}: {ex.Message}");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var result = await Dialogs.ConfirmDeleteDialog.ShowAsync(
                _server.Name,
                title: LocalizationManager.Get("MsgDel_Title") ?? "Delete Server",
                messageFormat: LocalizationManager.Get("MsgDel_Confirm") ?? "Are you sure you want to delete server \"{0}\"?");

        if (result != ContentDialogResult.Primary)
            return;

        await _viewModel.DeleteServerAsync();
        Back_Click(sender, e);
    }

    /// <summary>
    /// Безопасный вызов ShowError из sync-контекста (fire-and-forget с try/catch)
    /// </summary>
    private async void ShowErrorSafe(string message)
    {
        try { await UiHelper.ShowError(message); }
        catch (Exception ex) { Logger.Warning($"[ShowErrorSafe] Error: {ex.Message}", "ServerDetailPage"); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Отписываемся от событий процесса
        UnsubscribeFromProcess();

        StopStatusTimer();

        _errorResetCts?.Cancel();
        _errorResetCts?.Dispose();
        _errorResetCts = null;

        _modsSection?.Dispose();
        _settingsSection?.Dispose();

        _disposed = true;
    }
}