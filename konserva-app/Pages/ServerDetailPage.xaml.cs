using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfMenuItem = Wpf.Ui.Controls.MenuItem;

namespace Konserva.Pages;

/// <summary>
/// Страница деталей сервера - консоль, моды, плагины, настройки, удаление
/// </summary>
public partial class ServerDetailPage : Page, IDisposable
{
    private readonly ServerDetailViewModel _viewModel;
    private string? _serverId;
    private Server? _server;
    private McServerProcess? _process;
    private bool _disposed;
    private bool _isBusy;
    private CancellationTokenSource? _statusCts;
    private CancellationTokenSource? _errorResetCts;

    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public ServerDetailPage(string? serverId = null)
    {
        _serverId = serverId;
        _viewModel = Ioc.Default.GetService<ServerDetailViewModel>()
            ?? new ServerDetailViewModel(
                Ioc.Default.GetService<IServerManager>()!,
                Ioc.Default.GetService<IConfigService>()!,
                Ioc.Default.GetService<IPortForwardingService>());

        InitializeComponent();

        // Подписываемся после InitializeComponent чтобы избежать NullReferenceException
        SettingJavaAutoSelect.Checked += SettingJavaAutoSelect_CheckedChanged;
        SettingJavaAutoSelect.Unchecked += SettingJavaAutoSelect_CheckedChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Передаём serverId в ViewModel (устанавливается через конструктор или DataContext)
        _viewModel.ServerId = _serverId;

        // Подписываемся на событие ошибки запуска
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError += OnServerStartError;

        // Обновляем PageWidth лога при изменении размера
        LogBox.SizeChanged += LogBox_SizeChanged;

        StartStatusTimer();
        LoadServer();

        // Синхронизируем порт с моделью сервера при сохранении свойств
        PropertiesEditor.PropertiesSaved += OnPropertiesSaved;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от события ошибки запуска
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError -= OnServerStartError;
        LogBox.SizeChanged -= LogBox_SizeChanged;
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

    private double _maxContentWidth = 0;

    private void LogBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // При ресайзе: если контент уже шире новой ширины — не трогаем PageWidth
        // Если контент уже (или пустой) — ставим PageWidth = ActualWidth
        if (LogDocument != null && e.NewSize.Width > 0 && _maxContentWidth <= e.NewSize.Width)
        {
            // Контент помещается — скролл скрыт
            LogDocument.PageWidth = e.NewSize.Width;
        }
        // Иначе: контент шире — оставляем как есть, скролл виден
    }

    /// <summary>
    /// Обновляет PageWidth документа если строка длиннее видимой области
    /// </summary>
    private void UpdatePageWidthForText(string text)
    {
        if (LogDocument == null || string.IsNullOrEmpty(text)) return;

        try
        {
            // Consolas — моноширинный шрифт. Измеряем ширину строки.
            var formattedText = new System.Windows.Media.FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Consolas"),
                12,
                System.Windows.Media.Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Учитываем padding RichTextBox (8px с каждой стороны)
            var neededWidth = formattedText.Width + 16;

            if (neededWidth > _maxContentWidth)
            {
                _maxContentWidth = neededWidth;
            }

            // Если контент шире видимой области — расширяем PageWidth
            LogDocument.PageWidth = _maxContentWidth > LogBox.ActualWidth
                ? _maxContentWidth
                : LogBox.ActualWidth;
        }
        catch
        {
            // Suppress UI layout adjustment errors
        }
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

        // Проверяем существующий процесс
        if (_process != null)
        {
            // Сначала отписываемся от старого процесса (если был)
            UnsubscribeFromProcess();

            // Загружаем существующие логи
            LoadExistingLogs();

            // Подписываемся на события нового процесса
            _process.OnLog += UpdateLog;
            _process.OnStatusChanged += UpdateStatus;
            _process.OnPlayersChanged += UpdatePlayers;
        }

        // Заполняем UI
        ServerNameText.Text = _viewModel.ServerName;
        ServerInfoText.Text = _viewModel.ServerInfo;

        // Настройки
        SettingName.Text = _viewModel.SettingsName;
        SettingRamMin.Text = _viewModel.SettingsRamMin.ToString();
        SettingRamMax.Text = _viewModel.SettingsRamMax.ToString();
        SettingAutoRestart.IsChecked = _viewModel.SettingsAutoRestart;
        SettingAutoRestartDelay.Text = _viewModel.SettingsAutoRestartDelay.ToString();

        // Настройки Java
        SettingJavaAutoSelect.IsChecked = _viewModel.SettingsJavaAutoSelect;
        LoadJavaComboBox();
        UpdateJavaComboBoxVisibility();

        // UPnP настройки
        SettingEnableUpnp.IsChecked = _viewModel.SettingsEnableUpnp;
        UpdateServerAddressDisplay();

        // JVM аргументы
        SettingJvmArgs.Text = _viewModel.SettingsJvmArgs;

        UpdateStatus(_server.Status);

        // Выделяем первую кнопку (Консоль) при загрузке
        ResetNavigationButtons();
        if (ConsoleNavButton != null)
        {
            ConsoleNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;
            ConsoleNavIcon.Filled = true;
        }
    }

    private void LoadExistingLogs()
    {
        if (_process == null)
            return;

        var logs = _process.GetLogs();

        this.Invoke(() =>
        {
            var document = LogBox.Document;
            document.Blocks.Clear();
            _maxContentWidth = 0;

            if (logs.Count > 0)
            {
                var paragraph = new System.Windows.Documents.Paragraph();

                foreach (var logLine in logs)
                {
                    var run = new System.Windows.Documents.Run(logLine + "\n");
                    ApplyLogColor(run, logLine);
                    paragraph.Inlines.Add(run);
                }

                document.Blocks.Add(paragraph);
                ConsolePlaceholder.Visibility = System.Windows.Visibility.Collapsed;
                var longestLine = logs.OrderByDescending(l => l.Length).FirstOrDefault();
                if (!string.IsNullOrEmpty(longestLine))
                    UpdatePageWidthForText(longestLine);
            }
            else
            {
                ConsolePlaceholder.Visibility = System.Windows.Visibility.Visible;
                LogDocument.PageWidth = LogBox.ActualWidth;
            }
            LogBox.ScrollToEnd();
        });
    }

    private static void ApplyLogColor(System.Windows.Documents.Run run, string line)
    {
        if (line.Contains("[ERROR]"))
        {
            run.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 100, 100));
            run.FontWeight = System.Windows.FontWeights.Bold;
        }
        else if (line.Contains(LocalizationManager.Get("Log_ServerStarted")) ||
                 line.Contains(LocalizationManager.Get("Log_ServerStoppedSuccessfully")))
        {
            run.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 80));
            run.FontWeight = System.Windows.FontWeights.Bold;
        }
    }

    private async void UpdateLog(string line)
    {
        try
        {
            await this.InvokeAsync(() =>
            {
                var document = LogBox.Document;
                var run = new System.Windows.Documents.Run(line + "\n");
                ApplyLogColor(run, line);

                if (document.Blocks.LastBlock is System.Windows.Documents.Paragraph lastParagraph)
                {
                    lastParagraph.Inlines.Add(run);
                }
                else
                {
                    document.Blocks.Add(new System.Windows.Documents.Paragraph(run));
                }
                ConsolePlaceholder.Visibility = System.Windows.Visibility.Collapsed;
                UpdatePageWidthForText(line);
                LogBox.ScrollToEnd();
            });
        }
        catch (TaskCanceledException)
        {
            // Игнорируем — приложение закрывается, диспетчер уже не работает
        }
        catch (Exception ex)
        {
            Logger.Warning($"[UpdateLog] Error: {ex.Message}", "ServerDetailPage");
        }
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

            // Если статус изменился на запущенный или останавливается и процесс не актуален - переподключаемся
            if (status is ServerStatus.Starting or ServerStatus.Running or ServerStatus.Stopping)
            {
                // При переходе в рабочие состояния проверяем актуальность процесса и подписываемся
                if (_process == null || _process.Status == ServerStatus.Stopped || _process.Status == ServerStatus.Error)
                {
                    // Отписываемся от старого процесса
                    UnsubscribeFromProcess();
                    _process = null;

                    // Получаем свежий процесс
                    _process = Ioc.Default.GetService<IServerManager>()!.GetProcess(_serverId!);

                    // Загружаем существующие логи и подписываемся на события
                    if (_process == null) return;

                    LoadExistingLogs();

                    // Подписываемся на события
                    _process.OnLog += UpdateLog;
                    _process.OnStatusChanged += UpdateStatus;
                    _process.OnPlayersChanged += UpdatePlayers;
                }
            }

            // Для остановленных или ошибочных снимаем флаг занятости
            if (status is ServerStatus.Stopped or ServerStatus.Error)
            {
                _isBusy = false;
                // Не удаляем _process здесь, чтобы можно было перезапустить
                // Потом при следующем запуске переподключимся
            }

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
                        icon = Wpf.Ui.Controls.SymbolRegular.Stop20;
                        toolTip = LocalizationManager.Get("ServerDetail_Stop");
                        text = LocalizationManager.Get("ServerDetail_Stop");
                        appearance = ControlAppearance.Danger;
                        isTransitioning = false;
                        break;
                    case ServerStatus.Starting:
                        icon = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat120;
                        toolTip = LocalizationManager.Get("ServerDetail_Starting");
                        text = LocalizationManager.Get("ServerDetail_Starting");
                        appearance = ControlAppearance.Caution;
                        isTransitioning = true;
                        break;
                    case ServerStatus.Stopping:
                        icon = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat120;
                        toolTip = LocalizationManager.Get("ServerDetail_Stopping");
                        text = LocalizationManager.Get("ServerDetail_Stopping");
                        appearance = ControlAppearance.Caution;
                        isTransitioning = true;
                        break;
                    case ServerStatus.Error:
                        icon = Wpf.Ui.Controls.SymbolRegular.Play20;
                        toolTip = LocalizationManager.Get("ServerDetail_Start");
                        text = LocalizationManager.Get("ServerStatus_Error");
                        appearance = ControlAppearance.Danger;
                        isTransitioning = false;
                        break;
                    default: // Stopped
                        icon = Wpf.Ui.Controls.SymbolRegular.Play20;
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

        // Сбрасываем выделение всех кнопок
        ResetNavigationButtons();

        // Выделяем активную кнопку
        navButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as Brush;

        // Устанавливаем Filled=True для активной иконки
        SetIconFilled(tag, true);

        // Показываем нужную панель
        ConsoleView.Visibility = tag == "Console" ? Visibility.Visible : Visibility.Collapsed;
        ModsView.Visibility = tag == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        PluginsView.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        PropertiesView.Visibility = tag == "Properties" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        // Загружаем данные для соответствующих разделов
        switch (tag)
        {
            case "Mods":
                LoadMods();
                break;
            case "Plugins":
                LoadPlugins();
                break;
            case "Properties":
                LoadProperties();
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

    private void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CommandBox.Text))
            return;

        Ioc.Default.GetService<IServerManager>()!.SendCommand(_serverId!, CommandBox.Text);
        CommandBox.Clear();
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendCommand_Click(sender, e);
        }
    }

    /// <summary>
    /// Валидация: разрешаем ввод только цифр
    /// </summary>
    private void NumberValidationTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Заполнение ComboBox со списком Java
    /// </summary>
    private void LoadJavaComboBox()
    {
        var javaList = _viewModel.GetJavaList();

        SettingJavaComboBox.Items.Clear();

        foreach (var (id, display) in javaList)
        {
            SettingJavaComboBox.Items.Add(new ComboBoxItem
            {
                Content = display,
                Tag = id
            });
        }

        var selectedJavaId = _viewModel.GetSelectedJavaId();
        if (!string.IsNullOrEmpty(selectedJavaId))
        {
            var selectedItem = SettingJavaComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => (string?)item.Tag == selectedJavaId);

            if (selectedItem != null)
                SettingJavaComboBox.SelectedItem = selectedItem;
        }
        else
        {
            SettingJavaComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Автосохранение настроек при изменении
    /// </summary>
    private void Setting_Click(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    /// <summary>
    /// Автосохранение настроек при потере фокуса
    /// </summary>
    private void Setting_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    /// <summary>
    /// Автоматическое сохранение настроек сервера
    /// </summary>
    private void AutoSaveSettings()
    {
        if (_server == null)
            return;

        var newName = SettingName.Text.Trim();
        var ramMinStr = SettingRamMin.Text;
        var ramMaxStr = SettingRamMax.Text;
        var autoRestart = SettingAutoRestart.IsChecked;
        var autoRestartDelayStr = SettingAutoRestartDelay.Text;
        var javaAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
        var javaId = SettingJavaComboBox.SelectedItem is ComboBoxItem selectedItem
            ? selectedItem.Tag as string
            : null;
        var jvmArgs = SettingJvmArgs.Text;

        _viewModel.SaveSettings(newName, ramMinStr, ramMaxStr, autoRestart, autoRestartDelayStr,
            javaAutoSelect, javaId, jvmArgs);

        // Обновляем UI если имя изменилось
        if (!string.IsNullOrEmpty(newName) && newName != ServerNameText.Text)
        {
            ServerNameText.Text = newName;
        }
    }
    /// <summary>
    /// Обновление видимости ComboBox Java
    /// </summary>
    private void UpdateJavaComboBoxVisibility()
    {
        var isAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
        JavaSelectionGrid.Visibility = isAutoSelect ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Обработка изменения автоматического выбора Java
    /// </summary>
    private void SettingJavaAutoSelect_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // Проверка: защита от вызова до инициализации
        if (!IsInitialized || JavaSelectionGrid == null)
            return;

        UpdateJavaComboBoxVisibility();
    }

    /// <summary>
    /// Обработка изменения чекбокса UPnP
    /// </summary>
    private void SettingEnableUpnp_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var enable = SettingEnableUpnp.IsChecked ?? false;
        _viewModel.SaveUpnpSetting(enable);
    }

    /// <summary>
    /// Обновляет отображение адреса сервера (IP:Port).
    /// </summary>
    private void UpdateServerAddressDisplay()
    {
        _viewModel.UpdateServerAddressDisplay();

        if (!string.IsNullOrEmpty(_viewModel.UpnpAddress))
        {
            UpnpAddressText.Text = _viewModel.UpnpAddress;
            UpnpAddressText.Visibility = Visibility.Visible;
            CopyAddressButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpnpAddressText.Visibility = Visibility.Collapsed;
            CopyAddressButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Копирует адрес сервера (IP:Port) в буфер обмена.
    /// </summary>
    private void CopyAddressButton_Click(object sender, RoutedEventArgs e)
    {
        var address = UpnpAddressText.Text;
        if (string.IsNullOrEmpty(address))
            return;

        try
        {
            Clipboard.SetText(address);
            CopyAddressButton.Content = LocalizationManager.Get("Common_Copied");

            // Возвращаем текст кнопки через 2 секунды
            var dispatcher = Dispatcher;
            Task.Delay(2000).ContinueWith(_ =>
            {
                dispatcher.Invoke(() =>
                {
                    CopyAddressButton.Content = LocalizationManager.Get("ServerDetail_Upnp_CopyAddress");
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to copy address: {ex.Message}", "ServerDetailPage");
        }
    }

    /// <summary>
    /// Проверка UPnP доступности
    /// </summary>
    private async void CheckUpnpButton_Click(object sender, RoutedEventArgs e)
    {
        // Скрываем предыдущий результат перед новой проверкой
        UpnpCheckResultText.Visibility = Visibility.Collapsed;
        UpnpCheckResultIcon.Visibility = Visibility.Collapsed;

        try
        {
            CheckUpnpProgress.Visibility = Visibility.Visible;
            CheckUpnpButton.IsEnabled = false;
            CheckUpnpText.Text = LocalizationManager.Get("ServerDetail_Upnp_Checking");

            var isAvailable = await _viewModel.CheckUpnpAvailabilityAsync();

            CheckUpnpProgress.Visibility = Visibility.Collapsed;
            CheckUpnpButton.IsEnabled = true;
            CheckUpnpText.Text = LocalizationManager.Get("ServerDetail_Upnp_Check");

            if (isAvailable)
            {
                UpnpCheckResultText.Text = LocalizationManager.Get("ServerDetail_Upnp_Available");
                UpnpCheckResultText.Foreground = SuccessBrush;
            }
            else
            {
                UpnpCheckResultText.Text = LocalizationManager.Get("ServerDetail_Upnp_NotAvailable");
                UpnpCheckResultText.Foreground = WarningBrush;
            }

            UpnpCheckResultText.Visibility = Visibility.Visible;
            UpnpCheckResultIcon.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Error($"UPnP check error: {ex.Message}", ex, "ServerDetailPage");
            CheckUpnpProgress.Visibility = Visibility.Collapsed;
            CheckUpnpButton.IsEnabled = true;
            CheckUpnpText.Text = LocalizationManager.Get("ServerDetail_Upnp_Check");

            UpnpCheckResultText.Visibility = Visibility.Collapsed;
            UpnpCheckResultIcon.Symbol = SymbolRegular.ErrorCircle24;
            UpnpCheckResultIcon.ToolTip = new ToolTip
            {
                Content = $"UPnP: {ex.Message}",
                FontSize = 14
            };
            UpnpCheckResultIcon.Foreground = ErrorBrush;
            UpnpCheckResultIcon.Visibility = Visibility.Visible;
        }
        finally
        {
            _ = ResetCheckUpnpResultAsync();
        }
    }

    /// <summary>
    /// Сбрасывает результат проверки UPnP через 5 секунд.
    /// </summary>
    private async Task ResetCheckUpnpResultAsync()
    {
        try
        {
            await Task.Delay(5000);
            Dispatcher.Invoke(() =>
            {
                UpnpCheckResultText.Visibility = Visibility.Collapsed;
                UpnpCheckResultText.Text = string.Empty;
                UpnpCheckResultText.Foreground = DefaultBrush;
            });
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Проверка проброса порта через UPnP. Результат показывается слева от кнопок.
    /// </summary>
    private async void CheckPortButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        // Скрываем предыдущий результат перед новой проверкой
        UpnpCheckResultText.Visibility = Visibility.Collapsed;
        UpnpCheckResultIcon.Visibility = Visibility.Collapsed;

        try
        {
            CheckPortProgress.Visibility = Visibility.Visible;
            CheckPortButton.IsEnabled = false;
            CheckPortText.Text = LocalizationManager.Get("ServerDetail_Upnp_Checking");

            var port = _server.Port;
            var isForwarded = await _viewModel.CheckPortMappingAsync(port);

            CheckPortProgress.Visibility = Visibility.Collapsed;
            CheckPortButton.IsEnabled = true;
            CheckPortText.Text = LocalizationManager.Get("ServerDetail_Upnp_CheckPort");

            if (isForwarded)
            {
                UpnpCheckResultText.Text = LocalizationManager.Get("ServerDetail_Upnp_Port_Open");
                UpnpCheckResultText.Foreground = SuccessBrush;
                UpdateServerAddressDisplay();
            }
            else
            {
                UpnpCheckResultText.Text = LocalizationManager.Get("ServerDetail_Upnp_Port_Closed");
                UpnpCheckResultText.Foreground = ErrorBrush;

                // Скрываем адрес, если порт закрыт
                UpnpAddressText.Visibility = Visibility.Collapsed;
                CopyAddressButton.Visibility = Visibility.Collapsed;
            }

            UpnpCheckResultText.Visibility = Visibility.Visible;
            UpnpCheckResultIcon.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Error($"UPnP port check error: {ex.Message}", ex, "ServerDetailPage");
            CheckPortProgress.Visibility = Visibility.Collapsed;
            CheckPortButton.IsEnabled = true;
            CheckPortText.Text = LocalizationManager.Get("ServerDetail_Upnp_CheckPort");

            UpnpCheckResultText.Visibility = Visibility.Collapsed;
            UpnpCheckResultIcon.Symbol = SymbolRegular.ErrorCircle24;
            UpnpCheckResultIcon.ToolTip = new ToolTip
            {
                Content = $"UPnP: {ex.Message}",
                FontSize = 14
            };
            UpnpCheckResultIcon.Foreground = ErrorBrush;
            UpnpCheckResultIcon.Visibility = Visibility.Visible;
        }
        finally
        {
            _ = ResetCheckPortResultAsync();
        }
    }

    /// <summary>
    /// Сбрасывает результат проверки порта через 5 секунд.
    /// </summary>
    private async Task ResetCheckPortResultAsync()
    {
        try
        {
            await Task.Delay(5000);
            Dispatcher.Invoke(() =>
            {
                UpnpCheckResultText.Visibility = Visibility.Collapsed;
                UpnpCheckResultText.Text = string.Empty;
                UpnpCheckResultText.Foreground = DefaultBrush;
            });
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Обработка выбора Java в ComboBox (автосохранение)
    /// </summary>
    private void SettingJavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    /// <summary>
    /// Сохранение JVM аргументов при потере фокуса
    /// </summary>
    private void SettingJvmArgs_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

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

    private void LoadMods()
    {
        if (_server == null)
            return;

        _viewModel.LoadMods();

        ModsList.ItemsSource = _viewModel.Mods;
        ModsCountBadge.Value = _viewModel.ModsCount;
        ModsCountBadge.Visibility = _viewModel.Mods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleAllModsButton();
    }

    private void LoadPlugins()
    {
        if (_server == null)
            return;

        _viewModel.LoadPlugins();

        PluginsList.ItemsSource = _viewModel.Plugins;
        PluginsCountBadge.Value = _viewModel.PluginsCount;
        PluginsCountBadge.Visibility = _viewModel.Plugins.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateToggleAllPluginsButton();
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => LoadMods();
    private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => LoadPlugins();

    /// <summary>
    /// Обновить состояние кнопки «Отключить/Включить все моды»
    /// </summary>
    private void UpdateToggleAllModsButton()
    {
        if (_viewModel.Mods.Count == 0)
        {
            ToggleAllModsBtn.Visibility = Visibility.Collapsed;
            return;
        }

        ToggleAllModsBtn.Visibility = Visibility.Visible;
        var allDisabled = _viewModel.CheckAllModsDisabled();
        if (allDisabled)
        {
            // Все выключены — кнопка «Включить все»
            ToggleAllModsBtn.Content = LocalizationManager.Get("ServerDetail_Mods_ToggleAll_Enable");
        }
        else
        {
            // Есть включённые (все или часть) — кнопка «Отключить все»
            ToggleAllModsBtn.Content = LocalizationManager.Get("ServerDetail_Mods_ToggleAll_Disable");
        }
    }

    /// <summary>
    /// Обновить состояние кнопки «Отключить/Включить все плагины»
    /// </summary>
    private void UpdateToggleAllPluginsButton()
    {
        if (_viewModel.Plugins.Count == 0)
        {
            ToggleAllPluginsBtn.Visibility = Visibility.Collapsed;
            return;
        }

        ToggleAllPluginsBtn.Visibility = Visibility.Visible;
        var allDisabled = _viewModel.CheckAllPluginsDisabled();
        if (allDisabled)
        {
            ToggleAllPluginsBtn.Content = LocalizationManager.Get("ServerDetail_Plugins_ToggleAll_Enable");
        }
        else
        {
            ToggleAllPluginsBtn.Content = LocalizationManager.Get("ServerDetail_Plugins_ToggleAll_Disable");
        }
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var modsDir = Path.Combine(_server.Path, "mods");
        if (Directory.Exists(modsDir))
        {
            UiHelper.OpenFolder(modsDir);
        }
        else
        {
            ShowWarningSafe(LocalizationManager.Get("ServerDetail_ModsFolderNotFound"));
        }
    }

    private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var pluginsDir = Path.Combine(_server.Path, "plugins");
        if (Directory.Exists(pluginsDir))
        {
            UiHelper.OpenFolder(pluginsDir);
        }
        else
        {
            ShowWarningSafe(LocalizationManager.Get("ServerDetail_PluginsFolderNotFound"));
        }
    }

    /// <summary>
    /// Открыть контекстное меню мода (три точки)
    /// </summary>
    private void ModMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not ModItem mod)
            return;

        var contextMenu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            MinWidth = btn.ActualWidth
        };

        BuildModToggleMenuItem(contextMenu, mod);
        contextMenu.Items.Add(new Separator());
        BuildDeleteModMenuItem(contextMenu, mod);

        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// Открыть контекстное меню плагина (три точки)
    /// </summary>
    private void PluginMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not PluginItem plugin)
            return;

        var contextMenu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            MinWidth = btn.ActualWidth
        };

        BuildPluginToggleMenuItem(contextMenu, plugin);
        contextMenu.Items.Add(new Separator());
        BuildDeletePluginMenuItem(contextMenu, plugin);

        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// Динамическое построение контекстного меню мода (правый клик)
    /// </summary>
    private void ModCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not CardControl card || card.DataContext is not ModItem mod)
            return;

        // Перестраиваем контекстное меню динамически
        card.ContextMenu = new ContextMenu();
        BuildModToggleMenuItem(card.ContextMenu, mod);
        card.ContextMenu.Items.Add(new Separator());
        BuildDeleteModMenuItem(card.ContextMenu, mod);
    }

    /// <summary>
    /// Динамическое построение контекстного меню плагина (правый клик)
    /// </summary>
    private void PluginCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not CardControl card || card.DataContext is not PluginItem plugin)
            return;

        // Перестраиваем контекстное меню динамически
        card.ContextMenu = new ContextMenu();
        BuildPluginToggleMenuItem(card.ContextMenu, plugin);
        card.ContextMenu.Items.Add(new Separator());
        BuildDeletePluginMenuItem(card.ContextMenu, plugin);
    }

    /// <summary>
    /// Создаёт пункт меню переключения мода и добавляет его в указанное меню
    /// </summary>
    private void BuildModToggleMenuItem(ItemsControl menu, ModItem mod)
    {
        var isEnabled = mod.Enabled;
        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get(isEnabled ? "ServerDetail_Mods_Disable" : "ServerDetail_Mods_Enable"),
            Tag = mod
        };
        var toggleIcon = new SymbolIcon
        {
            FontSize = 16,
            Symbol = isEnabled ? SymbolRegular.CheckboxChecked20 : SymbolRegular.CheckboxUnchecked20
        };
        if (isEnabled)
        {
            toggleIcon.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
        }
        toggleItem.Icon = toggleIcon;
        toggleItem.Click += ToggleMod_Click;
        menu.Items.Add(toggleItem);
    }

    /// <summary>
    /// Создаёт пункт меню удаления мода и добавляет его в указанное меню
    /// </summary>
    private void BuildDeleteModMenuItem(ItemsControl menu, ModItem mod)
    {
        var deleteItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get("ServerDetail_Mods_Delete"),
            Tag = mod
        };
        deleteItem.Icon = new SymbolIcon
        {
            Symbol = SymbolRegular.Delete20,
            Foreground = (Brush)FindResource("SystemFillColorCriticalBrush")
        };
        deleteItem.Click += DeleteMod_Click;
        menu.Items.Add(deleteItem);
    }

    /// <summary>
    /// Создаёт пункт меню переключения плагина и добавляет его в указанное меню
    /// </summary>
    private void BuildPluginToggleMenuItem(ItemsControl menu, PluginItem plugin)
    {
        var isEnabled = plugin.Enabled;
        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get(isEnabled ? "ServerDetail_Plugins_Disable" : "ServerDetail_Plugins_Enable"),
            Tag = plugin
        };
        var toggleIcon = new SymbolIcon
        {
            Symbol = isEnabled ? SymbolRegular.CheckboxChecked20 : SymbolRegular.CheckboxUnchecked20
        };
        if (isEnabled)
        {
            toggleIcon.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
        }
        toggleItem.Icon = toggleIcon;
        toggleItem.Click += TogglePlugin_Click;
        menu.Items.Add(toggleItem);
    }

    /// <summary>
    /// Создаёт пункт меню удаления плагина и добавляет его в указанное меню
    /// </summary>
    private void BuildDeletePluginMenuItem(ItemsControl menu, PluginItem plugin)
    {
        var deleteItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get("ServerDetail_Plugins_Delete"),
            Tag = plugin
        };
        deleteItem.Icon = new SymbolIcon
        {
            FontSize = 16,
            Foreground = (Brush)FindResource("SystemFillColorCriticalBrush")
        };
        deleteItem.Click += DeletePlugin_Click;
        menu.Items.Add(deleteItem);
    }

    /// <summary>
    /// Переключить состояние одного мода (вкл/выкл) через переименование файла
    /// </summary>
    private async void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        ModItem? mod = null;
        if (sender is WpfButton btn && btn.Tag is ModItem bt)
            mod = bt;
        else if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is ModItem mt)
            mod = mt;

        if (mod == null) return;

        await ToggleModItem(mod);
    }

    /// <summary>
    /// Переключить состояние одного плагина (вкл/выкл) через переименование файла
    /// </summary>
    private async void TogglePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        PluginItem? plugin = null;
        if (sender is WpfButton btn && btn.Tag is PluginItem bt)
            plugin = bt;
        else if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is PluginItem mt)
            plugin = mt;

        if (plugin == null) return;

        await TogglePluginItem(plugin);
    }

    private async Task ToggleModItem(ModItem mod)
    {
        await _viewModel.ToggleModAsync(mod);
        LoadMods();
    }

    private async Task TogglePluginItem(PluginItem plugin)
    {
        await _viewModel.TogglePluginAsync(plugin);
        LoadPlugins();
    }

    /// <summary>
    /// Включить/отключить все моды
    /// </summary>
    private async void ToggleAllMods_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        _viewModel.ToggleAllMods();
        LoadMods();
    }

    /// <summary>
    /// Включить/отключить все плагины
    /// </summary>
    private async void ToggleAllPlugins_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        _viewModel.ToggleAllPlugins();
        LoadPlugins();
    }

    private async void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        ModItem? mod = null;
        if (sender is WpfButton btn && btn.Tag is ModItem bt)
            mod = bt;
        else if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is ModItem mt)
            mod = mt;

        if (mod == null) return;

        var result = await UiHelper.ShowConfirm(
            string.Format(LocalizationManager.Get("ServerDetail_DeleteModConfirm"), mod.Name, mod.FileName),
            LocalizationManager.Get("ServerDetail_DeleteModTitle"));

        if (result != ContentDialogResult.Primary)
            return;

        await _viewModel.DeleteModAsync(mod);
        LoadMods();
    }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        PluginItem? plugin = null;
        if (sender is WpfButton btn && btn.Tag is PluginItem bt)
            plugin = bt;
        else if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is PluginItem mt)
            plugin = mt;

        if (plugin == null) return;

        var result = await UiHelper.ShowConfirm(
            string.Format(LocalizationManager.Get("ServerDetail_DeletePluginConfirm"), plugin.Name, plugin.FileName),
            LocalizationManager.Get("ServerDetail_DeletePluginTitle"));

        if (result != ContentDialogResult.Primary)
            return;

        await _viewModel.DeletePluginAsync(plugin);
        LoadPlugins();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(_server.Name);

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

    /// <summary>
    /// Безопасный вызов ShowWarning из sync-контекста (fire-and-forget с try/catch)
    /// </summary>
    private async void ShowWarningSafe(string message)
    {
        try { await UiHelper.ShowWarning(message); }
        catch (Exception ex) { Logger.Warning($"[ShowWarningSafe] Error: {ex.Message}", "ServerDetailPage"); }
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

        _disposed = true;
    }
}