using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;

namespace Konserva.ViewModels;

/// <summary>
/// ViewModel для страницы настроек
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
  private readonly IConfigService _configService;
  private readonly IJavaManagementService _javaService;
  private readonly IUpdateService _updateService;

  private bool _isLoading;

  public SettingsViewModel(IConfigService configService, IJavaManagementService javaService, IUpdateService updateService)
  {
    _configService = configService;
    _javaService = javaService;
    _updateService = updateService;
  }

  // ─── Свойства конфига ───────────────────────────────────────────

  [ObservableProperty]
  private string _serversDirectory = string.Empty;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CheckUpdatesOnLaunch))]
  [NotifyPropertyChangedFor(nameof(CheckUpdatesModeText))]
  [NotifyPropertyChangedFor(nameof(IsScheduledModeVisible))]
  private bool _checkUpdatesScheduled;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(UpdateIntervalText))]
  private int _updateIntervalHours = 24;

  [ObservableProperty]
  private bool _minimizeToTray = true;

  [ObservableProperty]
  private bool _showTrayIconAlways;

  [ObservableProperty]
  private string _theme = "System";

  [ObservableProperty]
  private string _language = "System";

  [ObservableProperty]
  private string _downloadSource = "VanillaApi";

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsJavaEmpty))]
  private System.Collections.ObjectModel.ObservableCollection<JavaInstallation> _javaInstallations = new();

  // ─── Производные свойства ───────────────────────────────────────

  public bool IsJavaEmpty => JavaInstallations.Count == 0;

  public bool CheckUpdatesOnLaunch => !CheckUpdatesScheduled;

  public bool IsScheduledModeVisible => CheckUpdatesScheduled;

  public string CheckUpdatesModeText => CheckUpdatesScheduled
      ? LocalizationManager.Get("Settings_CheckUpdates_Scheduled")
      : LocalizationManager.Get("Settings_CheckUpdates_OnLaunch");

  public string UpdateIntervalText => FormatInterval(UpdateIntervalHours);

  // ─── Загрузка / Сохранение ──────────────────────────────────────

  /// <summary>
  /// Загружает настройки из конфига
  /// </summary>
  public void LoadSettings()
  {
    _isLoading = true;

    var config = _configService.GetConfig();

    ServersDirectory = config.ServersDirectory;
    JavaInstallations = new System.Collections.ObjectModel.ObservableCollection<JavaInstallation>(config.JavaInstallations);
    CheckUpdatesScheduled = config.CheckUpdates;
    UpdateIntervalHours = Math.Clamp(config.UpdateCheckIntervalHours, 1, 168);
    MinimizeToTray = config.MinimizeToTray;
    ShowTrayIconAlways = config.ShowTrayIconAlways;
    Theme = config.Theme ?? "System";
    Language = config.Language ?? "System";
    DownloadSource = config.DownloadSource ?? "VanillaApi";

    _isLoading = false;
  }

  /// <summary>
  /// Сохраняет настройки. Возвращает true, если язык был изменён.
  /// </summary>
  public bool SaveSettings()
  {
    if (_isLoading) return false;

    var config = _configService.GetConfig();
    var languageChanged = false;

    config.CheckUpdates = CheckUpdatesScheduled;
    config.UpdateCheckIntervalHours = UpdateIntervalHours;
    config.MinimizeToTray = MinimizeToTray;
    config.ShowTrayIconAlways = ShowTrayIconAlways;
    config.DownloadSource = DownloadSource;
    config.Theme = Theme;

    if (config.Language != Language)
    {
      config.Language = Language;
      languageChanged = true;
    }

    config.ServersDirectory = ServersDirectory;
    config.JavaInstallations = new System.Collections.ObjectModel.ObservableCollection<JavaInstallation>(JavaInstallations);

    _configService.SaveConfig(config);

    return languageChanged;
  }

  /// <summary>
  /// Устанавливает режим проверки обновлений
  /// </summary>
  public void SetCheckUpdatesMode(bool isScheduled)
  {
    CheckUpdatesScheduled = isScheduled;
  }

  /// <summary>
  /// Устанавливает интервал проверки обновлений
  /// </summary>
  public void SetUpdateInterval(int hours)
  {
    UpdateIntervalHours = hours;
  }

  /// <summary>
  /// Меняет папку серверов
  /// </summary>
  public void SetServersDirectory(string path)
  {
    ServersDirectory = path;
    var config = _configService.GetConfig();
    config.ServersDirectory = path;
    _configService.SaveConfig(config);
  }

  // ─── Java ───────────────────────────────────────────────────────

  /// <summary>
  /// Добавляет Java-установку по пути к java.exe
  /// </summary>
  public JavaInstallation? AddJava(string javaPath)
  {
    var java = _javaService.AddJava(javaPath);
    if (java != null)
    {
      LoadSettings(); // перезагружаем список
    }
    return java;
  }

  /// <summary>
  /// Удаляет Java-установку по Id
  /// </summary>
  public bool RemoveJava(string javaId)
  {
    var removed = _javaService.RemoveJava(javaId);
    if (removed)
    {
      LoadSettings();
    }
    return removed;
  }

  // ─── Проверка обновлений ────────────────────────────────────────

  /// <summary>
  /// Принудительная проверка обновлений
  /// </summary>
  public async Task<UpdateInfo> ForceCheckUpdatesAsync()
  {
    return await _updateService.ForceCheckAsync();
  }

  // ─── Helpers ────────────────────────────────────────────────────

  private static string FormatInterval(int hours)
  {
    if (hours <= 24)
      return $"{hours} ч";
    else
      return $"{hours / 24} д";
  }
}
