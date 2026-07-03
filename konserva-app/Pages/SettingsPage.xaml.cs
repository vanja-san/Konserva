using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace Konserva.Pages;

/// <summary>
/// Страница настроек приложения
/// </summary>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private bool _isUpdating; // Флаг для предотвращения рекурсивного сохранения
    private bool _isLoading = true; // Флаг загрузки страницы

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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadSettings();
        RefreshUI();

        // Скрываем InfoBar при загрузке
        LanguageChangeInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Обновляет UI из ViewModel
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

        // Загрузка темы
        if (!ThemeComboBox.SelectItemByTag(_viewModel.Theme))
            ThemeComboBox.SelectedIndex = 0;

        // Загрузка языка
        if (!LanguageComboBox.SelectItemByTag(_viewModel.Language))
            LanguageComboBox.SelectedIndex = 0;

        // Загрузка источника загрузки
        if (!DownloadSourceComboBox.SelectItemByTag(_viewModel.DownloadSource))
            DownloadSourceComboBox.SelectedIndex = 0;

        _isLoading = false;
        LanguageChangeInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Обновляет видимость placeholder текста, когда нет Java
    /// </summary>
    private void UpdateJavaEmptyVisibility()
    {
        JavaEmptyText.Visibility = _viewModel.IsJavaEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Автосохранение настроек
    /// </summary>
    private void AutoSaveSettings()
    {
        if (_isLoading || _isUpdating) return; // Защита от сохранения при загрузке и рекурсии

        try
        {
            _isUpdating = true;

            // Синхронизируем UI → ViewModel перед сохранением
            _viewModel.CheckUpdatesScheduled = CheckUpdatesScheduledItem.IsChecked;
            _viewModel.UpdateIntervalHours = ParseIntervalFromButton();
            _viewModel.MinimizeToTrayMode = GetSelectedMinimizeToTrayMode();

            if (DownloadSourceComboBox.SelectedItem is ComboBoxItem downloadItem)
                _viewModel.DownloadSource = (string)downloadItem.Tag;

            if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem)
            {
                var newTheme = (string)themeItem.Tag;
                if (_viewModel.Theme != newTheme)
                {
                    _viewModel.Theme = newTheme;
                    // Применяем тему сразу
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ApplyTheme(newTheme);
                }
            }

            if (LanguageComboBox.SelectedItem is ComboBoxItem languageItem)
            {
                _viewModel.Language = (string)languageItem.Tag;
            }

            var languageChanged = _viewModel.SaveSettings();

            // Показываем InfoBar только если язык изменился
            if (languageChanged)
            {
                LanguageChangeInfoBar.IsOpen = true;
            }

            // Показ уведомления об успешном сохранении
            ShowSaveNotification();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Парсит интервал из текста кнопки
    /// </summary>
    private int ParseIntervalFromButton()
    {
        var text = UpdateIntervalButton.Content?.ToString();
        if (string.IsNullOrEmpty(text)) return 24;

        // Пример: "24 ч" или "2 д"
        if (text.EndsWith(" ч") && int.TryParse(text[..^2], out var hours))
            return hours;
        if (text.EndsWith(" д") && int.TryParse(text[..^2], out var days))
            return days * 24;

        return 24;
    }

    /// <summary>
    /// Показ уведомления об успешном сохранении (через бадж справа сверху)
    /// </summary>
    private void ShowSaveNotification()
    {
        Ioc.Default.GetService<MainWindow>()?.ShowSaveBadge();
    }

    /// <summary>
    /// Асинхронно скрывает InfoBar через указанную задержку.
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
            ShowSaveNotification();
        }
    }

    private async void ScanJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanJavaButton.IsEnabled = false;
            ScanJavaButton.ToolTip = LocalizationManager.Get("Settings_Java_Scanning");
            JavaScanResultText.Visibility = Visibility.Collapsed;

            var totalFound = await Task.Run(() => _viewModel.ScanJava());

            RefreshUI();

            if (totalFound > 0)
            {
                JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_Success", totalFound.ToString());
                JavaScanResultText.Visibility = Visibility.Visible;

                // Auto-hide after 5 seconds
                _ = AutoHideJavaScanResultAsync();
            }
            else
            {
                JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_NoneFound");
                JavaScanResultText.Visibility = Visibility.Visible;

                _ = AutoHideJavaScanResultAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ScanJava_Click error: {ex.Message}", ex, "SettingsPage");
            JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_Error");
            JavaScanResultText.Visibility = Visibility.Visible;
            _ = AutoHideJavaScanResultAsync();
        }
        finally
        {
            ScanJavaButton.IsEnabled = true;
            ScanJavaButton.ToolTip = LocalizationManager.Get("Settings_Java_Scan");
        }
    }

    private async Task AutoHideJavaScanResultAsync()
    {
        try
        {
            await Task.Delay(Constants.InfoBarAutoCloseDelayMs);
            Dispatcher.Invoke(() => JavaScanResultText.Visibility = Visibility.Collapsed);
        }
        catch (Exception ex)
        {
            Logger.Warning($"AutoHideJavaScanResultAsync error: {ex.Message}", "SettingsPage");
        }
    }

    private async void AddJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Get("Settings_SelectJava"),
                Filter = LocalizationManager.Get("Settings_JavaFilter"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            if (dialog.ShowDialog() == true)
            {
                var java = _viewModel.AddJava(dialog.FileName);

                if (java != null)
                {
                    // Обновляем список Java
                    RefreshUI();

                    // Показываем InfoBar об успешной установке
                    JavaSuccessInfoBar.Title = LocalizationManager.Get("Settings_JavaAdded");
                    JavaSuccessInfoBar.Message = $"{LocalizationManager.Get("Settings_JavaVersion")}: {java.Version}\n{LocalizationManager.Get("Settings_JavaPath")}: {java.Path}";
                    JavaSuccessInfoBar.IsOpen = true;

                    // Автоматически закрываем через 5 секунд
                    _ = AutoHideInfoBarAsync(JavaSuccessInfoBar, Constants.InfoBarAutoCloseDelayMs);
                }
                else
                {
                    await UiHelper.ShowWarning(LocalizationManager.Get("Settings_JavaInvalid"));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AddJava_Click error: {ex.Message}", ex, "SettingsPage");
        }
    }

    private async void DeleteJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: JavaInstallation java })
            {
                var result = await UiHelper.ShowConfirm(
                    $"{LocalizationManager.Get("Settings_Java_Delete_Confirm_Message")}\n\n{java.DisplayName}",
                    LocalizationManager.Get("Settings_Java_Delete_Confirm_Title"));

                if (result == ContentDialogResult.Primary)
                {
                    var removed = _viewModel.RemoveJava(java.Id);
                    if (removed)
                    {
                        RefreshUI();
                    }
                    else
                    {
                        await UiHelper.ShowWarning(LocalizationManager.Get("Settings_Java_Delete_Failed"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"DeleteJava_Click error: {ex.Message}", ex, "SettingsPage");
        }
    }

    private void DefaultRamMin_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void DefaultRamMax_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void CheckUpdatesModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag)
        {
            var isScheduled = tag == "Scheduled";

            _viewModel.SetCheckUpdatesMode(isScheduled);

            // Обновляем UI
            CheckUpdatesOnLaunchItem.IsChecked = !isScheduled;
            CheckUpdatesScheduledItem.IsChecked = isScheduled;
            CheckUpdatesModeButton.Content = _viewModel.CheckUpdatesModeText;
            UpdateCheckModeVisibility(isScheduled);

            AutoSaveSettings();

            // Перезапускаем фоновый цикл проверки обновлений
            Ioc.Default.GetService<MainWindow>()?.StartUpdateCheckLoop();
        }
    }

    /// <summary>
    /// Показывает или скрывает карточку выбора интервала в зависимости от режима.
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
            // Обновляем UI
            MinimizeToTrayModeButton.Content = GetMinimizeToTrayModeText(tag);
            UpdateMinimizeToTrayMenuChecks(tag);

            // Сохраняем
            _viewModel.MinimizeToTrayMode = tag;
            AutoSaveSettings();
        }
    }

    /// <summary>
    /// Возвращает локализованный текст для указанного режима трея.
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
    /// Обновляет галочки в меню выбора режима трея.
    /// </summary>
    private void UpdateMinimizeToTrayMenuChecks(string mode)
    {
        MinimizeToTrayNoneItem.IsChecked = mode == "None";
        MinimizeToTrayOnCloseItem.IsChecked = mode == "OnClose";
        MinimizeToTrayOnMinimizeItem.IsChecked = mode == "OnMinimize";
        MinimizeToTrayAlwaysItem.IsChecked = mode == "Always";
    }

    /// <summary>
    /// Возвращает выбранный режим трея по галочкам в меню.
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
            AutoSaveSettings();
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;

            // Показываем индикатор загрузки
            UpdateWaveDots.Visibility = Visibility.Visible;

            var updateInfo = await _viewModel.ForceCheckUpdatesAsync();

            // Убираем индикатор, обновляем текст
            UpdateWaveDots.Visibility = Visibility.Collapsed;

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
            UpdateWaveDots.Visibility = Visibility.Collapsed;
            CheckUpdatesButtonText.Text = $"{LocalizationManager.Get("Settings_CheckForUpdates")} — {LocalizationManager.Get("Settings_UpdateCheckError")}";
            Logger.Error($"Update check error in button: {ex.Message}", ex, "SettingsPage");
        }
        finally
        {
            // Возвращаем кнопку в активное состояние через 3 секунды
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
            AutoSaveSettings(); // AutoSaveSettings сам применит тему
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        AutoSaveSettings();
    }

    private void DownloadSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        AutoSaveSettings();
    }
}
