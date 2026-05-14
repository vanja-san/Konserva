using System.IO;
using System.Text;

namespace Konserva.Models;

/// <summary>
/// Модель server.properties Minecraft сервера
/// </summary>
public class ServerProperties
{
    /// <summary>
    /// Ключи свойств, которые были найдены в файле
    /// </summary>
    public HashSet<string> FoundKeys { get; } = new();

    // ===== Основные настройки =====
    public int ServerPort { get; set; } = 25565;
    public string ServerIp { get; set; } = "";
    public int MaxPlayers { get; set; } = 20;
    public string Motd { get; set; } = "A Minecraft Server";
    public int ViewDistance { get; set; } = 10;
    public int SimulationDistance { get; set; } = 10;
    public int PauseWhenEmptySeconds { get; set; } = 60;

    // ===== Режим игры =====
    public string Gamemode { get; set; } = "survival";
    public bool ForceGamemode { get; set; } = false;
    public bool Hardcore { get; set; } = false;
    public string Difficulty { get; set; } = "easy";
    public bool AllowFlight { get; set; } = false;
    public bool Pvp { get; set; } = true;

    // ===== Мир =====
    public string LevelName { get; set; } = "world";
    public string LevelSeed { get; set; } = "";
    public string LevelType { get; set; } = "minecraft:normal";
    public string GeneratorSettings { get; set; } = "{}";
    public bool GenerateStructures { get; set; } = true;
    public int MaxWorldSize { get; set; } = 29999984;
    public int SpawnProtection { get; set; } = 16;
    public int SpawnRadius { get; set; } = 10;

    // ===== Сеть =====
    public bool OnlineMode { get; set; } = true;
    public bool EnforceSecureProfile { get; set; } = true;
    public bool PreventProxyConnections { get; set; } = false;
    public int NetworkCompressionThreshold { get; set; } = 256;
    public int MaxTickTime { get; set; } = 60000;
    public int PlayerIdleTimeout { get; set; } = 0;
    public int RateLimit { get; set; } = 0;
    public bool EnableStatus { get; set; } = true;
    public bool AcceptsTransfers { get; set; } = false;
    public int StatusHeartbeatInterval { get; set; } = 0;

    // ===== Whitelist =====
    public bool WhiteList { get; set; } = false;
    public bool EnforceWhitelist { get; set; } = false;

    // ===== RCON =====
    public bool EnableRcon { get; set; } = false;
    public string RconPassword { get; set; } = "";
    public int RconPort { get; set; } = 25575;

    // ===== Query =====
    public bool EnableQuery { get; set; } = false;
    public int QueryPort { get; set; } = 25565;

    // ===== Management Server =====
    public bool ManagementServerEnabled { get; set; } = false;
    public string ManagementServerHost { get; set; } = "localhost";
    public int ManagementServerPort { get; set; } = 0;
    public string ManagementServerAllowedOrigins { get; set; } = "";
    public string ManagementServerSecret { get; set; } = "";
    public bool ManagementServerTlsEnabled { get; set; } = true;
    public string ManagementServerTlsKeystore { get; set; } = "";
    public string ManagementServerTlsKeystorePassword { get; set; } = "";

    // ===== Resource Pack =====
    public string ResourcePack { get; set; } = "";
    public string ResourcePackSha1 { get; set; } = "";
    public string ResourcePackId { get; set; } = "";
    public string ResourcePackPrompt { get; set; } = "";
    public bool RequireResourcePack { get; set; } = false;

    // ===== Логи и мониторинг =====
    public bool LogIps { get; set; } = true;
    public bool BroadcastConsoleToOps { get; set; } = true;
    public bool BroadcastRconToOps { get; set; } = true;
    public bool HideOnlinePlayers { get; set; } = false;
    public bool EnableJmxMonitoring { get; set; } = false;
    public bool EnableCodeOfConduct { get; set; } = false;
    public string BugReportLink { get; set; } = "";

    // ===== Производительность =====
    public bool SyncChunkWrites { get; set; } = true;
    public bool UseNativeTransport { get; set; } = true;
    public int MaxChainedNeighborUpdates { get; set; } = 1000000;
    public int EntityBroadcastRangePercentage { get; set; } = 100;
    public string RegionFileCompression { get; set; } = "deflate";

    // ===== Permissions =====
    public int OpPermissionLevel { get; set; } = 4;
    public int FunctionPermissionLevel { get; set; } = 2;

    // ===== Датапаки =====
    public string InitialEnabledPacks { get; set; } = "vanilla";
    public string InitialDisabledPacks { get; set; } = "";

    // ===== Text Filtering =====
    public string TextFilteringConfig { get; set; } = "";
    public int TextFilteringVersion { get; set; } = 0;

    // ===== Удалено в 1.21.2+ (оставлено для совместимости) =====
    public bool CommandBlocks { get; set; } = true;
    public bool AllowNether { get; set; } = true;
    public bool SpawnNpcs { get; set; } = true;
    public bool SpawnAnimals { get; set; } = true;
    public bool SpawnMonsters { get; set; } = true;

    /// <summary>
    /// Загрузить свойства из файла
    /// </summary>
    public static ServerProperties Load(string path)
    {
        var props = new ServerProperties();

        if (!File.Exists(path))
            return props;

        var lines = File.ReadAllLines(path, Encoding.UTF8);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Пропускаем комментарии и пустые строки
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;

            var eqIndex = trimmedLine.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmedLine[..eqIndex].Trim();
            var value = trimmedLine[(eqIndex + 1)..].Trim();

            // Запоминаем ключ для последующей фильтрации UI
            props.FoundKeys.Add(key);

            SetProperty(props, key, value);
        }

        return props;
    }

    /// <summary>
    /// Сохранить свойства в файл (плоский формат как в vanilla Minecraft)
    /// </summary>
    public void Save(string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#Minecraft server properties");
        sb.AppendLine($"#{DateTime.Now:ddd MMM dd HH:mm:ss zzz yyyy}");

        // Sort properties alphabetically like vanilla Minecraft
        var allProps = new SortedDictionary<string, string>
        {
            ["accepts-transfers"] = AcceptsTransfers.ToString().ToLower(),
            ["allow-flight"] = AllowFlight.ToString().ToLower(),
            ["broadcast-console-to-ops"] = BroadcastConsoleToOps.ToString().ToLower(),
            ["broadcast-rcon-to-ops"] = BroadcastRconToOps.ToString().ToLower(),
            ["bug-report-link"] = BugReportLink,
            ["difficulty"] = Difficulty,
            ["enable-code-of-conduct"] = EnableCodeOfConduct.ToString().ToLower(),
            ["enable-jmx-monitoring"] = EnableJmxMonitoring.ToString().ToLower(),
            ["enable-query"] = EnableQuery.ToString().ToLower(),
            ["enable-rcon"] = EnableRcon.ToString().ToLower(),
            ["enable-status"] = EnableStatus.ToString().ToLower(),
            ["enforce-secure-profile"] = EnforceSecureProfile.ToString().ToLower(),
            ["enforce-whitelist"] = EnforceWhitelist.ToString().ToLower(),
            ["entity-broadcast-range-percentage"] = EntityBroadcastRangePercentage.ToString(),
            ["force-gamemode"] = ForceGamemode.ToString().ToLower(),
            ["function-permission-level"] = FunctionPermissionLevel.ToString(),
            ["gamemode"] = Gamemode,
            ["generate-structures"] = GenerateStructures.ToString().ToLower(),
            ["generator-settings"] = GeneratorSettings,
            ["hardcore"] = Hardcore.ToString().ToLower(),
            ["hide-online-players"] = HideOnlinePlayers.ToString().ToLower(),
            ["initial-disabled-packs"] = InitialDisabledPacks,
            ["initial-enabled-packs"] = InitialEnabledPacks,
            ["level-name"] = LevelName,
            ["level-seed"] = LevelSeed,
            ["level-type"] = LevelType,
            ["log-ips"] = LogIps.ToString().ToLower(),
            ["management-server-allowed-origins"] = ManagementServerAllowedOrigins,
            ["management-server-enabled"] = ManagementServerEnabled.ToString().ToLower(),
            ["management-server-host"] = ManagementServerHost,
            ["management-server-port"] = ManagementServerPort.ToString(),
            ["management-server-secret"] = ManagementServerSecret,
            ["management-server-tls-enabled"] = ManagementServerTlsEnabled.ToString().ToLower(),
            ["management-server-tls-keystore"] = ManagementServerTlsKeystore,
            ["management-server-tls-keystore-password"] = ManagementServerTlsKeystorePassword,
            ["max-chained-neighbor-updates"] = MaxChainedNeighborUpdates.ToString(),
            ["max-players"] = MaxPlayers.ToString(),
            ["max-tick-time"] = MaxTickTime.ToString(),
            ["max-world-size"] = MaxWorldSize.ToString(),
            ["motd"] = Motd,
            ["network-compression-threshold"] = NetworkCompressionThreshold.ToString(),
            ["online-mode"] = OnlineMode.ToString().ToLower(),
            ["op-permission-level"] = OpPermissionLevel.ToString(),
            ["pause-when-empty-seconds"] = PauseWhenEmptySeconds.ToString(),
            ["player-idle-timeout"] = PlayerIdleTimeout.ToString(),
            ["prevent-proxy-connections"] = PreventProxyConnections.ToString().ToLower(),
            ["query.port"] = QueryPort.ToString(),
            ["rate-limit"] = RateLimit.ToString(),
            ["rcon.password"] = RconPassword,
            ["rcon.port"] = RconPort.ToString(),
            ["region-file-compression"] = RegionFileCompression,
            ["require-resource-pack"] = RequireResourcePack.ToString().ToLower(),
            ["resource-pack"] = ResourcePack,
            ["resource-pack-id"] = ResourcePackId,
            ["resource-pack-prompt"] = ResourcePackPrompt,
            ["resource-pack-sha1"] = ResourcePackSha1,
            ["server-ip"] = ServerIp,
            ["server-port"] = ServerPort.ToString(),
            ["simulation-distance"] = SimulationDistance.ToString(),
            ["spawn-animals"] = SpawnAnimals.ToString().ToLower(),
            ["spawn-monsters"] = SpawnMonsters.ToString().ToLower(),
            ["spawn-npcs"] = SpawnNpcs.ToString().ToLower(),
            ["spawn-protection"] = SpawnProtection.ToString(),
            ["status-heartbeat-interval"] = StatusHeartbeatInterval.ToString(),
            ["sync-chunk-writes"] = SyncChunkWrites.ToString().ToLower(),
            ["text-filtering-config"] = TextFilteringConfig,
            ["text-filtering-version"] = TextFilteringVersion.ToString(),
            ["use-native-transport"] = UseNativeTransport.ToString().ToLower(),
            ["view-distance"] = ViewDistance.ToString(),
            ["white-list"] = WhiteList.ToString().ToLower(),
        };

        // Сохраняем только те свойства, которые были в оригинальном файле
        var props = FoundKeys.Count > 0
            ? allProps.Where(kv => FoundKeys.Contains(kv.Key))
            : allProps;

        foreach (var (key, value) in props)
        {
            sb.AppendLine($"{key}={value}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Установить свойство по имени
    /// </summary>
    private static void SetProperty(ServerProperties props, string key, string value)
    {
        switch (key)
        {
            case "server-port":
                props.ServerPort = ParseInt(value, 25565);
                break;
            case "server-ip":
                props.ServerIp = value;
                break;
            case "max-players":
                props.MaxPlayers = ParseInt(value, 20);
                break;
            case "view-distance":
                props.ViewDistance = ParseInt(value, 10);
                break;
            case "simulation-distance":
                props.SimulationDistance = ParseInt(value, 10);
                break;
            case "pause-when-empty-seconds":
                props.PauseWhenEmptySeconds = ParseInt(value, 60);
                break;
            case "motd":
                props.Motd = value;
                break;
            case "gamemode":
                props.Gamemode = value.ToLower();
                break;
            case "force-gamemode":
                props.ForceGamemode = ParseBool(value, false);
                break;
            case "hardcore":
                props.Hardcore = ParseBool(value, false);
                break;
            case "difficulty":
                props.Difficulty = value.ToLower();
                break;
            case "allow-flight":
                props.AllowFlight = ParseBool(value, false);
                break;
            case "pvp":
                props.Pvp = ParseBool(value, true);
                break;
            case "level-name":
                props.LevelName = value;
                break;
            case "level-seed":
                props.LevelSeed = value;
                break;
            case "level-type":
                props.LevelType = value;
                break;
            case "generator-settings":
                props.GeneratorSettings = value;
                break;
            case "generate-structures":
                props.GenerateStructures = ParseBool(value, true);
                break;
            case "max-world-size":
                props.MaxWorldSize = ParseInt(value, 29999984);
                break;
            case "spawn-protection":
                props.SpawnProtection = ParseInt(value, 16);
                break;
            case "spawn-radius":
                props.SpawnRadius = ParseInt(value, 10);
                break;
            case "online-mode":
                props.OnlineMode = ParseBool(value, true);
                break;
            case "enforce-secure-profile":
                props.EnforceSecureProfile = ParseBool(value, true);
                break;
            case "prevent-proxy-connections":
                props.PreventProxyConnections = ParseBool(value, false);
                break;
            case "network-compression-threshold":
                props.NetworkCompressionThreshold = ParseInt(value, 256);
                break;
            case "max-tick-time":
                props.MaxTickTime = ParseInt(value, 60000);
                break;
            case "player-idle-timeout":
                props.PlayerIdleTimeout = ParseInt(value, 0);
                break;
            case "rate-limit":
                props.RateLimit = ParseInt(value, 0);
                break;
            case "enable-status":
                props.EnableStatus = ParseBool(value, true);
                break;
            case "accepts-transfers":
                props.AcceptsTransfers = ParseBool(value, false);
                break;
            case "status-heartbeat-interval":
                props.StatusHeartbeatInterval = ParseInt(value, 0);
                break;
            case "white-list":
                props.WhiteList = ParseBool(value, false);
                break;
            case "enforce-whitelist":
                props.EnforceWhitelist = ParseBool(value, false);
                break;
            case "enable-rcon":
                props.EnableRcon = ParseBool(value, false);
                break;
            case "rcon.password":
                props.RconPassword = value;
                break;
            case "rcon.port":
                props.RconPort = ParseInt(value, 25575);
                break;
            case "enable-query":
                props.EnableQuery = ParseBool(value, false);
                break;
            case "query.port":
                props.QueryPort = ParseInt(value, 25565);
                break;
            case "management-server-enabled":
                props.ManagementServerEnabled = ParseBool(value, false);
                break;
            case "management-server-host":
                props.ManagementServerHost = value;
                break;
            case "management-server-port":
                props.ManagementServerPort = ParseInt(value, 0);
                break;
            case "management-server-allowed-origins":
                props.ManagementServerAllowedOrigins = value;
                break;
            case "management-server-secret":
                props.ManagementServerSecret = value;
                break;
            case "management-server-tls-enabled":
                props.ManagementServerTlsEnabled = ParseBool(value, true);
                break;
            case "management-server-tls-keystore":
                props.ManagementServerTlsKeystore = value;
                break;
            case "management-server-tls-keystore-password":
                props.ManagementServerTlsKeystorePassword = value;
                break;
            case "resource-pack":
                props.ResourcePack = value;
                break;
            case "resource-pack-sha1":
                props.ResourcePackSha1 = value;
                break;
            case "resource-pack-id":
                props.ResourcePackId = value;
                break;
            case "resource-pack-prompt":
                props.ResourcePackPrompt = value;
                break;
            case "require-resource-pack":
                props.RequireResourcePack = ParseBool(value, false);
                break;
            case "log-ips":
                props.LogIps = ParseBool(value, true);
                break;
            case "broadcast-console-to-ops":
                props.BroadcastConsoleToOps = ParseBool(value, true);
                break;
            case "broadcast-rcon-to-ops":
                props.BroadcastRconToOps = ParseBool(value, true);
                break;
            case "hide-online-players":
                props.HideOnlinePlayers = ParseBool(value, false);
                break;
            case "enable-jmx-monitoring":
                props.EnableJmxMonitoring = ParseBool(value, false);
                break;
            case "enable-code-of-conduct":
                props.EnableCodeOfConduct = ParseBool(value, false);
                break;
            case "bug-report-link":
                props.BugReportLink = value;
                break;
            case "sync-chunk-writes":
                props.SyncChunkWrites = ParseBool(value, true);
                break;
            case "use-native-transport":
                props.UseNativeTransport = ParseBool(value, true);
                break;
            case "max-chained-neighbor-updates":
                props.MaxChainedNeighborUpdates = ParseInt(value, 1000000);
                break;
            case "entity-broadcast-range-percentage":
                props.EntityBroadcastRangePercentage = ParseInt(value, 100);
                break;
            case "region-file-compression":
                props.RegionFileCompression = value;
                break;
            case "op-permission-level":
                props.OpPermissionLevel = ParseInt(value, 4);
                break;
            case "function-permission-level":
                props.FunctionPermissionLevel = ParseInt(value, 2);
                break;
            case "initial-enabled-packs":
                props.InitialEnabledPacks = value;
                break;
            case "initial-disabled-packs":
                props.InitialDisabledPacks = value;
                break;
            case "text-filtering-config":
                props.TextFilteringConfig = value;
                break;
            case "text-filtering-version":
                props.TextFilteringVersion = ParseInt(value, 0);
                break;
            // Удалено в 1.21.2+ (оставлено для совместимости)
            case "command-block-enabled":
                props.CommandBlocks = ParseBool(value, true);
                break;
            case "allow-nether":
                props.AllowNether = ParseBool(value, true);
                break;
            case "spawn-npcs":
                props.SpawnNpcs = ParseBool(value, true);
                break;
            case "spawn-animals":
                props.SpawnAnimals = ParseBool(value, true);
                break;
            case "spawn-monsters":
                props.SpawnMonsters = ParseBool(value, true);
                break;
        }
    }

    private static int ParseInt(string value, int defaultValue) =>
        int.TryParse(value, out var result) ? result : defaultValue;

    private static bool ParseBool(string value, bool defaultValue)
    {
        // Minecraft использует lowercase true/false
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return defaultValue;
    }

    /// <summary>
    /// Получить описание режима игры
    /// </summary>
    public string GamemodeDisplayName => Gamemode.ToLower() switch
    {
        "survival" => "Выживание",
        "creative" => "Творчество",
        "adventure" => "Приключение",
        "spectator" => "Наблюдатель",
        _ => Gamemode
    };

    /// <summary>
    /// Получить описание сложности
    /// </summary>
    public string DifficultyDisplayName => Difficulty.ToLower() switch
    {
        "peaceful" => "Мирный",
        "easy" => "Легкий",
        "normal" => "Нормальный",
        "hard" => "Сложный",
        _ => Difficulty
    };
}
