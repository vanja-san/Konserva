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

    IReadOnlyList<Server> GetServers();
    Server? GetServer(string id);
    McServerProcess? GetProcess(string id);
    IReadOnlyList<McServerProcess> GetProcesses();
    Server CreateServer(string name, string mcVersion, ModLoader modLoader, string path);
    void StartServer(string id);
    void StopServer(string id);
    void SendCommand(string id, string command);
    void UpdateServer(Server server);
    (int total, int running, int stopped) GetStats();
    long GetTotalMemoryUsage();
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
    Task<string> GetStringWithDecompressionAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Сервис для установки серверов
/// </summary>
public interface IServerInstaller
{
    Task<bool> InstallServer(Server server, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string?> FindServerJar(string path);
    string BuildLaunchArgs(string jarFile, ServerSettings settings, McServerInstaller.ServerLaunchType launchType);
    McServerInstaller.ServerLaunchType GetServerLaunchType(string path);
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
}
