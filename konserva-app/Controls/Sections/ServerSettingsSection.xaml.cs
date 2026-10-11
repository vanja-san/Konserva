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
/// Server settings section: general options, memory, auto-restart, Java, UPnP
/// and loader update.
/// </summary>
public partial class ServerSettingsSection : System.Windows.Controls.UserControl, IDisposable
{
    private ServerDetailViewModel _viewModel = null!;
    private readonly IModLoaderService _modLoaderService;
    private Server? _server;
    private bool _disposed;
    private bool _isLoadingSettings;

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
    /// Event: the server name changed (to update the page title).
    /// </summary>
    public event Action<string>? ServerRenamed;

    /// <summary>
    /// Event: the loader update availability changed.
    /// </summary>
    public event Action<bool, string?>? UpdateAvailabilityChanged;

    public ServerSettingsSection()
    {
        _modLoaderService = Ioc.Default.GetService<IModLoaderService>()!;

        InitializeComponent();

        // We subscribe after InitializeComponent to avoid a NullReferenceException
        SettingJavaAutoSelect.Checked += SettingJavaAutoSelect_CheckedChanged;
        SettingJavaAutoSelect.Unchecked += SettingJavaAutoSelect_CheckedChanged;
        SettingModBackup.Checked += SettingModBackup_CheckedChanged;
        SettingModBackup.Unchecked += SettingModBackup_CheckedChanged;
    }

    /// <summary>
    /// Initializes the section: binds the ViewModel and the server, fills the form.
    /// </summary>
    public void Initialize(ServerDetailViewModel viewModel, Server? server)
    {
        _viewModel = viewModel;
        _server = server;
        if (_server == null)
            return;

        _isLoadingSettings = true;
        try
        {
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

            SettingModBackup.IsChecked = _viewModel.SettingsModBackupEnabled;
            UpdateModBackupControlsState();
            UpdateModUpdateChannelComboBox();
            SettingJvmArgs.Text = _viewModel.SettingsJvmArgs;

            UpdateSettingsAvailability();
            LoadUpdateForm();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Restores the selected mod update channel from the server settings.
    /// </summary>
    private void UpdateModUpdateChannelComboBox()
    {
        var current = _viewModel.SettingsUpdateChannel.ToString();
        foreach (var item in SettingUpdateChannelComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                SettingUpdateChannelComboBox.SelectedItem = item;
                return;
            }
        }

        SettingUpdateChannelComboBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Updates the warning banner and the settings availability
    /// depending on the server status.
    /// </summary>
    public void UpdateSettingsAvailability()
    {
        var isRunning = _server?.IsRunning ?? false;

        SettingsRunningBanner.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        // Settings that require a server restart
        SettingName.IsEnabled = !isRunning;
        SettingRamMin.IsEnabled = !isRunning;
        SettingRamMax.IsEnabled = !isRunning;
        SettingJavaAutoSelect.IsEnabled = !isRunning;
        JavaSelectionGrid.IsEnabled = !isRunning;
        SettingJvmArgs.IsEnabled = !isRunning;
    }

    /// <summary>
    /// Expands the loader update card and scrolls to it.
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

    // ─── Filling the forms ────────────────────────────────────────────

    /// <summary>
    /// Fills the ComboBox with the Java list
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
    /// Updates the visibility of the Java ComboBox
    /// </summary>
    private void UpdateJavaComboBoxVisibility()
    {
        var isAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
        JavaSelectionGrid.Visibility = isAutoSelect ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Updates the displayed server address (IP:Port).
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

    // ─── UI handlers ─────────────────────────────────────────────

    /// <summary>
    /// Validation: only digits are allowed as input
    /// </summary>
    private void NumberValidationTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Auto-save the settings on change
    /// </summary>
    /// <summary>
    /// Determines which expander the sender control belongs to
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
                nameof(SettingModBackup) => ModsSaveStatus,
                nameof(SettingUpdateChannelComboBox) => ModsSaveStatus,
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
    /// Auto-save the settings when the focus is lost
    /// </summary>
    private void Setting_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings(GetSaveStatusTarget(sender));
    }

    /// <summary>
    /// Automatically saves the server settings
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
            var modBackup = SettingModBackup.IsChecked;
            var javaAutoSelect = SettingJavaAutoSelect.IsChecked ?? true;
            var javaId = SettingJavaComboBox.SelectedItem is ComboBoxItem selectedItem
                ? selectedItem.Tag as string
                : null;
            var jvmArgs = SettingJvmArgs.Text;
            var updateChannel = SettingUpdateChannelComboBox.SelectedItem is ComboBoxItem channelItem
                && Enum.TryParse<ModUpdateChannel>(channelItem.Tag as string, out var parsedChannel)
                ? parsedChannel
                : ModUpdateChannel.Release;

            // ─── Validation ──────────────────────────────────────────
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
                ModBackupEnabled: modBackup,
                JavaAutoSelect: javaAutoSelect,
                JavaId: javaId,
                JvmArgs: jvmArgs,
                UpdateChannel: updateChannel
            ));

            // Check the folder rename error
            if (_viewModel.LastRenameError != null)
            {
                if (statusText != null)
                {
                    statusText.Text = _viewModel.LastRenameError;
                    _ = ShowSaveStatus(statusText, isError: true);
                }
                // Restore the old name in the input field
                SettingName.Text = _viewModel.Server?.Name ?? newName;
                ServerRenamed?.Invoke(_viewModel.Server?.Name ?? newName);
            }
            else
            {
                // Update the UI if the name has changed
                if (!string.IsNullOrEmpty(newName))
                {
                    ServerRenamed?.Invoke(newName);
                }

                // Show the save status
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
    /// Shows a validation warning (red text, auto-hidden)
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
    /// Shows a temporary save status (green — success, red — error)
    /// with auto-hide after 2 seconds.
    /// </summary>
    private async Task ShowSaveStatus(Wpf.Ui.Controls.TextBlock statusText, bool isError, string? customMessage = null)
    {
        if (statusText == null) return;

        statusText.Text = customMessage
            ?? (isError
                ? LocalizationManager.Get("Props_SaveError")
                : LocalizationManager.Get("Message_SettingsSaved"));
        statusText.Foreground = isError ? ErrorBrush : SuccessBrush;
        statusText.Visibility = Visibility.Visible;
        statusText.Opacity = 0;

        // Smooth fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        statusText.BeginAnimation(OpacityProperty, fadeIn);

        // Wait 2 seconds
        await Task.Delay(2000);

        // Smooth fade out
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
    /// Handles the change of the automatic Java selection
    /// </summary>
    private void SettingJavaAutoSelect_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // Guard: protection against a call before initialization
        if (!IsInitialized || JavaSelectionGrid == null)
            return;

        UpdateJavaComboBoxVisibility();
    }

    /// <summary>
    /// Updates the availability of the mod backup delete button.
    /// </summary>
    private void SettingModBackup_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || DeleteModBackupsButton == null)
            return;

        UpdateModBackupControlsState();
    }

    /// <summary>
    /// The delete backups button is only available when the mod backup is enabled.
    /// </summary>
    private void UpdateModBackupControlsState()
    {
        DeleteModBackupsButton.IsEnabled = SettingModBackup.IsChecked == true;
    }

    /// <summary>
    /// Handles the change of the UPnP checkbox
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
    /// Handles the Java selection in the ComboBox (auto-save)
    /// </summary>
    private void SettingJavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AutoSaveSettings(JavaSaveStatus);
    }

    /// <summary>
    /// Handles the mod update channel selection (auto-save).
    /// </summary>
    private void SettingUpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        AutoSaveSettings(ModsSaveStatus);
    }

    /// <summary>
    /// Saves the JVM arguments when the focus is lost
    /// </summary>
    private void SettingJvmArgs_LostFocus(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings(JavaSaveStatus);
    }

    /// <summary>
    /// Deletes all mod backups of the server (with confirmation).
    /// </summary>
    private async void DeleteModBackupsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        var confirm = await UiHelper.ShowConfirm(
            LocalizationManager.Get("ServerDetail_Settings_ModBackup_Clear_Confirm"),
            LocalizationManager.Get("ServerDetail_Settings_ModBackup_Clear_Confirm_Title"));
        if (confirm != ContentDialogResult.Primary)
            return;

        if (_viewModel.DeleteModBackups())
        {
            _ = ShowSaveStatus(ModsSaveStatus, isError: false,
                customMessage: LocalizationManager.Get("ServerDetail_Settings_ModBackup_Clear_Done"));
        }
        else
        {
            _ = ShowSaveStatus(ModsSaveStatus, isError: true);
        }
    }

    /// <summary>
    /// Copies the server address (IP:Port) to the clipboard.
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

            // Restore the button text after 2 seconds
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
    /// UPnP availability check
    /// </summary>
    private async void CheckUpnpButton_Click(object sender, RoutedEventArgs e)
    {
        // Hide the previous result before a new check
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
    /// Resets the UPnP check result after 5 seconds.
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
    /// Checks the port forwarding via UPnP. The result is shown to the left of the buttons.
    /// </summary>
    private async void CheckPortButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null)
            return;

        // Hide the previous result before a new check
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

                // Hide the address if the port is closed
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
    /// Resets the port check result after 5 seconds.
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

    // ─── Server loader update ──────────────────────────────

    /// <summary>
    /// Fills the loader update card: visibility, current version,
    /// list of available versions, notification in the header.
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
    /// Loads the available loader versions and updates the card.
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

            // The latest available version — the maximum in the list
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
    /// Shows only versions newer than the installed one in the drop-down list
    /// (downgrade is not supported, so older ones are discarded).
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

        var selectable = LoaderVersionSelector.GetSelectableVersions(
            _allLoaderVersions, _currentLoaderVersion);

        if (selectable.Length > 0)
        {
            UpdateLoaderVersionBox.IsEnabled = true;
            UpdateLoaderVersionBox.ItemsSource = selectable;
            UpdateLoaderVersionBox.SelectedIndex = 0;
            UpdateServerButton.IsEnabled = !_updateInProgress;
            // Do not show a "hanging" status when there is something to select
            SetUpdateStatus(string.Empty, null);
        }
        else
        {
            // No new versions: disable the list and the button, show "up to date" in the list
            UpdateLoaderVersionBox.IsEnabled = false;
            UpdateLoaderVersionBox.ItemsSource =
                new[] { LocalizationManager.Get("ServerDetail_UpdateLoader_NoUpdates") };
            UpdateLoaderVersionBox.SelectedIndex = 0;
            UpdateServerButton.IsEnabled = false;
        }

        UpdateUpdateAvailability();
    }

    /// <summary>
    /// Notifies the page about the update availability (for the header notification).
    /// </summary>
    private void UpdateUpdateAvailability()
    {
        var notifyEnabled = SettingUpdateNotifications.IsChecked ?? false;
        // An unknown current version is not reported as an update: there is
        // nothing to compare against, and the header notification would be noise.
        var hasUpdate = notifyEnabled
            && !string.IsNullOrEmpty(_currentLoaderVersion)
            && LoaderVersionSelector.HasUpdates(_allLoaderVersions, _currentLoaderVersion);

        UpdateAvailabilityChanged?.Invoke(hasUpdate, _latestLoaderVersion);
    }

    /// <summary>
    /// Server loader update: stop confirmation, backup,
    /// loader reinstall, saving the new version.
    /// </summary>
    private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || _updateInProgress)
            return;

        var newVersion = UpdateLoaderVersionBox.SelectedItem as string;
        if (string.IsNullOrEmpty(newVersion))
            return;

        // 1. Stop the running server (with confirmation)
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

            // 2. Backup (optional)
            if (SettingUpdateBackup.IsChecked == true)
            {
                AppLogLine(LocalizationManager.Get("ServerDetail_UpdateLoader_BackupCreating"));
                var backupPath = ServerBackup.CreateBackupFolder(_server.Path, _server.Name);
                AppLogLine(string.Format(
                    LocalizationManager.Get("ServerDetail_UpdateLoader_BackupCreated"), backupPath));
            }

            // 3. Remove the files that the installer recreates on its own
            var removed = ServerBackup.RemoveLoaderFiles(_server.Path, _server.ModLoader.Type);
            foreach (var item in removed)
                AppLogLine($"{LocalizationManager.Get("ServerDetail_UpdateLoader_LogRemoved")} {item}");

            // 4. Install the new loader
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
                // 5. Save the new loader version
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
                // On success we show it in the progress bar and clear the separate status line
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
    /// Appends a line to the update card status (instead of a separate log window).
    /// </summary>
    private void AppLogLine(string line)
    {
        SetUpdateStatus($"{DateTime.Now:HH:mm:ss} {line}", null);
    }

    /// <summary>
    /// Handles the change of the "show update notification" checkbox.
    /// </summary>
    private void SettingUpdateNotifications_Click(object sender, RoutedEventArgs e)
    {
        var enable = SettingUpdateNotifications.IsChecked ?? false;
        _viewModel.SaveUpdateNotificationSetting(enable);
        UpdateUpdateAvailability();
    }

    /// <summary>
    /// Shows the status message in the loader update card.
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
    /// Smoothly hides the progress bar after the update completes. On success it shows
    /// the result text in the progress bar, holds it a bit longer and hides it smoothly.
    /// </summary>
    private async Task FinishUpdateAsync(bool success, string? resultMessage)
    {
        _updateResultCts?.Cancel();
        _updateResultCts?.Dispose();
        _updateResultCts = new CancellationTokenSource();
        var token = _updateResultCts.Token;

        // The progress bar smoothly disappears on completion
        if (UpdateLogProgress.Visibility == Visibility.Visible)
        {
            await FadeAsync(UpdateLogProgress, 1, 0, 300);
            if (token.IsCancellationRequested)
                return;
            UpdateLogProgress.Visibility = Visibility.Collapsed;
        }

        if (!success || string.IsNullOrEmpty(resultMessage))
            return;

        // The success text appears in the progress bar
        UpdateResultText.Text = resultMessage;
        UpdateResultText.Foreground = SuccessBrush;
        UpdateResultText.Visibility = Visibility.Visible;
        UpdateResultText.Opacity = 0;
        await FadeAsync(UpdateResultText, 0, 1, 250);
        if (token.IsCancellationRequested)
            return;

        // Hold the text a bit longer, then hide it smoothly
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
    /// Smoothly changes the opacity of an element.
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