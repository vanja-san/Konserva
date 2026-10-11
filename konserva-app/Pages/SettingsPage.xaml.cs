using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Konserva.Pages;

/// <summary>
/// Application settings page
/// </summary>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private bool _isUpdating; // Flag to prevent recursive saving
    private bool _isLoading = true; // Page loading flag

    public SettingsPage()
    {
        InitializeComponent();

        _viewModel = Ioc.Default.GetService<SettingsViewModel>()
            ?? new SettingsViewModel(
                Ioc.Default.GetService<IConfigService>()!,
                Ioc.Default.GetService<IJavaManagementService>()
                    ?? new JavaManagementService(Ioc.Default.GetService<IConfigService>()!),
                Ioc.Default.GetService<IUpdateService>()
                    ?? new UpdateService(
                        Ioc.Default.GetService<IConfigService>()!,
                        Ioc.Default.GetService<IUpdateChecker>()!));

        Title = LocalizationManager.Get("Settings_Title");
        Loaded += OnLoaded;
        Unloaded += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;

        PopulateUpdateIntervalMenu();
    }

    /// <summary>
    /// Allowed update check intervals in hours.
    /// </summary>
    private static readonly int[] UpdateIntervalOptions = [1, 2, 6, 12, 24, 48, 72, 168];

    /// <summary>
    /// Builds the interval dropdown menu from localized labels.
    /// Previously the items were hardcoded in XAML with Russian text, which made
    /// the English UI show "24 h", and parsing the label back into a number
    /// silently reset the interval to 24 hours.
    /// </summary>
    private void PopulateUpdateIntervalMenu()
    {
        if (UpdateIntervalMenu == null)
            return;

        UpdateIntervalMenu.Items.Clear();

        foreach (var hours in UpdateIntervalOptions)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = _viewModel.FormatUpdateInterval(hours),
                Tag = hours.ToString()
            };
            item.Click += UpdateIntervalMenuItem_Click;
            UpdateIntervalMenu.Items.Add(item);
        }
    }

    private void OnLanguageChanged(string culture)
    {
        Title = LocalizationManager.Get("Settings_Title");
        // Refresh the button/header texts that are set in code
        CheckUpdatesModeButton.Content = _viewModel.CheckUpdatesModeText;
        UpdateIntervalButton.Content = _viewModel.UpdateIntervalText;
        MinimizeToTrayModeButton.Content = GetMinimizeToTrayModeText(_viewModel.MinimizeToTrayMode);
        ScanJavaButton.ToolTip = LocalizationManager.Get("Settings_Java_Scan");
        // Reset the update check button text to its default value
        CheckUpdatesButtonText.Text = LocalizationManager.Get("Settings_CheckForUpdates");

        // Rebuild the interval menu for the new language
        PopulateUpdateIntervalMenu();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadSettings();
        RefreshUI();

        // Subscribe to language changes to refresh the code-behind text
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Updates the UI from the ViewModel
    /// </summary>
    private void RefreshUI()
    {
        ServersFolderPath.Text = _viewModel.ServersDirectory;
        JavaItemsControl.ItemsSource = _viewModel.JavaInstallations;
        UpdateJavaEmptyVisibility();

        CheckUpdatesOnLaunchItem.IsChecked = !_viewModel.CheckUpdatesScheduled;
        CheckUpdatesScheduledItem.IsChecked = _viewModel.CheckUpdatesScheduled;
        CheckUpdatesModeButton.Content = _viewModel.CheckUpdatesModeText;
        UpdateCheckModeVisibility(_viewModel.CheckUpdatesScheduled);
        UpdateIntervalButton.Content = _viewModel.UpdateIntervalText;

        MinimizeToTrayModeButton.Content = GetMinimizeToTrayModeText(_viewModel.MinimizeToTrayMode);
        UpdateMinimizeToTrayMenuChecks(_viewModel.MinimizeToTrayMode);

        // Theme loading
        if (!ThemeComboBox.SelectItemByTag(_viewModel.Theme))
            ThemeComboBox.SelectedIndex = 0;

        // Language loading
        if (!LanguageComboBox.SelectItemByTag(_viewModel.Language))
            LanguageComboBox.SelectedIndex = 0;

        // Download source loading
        if (!DownloadSourceComboBox.SelectItemByTag(_viewModel.DownloadSource))
            DownloadSourceComboBox.SelectedIndex = 0;

        // Console settings
        ConsoleAutoScrollBox.IsChecked = _viewModel.ConsoleAutoScroll;
        ConsoleWordWrapBox.IsChecked = _viewModel.ConsoleWordWrap;

        _isLoading = false;
    }

    /// <summary>
    /// Auto-save of the settings.
    /// The interval is taken directly from the ViewModel — it used to be parsed from the
    /// button text ("24 h" / "2 d"), which silently reset the value to 24 hours
    /// in any other UI language.
    /// </summary>
    private void AutoSaveSettings(Wpf.Ui.Controls.TextBlock? statusText = null)
    {
        if (_isLoading || _isUpdating) return; // Guard against saving during load and recursion

        try
        {
            _isUpdating = true;

            // Sync the UI → ViewModel before saving.
            // UpdateIntervalHours is intentionally not synced: it is already stored
            // in the ViewModel and only changes through the Tag of the menu items.
            _viewModel.CheckUpdatesScheduled = CheckUpdatesScheduledItem.IsChecked;
            _viewModel.MinimizeToTrayMode = GetSelectedMinimizeToTrayMode();
            _viewModel.ConsoleAutoScroll = ConsoleAutoScrollBox.IsChecked == true;
            _viewModel.ConsoleWordWrap = ConsoleWordWrapBox.IsChecked == true;

            if (DownloadSourceComboBox.SelectedItem is ComboBoxItem downloadItem)
                _viewModel.DownloadSource = (string)downloadItem.Tag;

            if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem)
            {
                var newTheme = (string)themeItem.Tag;
                if (_viewModel.Theme != newTheme)
                {
                    _viewModel.Theme = newTheme;
                    // Apply the theme immediately
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ApplyTheme(newTheme);
                }
            }

            if (LanguageComboBox.SelectedItem is ComboBoxItem languageItem)
            {
                _viewModel.Language = (string)languageItem.Tag;
            }

            _viewModel.SaveSettings();

            // Show the save success notification
            ShowSaveNotification(statusText);
        }
        catch (Exception ex)
        {
            Logger.Error($"AutoSaveSettings error: {ex.Message}", ex, "SettingsPage");
            if (statusText != null)
                _ = ShowSaveStatus(statusText, isError: true);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Shows the save success notification next to the group header
    /// </summary>
    private void ShowSaveNotification(Wpf.Ui.Controls.TextBlock? statusText = null)
    {
        if (statusText != null)
        {
            _ = ShowSaveStatus(statusText, isError: false);
        }
    }

    /// <summary>
    /// Shows a temporary save status (green — success, red — error)
    /// that hides automatically after 2 seconds.
    /// </summary>
    private static System.Windows.Media.Brush GetThemeBrush(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as System.Windows.Media.Brush
        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

    private async Task ShowSaveStatus(Wpf.Ui.Controls.TextBlock statusText, bool isError)
    {
        if (statusText == null) return;

        statusText.Text = isError
            ? LocalizationManager.Get("Props_SaveError")
            : LocalizationManager.Get("Message_SettingsSaved");
        statusText.Foreground = isError ? GetThemeBrush("SystemFillColorCriticalBrush") : GetThemeBrush("SystemFillColorSuccessBrush");
        statusText.Visibility = Visibility.Visible;
        statusText.Opacity = 0;

        // Smooth fade in
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        statusText.BeginAnimation(OpacityProperty, fadeIn);

        // Wait 2 seconds
        await Task.Delay(2000);

        // Smooth fade out
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => statusText.Visibility = Visibility.Collapsed;
        statusText.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Asynchronously hides the InfoBar after the specified delay.
    /// </summary>
    private async Task AutoHideInfoBarAsync(Wpf.Ui.Controls.InfoBar infoBar, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            Dispatcher.Invoke(() => infoBar.IsOpen = false);
        }
        catch (Exception ex)
        {
            Logger.Warning($"AutoHideInfoBarAsync error: {ex.Message}", "SettingsPage");
        }
    }

    private void ChangeServersPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.Get("Settings_SelectServerFolder"),
            InitialDirectory = _viewModel.ServersDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.SetServersDirectory(dialog.FolderName);
            ServersFolderPath.Text = dialog.FolderName;
            ShowSaveNotification(ServersSaveStatus);
        }
    }

    private void DefaultRamMin_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings(ServersSaveStatus);
    }

    private void DefaultRamMax_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings(ServersSaveStatus);
    }

    private void CheckUpdatesModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag)
        {
            var isScheduled = tag == "Scheduled";

            _viewModel.SetCheckUpdatesMode(isScheduled);

            // Update the UI
            CheckUpdatesOnLaunchItem.IsChecked = !isScheduled;
            CheckUpdatesScheduledItem.IsChecked = isScheduled;
            CheckUpdatesModeButton.Content = _viewModel.CheckUpdatesModeText;
            UpdateCheckModeVisibility(isScheduled);

            AutoSaveSettings(ServersSaveStatus);

            // Restart the background update check loop
            Ioc.Default.GetService<MainWindow>()?.StartUpdateCheckLoop();
        }
    }

    /// <summary>
    /// Shows or hides the interval selection card depending on the mode.
    /// </summary>
    private void UpdateCheckModeVisibility(bool isScheduled)
    {
        var visibility = isScheduled ? Visibility.Visible : Visibility.Collapsed;
        UpdateIntervalSeparator.Visibility = visibility;
        UpdateIntervalCard.Visibility = visibility;
    }

    private void MinimizeToTrayModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag)
        {
            // Update the UI
            MinimizeToTrayModeButton.Content = GetMinimizeToTrayModeText(tag);
            UpdateMinimizeToTrayMenuChecks(tag);

            // Save
            _viewModel.MinimizeToTrayMode = tag;
            AutoSaveSettings(ServersSaveStatus);
        }
    }

    /// <summary>
    /// Returns the localized text for the specified tray mode.
    /// </summary>
    private string GetMinimizeToTrayModeText(string mode) => mode switch
    {
        "None" => LocalizationManager.Get("Settings_MinimizeToTray_None"),
        "OnClose" => LocalizationManager.Get("Settings_MinimizeToTray_OnClose"),
        "OnMinimize" => LocalizationManager.Get("Settings_MinimizeToTray_OnMinimize"),
        "Always" => LocalizationManager.Get("Settings_MinimizeToTray_Always"),
        _ => LocalizationManager.Get("Settings_MinimizeToTray_OnClose"),
    };

    /// <summary>
    /// Updates the checkmarks in the tray mode selection menu.
    /// </summary>
    private void UpdateMinimizeToTrayMenuChecks(string mode)
    {
        MinimizeToTrayNoneItem.IsChecked = mode == "None";
        MinimizeToTrayOnCloseItem.IsChecked = mode == "OnClose";
        MinimizeToTrayOnMinimizeItem.IsChecked = mode == "OnMinimize";
        MinimizeToTrayAlwaysItem.IsChecked = mode == "Always";
    }

    /// <summary>
    /// Returns the selected tray mode from the menu checkmarks.
    /// </summary>
    private string GetSelectedMinimizeToTrayMode()
    {
        if (MinimizeToTrayAlwaysItem.IsChecked) return "Always";
        if (MinimizeToTrayOnMinimizeItem.IsChecked) return "OnMinimize";
        if (MinimizeToTrayOnCloseItem.IsChecked) return "OnClose";
        return "None";
    }

    private void UpdateIntervalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag && int.TryParse(tag, out var hours))
        {
            _viewModel.SetUpdateInterval(hours);
            UpdateIntervalButton.Content = _viewModel.UpdateIntervalText;
            AutoSaveSettings(ServersSaveStatus);
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;

            // Show the loading indicator
            UpdateWaveDots.ShowIndeterminate();

            var updateInfo = await _viewModel.ForceCheckUpdatesAsync();

            // Hide the indicator, update the text
            UpdateWaveDots.HideIndeterminate();

            if (!updateInfo.IsAvailable)
            {
                UpToDateIcon.Visibility = Visibility.Visible;
                CheckUpdatesButtonText.Text = LocalizationManager.Get("Settings_UpToDate_Button");
            }
            else
            {
                UpToDateIcon.Visibility = Visibility.Collapsed;
                CheckUpdatesButtonText.Text = LocalizationManager.Get("Settings_UpdateAvailable_Button", $"v{updateInfo.NewVersion}");
            }
        }
        catch (Exception ex)
        {
            UpdateWaveDots.HideIndeterminate();
            CheckUpdatesButtonText.Text = $"{LocalizationManager.Get("Settings_CheckForUpdates")} — {LocalizationManager.Get("Settings_UpdateCheckError")}";
            Logger.Error($"Update check error in button: {ex.Message}", ex, "SettingsPage");
        }
        finally
        {
            // Return the button to its active state after 3 seconds
            _ = ResetCheckUpdatesButtonAsync();
        }
    }

    private async Task ResetCheckUpdatesButtonAsync()
    {
        try
        {
            await Task.Delay(3000);
            Dispatcher.Invoke(() =>
            {
                CheckUpdatesButton.IsEnabled = true;
                UpToDateIcon.Visibility = Visibility.Collapsed;
                CheckUpdatesButtonText.Text = LocalizationManager.Get("Settings_CheckForUpdates");
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"ResetCheckUpdatesButtonAsync error: {ex.Message}", "SettingsPage");
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem)
        {
            AutoSaveSettings(AppearanceSaveStatus); // AutoSaveSettings will apply the theme itself
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        AutoSaveSettings(AppearanceSaveStatus);
    }

    private void DownloadSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        AutoSaveSettings(DownloadsSaveStatus);
    }

    private void ConsoleSetting_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        AutoSaveSettings(ConsoleSaveStatus);
    }
}
