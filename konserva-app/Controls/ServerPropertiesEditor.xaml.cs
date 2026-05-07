using Konserva.Controls;
using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace Konserva.Controls;

/// <summary>
/// Редактор server.properties в GUI
/// </summary>
public partial class ServerPropertiesEditor : UserControl
{
    private ServerProperties? _properties;
    private string? _propertiesPath;

    public event EventHandler? PropertiesSaved;

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
        MotdBox.Text = _properties.Motd;
        EnableStatusBox.IsChecked = _properties.EnableStatus;

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
            _properties.Motd = MotdBox.Text;
            _properties.EnableStatus = GetBoolValue(EnableStatusBox);

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