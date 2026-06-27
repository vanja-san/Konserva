using Konserva.Controls;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Konserva.Controls;

/// <summary>
/// Редактор server.properties в GUI
/// </summary>
public partial class ServerPropertiesEditor : UserControl
{
    private ServerProperties? _properties;
    private string? _propertiesPath;

    public event EventHandler? PropertiesSaved;

    /// <summary>
    /// Текущий порт сервера из загруженных свойств
    /// </summary>
    public int CurrentPort => _properties?.ServerPort ?? Constants.DefaultServerPort;

    public ServerPropertiesEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Загружает настройки из файла
    /// </summary>
    public void Load(string path)
    {
        _propertiesPath = path;

        // Проверяем существование файла
        if (!File.Exists(path))
        {
            NoFileMessage.Visibility = Visibility.Visible;
            PropertiesScrollViewer.Visibility = Visibility.Collapsed;
            _properties = null;
            return;
        }

        // Файл существует - показываем редактор
        NoFileMessage.Visibility = Visibility.Collapsed;
        PropertiesScrollViewer.Visibility = Visibility.Visible;
        _properties = ServerProperties.Load(path);
        PopulateFields();
        ShowStatus(LocalizationManager.Get("Props_Loaded"), false);
    }

    /// <summary>
    /// Заполняет поля значениями
    /// </summary>
    private void PopulateFields()
    {
        if (_properties == null)
            return;

        // General
        ServerPortBox.Text = _properties.ServerPort.ToString();
        ServerIpBox.Text = _properties.ServerIp;
        MaxPlayersBox.Text = _properties.MaxPlayers.ToString();
        ViewDistanceBox.Text = _properties.ViewDistance.ToString();
        SimulationDistanceBox.Text = _properties.SimulationDistance.ToString();
        PauseWhenEmptySecondsBox.Text = _properties.PauseWhenEmptySeconds.ToString();
        // MotdBox и EnableStatusBox отсутствуют в XAML
        // MotdBox.Text = _properties.Motd;
        // EnableStatusBox.IsChecked = _properties.EnableStatus;

        // Gamemode
        SelectComboBoxItem(GamemodeBox, _properties.Gamemode);
        ForceGamemodeBox.IsChecked = _properties.ForceGamemode;
        SelectComboBoxItem(DifficultyBox, _properties.Difficulty);
        HardcoreBox.IsChecked = _properties.Hardcore;
        PvpBox.IsChecked = _properties.Pvp;
        AllowFlightBox.IsChecked = _properties.AllowFlight;
        CommandBlocksBox.IsChecked = _properties.CommandBlocks;

        // World
        LevelNameBox.Text = _properties.LevelName;
        LevelSeedBox.Text = _properties.LevelSeed;
        SelectComboBoxItem(LevelTypeBox, _properties.LevelType);
        GeneratorSettingsBox.Text = _properties.GeneratorSettings;
        MaxWorldSizeBox.Text = _properties.MaxWorldSize.ToString();
        GenerateStructuresBox.IsChecked = _properties.GenerateStructures;
        AllowNetherBox.IsChecked = _properties.AllowNether;
        SpawnRadiusBox.Text = _properties.SpawnRadius.ToString();

        // Network
        OnlineModeBox.IsChecked = _properties.OnlineMode;
        EnforceSecureProfileBox.IsChecked = _properties.EnforceSecureProfile;
        PreventProxyConnectionsBox.IsChecked = _properties.PreventProxyConnections;
        RateLimitBox.Text = _properties.RateLimit.ToString();
        NetworkCompressionBox.Text = _properties.NetworkCompressionThreshold.ToString();
        MaxTickTimeBox.Text = _properties.MaxTickTime.ToString();
        PlayerIdleTimeoutBox.Text = _properties.PlayerIdleTimeout.ToString();
        AcceptsTransfersBox.IsChecked = _properties.AcceptsTransfers;
        StatusHeartbeatIntervalBox.Text = _properties.StatusHeartbeatInterval.ToString();

        // Whitelist
        WhiteListBox.IsChecked = _properties.WhiteList;
        EnforceWhitelistBox.IsChecked = _properties.EnforceWhitelist;
        HideOnlinePlayersBox.IsChecked = _properties.HideOnlinePlayers;

        // RCON
        EnableRconBox.IsChecked = _properties.EnableRcon;
        RconPasswordBox.Text = _properties.RconPassword;
        RconPortBox.Text = _properties.RconPort.ToString();

        // Query
        EnableQueryBox.IsChecked = _properties.EnableQuery;
        QueryPortBox.Text = _properties.QueryPort.ToString();

        // Permissions
        SpawnProtectionBox.Text = _properties.SpawnProtection.ToString();
        OpPermissionLevelBox.Text = _properties.OpPermissionLevel.ToString();
        FunctionPermissionLevelBox.Text = _properties.FunctionPermissionLevel.ToString();
        InitialEnabledPacksBox.Text = _properties.InitialEnabledPacks;
        InitialDisabledPacksBox.Text = _properties.InitialDisabledPacks;

        // Management Server
        ManagementServerEnabledBox.IsChecked = _properties.ManagementServerEnabled;
        ManagementServerHostBox.Text = _properties.ManagementServerHost;
        ManagementServerPortBox.Text = _properties.ManagementServerPort.ToString();
        ManagementServerSecretBox.Text = _properties.ManagementServerSecret;
        ManagementServerTlsEnabledBox.IsChecked = _properties.ManagementServerTlsEnabled;
        ManagementServerTlsKeystoreBox.Text = _properties.ManagementServerTlsKeystore;
        ManagementServerTlsKeystorePasswordBox.Password = _properties.ManagementServerTlsKeystorePassword;
        ManagementServerAllowedOriginsBox.Text = _properties.ManagementServerAllowedOrigins;

        // Resource Pack
        ResourcePackBox.Text = _properties.ResourcePack;
        ResourcePackSha1Box.Text = _properties.ResourcePackSha1;
        ResourcePackIdBox.Text = _properties.ResourcePackId;
        ResourcePackPromptBox.Text = _properties.ResourcePackPrompt;
        RequireResourcePackBox.IsChecked = _properties.RequireResourcePack;

        // Performance
        MaxChainedNeighborUpdatesBox.Text = _properties.MaxChainedNeighborUpdates.ToString();
        EntityBroadcastRangePercentageBox.Text = _properties.EntityBroadcastRangePercentage.ToString();
        SyncChunkWritesBox.IsChecked = _properties.SyncChunkWrites;
        UseNativeTransportBox.IsChecked = _properties.UseNativeTransport;
        SelectComboBoxItem(RegionFileCompressionBox, _properties.RegionFileCompression);

        // Logging
        LogIpsBox.IsChecked = _properties.LogIps;
        BroadcastConsoleToOpsBox.IsChecked = _properties.BroadcastConsoleToOps;
        BroadcastRconToOpsBox.IsChecked = _properties.BroadcastRconToOps;
        EnableJmxMonitoringBox.IsChecked = _properties.EnableJmxMonitoring;
        EnableCodeOfConductBox.IsChecked = _properties.EnableCodeOfConduct;
        BugReportLinkBox.Text = _properties.BugReportLink;
        TextFilteringConfigBox.Text = _properties.TextFilteringConfig;
        TextFilteringVersionBox.Text = _properties.TextFilteringVersion.ToString();

        // Spawn Settings (Legacy)
        SpawnNpcsBox.IsChecked = _properties.SpawnNpcs;
        SpawnAnimalsBox.IsChecked = _properties.SpawnAnimals;
        SpawnMonstersBox.IsChecked = _properties.SpawnMonsters;

        // Обновляем видимость строк на основе найденных в файле ключей
        UpdateRowVisibility();
    }

    /// <summary>
    /// Обновляет видимость строк на основе ключей, найденных в файле
    /// </summary>
    private void UpdateRowVisibility()
    {
        if (_properties == null) return;
        var keys = _properties.FoundKeys;

        // Helper для установки видимости обеих частей строки (label + control)
        static void SetRowVisibility(bool exists, FrameworkElement labelParent, FrameworkElement? control)
        {
            var visibility = exists ? Visibility.Visible : Visibility.Collapsed;
            labelParent.Visibility = visibility;
            if (control != null)
                control.Visibility = visibility;
        }

        static bool ContainsKey(HashSet<string> set, params string[] names) =>
            names.Any(n => set.Contains(n));

        // Общие свойства
        SetRowVisibility(ContainsKey(keys, "server-port"), ServerPortBoxParent, ServerPortBox);
        SetRowVisibility(ContainsKey(keys, "server-ip"), ServerIpBoxParent, ServerIpBox);
        SetRowVisibility(ContainsKey(keys, "max-players"), MaxPlayersBoxParent, MaxPlayersBox);
        SetRowVisibility(ContainsKey(keys, "view-distance"), ViewDistanceBoxParent, ViewDistanceBox);
        SetRowVisibility(ContainsKey(keys, "simulation-distance"), SimulationDistanceBoxParent, SimulationDistanceBox);
        SetRowVisibility(ContainsKey(keys, "pause-with-zero-players-delay-seconds"), PauseWhenEmptySecondsBoxParent, PauseWhenEmptySecondsBox);
        // MotdBoxParent и EnableStatusBoxParent временно отключены - поля отсутствуют в XAML

        // Режим игры
        SetRowVisibility(ContainsKey(keys, "gamemode"), GamemodeBoxParent, GamemodeBox);
        SetRowVisibility(ContainsKey(keys, "force-gamemode"), ForceGamemodeBoxParent, ForceGamemodeBox);
        SetRowVisibility(ContainsKey(keys, "difficulty"), DifficultyBoxParent, DifficultyBox);
        SetRowVisibility(ContainsKey(keys, "hardcore"), HardcoreBoxParent, HardcoreBox);
        SetRowVisibility(ContainsKey(keys, "pvp"), PvpBoxParent, PvpBox);
        SetRowVisibility(ContainsKey(keys, "allow-flight"), AllowFlightBoxParent, AllowFlightBox);
        SetRowVisibility(ContainsKey(keys, "command-block-enabled"), CommandBlocksBoxParent, CommandBlocksBox);
        SetRowVisibility(ContainsKey(keys, "spawn-npcs"), SpawnNpcsBoxParent, SpawnNpcsBox);
        SetRowVisibility(ContainsKey(keys, "spawn-animals"), SpawnAnimalsBoxParent, SpawnAnimalsBox);
        SetRowVisibility(ContainsKey(keys, "spawn-monsters"), SpawnMonstersBoxParent, SpawnMonstersBox);

        // Мир
        SetRowVisibility(ContainsKey(keys, "level-name"), LevelNameBoxParent, LevelNameBox);
        SetRowVisibility(ContainsKey(keys, "level-seed"), LevelSeedBoxParent, LevelSeedBox);
        SetRowVisibility(ContainsKey(keys, "level-type"), LevelTypeBoxParent, LevelTypeBox);
        SetRowVisibility(ContainsKey(keys, "generator-settings"), GeneratorSettingsBoxParent, GeneratorSettingsBox);
        SetRowVisibility(ContainsKey(keys, "max-world-size"), MaxWorldSizeBoxParent, MaxWorldSizeBox);
        SetRowVisibility(ContainsKey(keys, "generate-structures"), GenerateStructuresBoxParent, GenerateStructuresBox);
        SetRowVisibility(ContainsKey(keys, "allow-nether"), AllowNetherBoxParent, AllowNetherBox);
        SetRowVisibility(ContainsKey(keys, "spawn-radius"), SpawnRadiusBoxParent, SpawnRadiusBox);

        // Сеть
        SetRowVisibility(ContainsKey(keys, "online-mode"), OnlineModeBoxParent, OnlineModeBox);
        SetRowVisibility(ContainsKey(keys, "enforce-secure-profile"), EnforceSecureProfileBoxParent, EnforceSecureProfileBox);
        SetRowVisibility(ContainsKey(keys, "prevent-proxy-connections"), PreventProxyConnectionsBoxParent, PreventProxyConnectionsBox);
        SetRowVisibility(ContainsKey(keys, "rate-limit"), RateLimitBoxParent, RateLimitBox);
        SetRowVisibility(ContainsKey(keys, "network-compression-threshold"), NetworkCompressionBoxParent, NetworkCompressionBox);
        SetRowVisibility(ContainsKey(keys, "max-tick-time"), MaxTickTimeBoxParent, MaxTickTimeBox);
        SetRowVisibility(ContainsKey(keys, "player-idle-timeout"), PlayerIdleTimeoutBoxParent, PlayerIdleTimeoutBox);
        SetRowVisibility(ContainsKey(keys, "accepts-transfers"), AcceptsTransfersBoxParent, AcceptsTransfersBox);
        SetRowVisibility(ContainsKey(keys, "status-heartbeat-interval"), StatusHeartbeatIntervalBoxParent, StatusHeartbeatIntervalBox);

        // Whitelist
        SetRowVisibility(ContainsKey(keys, "white-list"), WhiteListBoxParent, WhiteListBox);
        SetRowVisibility(ContainsKey(keys, "enforce-whitelist"), EnforceWhitelistBoxParent, EnforceWhitelistBox);
        SetRowVisibility(ContainsKey(keys, "hide-online-players"), HideOnlinePlayersBoxParent, HideOnlinePlayersBox);

        // RCON
        SetRowVisibility(ContainsKey(keys, "enable-rcon"), EnableRconBoxParent, EnableRconBox);
        SetRowVisibility(ContainsKey(keys, "rcon.password"), RconPasswordBoxParent, RconPasswordBox);
        SetRowVisibility(ContainsKey(keys, "rcon.port"), RconPortBoxParent, RconPortBox);

        // Query
        SetRowVisibility(ContainsKey(keys, "enable-query"), EnableQueryBoxParent, EnableQueryBox);
        SetRowVisibility(ContainsKey(keys, "query.port"), QueryPortBoxParent, QueryPortBox);

        // Permissions
        SetRowVisibility(ContainsKey(keys, "spawn-protection"), SpawnProtectionBoxParent, SpawnProtectionBox);
        SetRowVisibility(ContainsKey(keys, "op-permission-level"), OpPermissionLevelBoxParent, OpPermissionLevelBox);
        SetRowVisibility(ContainsKey(keys, "function-permission-level"), FunctionPermissionLevelBoxParent, FunctionPermissionLevelBox);
        SetRowVisibility(ContainsKey(keys, "initial-enabled-packs"), InitialEnabledPacksBoxParent, InitialEnabledPacksBox);
        SetRowVisibility(ContainsKey(keys, "initial-disabled-packs"), InitialDisabledPacksBoxParent, InitialDisabledPacksBox);

        // Management Server
        SetRowVisibility(ContainsKey(keys, "enable-minecraft-server"), ManagementServerEnabledBoxParent, ManagementServerEnabledBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-host"), ManagementServerHostBoxParent, ManagementServerHostBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-port"), ManagementServerPortBoxParent, ManagementServerPortBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-api-secret"), ManagementServerSecretBoxParent, ManagementServerSecretBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-api-use-tls"), ManagementServerTlsEnabledBoxParent, ManagementServerTlsEnabledBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-api-tls-certificate-file"), ManagementServerTlsKeystoreBoxParent, ManagementServerTlsKeystoreBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-api-allowed-origins"), ManagementServerAllowedOriginsBoxParent, ManagementServerAllowedOriginsBox);
        SetRowVisibility(ContainsKey(keys, "minecraft-server-api-tls-certificate-password"), ManagementServerTlsKeystorePasswordBoxParent, ManagementServerTlsKeystorePasswordBox);

        // Resource Pack
        SetRowVisibility(ContainsKey(keys, "resource-pack"), ResourcePackBoxParent, ResourcePackBox);
        SetRowVisibility(ContainsKey(keys, "resource-pack-sha1"), ResourcePackSha1BoxParent, ResourcePackSha1Box);
        SetRowVisibility(ContainsKey(keys, "resource-pack-id"), ResourcePackIdBoxParent, ResourcePackIdBox);
        SetRowVisibility(ContainsKey(keys, "resource-pack-prompt"), ResourcePackPromptBoxParent, ResourcePackPromptBox);
        SetRowVisibility(ContainsKey(keys, "require-resource-pack"), RequireResourcePackBoxParent, RequireResourcePackBox);

        // Performance
        SetRowVisibility(ContainsKey(keys, "max-chained-neighbor-updates"), MaxChainedNeighborUpdatesBoxParent, MaxChainedNeighborUpdatesBox);
        SetRowVisibility(ContainsKey(keys, "entity-broadcast-range-percentage"), EntityBroadcastRangePercentageBoxParent, EntityBroadcastRangePercentageBox);
        SetRowVisibility(ContainsKey(keys, "sync-chunk-writes"), SyncChunkWritesBoxParent, SyncChunkWritesBox);
        SetRowVisibility(ContainsKey(keys, "use-native-transport"), UseNativeTransportBoxParent, UseNativeTransportBox);
        SetRowVisibility(ContainsKey(keys, "region-file-compression"), RegionFileCompressionBoxParent, RegionFileCompressionBox);

        // Logging
        SetRowVisibility(ContainsKey(keys, "log-ips"), LogIpsBoxParent, LogIpsBox);
        SetRowVisibility(ContainsKey(keys, "broadcast-console-to-ops"), BroadcastConsoleToOpsBoxParent, BroadcastConsoleToOpsBox);
        SetRowVisibility(ContainsKey(keys, "broadcast-rcon-to-ops"), BroadcastRconToOpsBoxParent, BroadcastRconToOpsBox);
        SetRowVisibility(ContainsKey(keys, "enable-jmx-monitoring"), EnableJmxMonitoringBoxParent, EnableJmxMonitoringBox);
        SetRowVisibility(ContainsKey(keys, "enable-code-of-conduct"), EnableCodeOfConductBoxParent, EnableCodeOfConductBox);
        SetRowVisibility(ContainsKey(keys, "bug-report-link"), BugReportLinkBoxParent, BugReportLinkBox);
        SetRowVisibility(ContainsKey(keys, "text-filtering-config"), TextFilteringConfigBoxParent, TextFilteringConfigBox);
        SetRowVisibility(ContainsKey(keys, "text-filtering-version"), TextFilteringVersionBoxParent, TextFilteringVersionBox);

        // Скрываем пустые экспандеры
        UpdateExpanderVisibility();
    }

    /// <summary>
    /// Скрывает экспандеры, у которых все строки скрыты
    /// </summary>
    private void UpdateExpanderVisibility()
    {
        static bool IsVisible(FrameworkElement? el) => el?.Visibility == Visibility.Visible;

        // Общие свойства
        GeneralExpander.Visibility =
            IsVisible(ServerPortBoxParent) || IsVisible(ServerIpBoxParent) ||
            IsVisible(MaxPlayersBoxParent) || IsVisible(ViewDistanceBoxParent) ||
            IsVisible(SimulationDistanceBoxParent) || IsVisible(PauseWhenEmptySecondsBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Режим игры
        GamemodeExpander.Visibility =
            IsVisible(GamemodeBoxParent) || IsVisible(ForceGamemodeBoxParent) ||
            IsVisible(DifficultyBoxParent) || IsVisible(HardcoreBoxParent) ||
            IsVisible(PvpBoxParent) || IsVisible(AllowFlightBoxParent) ||
            IsVisible(CommandBlocksBoxParent) || IsVisible(SpawnNpcsBoxParent) ||
            IsVisible(SpawnAnimalsBoxParent) || IsVisible(SpawnMonstersBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Мир
        WorldExpander.Visibility =
            IsVisible(LevelNameBoxParent) || IsVisible(LevelSeedBoxParent) ||
            IsVisible(LevelTypeBoxParent) || IsVisible(GeneratorSettingsBoxParent) ||
            IsVisible(MaxWorldSizeBoxParent) || IsVisible(GenerateStructuresBoxParent) ||
            IsVisible(AllowNetherBoxParent) || IsVisible(SpawnRadiusBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Сеть
        NetworkExpander.Visibility =
            IsVisible(OnlineModeBoxParent) || IsVisible(EnforceSecureProfileBoxParent) ||
            IsVisible(PreventProxyConnectionsBoxParent) || IsVisible(RateLimitBoxParent) ||
            IsVisible(NetworkCompressionBoxParent) || IsVisible(MaxTickTimeBoxParent) ||
            IsVisible(PlayerIdleTimeoutBoxParent) || IsVisible(AcceptsTransfersBoxParent) ||
            IsVisible(StatusHeartbeatIntervalBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Whitelist
        WhitelistExpander.Visibility =
            IsVisible(WhiteListBoxParent) || IsVisible(EnforceWhitelistBoxParent) ||
            IsVisible(HideOnlinePlayersBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // RCON
        RconExpander.Visibility =
            IsVisible(EnableRconBoxParent) || IsVisible(RconPasswordBoxParent) ||
            IsVisible(RconPortBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Query
        QueryExpander.Visibility =
            IsVisible(EnableQueryBoxParent) || IsVisible(QueryPortBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Permissions
        PermissionsExpander.Visibility =
            IsVisible(SpawnProtectionBoxParent) || IsVisible(OpPermissionLevelBoxParent) ||
            IsVisible(FunctionPermissionLevelBoxParent) || IsVisible(InitialEnabledPacksBoxParent) ||
            IsVisible(InitialDisabledPacksBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Management Server
        ManagementServerExpander.Visibility =
            IsVisible(ManagementServerEnabledBoxParent) || IsVisible(ManagementServerHostBoxParent) ||
            IsVisible(ManagementServerPortBoxParent) || IsVisible(ManagementServerSecretBoxParent) ||
            IsVisible(ManagementServerTlsEnabledBoxParent) || IsVisible(ManagementServerTlsKeystoreBoxParent) ||
            IsVisible(ManagementServerAllowedOriginsBoxParent) || IsVisible(ManagementServerTlsKeystorePasswordBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Resource Pack
        ResourcePackExpander.Visibility =
            IsVisible(ResourcePackBoxParent) || IsVisible(ResourcePackSha1BoxParent) ||
            IsVisible(ResourcePackIdBoxParent) || IsVisible(ResourcePackPromptBoxParent) ||
            IsVisible(RequireResourcePackBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Performance
        PerformanceExpander.Visibility =
            IsVisible(MaxChainedNeighborUpdatesBoxParent) || IsVisible(EntityBroadcastRangePercentageBoxParent) ||
            IsVisible(SyncChunkWritesBoxParent) || IsVisible(UseNativeTransportBoxParent) ||
            IsVisible(RegionFileCompressionBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;

        // Logging
        LoggingExpander.Visibility =
            IsVisible(LogIpsBoxParent) || IsVisible(BroadcastConsoleToOpsBoxParent) ||
            IsVisible(BroadcastRconToOpsBoxParent) || IsVisible(EnableJmxMonitoringBoxParent) ||
            IsVisible(EnableCodeOfConductBoxParent) || IsVisible(BugReportLinkBoxParent) ||
            IsVisible(TextFilteringConfigBoxParent) || IsVisible(TextFilteringVersionBoxParent)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Выбирает элемент в ComboBox по значению
    /// </summary>
    private void SelectComboBoxItem(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag?.ToString() == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>
    /// Получает значение из ComboBox
    /// </summary>
    private string GetComboBoxValue(ComboBox comboBox, string defaultValue)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Получает целое число из TextBox
    /// </summary>
    private int GetIntValue(TextBox textBox, int defaultValue)
    {
        if (int.TryParse(textBox.Text, out var result))
            return result;
        return defaultValue;
    }

    /// <summary>
    /// Получает булево значение из CheckBox
    /// </summary>
    private bool GetBoolValue(CheckBox checkBox)
    {
        return checkBox.IsChecked ?? false;
    }

    /// <summary>
    /// Сохраняет настройки в файл
    /// </summary>
    public void Save()
    {
        if (_properties == null || string.IsNullOrEmpty(_propertiesPath))
        {
            ShowStatus(LocalizationManager.Get("Props_NotLoaded"), true);
            return;
        }

        try
        {
            // General
            _properties.ServerPort = GetIntValue(ServerPortBox, 25565);
            _properties.ServerIp = ServerIpBox.Text;
            _properties.MaxPlayers = GetIntValue(MaxPlayersBox, 20);
            _properties.ViewDistance = GetIntValue(ViewDistanceBox, 10);
            _properties.SimulationDistance = GetIntValue(SimulationDistanceBox, 10);
            _properties.PauseWhenEmptySeconds = GetIntValue(PauseWhenEmptySecondsBox, 60);
            // MotdBox и EnableStatusBox отсутствуют в XAML
            // _properties.Motd = MotdBox.Text;
            // _properties.EnableStatus = GetBoolValue(EnableStatusBox);

            // Gamemode
            _properties.Gamemode = GetComboBoxValue(GamemodeBox, "survival");
            _properties.ForceGamemode = GetBoolValue(ForceGamemodeBox);
            _properties.Difficulty = GetComboBoxValue(DifficultyBox, "easy");
            _properties.Hardcore = GetBoolValue(HardcoreBox);
            _properties.Pvp = GetBoolValue(PvpBox);
            _properties.AllowFlight = GetBoolValue(AllowFlightBox);
            _properties.CommandBlocks = GetBoolValue(CommandBlocksBox);

            // World
            _properties.LevelName = LevelNameBox.Text;
            _properties.LevelSeed = LevelSeedBox.Text;
            _properties.LevelType = GetComboBoxValue(LevelTypeBox, "minecraft:normal");
            _properties.GeneratorSettings = GeneratorSettingsBox.Text;
            _properties.MaxWorldSize = GetIntValue(MaxWorldSizeBox, 29999984);
            _properties.GenerateStructures = GetBoolValue(GenerateStructuresBox);
            _properties.AllowNether = GetBoolValue(AllowNetherBox);
            _properties.SpawnRadius = GetIntValue(SpawnRadiusBox, 10);

            // Network
            _properties.OnlineMode = GetBoolValue(OnlineModeBox);
            _properties.EnforceSecureProfile = GetBoolValue(EnforceSecureProfileBox);
            _properties.PreventProxyConnections = GetBoolValue(PreventProxyConnectionsBox);
            _properties.RateLimit = GetIntValue(RateLimitBox, 0);
            _properties.NetworkCompressionThreshold = GetIntValue(NetworkCompressionBox, 256);
            _properties.MaxTickTime = GetIntValue(MaxTickTimeBox, 60000);
            _properties.PlayerIdleTimeout = GetIntValue(PlayerIdleTimeoutBox, 0);
            _properties.AcceptsTransfers = GetBoolValue(AcceptsTransfersBox);
            _properties.StatusHeartbeatInterval = GetIntValue(StatusHeartbeatIntervalBox, 0);

            // Whitelist
            _properties.WhiteList = GetBoolValue(WhiteListBox);
            _properties.EnforceWhitelist = GetBoolValue(EnforceWhitelistBox);
            _properties.HideOnlinePlayers = GetBoolValue(HideOnlinePlayersBox);

            // RCON
            _properties.EnableRcon = GetBoolValue(EnableRconBox);
            _properties.RconPassword = RconPasswordBox.Text;
            _properties.RconPort = GetIntValue(RconPortBox, 25575);

            // Query
            _properties.EnableQuery = GetBoolValue(EnableQueryBox);
            _properties.QueryPort = GetIntValue(QueryPortBox, 25565);

            // Permissions
            _properties.SpawnProtection = GetIntValue(SpawnProtectionBox, 16);
            _properties.OpPermissionLevel = GetIntValue(OpPermissionLevelBox, 4);
            _properties.FunctionPermissionLevel = GetIntValue(FunctionPermissionLevelBox, 2);
            _properties.InitialEnabledPacks = InitialEnabledPacksBox.Text;
            _properties.InitialDisabledPacks = InitialDisabledPacksBox.Text;

            // Management Server
            _properties.ManagementServerEnabled = GetBoolValue(ManagementServerEnabledBox);
            _properties.ManagementServerHost = ManagementServerHostBox.Text;
            _properties.ManagementServerPort = GetIntValue(ManagementServerPortBox, 0);
            _properties.ManagementServerSecret = ManagementServerSecretBox.Text;
            _properties.ManagementServerTlsEnabled = GetBoolValue(ManagementServerTlsEnabledBox);
            _properties.ManagementServerTlsKeystore = ManagementServerTlsKeystoreBox.Text;
            _properties.ManagementServerTlsKeystorePassword = ManagementServerTlsKeystorePasswordBox.Password;
            _properties.ManagementServerAllowedOrigins = ManagementServerAllowedOriginsBox.Text;

            // Resource Pack
            _properties.ResourcePack = ResourcePackBox.Text;
            _properties.ResourcePackSha1 = ResourcePackSha1Box.Text;
            _properties.ResourcePackId = ResourcePackIdBox.Text;
            _properties.ResourcePackPrompt = ResourcePackPromptBox.Text;
            _properties.RequireResourcePack = GetBoolValue(RequireResourcePackBox);

            // Performance
            _properties.MaxChainedNeighborUpdates = GetIntValue(MaxChainedNeighborUpdatesBox, 1000000);
            _properties.EntityBroadcastRangePercentage = GetIntValue(EntityBroadcastRangePercentageBox, 100);
            _properties.SyncChunkWrites = GetBoolValue(SyncChunkWritesBox);
            _properties.UseNativeTransport = GetBoolValue(UseNativeTransportBox);
            _properties.RegionFileCompression = GetComboBoxValue(RegionFileCompressionBox, "deflate");

            // Logging
            _properties.LogIps = GetBoolValue(LogIpsBox);
            _properties.BroadcastConsoleToOps = GetBoolValue(BroadcastConsoleToOpsBox);
            _properties.BroadcastRconToOps = GetBoolValue(BroadcastRconToOpsBox);
            _properties.EnableJmxMonitoring = GetBoolValue(EnableJmxMonitoringBox);
            _properties.EnableCodeOfConduct = GetBoolValue(EnableCodeOfConductBox);
            _properties.BugReportLink = BugReportLinkBox.Text;
            _properties.TextFilteringConfig = TextFilteringConfigBox.Text;
            _properties.TextFilteringVersion = GetIntValue(TextFilteringVersionBox, 0);

            // Spawn Settings (Legacy)
            _properties.SpawnNpcs = GetBoolValue(SpawnNpcsBox);
            _properties.SpawnAnimals = GetBoolValue(SpawnAnimalsBox);
            _properties.SpawnMonsters = GetBoolValue(SpawnMonstersBox);

            // Сохраняем
            _properties.Save(_propertiesPath);

            ShowStatus(LocalizationManager.Get("Props_Saved"), false);
            PropertiesSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save server properties: {ex.Message}", "ServerPropertiesEditor");
            ShowStatus($"{LocalizationManager.Get("Props_SaveError")}: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Сброс настроек
    /// </summary>
    public void Reset()
    {
        if (!string.IsNullOrEmpty(_propertiesPath))
        {
            Load(_propertiesPath);
            ShowStatus(LocalizationManager.Get("Props_Reset"), false);
        }
    }

    /// <summary>
    /// Отображает статус в панели
    /// </summary>
    private void ShowStatus(string message, bool isError)
    {
        StatusMessage.Text = message;
        StatusMessage.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_propertiesPath))
            Load(_propertiesPath);
    }
    private void Reset_Click(object sender, RoutedEventArgs e) => Reset();
}