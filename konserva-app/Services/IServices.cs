using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Application configuration service
/// </summary>
public interface IConfigService
{
    AppConfig GetConfig();
    void SaveConfig(AppConfig config);
    void UpdateConfig(Action<AppConfig> updateAction);
    Task<AppConfig> GetConfigAsync(CancellationToken ct = default);
    Task SaveConfigAsync(AppConfig config, CancellationToken ct = default);
}

/// <summary>
/// Server data storage service
/// </summary>
public interface IServerStorageService
{
    /// <summary>
    /// Path to the server storage directory (taken from the config).
    /// </summary>
    string ServersPath { get; }

    List<Server> LoadServers();
    void SaveServers(List<Server> servers);
    void AddServer(Server server);
    void UpdateServer(Server server);
    void DeleteServer(string serverId);
    Task<List<Server>> LoadServersAsync(CancellationToken ct = default);
    Task SaveServersAsync(List<Server> servers, CancellationToken ct = default);
}

/// <summary>
/// Server management service
/// </summary>
public interface IServerManager
{
    event Action? OnServersChanged;
    event Action<Server, string>? OnServerStartError;  // Event raised on a start failure

    IReadOnlyList<Server> GetServers();
    Server? GetServer(string id);
    McServerProcess? GetProcess(string id);
    IReadOnlyList<McServerProcess> GetProcesses();
    Server CreateServer(string name, string mcVersion, ModLoader modLoader, string path);
    void SendCommand(string id, string command);
    void UpdateServer(Server server);
    bool RenameServerFolder(string serverId, string newName, out string? error);
    (int total, int running, int stopped) GetStats();
    long GetTotalMemoryUsage();
    double GetTotalCpuUsage();
    Task StartServerAsync(string id, CancellationToken ct = default);
    Task StopServerAsync(string id, CancellationToken ct = default);
    Task DeleteServerAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Flushes pending server-state writes to disk and stops the background writer.
    /// Called on application shutdown.
    /// </summary>
    void FlushPendingWrites();
}

/// <summary>
/// Service for working with the Minecraft version APIs
/// </summary>
public interface IMcVersionsApi
{
    Task<string[]> GetMcVersions(CancellationToken ct = default);
    Task<HashSet<string>> GetQuiltSupportedVersionsAsync(CancellationToken ct = default);
    Task<HashSet<string>> GetPaperApiVersionsAsync(CancellationToken ct = default);
    Task<string[]> GetForgeVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetFabricVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetQuiltVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetPaperVersions(string mcVersion, CancellationToken ct = default);
    Task<string> GetStringWithDecompressionAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Service for installing servers
/// </summary>
public interface IServerInstaller
{
    Task<McServerInstaller.InstallResult> InstallServer(ModLoaderType modLoaderType, string mcVersion, string loaderVersion, string serverPath, int port, int ramMin, int ramMax, IProgress<string>? progress = null, CancellationToken ct = default);
    string FindServerJar(string path);
    string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType launchType = ServerLaunchType.Standard, int javaMajorVersion = 0, string? serverPath = null);
    ServerLaunchType GetServerLaunchType(string path);
}

/// <summary>
/// Service for Java management
/// </summary>
public interface IJavaManagementService
{
    List<JavaInstallation> FindInstalledJava();
    JavaInstallation? GetJavaInfo(string javaPath);
    JavaInstallation? AddJava(string javaPath);
    bool RemoveJava(string javaId);
    bool SetDefaultJava(string javaId);

    /// <summary>
    /// Scans the system for all installed Java runtimes.
    /// Removes config entries whose Java file no longer exists.
    /// Updates existing entries to use javaw.exe when available.
    /// Adds new installations not yet in the configuration.
    /// Returns the full updated list of Java installations from config.
    /// </summary>
    List<JavaInstallation> ScanAndAddJava();
}

/// <summary>
/// Service for checking application updates via version.json on GitHub
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Current application version (from AssemblyVersion)</summary>
    string GetCurrentVersion();

    /// <summary>Check for available updates</summary>
    Task<UpdateInfo> CheckAsync();
}

/// <summary>
/// Service for downloading and applying application updates
/// </summary>
public interface IAppUpdater
{
    /// <summary>Download and apply an update</summary>
    Task<bool> ApplyAsync(UpdateInfo updateInfo, IProgress<double>? progress = null);
}
