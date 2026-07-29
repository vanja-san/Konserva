using CommunityToolkit.Mvvm.ComponentModel;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace Konserva.ViewModels;

/// <summary>
/// ViewModel для страницы создания сервера
/// </summary>
public partial class CreateServerViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IMcVersionsApi _versionsApi;
    private readonly IModLoaderService _modLoaderService;
    private readonly IServerInstaller _installer;
    private readonly IServerManager _serverManager;

    private CancellationTokenSource? _installCts;
    private CancellationTokenSource? _loaderLoadingCts;
    private bool _wasCancelled;
    public bool IsFindingCompatibleVersion { get; set; }
    private bool _isLoadingInProgress;

    private string[] _allMcVersions = [];
    private HashSet<string> _paperVersions = [];
    private HashSet<string>? _quiltSupportedVersions;
    private string? _lastLoadedModLoader;
    private string? _lastLoadedMcVersion;

    public CreateServerViewModel(
        IConfigService configService,
        IMcVersionsApi versionsApi,
        IModLoaderService modLoaderService,
        IServerInstaller installer,
        IServerManager serverManager)
    {
        _configService = configService;
        _versionsApi = versionsApi;
        _modLoaderService = modLoaderService;
        _installer = installer;
        _serverManager = serverManager;
    }

    // ─── Свойства ───────────────────────────────────────────────────

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _serverPath = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _selectedModLoader = "Vanilla";

    [ObservableProperty]
    private string _selectedMcVersion = string.Empty;

    [ObservableProperty]
    private string _selectedLoaderVersion = string.Empty;

    [ObservableProperty]
    private bool _showSnapshots;

    [ObservableProperty]
    private bool _isLoaderVersionEnabled;

    [ObservableProperty]
    private bool _isLoaderLoading;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<string> _mcVersions = new();

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<string> _loaderVersions = new();

    public bool IsCancelled => _wasCancelled;

    /// <summary>Полный список всех версий MC (нефильтрованный)</summary>
    public string[] McVersionList => _allMcVersions;

    /// <summary>Загруженные Paper-версии</summary>
    public HashSet<string> PaperVersions => _paperVersions;

    /// <summary>Загруженные Quilt-совместимые версии</summary>
    public HashSet<string>? QuiltSupportedVersions => _quiltSupportedVersions;

    // ─── Загрузка начальных данных ──────────────────────────────────

    public async Task InitializeAsync()
    {
        _allMcVersions = await _versionsApi.GetMcVersions();
        FilterMcVersions();
    }

    // ─── Фильтрация версий MC ───────────────────────────────────────

    public void FilterMcVersions(string? modLoaderOverride = null)
    {
        var modLoader = modLoaderOverride ?? SelectedModLoader;
        var versions = _modLoaderService.FilterMcVersions(
            _allMcVersions, modLoader, _paperVersions, _quiltSupportedVersions, ShowSnapshots);
        McVersions = new System.Collections.ObjectModel.ObservableCollection<string>(versions);
    }

    // ─── Загрузка Paper versions ────────────────────────────────────

    public async Task LoadPaperVersionsAsync()
    {
        if (_paperVersions.Count > 0) return;
        _paperVersions = await _versionsApi.GetPaperApiVersionsAsync();
    }

    // ─── Загрузка Quilt supported versions ──────────────────────────

    public async Task LoadQuiltSupportedVersionsAsync()
    {
        if (_quiltSupportedVersions != null) return;
        _quiltSupportedVersions = await _versionsApi.GetQuiltSupportedVersionsAsync();
    }

    public int GetPaperVersionsCount() => _paperVersions.Count;

    public HashSet<string>? GetQuiltSupportedVersions() => _quiltSupportedVersions;

    public Task<string[]> GetLoaderVersions(string modLoaderType, string mcVersion, bool showSnapshots)
        => _modLoaderService.GetLoaderVersionsAsync(modLoaderType, mcVersion, showSnapshots);

    // ─── Загрузка версий загрузчика ─────────────────────────────────

    public async Task LoadLoaderVersions(string modLoaderType, string mcVersion)
    {
        _loaderLoadingCts?.Cancel();
        _loaderLoadingCts?.Dispose();
        _loaderLoadingCts = new CancellationTokenSource();
        var cts = _loaderLoadingCts;

        if (_isLoadingInProgress &&
            _lastLoadedModLoader == modLoaderType && _lastLoadedMcVersion == mcVersion)
            return;

        if (IsFindingCompatibleVersion) return;

        _isLoadingInProgress = true;
        IsLoaderLoading = true;

        try
        {
            var versions = await _modLoaderService.GetLoaderVersionsAsync(modLoaderType, mcVersion, ShowSnapshots);

            if (cts.IsCancellationRequested) return;

            _lastLoadedModLoader = modLoaderType;
            _lastLoadedMcVersion = mcVersion;

            LoaderVersions = new System.Collections.ObjectModel.ObservableCollection<string>(versions.Length > 0 ? versions : []);
            IsLoaderVersionEnabled = versions.Length > 0;
            SelectedLoaderVersion = versions.Length > 0 ? versions[0] : string.Empty;
        }
        catch
        {
            LoaderVersions = new System.Collections.ObjectModel.ObservableCollection<string>();
            IsLoaderVersionEnabled = false;
        }
        finally
        {
            _isLoadingInProgress = false;
            IsLoaderLoading = false;
        }
    }

    // ─── Поиск совместимой версии ───────────────────────────────────

    public Task<string?> FindLastCompatibleMcVersionAsync(string modLoaderType, string currentMcVersion)
    {
        return _modLoaderService.FindCompatibleMcVersionAsync(modLoaderType, currentMcVersion, _allMcVersions, ShowSnapshots);
    }

    // ─── Валидация ──────────────────────────────────────────────────

    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ServerName))
            errors.Add(LocalizationManager.Get("CreateServer_Validation_NoName"));

        if (SelectedModLoader is "Forge" or "NeoForge" or "Fabric" or "Quilt" or "Paper")
        {
            if (LoaderVersions.Count == 0 || string.IsNullOrEmpty(SelectedLoaderVersion))
                errors.Add(LocalizationManager.Get("CreateServer_Validation_NoLoaderVersion"));
        }

        if (string.IsNullOrWhiteSpace(ServerPath))
            errors.Add(LocalizationManager.Get("CreateServer_Validation_NoFolder"));

        return errors;
    }

    public bool CanCreate => !IsInstalling && GetValidationErrors().Count == 0;

    // ─── Путь к серверу ─────────────────────────────────────────────

    public string GetDefaultServerPath()
    {
        try
        {
            var config = _configService.GetConfig();
            var dir = config.ServersDirectory;
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            var serversDir = Path.Combine(AppContext.BaseDirectory, "Servers");
            Directory.CreateDirectory(serversDir);
            return serversDir;
        }
    }

    public void SaveServersPath(string path)
    {
        try
        {
            var config = _configService.GetConfig();
            config.ServersDirectory = path;
            _configService.SaveConfig(config);
        }
        catch { /* ignore */ }
    }

    // ─── Импорт сервера ─────────────────────────────────────────────

    public Server? ImportServer(string serverPath, out string? errorMessage)
    {
        errorMessage = null;

        var existingServer = _serverManager.GetServers()
            .FirstOrDefault(s => s.Path.Equals(serverPath, StringComparison.OrdinalIgnoreCase));

        if (existingServer != null)
        {
            errorMessage = string.Format(LocalizationManager.Get("CreateServer_Import_Duplicate"), existingServer.Name);
            return null;
        }

        var jarFile = Directory.GetFiles(serverPath, "*.jar").FirstOrDefault();
        if (jarFile == null)
        {
            errorMessage = LocalizationManager.Get("CreateServer_Import_NoJar");
            return null;
        }

        var launchType = _installer.GetServerLaunchType(serverPath);
        var modLoader = launchType switch
        {
            ServerLaunchType.Forge => new ModLoader { Type = ModLoaderType.Forge },
            ServerLaunchType.NeoForge => new ModLoader { Type = ModLoaderType.NeoForge },
            ServerLaunchType.Fabric => new ModLoader { Type = ModLoaderType.Fabric },
            ServerLaunchType.Quilt => new ModLoader { Type = ModLoaderType.Quilt },
            _ => new ModLoader { Type = ModLoaderType.Vanilla }
        };

        var mcVersion = LocalizationManager.Get("Common_Unknown");
        var versionFile = Path.Combine(serverPath, "version.json");
        if (File.Exists(versionFile))
        {
            try
            {
                var json = File.ReadAllText(versionFile);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                    mcVersion = idElement.GetString() ?? "Unknown";
            }
            catch { /* ignore */ }
        }

        var serverName = Path.GetFileName(serverPath);
        return _serverManager.CreateServer(serverName, mcVersion, modLoader, serverPath);
    }

    // ─── Создание сервера ───────────────────────────────────────────

    public Server? CreateServer(string serverName, string serverPath, out string? errorMessage)
    {
        errorMessage = null;

        if (_serverManager.GetServers().Any(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = string.Format(LocalizationManager.Get("CreateServer_Error_DuplicateName"), serverName);
            return null;
        }

        var mcVersion = SelectedMcVersion;
        if (string.IsNullOrEmpty(mcVersion)) mcVersion = "1.20.4";

        var modLoaderType = ModLoaderType.Vanilla;
        if (Enum.TryParse(SelectedModLoader, true, out ModLoaderType parsedType))
            modLoaderType = parsedType;

        var modLoader = new ModLoader
        {
            Type = modLoaderType,
            Version = mcVersion,
            LoaderVersion = SelectedLoaderVersion
        };

        return _serverManager.CreateServer(serverName, mcVersion, modLoader, serverPath);
    }

    public async Task<McServerInstaller.InstallResult> InstallServerAsync(
        ModLoaderType modLoaderType, string mcVersion, string loaderVersion,
        string serverPath, int port, int ramMin, int ramMax,
        IProgress<string> progress)
    {
        _wasCancelled = false;
        _installCts = new CancellationTokenSource();
        IsInstalling = true;

        try
        {
            var result = await _installer.InstallServer(
                modLoaderType, mcVersion, loaderVersion, serverPath,
                port, ramMin, ramMax, progress, _installCts.Token);
            return result;
        }
        catch (OperationCanceledException)
        {
            _wasCancelled = true;
            return new McServerInstaller.InstallResult { Success = false, Error = "Cancelled" };
        }
        finally
        {
            IsInstalling = false;
            _installCts?.Dispose();
            _installCts = null;
        }
    }

    public void CancelInstall()
    {
        _wasCancelled = true;
        _installCts?.Cancel();
    }

    // ─── Helpers ────────────────────────────────────────────────────
    // Статические методы TryParseMcVersion, IsSnapshot, IsNeoForgeSnapshot
    // вынесены в общий класс McVersionHelper в Utilities.
    // Используйте McVersionHelper.* вместо этих методов.

    /// <summary>
    /// Результат валидации пути
    /// </summary>
    public sealed class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public PathValidationResult ValidateServerPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_PathEmpty") };

            var invalidPathChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidPathChars) >= 0)
                return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_InvalidChars") };

            if (path.Length > 260)
                return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_PathTooLong") };

            if (File.Exists(path))
                return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_FileNotFolder") };

            if (Directory.Exists(path))
            {
                var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();

                var systemPaths = new[]
                {
          Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant(),
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant(),
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)?.ToLowerInvariant() ?? ""
        };

                if (systemPaths.Any(sysPath => !string.IsNullOrEmpty(sysPath) && normalizedPath.StartsWith(sysPath)))
                    return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_SystemFolder") };

                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                    return new PathValidationResult { IsValid = true, ErrorMessage = string.Format(LocalizationManager.Get("CreateServer_Validation_FolderNotEmpty"), files.Length) };
            }
            else
            {
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_ParentNotFound") };

                try
                {
                    var tempPath = Path.Combine(path, ".konserva_test");
                    Directory.CreateDirectory(tempPath);
                    Directory.Delete(tempPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return new PathValidationResult { IsValid = false, ErrorMessage = LocalizationManager.Get("CreateServer_Validation_NoPermission") };
                }
            }

            return new PathValidationResult { IsValid = true, ErrorMessage = string.Empty };
        }
        catch (Exception ex)
        {
            return new PathValidationResult { IsValid = false, ErrorMessage = string.Format(LocalizationManager.Get("CreateServer_Validation_Error"), ex.Message) };
        }
    }

    public void Cleanup()
    {
        _installCts?.Cancel();
        _installCts?.Dispose();
        _installCts = null;
        _loaderLoadingCts?.Cancel();
        _loaderLoadingCts?.Dispose();
        _loaderLoadingCts = null;
    }
}
