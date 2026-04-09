using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace Konserva.Pages;

using Konserva.Services;

/// <summary>
/// Страница настроек приложения
/// </summary>
public partial class SettingsPage(IConfigService? configService = null) : Page
{
    private readonly IConfigService _configService = configService ?? App.ConfigService;
    private bool _isUpdating; // Флаг для предотвращения рекурсивного сохранения
    private bool _isLoading = true; // Флаг загрузки страницы

    public SettingsPage() : this(null)
    {
        InitializeComponent();
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

        // Загрузка Java в ComboBox
        JavaComboBox.Items.Clear();
        JavaComboBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.Get("Common_NotSelected"), Tag = (JavaInstallation?)null });

        foreach (var java in config.JavaInstallations)
        {
            JavaComboBox.Items.Add(new ComboBoxItem
            {
                Content = java.DisplayName,
                Tag = java
            });
        }

        // Выбор выбранной Java по умолчанию
        if (!string.IsNullOrEmpty(config.DefaultJavaId))
        {
            var defaultJava = config.JavaInstallations.FirstOrDefault(j => j.Id == config.DefaultJavaId);
            if (defaultJava != null)
            {
                JavaComboBox.SelectedItem = JavaComboBox.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(item => ((JavaInstallation)item.Tag)?.Id == defaultJava.Id);
            }
        }

        if (JavaComboBox.SelectedItem == null)
            JavaComboBox.SelectedIndex = 0;

        CheckUpdatesBox.IsChecked = config.CheckUpdates;

        // Загрузка темы
        var theme = config.Theme ?? "System";
        ThemeComboBox.SelectedIndex = -1; // Сброс
        for (int i = 0; i < ThemeComboBox.Items.Count; i++)
        {
            if (ThemeComboBox.Items[i] is ComboBoxItem item && (string)item.Tag == theme)
            {
                ThemeComboBox.SelectedIndex = i;
                break;
            }
        }
        if (ThemeComboBox.SelectedIndex == -1)
            ThemeComboBox.SelectedIndex = 0;

        // Загрузка языка - принудительно выбираем элемент
        var language = config.Language ?? "System";
        LanguageComboBox.SelectedIndex = -1; // Сброс
        for (int i = 0; i < LanguageComboBox.Items.Count; i++)
        {
            if (LanguageComboBox.Items[i] is ComboBoxItem item && (string)item.Tag == language)
            {
                LanguageComboBox.SelectedIndex = i;
                break;
            }
        }
        if (LanguageComboBox.SelectedIndex == -1)
            LanguageComboBox.SelectedIndex = 0;

        _isLoading = false; // Разрешаем сохранение после загрузки

        // Скрываем InfoBar при загрузке
        LanguageChangeInfoBar.IsOpen = false;
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

            // Сохранение выбранной Java
            if (JavaComboBox.SelectedItem is ComboBoxItem selectedItem &&
                selectedItem.Tag is JavaInstallation selectedJava)
            {
                config.DefaultJavaId = selectedJava.Id;
            }
            else
            {
                config.DefaultJavaId = null;
            }

            config.CheckUpdates = CheckUpdatesBox.IsChecked ?? false;

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
    /// Показ уведомления об успешном сохранении
    /// </summary>
    private void ShowSaveNotification()
    {
        SaveNotification.Visibility = Visibility.Visible;

        // Автоматическое скрытие через 2 секунды
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                SaveNotification.Visibility = Visibility.Collapsed;
            });
        });
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
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Settings_SelectJava"),
            Filter = LocalizationManager.Get("Settings_JavaFilter"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog() == true)
        {
            var javaService = new JavaManagementService(_configService);
            var java = javaService.AddJava(dialog.FileName);

            if (java != null)
            {
                // Обновляем ComboBox с новой Java
                LoadSettings();

                // Выбираем добавленную Java
                var selectedItem = JavaComboBox.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(item => ((JavaInstallation)item.Tag)?.Id == java.Id);

                if (selectedItem != null)
                    JavaComboBox.SelectedItem = selectedItem;

                // Показываем InfoBar об успешной установке
                JavaSuccessInfoBar.Title = LocalizationManager.Get("Settings_JavaAdded");
                JavaSuccessInfoBar.Message = $"{LocalizationManager.Get("Settings_JavaVersion")}: {java.Version}\n{LocalizationManager.Get("Settings_JavaPath")}: {java.Path}";
                JavaSuccessInfoBar.IsOpen = true;

                // Автоматически закрываем через 5 секунд
                _ = Task.Delay(Constants.InfoBarAutoCloseDelayMs).ContinueWith(_ =>
                {
                    this.Invoke(() =>
                    {
                        JavaSuccessInfoBar.IsOpen = false;
                    });
                });
            }
            else
            {
                await UiHelper.ShowWarning(LocalizationManager.Get("Settings_JavaInvalid"));
            }
        }
    }

    private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void DefaultRamMin_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void DefaultRamMax_TextChanged(object sender, TextChangedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void CheckUpdatesBox_Checked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void CheckUpdatesBox_Unchecked(object sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;
            CheckUpdatesButton.Content = "...";

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null)
                return;

            var updateInfo = await mainWindow.ForceCheckForUpdatesAsync();

            if (!updateInfo.IsAvailable)
            {
                CheckUpdatesButton.Content = LocalizationManager.Get("Settings_UpToDate_Button");
            }
            else
            {
                CheckUpdatesButton.Content = LocalizationManager.Get("Settings_UpdateAvailable_Button", $"v{updateInfo.NewVersion}");
            }
        }
        catch (Exception ex)
        {
            CheckUpdatesButton.Content = $"{LocalizationManager.Get("Settings_CheckForUpdates")} — {LocalizationManager.Get("Settings_UpdateCheckError")}";
            Logger.Error($"Update check error in button: {ex.Message}", ex, "SettingsPage");
        }
        finally
        {
            // Возвращаем кнопку в активное состояние через 3 секунды
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    CheckUpdatesButton.IsEnabled = true;
                    CheckUpdatesButton.Content = LocalizationManager.Get("Settings_CheckForUpdates");
                });
            });
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

    /// <summary>
    /// Применение темы
    /// </summary>
    private void ApplyTheme(string theme)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;
        mainWindow?.ApplyTheme(theme);
    }
}
