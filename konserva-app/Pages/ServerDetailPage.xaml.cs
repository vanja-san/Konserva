using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfMenuItem = Wpf.Ui.Controls.MenuItem;

namespace Konserva.Pages;

/// <summary>
/// Страница деталей сервера - консоль, моды, плагины, настройки, удаление
/// </summary>
public partial class ServerDetailPage : Page, IDisposable
{
    private readonly IConfigService? _configService;
    private string? _serverId;
    private Server? _server;
    private McServerProcess? _process;
    private bool _disposed;
    private bool _isBusy;
    private CancellationTokenSource? _statusCts;

    /// <summary>
    /// Конструктор для навигации через NavigationView (с параметром serverId)
    /// </summary>
    public ServerDetailPage(IConfigService? configService = null)
    {
        _configService = configService;
        InitializeComponent();

        // Подписываемся после InitializeComponent чтобы избежать NullReferenceException
        SettingJavaAutoSelect.Checked += SettingJavaAutoSelect_CheckedChanged;
        SettingJavaAutoSelect.Unchecked += SettingJavaAutoSelect_CheckedChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Конструктор для прямой инициализации с serverId
    /// </summary>
    public ServerDetailPage(string serverId) : this()
    {
        _serverId = serverId;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Получаем serverId из DataContext если не был установлен через конструктор
        if (_serverId == null && DataContext is string dataContextId)
        {
            _serverId = dataContextId;
        }

        // Подписываемся на событие ошибки запуска
        App.ServerManager.OnServerStartError += OnServerStartError;

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
        App.ServerManager.OnServerStartError -= OnServerStartError;
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
        if (_server.Port != newPort)
        {
            _server.Port = newPort;
            App.ServerManager.UpdateServer(_server);
        }
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

        _server = App.ServerManager.GetServer(_serverId!);
        _process = App.ServerManager.GetProcess(_serverId!);

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
        ServerNameText.Text = _server.Name;
        ServerInfoText.Text = _server.Description;

        // Настройки
        SettingName.Text = _server.Name;
        SettingRamMin.Text = _server.Settings.RamMin.ToString();
        SettingRamMax.Text = _server.Settings.RamMax.ToString();
        SettingAutoRestart.IsChecked = _server.Settings.AutoRestart;
        SettingAutoRestartDelay.Text = _server.Settings.AutoRestartDelay.ToString();

        // Настройки Java
        SettingJavaAutoSelect.IsChecked = _server.Settings.JavaAutoSelect;
        LoadJavaComboBox();
        UpdateJavaComboBoxVisibility();

        // JVM аргументы
        SettingJvmArgs.Text = _server.Settings.JvmArgsText;

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
        else if (line.Contains("[SUCCESS]"))
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
                    _process = App.ServerManager.GetProcess(_serverId!);

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
                // Обновляем иконку и ToolTip кнопки старт/стоп
                StartStopIcon.Symbol = status switch
                {
                    ServerStatus.Running => Wpf.Ui.Controls.SymbolRegular.Stop20,
                    ServerStatus.Starting => Wpf.Ui.Controls.SymbolRegular.Stop20,
                    ServerStatus.Stopping => Wpf.Ui.Controls.SymbolRegular.Stop20,
                    _ => Wpf.Ui.Controls.SymbolRegular.Play20
                };

                StartStopButton.ToolTip = status switch
                {
                    ServerStatus.Running => LocalizationManager.Get("ServerDetail_Stop"),
                    ServerStatus.Starting => LocalizationManager.Get("ServerDetail_Starting"),
                    ServerStatus.Stopping => LocalizationManager.Get("ServerDetail_Stopping"),
                    _ => LocalizationManager.Get("ServerDetail_Start")
                };

                StartStopButton.IsEnabled = status is not (ServerStatus.Starting or ServerStatus.Stopping);
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
            await this.InvokeAsync(() =>
            {
                // Здесь обновляем UI с количеством игроков
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
            App.MainWindow?.ContentFrame?.Navigate(new Pages.ServersPage());
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
            if (_server.IsRunning)
            {
                App.ServerManager.StopServer(_serverId!);
            }
            else
            {
                // Сбрасываем флаг ошибки перед новым запуском
                _server.ErrorDialogShown = false;
                App.ServerManager.StartServer(_serverId!);
            }
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

        // Статус обновится через событие OnStatusChanged в McServerManager
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

        App.ServerManager.SendCommand(_serverId!, CommandBox.Text);
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
        var config = _configService?.GetConfig() ?? App.ConfigService.GetConfig();

        SettingJavaComboBox.Items.Clear();

        // Добавляем пункт "По умолчанию"
        var defaultJava = config.GetDefaultJava();
        SettingJavaComboBox.Items.Add(new ComboBoxItem
        {
            Content = $"{LocalizationManager.Get("ServerDetail_JavaDefault")} ({(defaultJava != null ? defaultJava.DisplayName : LocalizationManager.Get("ServerDetail_JavaNotSelected"))})",
            Tag = null
        });

        // Добавляем все установленные Java
        foreach (var java in config.JavaInstallations.Where(j => j.Exists))
        {
            SettingJavaComboBox.Items.Add(new ComboBoxItem
            {
                Content = java.DisplayName,
                Tag = java.Id
            });
        }

        // Выбираем сохранённую Java сервера
        if (!string.IsNullOrEmpty(_server?.Settings.JavaId))
        {
            var selectedItem = SettingJavaComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => (string?)item.Tag == _server.Settings.JavaId);

            if (selectedItem != null)
                SettingJavaComboBox.SelectedItem = selectedItem;
        }
        else
        {
            SettingJavaComboBox.SelectedIndex = 0; // "По умолчанию"
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

        // Сохраняем название
        var newName = SettingName.Text.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _server.Name)
        {
            _server.Name = newName;
            ServerNameText.Text = newName;
        }

        // Сохраняем RAM
        if (int.TryParse(SettingRamMin.Text, out var ramMin) && ramMin >= Constants.MinRamMb && ramMin != _server.Settings.RamMin)
        {
            _server.Settings.RamMin = ramMin;
        }

        if (int.TryParse(SettingRamMax.Text, out var ramMax) && ramMax >= ramMin && ramMax != _server.Settings.RamMax)
        {
            _server.Settings.RamMax = ramMax;
        }

        // Сохраняем авто-рестарт
        var autoRestart = SettingAutoRestart.IsChecked ?? false;
        if (autoRestart != _server.Settings.AutoRestart)
        {
            _server.Settings.AutoRestart = autoRestart;
        }

        // Сохраняем задержку авто-рестарта
        if (int.TryParse(SettingAutoRestartDelay.Text, out var delay) && delay >= 0 && delay != _server.Settings.AutoRestartDelay)
        {
            _server.Settings.AutoRestartDelay = delay;
        }

        // Сохраняем настройки Java
        var javaAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
        if (javaAutoSelect != _server.Settings.JavaAutoSelect)
        {
            _server.Settings.JavaAutoSelect = javaAutoSelect;
        }

        if (!javaAutoSelect && SettingJavaComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var javaId = selectedItem.Tag as string;
            if (javaId != _server.Settings.JavaId)
            {
                _server.Settings.JavaId = javaId;
            }
        }
        else if (javaAutoSelect && _server.Settings.JavaId != null)
        {
            _server.Settings.JavaId = null;
        }

        // Сохраняем JVM аргументы
        _server.Settings.JvmArgsText = SettingJvmArgs.Text;

        // Обновляем сервер в хранилище
        App.ServerManager.UpdateServer(_server);
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

        try
        {
            var modsDir = Path.Combine(_server.Path, "mods");
            if (!Directory.Exists(modsDir))
            {
                ModsList.ItemsSource = Array.Empty<ModItem>();
                ModsCountBadge.Visibility = Visibility.Collapsed;
                UpdateToggleAllModsButton();
                return;
            }

            // Сканируем как .jar так и .jar.disabled
            var mods = new List<ModItem>();
            foreach (var pattern in new[] { "*.jar", "*.jar.disabled" })
            {
                foreach (var path in Directory.GetFiles(modsDir, pattern))
                {
                    var fileName = Path.GetFileName(path);
                    var isDisabled = fileName.EndsWith(".disabled");
                    var cleanName = isDisabled ? fileName.Replace(".jar.disabled", ".jar") : fileName;
                    var version = ParseModVersion(cleanName);
                    mods.Add(new ModItem
                    {
                        Name = Path.GetFileNameWithoutExtension(cleanName),
                        Version = version,
                        FileName = cleanName,
                        FilePath = path,
                        FileSize = new FileInfo(path).Length,
                        Enabled = !isDisabled
                    });
                }
            }

            mods = mods.OrderBy(m => m.Name).ToList();
            ModsList.ItemsSource = mods;

            // Обновляем бейдж
            if (mods.Count > 0)
            {
                ModsCountBadge.Value = mods.Count.ToString();
                ModsCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                ModsCountBadge.Visibility = Visibility.Collapsed;
            }

            UpdateToggleAllModsButton();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load mods: {ex.Message}", "ServerDetailPage");
            ShowErrorSafe($"{LocalizationManager.Get("ServerDetail_ModsLoadError")}: {ex.Message}");
        }
    }

    private void LoadPlugins()
    {
        if (_server == null)
            return;

        try
        {
            var pluginsDir = Path.Combine(_server.Path, "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                PluginsList.ItemsSource = Array.Empty<PluginItem>();
                PluginsCountBadge.Visibility = Visibility.Collapsed;
                UpdateToggleAllPluginsButton();
                return;
            }

            // Сканируем как .jar так и .jar.disabled
            var plugins = new List<PluginItem>();
            foreach (var pattern in new[] { "*.jar", "*.jar.disabled" })
            {
                foreach (var path in Directory.GetFiles(pluginsDir, pattern))
                {
                    var fileName = Path.GetFileName(path);
                    var isDisabled = fileName.EndsWith(".disabled");
                    var cleanName = isDisabled ? fileName.Replace(".jar.disabled", ".jar") : fileName;
                    var version = ParsePluginVersion(cleanName);
                    plugins.Add(new PluginItem
                    {
                        Name = Path.GetFileNameWithoutExtension(cleanName),
                        Version = version,
                        FileName = cleanName,
                        FilePath = path,
                        FileSize = new FileInfo(path).Length,
                        Enabled = !isDisabled
                    });
                }
            }

            plugins = plugins.OrderBy(p => p.Name).ToList();
            PluginsList.ItemsSource = plugins;

            // Обновляем бейдж
            if (plugins.Count > 0)
            {
                PluginsCountBadge.Value = plugins.Count.ToString();
                PluginsCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                PluginsCountBadge.Visibility = Visibility.Collapsed;
            }

            UpdateToggleAllPluginsButton();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load plugins: {ex.Message}", "ServerDetailPage");
            ShowErrorSafe($"{LocalizationManager.Get("ServerDetail_PluginsLoadError")}: {ex.Message}");
        }
    }

    private static string ParseModVersion(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        return parts.Length > 1 ? parts[^1] : LocalizationManager.Get("Common_Unknown");
    }

    private static string ParsePluginVersion(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        return parts.Length > 1 ? parts[^1] : LocalizationManager.Get("Common_Unknown");
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => LoadMods();
    private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => LoadPlugins();

    /// <summary>
    /// Обновить состояние кнопки «Отключить/Включить все моды»
    /// </summary>
    private void UpdateToggleAllModsButton()
    {
        if (ModsList.ItemsSource is not IList<ModItem> mods || mods.Count == 0)
        {
            ToggleAllModsBtn.Visibility = Visibility.Collapsed;
            return;
        }

        ToggleAllModsBtn.Visibility = Visibility.Visible;
        var allDisabled = mods.All(m => !m.Enabled);
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
        if (PluginsList.ItemsSource is not IList<PluginItem> plugins || plugins.Count == 0)
        {
            ToggleAllPluginsBtn.Visibility = Visibility.Collapsed;
            return;
        }

        ToggleAllPluginsBtn.Visibility = Visibility.Visible;
        var allDisabled = plugins.All(p => !p.Enabled);
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
        try
        {
            var disabledPath = mod.FilePath + ".disabled";
            if (mod.Enabled)
            {
                // Disable: .jar → .jar.disabled
                if (File.Exists(mod.FilePath))
                {
                    File.Move(mod.FilePath, disabledPath);
                    mod.FilePath = disabledPath;
                    mod.Enabled = false;
                }
            }
            else
            {
                // Enable: .jar.disabled → .jar
                if (File.Exists(mod.FilePath))
                {
                    var enabledPath = mod.FilePath.Replace(".jar.disabled", ".jar");
                    File.Move(mod.FilePath, enabledPath);
                    mod.FilePath = enabledPath;
                    mod.Enabled = true;
                }
            }
            LoadMods();
        }
        catch (Exception ex)
        {
            Logger.Error($"Toggle mod error: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_ModToggleError")}: {ex.Message}");
        }
    }

    private async Task TogglePluginItem(PluginItem plugin)
    {
        try
        {
            if (plugin.Enabled)
            {
                // Disable: .jar → .jar.disabled
                var disabledPath = plugin.FilePath + ".disabled";
                if (File.Exists(plugin.FilePath))
                {
                    File.Move(plugin.FilePath, disabledPath);
                    plugin.FilePath = disabledPath;
                    plugin.Enabled = false;
                }
            }
            else
            {
                // Enable: .jar.disabled → .jar
                if (File.Exists(plugin.FilePath))
                {
                    var enabledPath = plugin.FilePath.Replace(".jar.disabled", ".jar");
                    File.Move(plugin.FilePath, enabledPath);
                    plugin.FilePath = enabledPath;
                    plugin.Enabled = true;
                }
            }
            LoadPlugins();
        }
        catch (Exception ex)
        {
            Logger.Error($"Toggle plugin error: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_PluginToggleError")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Включить/отключить все моды
    /// </summary>
    private async void ToggleAllMods_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || ModsList.ItemsSource is not IList<ModItem> mods || mods.Count == 0)
            return;

        var anyEnabled = mods.Any(m => m.Enabled);
        var targetEnabled = !anyEnabled; // если хоть один включён — выключаем все, иначе включаем

        foreach (var mod in mods)
        {
            if (mod.Enabled == targetEnabled)
                continue;
            try
            {
                if (targetEnabled)
                {
                    var enabledPath = mod.FilePath.Replace(".jar.disabled", ".jar");
                    if (File.Exists(mod.FilePath))
                    {
                        File.Move(mod.FilePath, enabledPath);
                        mod.FilePath = enabledPath;
                        mod.Enabled = true;
                    }
                }
                else
                {
                    var disabledPath = mod.FilePath + ".disabled";
                    if (File.Exists(mod.FilePath))
                    {
                        File.Move(mod.FilePath, disabledPath);
                        mod.FilePath = disabledPath;
                        mod.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Toggle all mods error: {ex.Message}", ex, "ServerDetailPage");
            }
        }

        LoadMods();
    }

    /// <summary>
    /// Включить/отключить все плагины
    /// </summary>
    private async void ToggleAllPlugins_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || PluginsList.ItemsSource is not IList<PluginItem> plugins || plugins.Count == 0)
            return;

        var anyEnabled = plugins.Any(p => p.Enabled);
        var targetEnabled = !anyEnabled;

        foreach (var plugin in plugins)
        {
            if (plugin.Enabled == targetEnabled)
                continue;
            try
            {
                if (targetEnabled)
                {
                    var enabledPath = plugin.FilePath.Replace(".jar.disabled", ".jar");
                    if (File.Exists(plugin.FilePath))
                    {
                        File.Move(plugin.FilePath, enabledPath);
                        plugin.FilePath = enabledPath;
                        plugin.Enabled = true;
                    }
                }
                else
                {
                    var disabledPath = plugin.FilePath + ".disabled";
                    if (File.Exists(plugin.FilePath))
                    {
                        File.Move(plugin.FilePath, disabledPath);
                        plugin.FilePath = disabledPath;
                        plugin.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Toggle all plugins error: {ex.Message}", ex, "ServerDetailPage");
            }
        }

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

        try
        {
            if (File.Exists(mod.FilePath))
            {
                File.Delete(mod.FilePath);
                LoadMods();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Delete mod error: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_ModDeleteError")}: {ex.Message}");
        }
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

        try
        {
            if (File.Exists(plugin.FilePath))
            {
                File.Delete(plugin.FilePath);
                LoadPlugins();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Delete plugin error: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_PluginDeleteError")}: {ex.Message}");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(_server.Name);

        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            await App.ServerManager.DeleteServerAsync(_serverId!);
            Back_Click(sender, e);
        }
        catch (Exception ex)
        {
            Logger.Error($"Delete server error from detail page: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_DeleteServerError")}: {ex.Message}");
        }
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
        _disposed = true;
    }
}