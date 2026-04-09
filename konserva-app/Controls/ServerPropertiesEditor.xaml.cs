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

        // Основные
        ServerPortBox.Text = _properties.ServerPort.ToString();
        ServerIpBox.Text = _properties.ServerIp;
        MaxPlayersBox.Text = _properties.MaxPlayers.ToString();
        ViewDistanceBox.Text = _properties.ViewDistance.ToString();
        SimulationDistanceBox.Text = _properties.SimulationDistance.ToString();
        MotdBox.Text = _properties.Motd;

        // Режим игры
        SelectComboBoxItem(GamemodeBox, _properties.Gamemode);
        SelectComboBoxItem(DifficultyBox, _properties.Difficulty);
        HardcoreBox.IsChecked = _properties.Hardcore;
        PvpBox.IsChecked = _properties.Pvp;
        CommandBlocksBox.IsChecked = _properties.CommandBlocks;

        // Мир
        LevelNameBox.Text = _properties.LevelName;
        LevelSeedBox.Text = _properties.LevelSeed;
        SelectComboBoxItem(LevelTypeBox, _properties.LevelType);
        MaxWorldSizeBox.Text = _properties.MaxWorldSize.ToString();
        GenerateStructuresBox.IsChecked = _properties.GenerateStructures;
        AllowNetherBox.IsChecked = _properties.AllowNether;

        // Безопасность
        OnlineModeBox.IsChecked = _properties.OnlineMode;
        WhiteListBox.IsChecked = _properties.WhiteList;
        EnforceWhitelistBox.IsChecked = _properties.EnforceWhitelist;
        EnforceSecureProfileBox.IsChecked = _properties.EnforceSecureProfile;
        AllowFlightBox.IsChecked = _properties.AllowFlight;

        // RCON
        EnableRconBox.IsChecked = _properties.EnableRcon;
        RconPasswordBox.Text = _properties.RconPassword;
        RconPortBox.Text = _properties.RconPort.ToString();
        RconIpBox.Text = _properties.RconIp;

        // Дополнительные
        SpawnProtectionBox.Text = _properties.SpawnProtection.ToString();
        SpawnRadiusBox.Text = _properties.SpawnRadius.ToString();
        OpPermissionLevelBox.Text = _properties.OpPermissionLevel.ToString();
        MaxTickTimeBox.Text = _properties.MaxTickTime.ToString();
        NetworkCompressionBox.Text = _properties.NetworkCompressionThreshold.ToString();
        SpawnNpcsBox.IsChecked = _properties.SpawnNpcs;
        SpawnAnimalsBox.IsChecked = _properties.SpawnAnimals;
        SpawnMonstersBox.IsChecked = _properties.SpawnMonsters;
        InitialEnabledPacksBox.Text = _properties.InitialEnabledPacks;
        InitialDisabledPacksBox.Text = _properties.InitialDisabledPacks;
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
            // Заполняем значения из полей
            _properties.ServerPort = GetIntValue(ServerPortBox, 25565);
            _properties.ServerIp = ServerIpBox.Text;
            _properties.MaxPlayers = GetIntValue(MaxPlayersBox, 20);
            _properties.ViewDistance = GetIntValue(ViewDistanceBox, 10);
            _properties.SimulationDistance = GetIntValue(SimulationDistanceBox, 10);
            _properties.Motd = MotdBox.Text;

            _properties.Gamemode = GetComboBoxValue(GamemodeBox, "survival");
            _properties.Difficulty = GetComboBoxValue(DifficultyBox, "easy");
            _properties.Hardcore = GetBoolValue(HardcoreBox);
            _properties.Pvp = GetBoolValue(PvpBox);
            _properties.CommandBlocks = GetBoolValue(CommandBlocksBox);

            _properties.LevelName = LevelNameBox.Text;
            _properties.LevelSeed = LevelSeedBox.Text;
            _properties.LevelType = GetComboBoxValue(LevelTypeBox, "minecraft:normal");
            _properties.MaxWorldSize = GetIntValue(MaxWorldSizeBox, 29999984);
            _properties.GenerateStructures = GetBoolValue(GenerateStructuresBox);
            _properties.AllowNether = GetBoolValue(AllowNetherBox);

            _properties.OnlineMode = GetBoolValue(OnlineModeBox);
            _properties.WhiteList = GetBoolValue(WhiteListBox);
            _properties.EnforceWhitelist = GetBoolValue(EnforceWhitelistBox);
            _properties.EnforceSecureProfile = GetBoolValue(EnforceSecureProfileBox);
            _properties.AllowFlight = GetBoolValue(AllowFlightBox);

            _properties.EnableRcon = GetBoolValue(EnableRconBox);
            _properties.RconPassword = RconPasswordBox.Text;
            _properties.RconPort = GetIntValue(RconPortBox, 25575);
            _properties.RconIp = RconIpBox.Text;

            _properties.SpawnProtection = GetIntValue(SpawnProtectionBox, 16);
            _properties.SpawnRadius = GetIntValue(SpawnRadiusBox, 10);
            _properties.OpPermissionLevel = GetIntValue(OpPermissionLevelBox, 4);
            _properties.MaxTickTime = GetIntValue(MaxTickTimeBox, 60000);
            _properties.NetworkCompressionThreshold = GetIntValue(NetworkCompressionBox, 256);
            _properties.SpawnNpcs = GetBoolValue(SpawnNpcsBox);
            _properties.SpawnAnimals = GetBoolValue(SpawnAnimalsBox);
            _properties.SpawnMonsters = GetBoolValue(SpawnMonstersBox);
            _properties.InitialEnabledPacks = InitialEnabledPacksBox.Text;
            _properties.InitialDisabledPacks = InitialDisabledPacksBox.Text;

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