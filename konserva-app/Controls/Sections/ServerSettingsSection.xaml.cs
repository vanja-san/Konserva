using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;

namespace Konserva.Controls.Sections;

/// <summary>
/// Секция настроек сервера: общие параметры, память, авто-рестарт, Java, UPnP
/// и обновление загрузчика.
/// </summary>
public partial class ServerSettingsSection : System.Windows.Controls.UserControl, IDisposable
{
    private ServerDetailViewModel _viewModel = null!;
    private readonly IModLoaderService _modLoaderService;
    private Server? _server;
    private bool _disposed;

    private CancellationTokenSource? _updateCts;
    private string? _currentLoaderVersion;
    private string? _latestLoaderVersion;
    private string[] _allLoaderVersions = [];
    private bool _updateInProgress;
    private CancellationTokenSource? _updateResultCts;

    private static readonly Brush SuccessBrush = GetThemeBrush("SystemFillColorSuccessBrush");
    private static readonly Brush WarningBrush = GetThemeBrush("SystemFillColorCautionBrush");
    private static readonly Brush ErrorBrush = GetThemeBrush("SystemFillColorCriticalBrush");
    private static readonly Brush DefaultBrush = GetThemeBrush("TextFillColorPrimaryBrush");

    /// <summary>
    /// Событие: изменено имя сервера (для обновления заголовка страницы).
    /// </summary>
    public event Action<string>? ServerRenamed;

    /// <summary>
    /// Событие: изменилась доступность обновления загрузчика.
    /// </summary>
    public event Action<bool, string?>? UpdateAvailabilityChanged;

    public ServerSettingsSection()
    {
        _modLoaderService = Ioc.Default.GetService<IModLoaderService>()!;

        InitializeComponent();

        // Подписываемся после InitializeComponent чтобы избежать NullReferenceException
        SettingJavaAutoSelect.Checked += SettingJavaAutoSelect_CheckedChanged;
        SettingJavaAutoSelect.Unchecked += SettingJavaAutoSelect_CheckedChanged;
    }

    /// <summary>
    /// Инициализация секции: привязка ViewModel и сервера, заполнение формы.
    /// </summary>
    public void Initialize(ServerDetailViewModel viewModel, Server? server)
    {
        _viewModel = viewModel;
        _server = server;
        if (_server == null)
            return;

        SettingName.Text = _viewModel.SettingsName;
        SettingRamMin.Text = _viewModel.SettingsRamMin.ToString();
        SettingRamMax.Text = _viewModel.SettingsRamMax.ToString();
        SettingAutoRestart.IsChecked = _viewModel.SettingsAutoRestart;
        SettingAutoRestartDelay.Text = _viewModel.SettingsAutoRestartDelay.ToString();

        SettingJavaAutoSelect.IsChecked = _viewModel.SettingsJavaAutoSelect;
        LoadJavaComboBox();
        UpdateJavaComboBoxVisibility();

        SettingEnableUpnp.IsChecked = _viewModel.SettingsEnableUpnp;
        UpdateServerAddressDisplay();

        SettingJvmArgs.Text = _viewModel.SettingsJvmArgs;

        UpdateSettingsAvailability();
        LoadUpdateForm();
    }

    /// <summary>
    /// Обновляет баннер-предупреждение и доступность настроек
    /// в зависимости от статуса сервера.
    /// </summary>
    public void UpdateSettingsAvailability()
    {
        var isRunning = _server?.IsRunning ?? false;

        SettingsRunningBanner.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        // Настройки, которые требуют перезапуска сервера
        SettingName.IsEnabled = !isRunning;
        SettingRamMin.IsEnabled = !isRunning;
        SettingRamMax.IsEnabled = !isRunning;
        SettingJavaAutoSelect.IsEnabled = !isRunning;
        JavaSelectionGrid.IsEnabled = !isRunning;
        SettingJvmArgs.IsEnabled = !isRunning;
    }

    /// <summary>
    /// Разворачивает карточку обновления загрузчика и прокручивает к ней.
    /// </summary>
    public void ShowUpdateCard()
    {
        UpdateServerCard.IsExpanded = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => UpdateServerCard.BringIntoView()));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateResultCts?.Cancel();
        _updateResultCts?.Dispose();

        _disposed = true;
    }

    private static Brush GetThemeBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush
        ?? new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));

    public void OnServerRefreshed(Server server)
    {
        _server = server;
        UpdateSettingsAvailability();
    }

    // ─── Заполнение форм ────────────────────────────────────────────

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
    /// Обновление видимости ComboBox Java
    /// </summary>
    private void UpdateJavaComboBoxVisibility()
    {
        var isAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
        JavaSelectionGrid.Visibility = isAutoSelect ? Visibility.Collapsed : Visibility.Visible;
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

    // ─── Обработчики UI ─────────────────────────────────────────────

    /// <summary>
    /// Валидация: разрешаем ввод только цифр
    /// </summary>
    private void NumberValidationTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Автосохранение настроек при изменении
    /// </summary>
    /// <summary>
    /// Определяет, к какому экспандеру относится контрол-отправитель
    /// </summary>
    private Wpf.Ui.Controls.TextBlock? GetSaveStatusTarget(object? sender)
    {
        if (sender is FrameworkElement element)
        {
            return element.Name switch
            {
                nameof(SettingName) => GeneralSaveStatus,
                nameof(SettingRamMin) or nameof(SettingRamMax) => RamSaveStatus,
                nameof(SettingAutoRestart) or nameof(SettingAutoRestartDelay) => AutoRestartSaveStatus,
                nameof(SettingJavaAutoSelect) => JavaSaveStatus,
                _ => null
            };
        }
        return null;
    }

    private void Setting_Click(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings(GetSaveStatusTarget(sender));
    }

    /// <summary>
    /// Автосохранение настроек при потере фокуса
    /// </summary>
    private void Setting_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings(GetSaveStatusTarget(sender));
    }

    /// <summary>
    /// Автоматическое сохранение настроек сервера
    /// </summary>
    private void AutoSaveSettings(Wpf.Ui.Controls.TextBlock? statusText = null)
    {
        if (_server == null)
            return;

        try
        {
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

            // ─── Валидация ──────────────────────────────────────────
            if (statusText != null)
            {
                string? validationError = null;

                if (statusText == GeneralSaveStatus && string.IsNullOrWhiteSpace(newName))
                    validationError = LocalizationManager.Get("Validation_NameRequired");
                else if (statusText == RamSaveStatus && string.IsNullOrWhiteSpace(ramMinStr))
                    validationError = LocalizationManager.Get("Validation_RamMinRequired");
                else if (statusText == RamSaveStatus && string.IsNullOrWhiteSpace(ramMaxStr))
                    validationError = LocalizationManager.Get("Validation_RamMaxRequired");
                else if (statusText == AutoRestartSaveStatus && string.IsNullOrWhiteSpace(autoRestartDelayStr))
                    validationError = LocalizationManager.Get("Validation_AutoRestartDelayRequired");

                if (validationError != null)
                {
                    _ = ShowValidationWarning(statusText, validationError);
                    return;
                }
            }

            _viewModel.SaveSettings(new ServerSettingsRequest(
                Name: newName,
                RamMinStr: ramMinStr,
                RamMaxStr: ramMaxStr,
                AutoRestart: autoRestart,
                AutoRestartDelayStr: autoRestartDelayStr,
                JavaAutoSelect: javaAutoSelect,
                JavaId: javaId,
                JvmArgs: jvmArgs
            ));

            // Проверяем ошибку переименования папки
            if (_viewModel.LastRenameError != null)
            {
                if (statusText != null)
                {
                    statusText.Text = _viewModel.LastRenameError;
                    _ = ShowSaveStatus(statusText, isError: true);
                }
                // Возвращаем старое имя в поле ввода
                SettingName.Text = _viewModel.Server?.Name ?? newName;
                ServerRenamed?.Invoke(_viewModel.Server?.Name ?? newName);
            }
            else
            {
                // Обновляем UI если имя изменилось
                if (!string.IsNullOrEmpty(newName))
                {
                    ServerRenamed?.Invoke(newName);
                }

                // Показываем статус сохранения
                if (statusText != null)
                    _ = ShowSaveStatus(statusText, isError: false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AutoSaveSettings error: {ex.Message}", ex, "ServerSettingsSection");
            if (statusText != null)
                _ = ShowSaveStatus(statusText, isError: true);
        }
    }

    /// <summary>
    /// Показывает предупреждение валидации (красный текст, автоматическое скрытие)
    /// </summary>
    private async Task ShowValidationWarning(Wpf.Ui.Controls.TextBlock statusText, string message)
    {
        if (statusText == null) return;

        statusText.Text = message;
        statusText.Foreground = ErrorBrush;
        statusText.Visibility = Visibility.Visible;
        statusText.Opacity = 0;

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        statusText.BeginAnimation(OpacityProperty, fadeIn);

        await Task.Delay(2500);

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        statusText.BeginAnimation(OpacityProperty, fadeOut);

        await Task.Delay(300);
        statusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Показывает временный статус сохранения (зелёный — успех, красный — ошибка)
    /// с автоматическим скрытием через 2 секунды.
    /// </summary>
    private async Task ShowSaveStatus(Wpf.Ui.Controls.TextBlock statusText, bool isError)
    {
        if (statusText == null) return;

        statusText.Text = isError
            ? LocalizationManager.Get("Props_SaveError")
            : LocalizationManager.Get("Message_SettingsSaved");
        statusText.Foreground = isError ? ErrorBrush : SuccessBrush;
        statusText.Visibility = Visibility.Visible;
        statusText.Opacity = 0;

        // Плавное появление
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        statusText.BeginAnimation(OpacityProperty, fadeIn);

        // Ждём 2 секунды
        await Task.Delay(2000);

        // Плавное исчезновение
        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => statusText.Visibility = Visibility.Collapsed;
        statusText.BeginAnimation(OpacityProperty, fadeOut);
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

        _ = ShowSaveStatus(UpnpSaveStatus, isError: false);
    }

    /// <summary>
    /// Обработка выбора Java в ComboBox (автосохранение)
    /// </summary>
    private void SettingJavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AutoSaveSettings(JavaSaveStatus);
    }

    /// <summary>
    /// Сохранение JVM аргументов при потере фокуса
    /// </summary>
    private void SettingJvmArgs_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings(JavaSaveStatus);
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
            Logger.Warning($"Failed to copy address: {ex.Message}", "ServerSettingsSection");
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
            CheckUpnpProgress.ShowIndeterminate();
            CheckUpnpButton.IsEnabled = false;
            CheckUpnpText.Text = LocalizationManager.Get("ServerDetail_Upnp_Checking");

            var isAvailable = await _viewModel.CheckUpnpAvailabilityAsync();

            CheckUpnpProgress.HideIndeterminate();
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
            Logger.Error($"UPnP check error: {ex.Message}", ex, "ServerSettingsSection");
            CheckUpnpProgress.HideIndeterminate();
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
            Logger.Error($"UPnP port check error: {ex.Message}", ex, "ServerSettingsSection");
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

    // ─── Обновление загрузчика сервера ──────────────────────────────

    /// <summary>
    /// Заполняет карточку обновления загрузчика: видимость, текущая версия,
    /// список доступных версий, уведомление в заголовке.
    /// </summary>
    private void LoadUpdateForm()
    {
        if (_server == null || _modLoaderService == null)
            return;

        var loaderType = _server.ModLoader.Type;
        var updatable = loaderType is ModLoaderType.Forge or ModLoaderType.NeoForge
            or ModLoaderType.Fabric or ModLoaderType.Quilt or ModLoaderType.Paper;

        UpdateServerCard.Visibility = updatable ? Visibility.Visible : Visibility.Collapsed;
        if (!updatable)
            return;

        _currentLoaderVersion = string.IsNullOrWhiteSpace(_server.ModLoader.LoaderVersion)
            ? null
            : _server.ModLoader.LoaderVersion;

        UpdateCurrentVersionText.Text = _currentLoaderVersion
            ?? LocalizationManager.Get("ServerDetail_UpdateLoader_CurrentEmpty");

        UpdateServerButton.IsEnabled = false;
        SettingUpdateNotifications.IsChecked = _viewModel.SettingsEnableUpdateNotification;
        _ = LoadLoaderVersionsAsync();
    }

    /// <summary>
    /// Загружает доступные версии загрузчика и обновляет карточку.
    /// </summary>
    private async Task LoadLoaderVersionsAsync()
    {
        if (_server == null || _modLoaderService == null)
            return;

        try
        {
            var versions = await _modLoaderService.GetLoaderVersionsAsync(
                _server.ModLoader.Type.ToString(), _server.McVersion, showSnapshots: false);

            _allLoaderVersions = versions;

            if (versions.Length == 0)
            {
                _latestLoaderVersion = null;
                UpdateLoaderVersionBox.ItemsSource = Array.Empty<string>();
                UpdateServerButton.IsEnabled = false;
                SetUpdateStatus(LocalizationManager.Get("ServerDetail_UpdateLoader_NoVersions"), WarningBrush);
                UpdateUpdateAvailability();
                return;
            }

            // Последняя доступная версия — максимум по списку
            var newest = versions[0];
            foreach (var v in versions)
            {
                if (VersionCompare.CompareLoaderVersions(v, newest) > 0)
                    newest = v;
            }
            _latestLoaderVersion = newest;

            ApplyVersionFilter();
        }
        catch (Exception ex)
        {
            Logger.Warning($"LoadLoaderVersionsAsync error: {ex.Message}", "ServerSettingsSection");
            SetUpdateStatus(string.Format(
                LocalizationManager.Get("ServerDetail_UpdateLoader_LoadVersFailed"), ex.Message), ErrorBrush);
        }
    }

    /// <summary>
    /// Показывает в выпадающем списке только версии, новее установленной
    /// (даунгрейд не поддерживается, поэтому более старые отбрасываются).
    /// </summary>
    private void ApplyVersionFilter()
    {
        if (_allLoaderVersions.Length == 0)
        {
            UpdateLoaderVersionBox.IsEnabled = true;
            UpdateLoaderVersionBox.ItemsSource = Array.Empty<string>();
            UpdateServerButton.IsEnabled = false;
            UpdateUpdateAvailability();
            return;
        }

        var current = _currentLoaderVersion;
        var selectable = string.IsNullOrEmpty(current)
            ? _allLoaderVersions
            : [.. _allLoaderVersions.Where(v => VersionCompare.CompareLoaderVersions(v, current) > 0)];

        if (selectable.Length > 0)
        {
            UpdateLoaderVersionBox.IsEnabled = true;
            UpdateLoaderVersionBox.ItemsSource = selectable;
            UpdateLoaderVersionBox.SelectedIndex = 0;
            UpdateServerButton.IsEnabled = !_updateInProgress;
            // Не показываем «висящий» статус, когда есть что выбирать
            SetUpdateStatus(string.Empty, null);
        }
        else
        {
            // Новых версий нет: выключаем список и кнопку, в списке — «актуальная версия»
            UpdateLoaderVersionBox.IsEnabled = false;
            UpdateLoaderVersionBox.ItemsSource =
                new[] { LocalizationManager.Get("ServerDetail_UpdateLoader_NoUpdates") };
            UpdateLoaderVersionBox.SelectedIndex = 0;
            UpdateServerButton.IsEnabled = false;
        }

        UpdateUpdateAvailability();
    }

    /// <summary>
    /// Сообщает странице о доступности обновления (для уведомления в заголовке).
    /// </summary>
    private void UpdateUpdateAvailability()
    {
        var notifyEnabled = SettingUpdateNotifications.IsChecked ?? false;
        var hasUpdate = notifyEnabled
            && !string.IsNullOrEmpty(_currentLoaderVersion)
            && _allLoaderVersions.Any(v => VersionCompare.CompareLoaderVersions(v, _currentLoaderVersion) > 0);

        UpdateAvailabilityChanged?.Invoke(hasUpdate, _latestLoaderVersion);
    }

    /// <summary>
    /// Обновление загрузчика сервера: подтверждение остановки, резервная копия,
    /// переустановка загрузчика, сохранение новой версии.
    /// </summary>
    private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || _updateInProgress)
            return;

        var newVersion = UpdateLoaderVersionBox.SelectedItem as string;
        if (string.IsNullOrEmpty(newVersion))
            return;

        // 1. Останавливаем запущенный сервер (с подтверждением)
        if (_server.IsRunning)
        {
            var confirm = await UiHelper.ShowConfirm(
                LocalizationManager.Get("ServerDetail_UpdateLoader_StopConfirm"),
                LocalizationManager.Get("ServerDetail_UpdateLoader_StopConfirm_Title"));
            if (confirm != ContentDialogResult.Primary)
                return;

            if (!await _viewModel.StopServerIfRunningAsync())
                return;
        }

        var oldVersionDisplay = _currentLoaderVersion
            ?? LocalizationManager.Get("ServerDetail_UpdateLoader_CurrentEmpty");

        var updated = false;
        string? resultMessage = null;

        _updateInProgress = true;
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();

        _updateResultCts?.Cancel();
        UpdateLogProgress.Opacity = 1;
        UpdateLogProgress.Visibility = Visibility.Visible;
        UpdateResultText.Visibility = Visibility.Collapsed;

        UpdateServerButton.IsEnabled = false;
        UpdateProgress.ShowIndeterminate();
        UpdateLogProgress.Visibility = Visibility.Visible;
        UpdateButtonText.Text = LocalizationManager.Get("ServerDetail_UpdateLoader_Updating");

        try
        {
            var installer = Ioc.Default.GetService<IServerInstaller>()!;
            var progress = new DispatcherProgress<string>(msg => AppLogLine(msg), Dispatcher);

            AppLogLine(LocalizationManager.Get("ServerDetail_UpdateLoader_Started"));

            // 2. Резервная копия (по желанию)
            if (SettingUpdateBackup.IsChecked == true)
            {
                AppLogLine(LocalizationManager.Get("ServerDetail_UpdateLoader_BackupCreating"));
                var backupPath = ServerBackup.CreateBackupFolder(_server.Path, _server.Name);
                AppLogLine(string.Format(
                    LocalizationManager.Get("ServerDetail_UpdateLoader_BackupCreated"), backupPath));
            }

            // 3. Удаляем файлы, которые установщик пересоздаст сам
            var removed = ServerBackup.RemoveLoaderFiles(_server.Path, _server.ModLoader.Type);
            foreach (var item in removed)
                AppLogLine($"{LocalizationManager.Get("ServerDetail_UpdateLoader_LogRemoved")} {item}");

            // 4. Устанавливаем новый загрузчик
            var result = await installer.InstallServer(
                _server.ModLoader.Type,
                _server.McVersion,
                newVersion,
                _server.Path,
                _server.Port,
                _server.Settings.RamMin,
                _server.Settings.RamMax,
                progress,
                _updateCts.Token);

            if (result.Success)
            {
                // 5. Сохраняем новую версию загрузчика
                _server.ModLoader.LoaderVersion = newVersion;
                if (result.BuildNumber.HasValue)
                    _server.ServerBuild = result.BuildNumber.Value;
                _server.Status = ServerStatus.Stopped;
                Ioc.Default.GetService<IServerManager>()!.UpdateServer(_server);

                _currentLoaderVersion = newVersion;
                UpdateCurrentVersionText.Text = newVersion;
                ApplyVersionFilter();

                var successMsg = string.Format(
                    LocalizationManager.Get("ServerDetail_UpdateLoader_Success"),
                    oldVersionDisplay, newVersion);
                updated = true;
                resultMessage = successMsg;
                // Успех показываем в прогрессбаре, отдельную строку статуса очищаем
                SetUpdateStatus(string.Empty, null);
            }
            else
            {
                var errorMsg = string.Format(
                    LocalizationManager.Get("ServerDetail_UpdateLoader_Error"),
                    result.Error ?? result.Status.ToString());
                AppLogLine(errorMsg);
                SetUpdateStatus(errorMsg, ErrorBrush);
            }

            UpdateUpdateAvailability();
        }
        catch (OperationCanceledException)
        {
            AppLogLine(LocalizationManager.Get("ServerDetail_UpdateLoader_Cancelled"));
        }
        catch (Exception ex)
        {
            Logger.Error($"UpdateServerButton_Click error: {ex.Message}", ex, "ServerSettingsSection");
            var errorMsg = string.Format(
                LocalizationManager.Get("ServerDetail_UpdateLoader_Error"), ex.Message);
            AppLogLine(errorMsg);
            SetUpdateStatus(errorMsg, ErrorBrush);
        }
        finally
        {
            _updateInProgress = false;
            _updateCts?.Dispose();
            _updateCts = null;
            UpdateProgress.HideIndeterminate();
            UpdateButtonText.Text = LocalizationManager.Get("ServerDetail_UpdateLoader_Update");
            UpdateServerButton.IsEnabled = UpdateLoaderVersionBox.IsEnabled
                && UpdateLoaderVersionBox.SelectedIndex >= 0;
        }

        _ = FinishUpdateAsync(updated, resultMessage);
    }

    /// <summary>
    /// Добавляет строку в статус карточки обновления (вместо отдельного окна лога).
    /// </summary>
    private void AppLogLine(string line)
    {
        SetUpdateStatus($"{DateTime.Now:HH:mm:ss} {line}", null);
    }

    /// <summary>
    /// Обработка изменения чекбокса «показывать уведомление об обновлении».
    /// </summary>
    private void SettingUpdateNotifications_Click(object sender, RoutedEventArgs e)
    {
        var enable = SettingUpdateNotifications.IsChecked ?? false;
        _viewModel.SaveUpdateNotificationSetting(enable);
        UpdateUpdateAvailability();
    }

    /// <summary>
    /// Показывает сообщение статуса в карточке обновления загрузчика.
    /// </summary>
    private void SetUpdateStatus(string message, Brush? brush)
    {
        UpdateStatusText.Text = message;
        UpdateStatusText.Foreground = brush ?? DefaultBrush;
        UpdateStatusText.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Плавно прячет прогрессбар после завершения обновления. При успехе показывает
    /// текст результата в прогрессбаре, держит его чуть дольше и плавно скрывает.
    /// </summary>
    private async Task FinishUpdateAsync(bool success, string? resultMessage)
    {
        _updateResultCts?.Cancel();
        _updateResultCts?.Dispose();
        _updateResultCts = new CancellationTokenSource();
        var token = _updateResultCts.Token;

        // Прогрессбар плавно исчезает при завершении
        if (UpdateLogProgress.Visibility == Visibility.Visible)
        {
            await FadeAsync(UpdateLogProgress, 1, 0, 300);
            if (token.IsCancellationRequested)
                return;
            UpdateLogProgress.Visibility = Visibility.Collapsed;
        }

        if (!success || string.IsNullOrEmpty(resultMessage))
            return;

        // Текст успеха появляется в прогрессбаре
        UpdateResultText.Text = resultMessage;
        UpdateResultText.Foreground = SuccessBrush;
        UpdateResultText.Visibility = Visibility.Visible;
        UpdateResultText.Opacity = 0;
        await FadeAsync(UpdateResultText, 0, 1, 250);
        if (token.IsCancellationRequested)
            return;

        // Держим текст чуть дольше, затем плавно скрываем
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FadeAsync(UpdateResultText, 1, 0, 450);
        UpdateResultText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Плавно изменяет прозрачность элемента.
    /// </summary>
    private static Task FadeAsync(FrameworkElement element, double from, double to, int milliseconds)
    {
        var tcs = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new QuadraticEase
            {
                EasingMode = to > from ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        animation.Completed += (_, _) => tcs.TrySetResult();
        element.BeginAnimation(OpacityProperty, animation);
        return tcs.Task;
    }
}