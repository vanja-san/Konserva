using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace Konserva.Pages;

using Konserva.Services;

/// <summary>
/// Страница настроек приложения
/// </summary>
public partial class SettingsPage(IConfigService? configService = null) : Page
{
    private readonly IConfigService _configService = configService ?? App.ConfigService;
    private readonly JavaManagementService _javaService = new(configService ?? App.ConfigService);
    private bool _isUpdating; // Флаг для предотвращения рекурсивного сохранения
    private bool _isLoading = true; // Флаг загрузки страницы
    private int _updateIntervalHours = 24; // Текущий интервал проверки обновлений (часы)

    public SettingsPage() : this(null)
    {
        InitializeComponent();

        Title = LocalizationManager.Get("Settings_Title");
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();

        // Скрываем InfoBar при загрузке
        LanguageChangeInfoBar.IsOpen = false;
    }

    private void LoadSettings()
    {
        _isLoading = true; // Блокируем сохранение во время загрузки

        var config = _configService.GetConfig();

        ServersFolderPath.Text = config.ServersDirectory;

        // Загрузка Java в ItemsControl
        JavaItemsControl.ItemsSource = config.JavaInstallations;
        UpdateJavaEmptyVisibility();

        CheckUpdatesOnLaunchItem.IsChecked = !config.CheckUpdates;
        CheckUpdatesScheduledItem.IsChecked = config.CheckUpdates;
        CheckUpdatesModeButton.Content = config.CheckUpdates
            ? LocalizationManager.Get("Settings_CheckUpdates_Scheduled")
            : LocalizationManager.Get("Settings_CheckUpdates_OnLaunch");
        UpdateCheckModeVisibility(config.CheckUpdates);
        _updateIntervalHours = Math.Clamp(config.UpdateCheckIntervalHours, 1, 168);
        UpdateIntervalButton.Content = FormatInterval(_updateIntervalHours);

        MinimizeToTrayBox.IsChecked = config.MinimizeToTray;
        ShowTrayIconBox.IsChecked = config.ShowTrayIconAlways;

        // Загрузка темы
        var theme = config.Theme ?? "System";
        if (!ThemeComboBox.SelectItemByTag(theme))
            ThemeComboBox.SelectedIndex = 0;

        // Загрузка языка - принудительно выбираем элемент
        var language = config.Language ?? "System";
        if (!LanguageComboBox.SelectItemByTag(language))
            LanguageComboBox.SelectedIndex = 0;

        // Загрузка источника загрузки
        var downloadSource = config.DownloadSource ?? "VanillaApi";
        if (!DownloadSourceComboBox.SelectItemByTag(downloadSource))
            DownloadSourceComboBox.SelectedIndex = 0;

        _isLoading = false; // Разрешаем сохранение после загрузки

        // Скрываем InfoBar при загрузке
        LanguageChangeInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Обновляет видимость placeholder текста, когда нет Java
    /// </summary>
    private void UpdateJavaEmptyVisibility()
    {
        JavaEmptyText.Visibility = JavaItemsControl.Items.Count == 0
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

            var config = _configService.GetConfig();
            var languageChanged = false;

            config.CheckUpdates = CheckUpdatesScheduledItem.IsChecked;
            config.UpdateCheckIntervalHours = _updateIntervalHours;
            config.MinimizeToTray = MinimizeToTrayBox.IsChecked ?? true;
            config.ShowTrayIconAlways = ShowTrayIconBox.IsChecked ?? false;

            // Сохранение источника загрузки
            if (DownloadSourceComboBox.SelectedItem is ComboBoxItem downloadItem)
            {
                config.DownloadSource = (string)downloadItem.Tag;
            }

            // Сохранение темы
            if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem)
            {
                var newTheme = (string)themeItem.Tag;
                if (config.Theme != newTheme)
                {
                    config.Theme = newTheme;
                    // Применяем тему сразу
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ApplyTheme(newTheme);
                }
            }

            // Сохранение языка
            if (LanguageComboBox.SelectedItem is ComboBoxItem languageItem)
            {
                var selectedLanguage = (string)languageItem.Tag;

                // Проверяем, изменился ли язык
                if (config.Language != selectedLanguage)
                {
                    config.Language = selectedLanguage;
                    languageChanged = true;
                }
            }

            _configService.SaveConfig(config);

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
    /// Показ уведомления об успешном сохранении (через бадж справа сверху)
    /// </summary>
    private void ShowSaveNotification()
    {
        App.MainWindow?.ShowSaveBadge();
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
            InitialDirectory = _configService.GetConfig().ServersDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            var config = _configService.GetConfig();
            config.ServersDirectory = dialog.FolderName;
            _configService.SaveConfig(config);
            ServersFolderPath.Text = dialog.FolderName;
            ShowSaveNotification();
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
                var java = _javaService.AddJava(dialog.FileName);

                if (java != null)
                {
                    // Обновляем список Java
                    LoadSettings();

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
                    var removed = _javaService.RemoveJava(java.Id);
                    if (removed)
                    {
                        LoadSettings();
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

            // Обновляем checked-состояние пунктов меню
            CheckUpdatesOnLaunchItem.IsChecked = !isScheduled;
            CheckUpdatesScheduledItem.IsChecked = isScheduled;

            // Обновляем текст на кнопке
            CheckUpdatesModeButton.Content = isScheduled
                ? LocalizationManager.Get("Settings_CheckUpdates_Scheduled")
                : LocalizationManager.Get("Settings_CheckUpdates_OnLaunch");

            // Показываем/скрываем настройку интервала
            UpdateCheckModeVisibility(isScheduled);

            AutoSaveSettings();
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

    private void MinimizeToTrayBox_Checked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void MinimizeToTrayBox_Unchecked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void ShowTrayIconBox_Checked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
        App.MainWindow?.UpdateTrayIconVisibility();
    }

    private void ShowTrayIconBox_Unchecked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
        App.MainWindow?.UpdateTrayIconVisibility();
    }

    private void UpdateIntervalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string tag && int.TryParse(tag, out var hours))
        {
            _updateIntervalHours = hours;
            UpdateIntervalButton.Content = FormatInterval(hours);
            AutoSaveSettings();
        }
    }

    private static string FormatInterval(int hours)
    {
        if (hours <= 24)
            return $"{hours} ч";
        else
            return $"{hours / 24} д";
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;

            // Показываем анимацию точек рядом с текстом
            UpdateWaveDots.Visibility = Visibility.Visible;
            UpdateWaveDots.Start();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null)
                return;

            var updateInfo = await mainWindow.ForceCheckForUpdatesAsync();

            // Останавливаем анимацию, убираем точки, обновляем текст
            UpdateWaveDots.Stop();
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
            UpdateWaveDots.Stop();
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

    /// <summary>
    /// Применение темы
    /// </summary>
    private void ApplyTheme(string theme)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;
        mainWindow?.ApplyTheme(theme);
    }
}
