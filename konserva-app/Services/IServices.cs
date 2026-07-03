using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Сервис конфигурации приложения
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
/// Сервис хранения данных серверов
/// </summary>
public interface IServerStorageService
{
    /// <summary>
    /// Путь к директории хранения серверов (берётся из конфига).
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
/// Сервис управления серверами
/// </summary>
public interface IServerManager
{
    event Action? OnServersChanged;
    event Action<Server, string>? OnServerStartError;  // Событие об ошибке запуска

    IReadOnlyList<Server> GetServers();
    Server? GetServer(string id);
    McServerProcess? GetProcess(string id);
    IReadOnlyList<McServerProcess> GetProcesses();
    Server CreateServer(string name, string mcVersion, ModLoader modLoader, string path);
    void SendCommand(string id, string command);
    void UpdateServer(Server server);
    (int total, int running, int stopped) GetStats();
    long GetTotalMemoryUsage();
    double GetTotalCpuUsage();
    Task StartServerAsync(string id, CancellationToken ct = default);
    Task StopServerAsync(string id, CancellationToken ct = default);
    Task DeleteServerAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// Сервис для работы с API версий Minecraft
/// </summary>
public interface IMcVersionsApi
{
    Task<string[]> GetMcVersions(CancellationToken ct = default);
    Task<string[]> GetForgeVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetFabricVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetQuiltVersions(string mcVersion, CancellationToken ct = default);
    Task<string[]> GetPaperVersions(string mcVersion, CancellationToken ct = default);
    Task<string> GetStringWithDecompressionAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Сервис для установки серверов
/// </summary>
public interface IServerInstaller
{
    Task<McServerInstaller.InstallResult> InstallServer(ModLoaderType modLoaderType, string mcVersion, string loaderVersion, string serverPath, int port, int ramMin, int ramMax, IProgress<string>? progress = null, CancellationToken ct = default);
    string FindServerJar(string path);
    string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType launchType = ServerLaunchType.Standard, int javaMajorVersion = 0, string? serverPath = null);
    ServerLaunchType GetServerLaunchType(string path);
}

/// <summary>
/// Сервис для управления Java
/// </summary>
public interface IJavaManagementService
{
    List<JavaInstallation> FindInstalledJava();
    JavaInstallation? GetJavaInfo(string javaPath);
    JavaInstallation? AddJava(string javaPath);
    bool RemoveJava(string javaId);
    bool SetDefaultJava(string javaId);
    Task<JavaInstallation?> GetCompatibleJavaAsync(string mcVersion, IServerInstaller installer, string serverPath, CancellationToken ct = default);

    /// <summary>
    /// Scans the system for all installed Java runtimes and adds new ones to config.
    /// Skips paths already present in the configuration.
    /// Returns the full updated list of Java installations from config.
    /// </summary>
    List<JavaInstallation> ScanAndAddJava();
}

/// <summary>
/// Сервис проверки обновлений приложения через version.json на GitHub
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Текущая версия приложения (из AssemblyVersion)</summary>
    string GetCurrentVersion();

    /// <summary>Проверить наличие обновлений</summary>
    Task<UpdateInfo> CheckAsync();
}

/// <summary>
/// Сервис скачивания и применения обновлений приложения
/// </summary>
public interface IAppUpdater
{
    /// <summary>Скачать и применить обновление</summary>
    Task<bool> ApplyAsync(UpdateInfo updateInfo, IProgress<double>? progress = null);
}
