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

        StartStatusTimer();
        LoadServer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от события ошибки запуска
        MainWindow.ServerManager.OnServerStartError -= OnServerStartError;
        StopStatusTimer();
        Dispose();
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

        // Если сервер в состоянии ошибки — показываем диалог (только один раз)
        if (_server.Status == ServerStatus.Error && !string.IsNullOrEmpty(_server.InstallStatus) && !_server.ErrorDialogShown)
        {
            Logger.Info($"[LoadServer] Server in Error status with message: {_server.InstallStatus}", "ServerDetailPage");
            _server.ErrorDialogShown = true;
            // Показываем ошибку после полной загрузки UI
            this.Invoke(() =>
            {
                _ = ShowJavaErrorDialog(_server.InstallStatus);
            });
        }
        else
        {
            Logger.Info($"[LoadServer] Server status is {_server.Status}, InstallStatus='{_server.InstallStatus}', DialogShown={_server.ErrorDialogShown}", "ServerDetailPage");
        }

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
            if (logs.Count > 0)
            {
                var text = string.Join("\n", logs);
                document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(text)));
                ConsolePlaceholder.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                ConsolePlaceholder.Visibility = System.Windows.Visibility.Visible;
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

        // Проверяем, не показан ли уже диалог
        if (_server != null && _server.ErrorDialogShown)
        {
            Logger.Info($"[OnServerStartError] Error dialog already shown for server {server.Name}", "ServerDetailPage");
            return;
        }

        Logger.Info($"[OnServerStartError] Received error for server {server.Name}: {errorMessage}", "ServerDetailPage");

        // Помечаем, что диалог показан
        if (_server != null)
            _server.ErrorDialogShown = true;

        // Показываем диалог в UI потоке
        this.Invoke(() =>
        {
            _ = ShowJavaErrorDialog(errorMessage);
        });
    }

    /// <summary>
    /// Показывает диалог с ошибкой Java
    /// </summary>
    private async Task ShowJavaErrorDialog(string errorMessage)
    {
        Logger.Info($"[ShowJavaErrorDialog] Showing dialog with message: {errorMessage}", "ServerDetailPage");

        // Проверяем, связана ли ошибка с Java
        bool isJavaError = errorMessage.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
                          errorMessage.Contains("java", StringComparison.OrdinalIgnoreCase);

        Logger.Info($"[ShowJavaErrorDialog] Is Java error: {isJavaError}", "ServerDetailPage");

        if (!isJavaError)
        {
            // Не Java ошибка - показываем обычное сообщение
            Logger.Info($"[ShowJavaErrorDialog] Showing regular error dialog", "ServerDetailPage");
            await UiHelper.ShowError(errorMessage);
            return;
        }

        // Java ошибка - показываем подробное сообщение с кнопкой
        Logger.Info($"[ShowJavaErrorDialog] Showing Java error dialog", "ServerDetailPage");
        
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "⚠️ Ошибка Java",
            Content = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text = errorMessage,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 16)
                    },
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12),
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = "Требуется установить или обновить Java",
                                    FontWeight = FontWeights.Bold,
                                    Margin = new Thickness(0, 0, 0, 8)
                                },
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = "Скачайте последнюю версию Java с официального сайта:",
                                    Margin = new Thickness(0, 0, 0, 8)
                                },
                                new System.Windows.Controls.Button
                                {
                                    Content = "📥 Скачать Java (adoptium.net)",
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    Padding = new Thickness(16, 8, 16, 8),
                                    Cursor = System.Windows.Input.Cursors.Hand
                                }
                            }
                        }
                    }
                }
            },
            PrimaryButtonText = "OK",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Info24),
            ShowTitle = true,
            Padding = new Thickness(16)
        };

        // Обработчик нажатия на кнопку
        var downloadButton = ((StackPanel)((Border)((StackPanel)dialog.Content).Children[1]).Child).Children[2] as System.Windows.Controls.Button;
        if (downloadButton != null)
        {
            downloadButton.Click += (s, e) =>
            {
                // Открываем сайт Adoptium в браузере
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = "https://adoptium.net/",
                    UseShellExecute = true
                });
            };
        }

        await dialog.ShowDialogAsync();
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
                MainWindow.ServerManager.StartServer(_serverId!);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"StartStop_Click: Error managing server {_server.Name}: {ex.Message}", ex, "ServerDetailPage");
            await UiHelper.ShowError($"Не удалось выполнить операцию: {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }

        // Статус обновится через событие OnStatusChanged в McServerManager
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        try
        {
            if (Directory.Exists(_server.Path))
            {
                Process.Start("explorer.exe", _server.Path);
            }
        }
        catch
        {
            await UiHelper.ShowWarning("Не удалось открыть папку сервера");
        }
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
            Content = $"По умолчанию ({(defaultJava != null ? defaultJava.DisplayName : "не выбрана")})",
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
            _ = UiHelper.ShowError($"Ошибка загрузки свойств: {ex.Message}");
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
            _ = UiHelper.ShowError($"Ошибка загрузки модов: {ex.Message}");
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
            _ = UiHelper.ShowError($"Ошибка загрузки плагинов: {ex.Message}");
        }
    }

    private static string ParseModVersion(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        return parts.Length > 1 ? parts[^1] : "Неизвестно";
    }

    private static string ParsePluginVersion(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        return parts.Length > 1 ? parts[^1] : "Неизвестно";
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => LoadMods();
    private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => LoadPlugins();

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        try
        {
            var modsDir = Path.Combine(_server.Path, "mods");
            if (Directory.Exists(modsDir))
            {
                Process.Start("explorer.exe", modsDir);
            }
            else
            {
                _ = UiHelper.ShowWarning("Папка mods не найдена");
            }
        }
        catch
        {
            _ = UiHelper.ShowWarning("Не удалось открыть папку");
        }
    }

    private async void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        try
        {
            var pluginsDir = Path.Combine(_server.Path, "plugins");
            if (Directory.Exists(pluginsDir))
            {
                Process.Start("explorer.exe", pluginsDir);
            }
            else
            {
                _ = UiHelper.ShowWarning("Папка plugins не найдена");
            }
        }
        catch
        {
            _ = UiHelper.ShowWarning("Не удалось открыть папку");
        }
    }

    private async void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not ModItem mod)
            return;

        var result = await UiHelper.ShowConfirm(
            $"Удалить мод \"{mod.Name}\"?\n\nФайл: {mod.FileName}",
            "Удаление мода");

        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
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
            await UiHelper.ShowError($"Ошибка удаления мода: {ex.Message}");
        }
    }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not PluginItem plugin)
            return;

        var result = await UiHelper.ShowConfirm(
            $"Удалить плагин \"{plugin.Name}\"?\n\nФайл: {plugin.FileName}",
            "Удаление плагина");

        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
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
            await UiHelper.ShowError($"Ошибка удаления плагина: {ex.Message}");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(_server.Name, _server.Path);

        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        try
        {
            await MainWindow.ServerManager.DeleteServerAsync(_serverId!);
            Back_Click(sender, e);
        }
        catch (Exception ex)
        {
            await UiHelper.ShowError($"Ошибка при удалении сервера: {ex.Message}");
        }
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