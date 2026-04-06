using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Konserva.Pages;

/// <summary>
/// Страница деталей сервера - консоль, моды, плагины, настройки, удаление
/// </summary>
public partial class ServerDetailPage : Page, IDisposable
{
    private string? _serverId;
    private Server? _server;
    private McServerProcess? _process;
    private bool _disposed;
    private bool _isBusy;
    private CancellationTokenSource? _statusCts;

    /// <summary>
    /// Конструктор для навигации через NavigationView (с параметром serverId)
    /// </summary>
    public ServerDetailPage()
    {
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
        MainWindow.ServerManager.OnServerStartError += OnServerStartError;

        // Обновляем PageWidth лога при изменении размера
        LogBox.SizeChanged += LogBox_SizeChanged;

        StartStatusTimer();
        LoadServer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от события ошибки запуска
        MainWindow.ServerManager.OnServerStartError -= OnServerStartError;
        LogBox.SizeChanged -= LogBox_SizeChanged;
        StopStatusTimer();
        Dispose();
    }

    private double _maxContentWidth = 0;

    private void LogBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // При ресайзе: если контент уже шире новой ширины — не трогаем PageWidth
        // Если контент уже (или пустой) — ставим PageWidth = ActualWidth
        if (LogDocument != null && e.NewSize.Width > 0)
        {
            if (_maxContentWidth <= e.NewSize.Width)
            {
                // Контент помещается — скролл скрыт
                LogDocument.PageWidth = e.NewSize.Width;
            }
            // Иначе: контент шире — оставляем как есть, скролл виден
        }
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
            if (_maxContentWidth > LogBox.ActualWidth)
            {
                LogDocument.PageWidth = _maxContentWidth;
            }
            else
            {
                // Всё помещается — скролл скрыт
                LogDocument.PageWidth = LogBox.ActualWidth;
            }
        }
        catch { }
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

        _server = MainWindow.ServerManager.GetServer(_serverId!);
        _process = MainWindow.ServerManager.GetProcess(_serverId!);

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

        UpdateStatus(_server.Status);

        // Выделяем первую кнопку (Консоль) при загрузке
        ResetNavigationButtons();
        if (ConsoleNavButton != null)
        {
            ConsoleNavButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as System.Windows.Media.Brush;
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
                var text = string.Join("\n", logs);
                document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(text)));
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

    private async void UpdateLog(string line)
    {
        await this.InvokeAsync(() =>
        {
            var document = LogBox.Document;
            if (document.Blocks.LastBlock is System.Windows.Documents.Paragraph lastParagraph)
            {
                lastParagraph.Inlines.Add(new System.Windows.Documents.Run(line + "\n"));
            }
            else
            {
                document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(line + "\n")));
            }
            ConsolePlaceholder.Visibility = System.Windows.Visibility.Collapsed;
            UpdatePageWidthForText(line);
            LogBox.ScrollToEnd();
        });
    }

    private async void UpdateStatus(ServerStatus status)
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
                _process = MainWindow.ServerManager.GetProcess(_serverId!);

                if (_process != null)
                {
                    // Загружаем существующие логи
                    LoadExistingLogs();

                    // Подписываемся на события
                    _process.OnLog += UpdateLog;
                    _process.OnStatusChanged += UpdateStatus;
                    _process.OnPlayersChanged += UpdatePlayers;
                }
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

    private async void UpdatePlayers(int players)
    {
        await this.InvokeAsync(() =>
        {
            // Здесь обновляем UI с количеством игроков
        });
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
            MainWindow.Instance?.ContentFrame?.Navigate(new Pages.ServersPage());
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

        // Помечаем, что Snackbar показан
        if (_server != null)
            _server.ErrorDialogShown = true;

        // Показываем Snackbar на UI потоке MainWindow
        MainWindow.Instance?.Dispatcher.Invoke(() =>
        {
            ShowJavaErrorSnackbar(errorMessage);
        });
    }

    /// <summary>
    /// Показывает Snackbar с ошибкой несовместимости Java.
    /// </summary>
    private void ShowJavaErrorSnackbar(string errorMessage)
    {
        if (_server == null)
            return;

        bool isJavaError = errorMessage.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
                          errorMessage.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                          errorMessage.Contains("Требуется Java", StringComparison.OrdinalIgnoreCase);

        if (!isJavaError)
        {
            MainWindow.Instance?.Dispatcher.Invoke(async () => await UiHelper.ShowError(errorMessage));
            return;
        }

        var requiredVersion = JavaVersionParser.ParseRequiredJavaVersion(errorMessage);
        var foundVersion = JavaVersionParser.ParseFoundJavaVersion(errorMessage);
        var javaPath = JavaVersionParser.ParseJavaPath(errorMessage);

        // Получаем все установленные Java
        var allJava = App.ConfigService?.GetConfig().JavaInstallations.Where(j => j.Exists).ToList();

        MainWindow.Instance?.ShowJavaErrorSnackbar(_server, errorMessage, requiredVersion, foundVersion, allJava);
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
                MainWindow.ServerManager.StopServer(_serverId!);
            }
            else
            {
                // Сбрасываем флаг ошибки перед новым запуском
                _server.ErrorDialogShown = false;
                MainWindow.ServerManager.StartServer(_serverId!);
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
        navButton.Background = TryFindResource("ControlFillColorSecondaryBrush") as System.Windows.Media.Brush;

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
        ConsoleNavButton.Background = System.Windows.Media.Brushes.Transparent;
        ModsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        PluginsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        PropertiesNavButton.Background = System.Windows.Media.Brushes.Transparent;
        SettingsNavButton.Background = System.Windows.Media.Brushes.Transparent;

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

        MainWindow.ServerManager.SendCommand(_serverId!, CommandBox.Text);
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
        var config = App.ConfigService.GetConfig();

        SettingJavaComboBox.Items.Clear();

        // Добавляем пункт "По умолчанию"
        var defaultJava = config.GetDefaultJava();
        SettingJavaComboBox.Items.Add(new ComboBoxItem
        {
            Content = $"{LocalizationManager.Get("ServerDetail_JavaDefault")} ({(defaultJava != null ? defaultJava.DisplayName : LocalizationManager.Get("ServerDetail_JavaNotSelected"))})",
            Tag = (string?)null
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

        // Обновляем сервер в хранилище
        MainWindow.ServerManager.UpdateServer(_server);
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
                return;
            }

            var mods = Directory.GetFiles(modsDir, "*.jar")
                .Select(path =>
                {
                    var fileName = Path.GetFileName(path);
                    var version = ParseModVersion(fileName);
                    return new ModItem
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        Version = version,
                        FileName = fileName,
                        FilePath = path,
                        FileSize = new FileInfo(path).Length
                    };
                })
                .OrderBy(m => m.Name)
                .ToList();

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
        }
        catch (Exception ex)
        {
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
                return;
            }

            var plugins = Directory.GetFiles(pluginsDir, "*.jar")
                .Select(path =>
                {
                    var fileName = Path.GetFileName(path);
                    var version = ParsePluginVersion(fileName);
                    return new PluginItem
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        Version = version,
                        FileName = fileName,
                        FilePath = path,
                        FileSize = new FileInfo(path).Length
                    };
                })
                .OrderBy(p => p.Name)
                .ToList();

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
        }
        catch (Exception ex)
        {
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

    private async void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not ModItem mod)
            return;

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
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_ModDeleteError")}: {ex.Message}");
        }
    }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not PluginItem plugin)
            return;

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
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_PluginDeleteError")}: {ex.Message}");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(_server.Name, _server.Path);

        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            await MainWindow.ServerManager.DeleteServerAsync(_serverId!);
            Back_Click(sender, e);
        }
        catch (Exception ex)
        {
            await UiHelper.ShowError($"{LocalizationManager.Get("ServerDetail_DeleteServerError")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Безопасный вызов ShowError из sync-контекста (fire-and-forget с try/catch)
    /// </summary>
    private async void ShowErrorSafe(string message)
    {
        try { await UiHelper.ShowError(message); }
        catch { /* Игнорируем — диалог уже не критичен */ }
    }

    /// <summary>
    /// Безопасный вызов ShowWarning из sync-контекста (fire-and-forget с try/catch)
    /// </summary>
    private async void ShowWarningSafe(string message)
    {
        try { await UiHelper.ShowWarning(message); }
        catch { /* Игнорируем */ }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Отписываемся от событий процесса
        UnsubscribeFromProcess();

        StopStatusTimer();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}