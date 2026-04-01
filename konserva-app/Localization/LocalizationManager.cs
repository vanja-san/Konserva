using Konserva.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Markup;

namespace Konserva.Localization;

/// <summary>
/// Менеджер локализации. Управляет загрузкой и хранением переводов.
/// </summary>
public static class LocalizationManager
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new();
    private static readonly string _i18nPath = Path.Combine(AppContext.BaseDirectory, "i18n");
    private static CultureInfo _currentCulture = new("ru");
    private static readonly Lock _lock = new();

    // Кэшированные настройки JSON для производительности
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Событие изменения языка (для обновления UI в runtime)
    /// </summary>
    public static event Action<string>? LanguageChanged;

    /// <summary>
    /// Текущая культура
    /// </summary>
    public static CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            lock (_lock)
            {
                _currentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                Thread.CurrentThread.CurrentCulture = value;
            }
        }
    }

    /// <summary>
    /// Список поддерживаемых языков
    /// </summary>
    public static string[] SupportedCultures => ["ru", "en"];

    /// <summary>
    /// Инициализация локализации. Загружает файлы переводов.
    /// </summary>
    public static void Initialize()
    {
        if (!Directory.Exists(_i18nPath))
        {
            Directory.CreateDirectory(_i18nPath);
        }

        // Создаём и загружаем файлы переводов
        foreach (var culture in SupportedCultures)
        {
            var filePath = Path.Combine(_i18nPath, $"{culture}.json");
            if (!File.Exists(filePath))
            {
                CreateDefaultLocalizationFile(filePath, culture);
            }

            LoadCulture(culture);
        }

        Logger.Info("Localization initialized", "LocalizationManager");
    }

    /// <summary>
    /// Загружает переводы для указанной культуры
    /// </summary>
    public static void LoadCulture(string culture)
    {
        var filePath = Path.Combine(_i18nPath, $"{culture}.json");
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);
            if (translations != null)
            {
                _translations[culture] = translations;
            }
        }
    }

    /// <summary>
    /// Пытается получить перевод для указанного ключа
    /// </summary>
    public static bool TryGetTranslation(string key, out string value)
    {
        value = key;

        if (_translations.TryGetValue(CurrentCulture.Name, out var translations) && translations.TryGetValue(key, out var translatedValue))
        {
            value = translatedValue;
            return true;
        }

        // Fallback на английский
        if (_translations.TryGetValue("en", out var enTranslations) && enTranslations.TryGetValue(key, out var enValue))
        {
            value = enValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Получает перевод для указанного ключа
    /// </summary>
    public static string Get(string key)
    {
        // Сначала пробуем получить из загруженных файлов
        if (TryGetTranslation(key, out var value))
        {
            return value;
        }
        
        // Если файлы не загружены, используем default translations
        var defaultTranslations = GetDefaultTranslationsForCulture(CurrentCulture.Name);
        if (defaultTranslations != null && defaultTranslations.TryGetValue(key, out var defaultValue))
        {
            return defaultValue;
        }
        
        // Fallback на английский
        var enTranslations = GetDefaultTranslationsForCulture("en");
        if (enTranslations != null && enTranslations.TryGetValue(key, out var enValue))
        {
            return enValue;
        }
        
        return key; // Возвращаем ключ если ничего не найдено
    }

    /// <summary>
    /// Получает перевод с форматированием
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        var format = Get(key);
        return string.Format(format, args);
    }

    /// <summary>
    /// Устанавливает язык приложения
    /// </summary>
    public static void SetLanguage(string culture)
    {
        // Определяем фактический язык
        string actualCulture = culture;
        
        if (culture == "System")
        {
            // Автоопределение языка системы
            var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            actualCulture = systemLanguage == "ru" ? "ru" : "en";
        }
        else if (!SupportedCultures.Contains(culture))
        {
            Logger.Warning($"Unsupported language: {culture}, falling back to 'ru'", "LocalizationManager");
            actualCulture = "ru";
        }

        var cultureInfo = new CultureInfo(actualCulture);
        CurrentCulture = cultureInfo;
        LoadCulture(actualCulture);

        // Уведомляем об изменении языка
        LanguageChanged?.Invoke(actualCulture);

        Logger.Info($"Language changed to: {actualCulture}", "LocalizationManager");
    }

    /// <summary>
    /// Проверяет существование ключа перевода
    /// </summary>
    public static bool HasKey(string key)
    {
        if (_translations.TryGetValue(CurrentCulture.Name, out var translations))
        {
            return translations.ContainsKey(key);
        }
        return false;
    }

    /// <summary>
    /// Возвращает все ключи для текущей культуры
    /// </summary>
    public static IEnumerable<string> GetAllKeys()
    {
        if (_translations.TryGetValue(CurrentCulture.Name, out var translations))
        {
            return translations.Keys;
        }
        return [];
    }

    /// <summary>
    /// Создаёт файл локализации по умолчанию
    /// </summary>
    private static void CreateDefaultLocalizationFile(string filePath, string culture)
    {
        var translations = GetDefaultTranslations(culture);
        var json = JsonSerializer.Serialize(translations, _jsonOptions);
        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);

        Logger.Info($"Created default localization file for: {culture}", "LocalizationManager");
    }

    /// <summary>
    /// Возвращает переводы по умолчанию для указанной культуры (публичный метод для LocExtension)
    /// </summary>
    public static Dictionary<string, string>? GetDefaultTranslationsForCulture(string culture)
    {
        return GetDefaultTranslations(culture);
    }

    /// <summary>
    /// Возвращает переводы по умолчанию для указанной культуры
    /// </summary>
    private static Dictionary<string, string> GetDefaultTranslations(string culture)
    {
        return culture switch
        {
            "ru" => new Dictionary<string, string>
            {
                // MainWindow
                { "MainWindow_Title", "Konserva — Менеджер серверов Minecraft" },
                { "MainWindow_Servers", "Серверы" },
                { "MainWindow_Settings", "Настройки" },
                { "MainWindow_Header", "Konserva Manager" },
                { "StatusBar_TotalServers", "Всего серверов" },
                { "StatusBar_Running", "Запущено" },
                { "StatusBar_Memory", "Память" },
                { "StatusBar_Java_Configured", "Java настроена" },
                { "StatusBar_Java_NotConfigured", "Java не настроена" },
                { "StatusBar_Version", "Версия" },

                // Settings
                { "Settings_Title", "Настройки" },
                { "Settings_Servers", "Серверы" },
                { "Settings_Servers_Directory", "Папка серверов" },
                { "Settings_Servers_Browse", "Обзор" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Добавить Java" },
                { "Settings_RAM_Min", "Память и приложение" },
                { "Settings_RAM_Min_Label", "Мин. ОЗУ (МБ)" },
                { "Settings_RAM_Max_Label", "Макс. ОЗУ (МБ)" },
                { "Settings_RAM_Min_Desc", "Начальный объем памяти" },
                { "Settings_RAM_Max_Desc", "Максимальный объем памяти" },
                { "Settings_App", "Приложение" },
                { "Settings_CheckUpdates", "Проверка обновлений" },
                { "Settings_CheckUpdates_Desc", "Автоматически проверять обновления" },
                { "Settings_Theme", "Тема" },
                { "Settings_Theme_Desc", "Выберите тему приложения" },
                { "Settings_Theme_System", "Как в системе" },
                { "Settings_Theme_Dark", "Тёмная" },
                { "Settings_Theme_Light", "Светлая" },
                { "Settings_Language", "Язык" },
                { "Settings_Language_Desc", "Выберите язык приложения" },
                { "Settings_Language_System", "Как в системе" },
                { "Settings_Language_English", "Английский" },
                { "Settings_Language_Russian", "Русский" },
                { "Settings_About", "О программе" },
                { "Settings_About_Version", "Версия" },
                { "Settings_About_Description", "Менеджер серверов Minecraft" },
                { "Settings_About_ModalLoaders", "Поддерживаемые модлоадеры:" },
                { "Settings_About_InDevelopment", "В разработке" },
                { "Message_SettingsSaved", "Настройки сохранены" },

                // CreateServer
                { "CreateServer_Title", "Создать сервер" },
                { "CreateServer_Name", "Название сервера" },
                { "CreateServer_MinecraftVersion", "Версия Minecraft" },
                { "CreateServer_ModLoader", "Модлоадер" },
                { "CreateServer_Folder", "Папка" },
                { "CreateServer_Browse", "Обзор" },
                { "CreateServer_Create", "Создать" },
                { "CreateServer_Cancel", "Отмена" },
                { "CreateServer_Filter_Stable", "Только стабильные" },
                { "CreateServer_Import", "Импортировать" },

                // ServersPage
                { "ServersPage_Search", "Поиск..." },
                { "ServersPage_Filter_All", "Все типы" },
                { "ServersPage_Filter_AllServers", "Все серверы" },
                { "ServersPage_Filter_Running", "Запущен" },
                { "ServersPage_Filter_Stopped", "Остановлен" },
                { "ServersPage_Create", "Создать сервер" },
                { "ServersPage_Port", "Порт:" },
                { "ServersPage_OpenFolder", "Открыть папку сервера" },
                { "ServersPage_Delete", "Удалить сервер" },
                { "ServersPage_NoServers", "Нет серверов" },
                { "ServersPage_NoServers_Description", "Создайте первый сервер для управления" },

                // Common
                { "Common_Cancel", "Отмена" },
                { "Common_OK", "ОК" },
                { "Common_Yes", "Да" },
                { "Common_No", "Нет" },
                { "Common_Loading", "Загрузка..." },
                { "Common_None", "Нет" },
                { "Common_InDevelopment", "В разработке" },

                // ServerDetail
                { "ServerDetail_Title", "Детали сервера" },
                { "ServerDetail_Console", "Консоль" },
                { "ServerDetail_Properties", "Свойства" },
                { "ServerDetail_Mods", "Моды" },
                { "ServerDetail_Plugins", "Плагины" },
                { "ServerDetail_Start", "Запустить" },
                { "ServerDetail_Stop", "Остановить" },
                { "ServerDetail_StartStop", "Запустить/Остановить" },
                { "ServerDetail_Starting", "Запускается..." },
                { "ServerDetail_Stopping", "Останавливается..." },
                { "ServerDetail_Delete", "Удалить" },
                { "ServerDetail_OpenFolder", "Открыть папку" },
                { "ServerDetail_AutoRestart", "Авто-рестарт" },
                { "ServerDetail_Java", "Java" },
                { "ServerDetail_RAM", "ОЗУ" },
                { "ServerDetail_Port", "Порт" },
                { "ServerDetail_Name", "Название" },
                { "ServerDetail_Version", "Версия" },
                { "ServerDetail_ModLoader", "Модлоадер" },
                { "ServerDetail_Status", "Статус" },
                { "ServerDetail_Status_Running", "Запущен" },
                { "ServerDetail_Status_Stopped", "Остановлен" },
                { "ServerDetail_Console_Empty", "Консоль пуста, запустите сервер" },
                { "ServerDetail_SendCommand", "Отправить" },
                { "ServerDetail_Mods_List", "Список установленных модов" },
                { "ServerDetail_Mods_OpenFolder", "Открыть папку mods" },
                { "ServerDetail_Mods_Refresh", "Обновить" },
                { "ServerDetail_Mods_Delete", "Удалить" },
                { "ServerDetail_Plugins_List", "Список установленных плагинов" },
                { "ServerDetail_Plugins_OpenFolder", "Открыть папку plugins" },
                { "ServerDetail_Plugins_Refresh", "Обновить" },
                { "ServerDetail_Plugins_Delete", "Удалить" },

                // ServerProperties
                { "ServerProperties_Title", "Свойства сервера" },
                { "ServerProperties_Save", "Сохранить" },
                { "ServerProperties_Cancel", "Отмена" },
                { "ServerProperties_General", "Основные" },
                { "ServerProperties_ServerPort", "Порт сервера" },
                { "ServerProperties_ServerIp", "IP сервера" },
                { "ServerProperties_MaxPlayers", "Макс. игроков" },
                { "ServerProperties_ViewDistance", "Дальность прорисовки" },
                { "ServerProperties_SimulationDistance", "Дальность симуляции" },
                { "ServerProperties_MOTD", "MOTD (описание)" },
                { "ServerProperties_Gamemode", "Режим игры" },
                { "ServerProperties_Gamemode_Survival", "Выживание" },
                { "ServerProperties_Gamemode_Creative", "Творчество" },
                { "ServerProperties_Gamemode_Adventure", "Приключение" },
                { "ServerProperties_Gamemode_Spectator", "Наблюдатель" },
                { "ServerProperties_Difficulty", "Сложность" },
                { "ServerProperties_Difficulty_Peaceful", "Мирный" },
                { "ServerProperties_Difficulty_Easy", "Лёгкий" },
                { "ServerProperties_Difficulty_Normal", "Обычный" },
                { "ServerProperties_Difficulty_Hard", "Сложный" },
                { "ServerProperties_Hardcore", "Хардкор" },
                { "ServerProperties_Hardcore_Content", "Режим хардкора" },
                { "ServerProperties_PvP", "PvP" },
                { "ServerProperties_PvP_Content", "Разрешить PvP" },
                { "ServerProperties_CommandBlocks", "Командные блоки" },
                { "ServerProperties_CommandBlocks_Content", "Разрешить командные блоки" },
                { "ServerProperties_World", "Мир" },
                { "ServerProperties_LevelName", "Имя мира" },
                { "ServerProperties_LevelSeed", "Сид (seed)" },
                { "ServerProperties_LevelType", "Тип мира" },
                { "ServerProperties_LevelType_Normal", "Обычный" },
                { "ServerProperties_LevelType_Flat", "Плоский" },
                { "ServerProperties_LevelType_LargeBiomes", "Крупные биомы" },
                { "ServerProperties_LevelType_Amplified", "Амплифицированный" },
                { "ServerProperties_LevelType_SingleBiome", "Один биом" },
                { "ServerProperties_MaxWorldSize", "Макс. размер мира" },
                { "ServerProperties_SpawnStructures", "Генерация структур" },
                { "ServerProperties_SpawnStructures_Content", "Генерировать структуры" },
                { "ServerProperties_AllowNether", "Доступ в Незер" },
                { "ServerProperties_AllowNether_Content", "Разрешить Незер" },
                { "ServerProperties_Network", "Сеть" },
                { "ServerProperties_OnlineMode", "Проверка лицензии" },
                { "ServerProperties_OnlineMode_Content", "Проверять лицензию (online-mode)" },
                { "ServerProperties_Whitelist", "Белый список" },
                { "ServerProperties_Whitelist_Content", "Использовать whitelist" },
                { "ServerProperties_EnforceWhitelist", "Принудительный whitelist" },
                { "ServerProperties_EnforceWhitelist_Content", "Принудительный whitelist" },
                { "ServerProperties_EnforceSecureProfile", "Безопасный профиль" },
                { "ServerProperties_EnforceSecureProfile_Content", "Требовать безопасный профиль" },
                { "ServerProperties_AllowFlight", "Полёты" },
                { "ServerProperties_AllowFlight_Content", "Разрешить полёты" },
                { "ServerProperties_RCON", "RCON" },
                { "ServerProperties_EnableRcon", "Включить RCON" },
                { "ServerProperties_EnableRcon_Content", "Включить RCON" },
                { "ServerProperties_RconPassword", "Пароль RCON" },
                { "ServerProperties_RconPort", "Порт RCON" },
                { "ServerProperties_RconIp", "RCON IP" },
                { "ServerProperties_Advanced", "Дополнительно" },
                { "ServerProperties_SpawnProtection", "Защита спавна" },
                { "ServerProperties_SpawnRadius", "Радиус спавна" },
                { "ServerProperties_OpPermissionLevel", "Уровень прав OP" },
                { "ServerProperties_MaxTickTime", "Max tick time (мс)" },
                { "ServerProperties_NetworkCompression", "Сжатие сети" },
                { "ServerProperties_SpawnNPCs", "Спавн NPC" },
                { "ServerProperties_SpawnAnimals", "Спавн животных" },
                { "ServerProperties_SpawnMonsters", "Спавн монстров" },
                { "ServerProperties_EnabledPacks", "Включенные датапаки" },
                { "ServerProperties_DisabledPacks", "Отключённые датапаки" },
                { "ServerProperties_Reset", "Сбросить" },
                { "ServerProperties_Refresh", "Обновить" },
                { "ServerProperties_Performance", "Производительность" },
                { "ServerProperties_Gameplay", "Геймплей" },

                // ServerDetail Settings
                { "ServerDetail_Settings_General", "Название сервера" },
                { "ServerDetail_Settings_General_Desc", "Название сервера в приложении" },
                { "ServerDetail_Settings_Port", "Порт" },
                { "ServerDetail_Settings_Port_Desc", "Порт для сервера" },
                { "ServerDetail_Settings_RAM", "Выделение памяти" },
                { "ServerDetail_Settings_RAM_Min", "Минимум RAM (МБ)" },
                { "ServerDetail_Settings_RAM_Min_Desc", "Минимальный размер памяти для сервера" },
                { "ServerDetail_Settings_RAM_Max", "Максимум RAM (МБ)" },
                { "ServerDetail_Settings_RAM_Max_Desc", "Максимальный размер памяти для сервера" },
                { "ServerDetail_Settings_AutoRestart", "Авто-рестарт" },
                { "ServerDetail_Settings_AutoRestart_Enable", "Включить авто-рестарт" },
                { "ServerDetail_Settings_AutoRestart_Desc", "Автоматически перезапускать сервер при остановке" },
                { "ServerDetail_Settings_AutoRestart_Delay", "Задержка перед рестартом (сек)" },
                { "ServerDetail_Settings_AutoRestart_Delay_Desc", "Задержка перед автоматическим перезапуском" },
                { "ServerDetail_Settings_Java_Auto", "Автовыбор версии Java" },
                { "ServerDetail_Settings_Java_Auto_Desc", "Автоматически выбирать Java на основе версии Minecraft" },
                { "ServerDetail_Settings_Java_Version", "Версия Java" },

                // Messages
                { "Message_ConfirmDelete", "Вы уверены, что хотите удалить сервер?" },
                { "Message_Error", "Ошибка" },
                { "Message_DeleteMod", "Удалить" },
                { "Message_DeletePlugin", "Удалить" },
                { "Message_RestartRequired", "Для применения нового языка требуется перезапуск приложения." },

                // Errors
                { "Error_JavaNotFound", "Java не найдена" },
                { "Error_JavaIncompatible", "Несовместимая версия Java" },
                { "Error_ServerInstallFailed", "Ошибка установки сервера" },
                { "Error_PortInUse", "Порт уже занят" },
                { "Error_OutOfMemory", "Недостаточно памяти" }
            },
            "en" => new Dictionary<string, string>
            {
                // MainWindow
                { "MainWindow_Title", "Konserva — Minecraft Server Manager" },
                { "MainWindow_Servers", "Servers" },
                { "MainWindow_Settings", "Settings" },
                { "MainWindow_Header", "Konserva Manager" },
                { "StatusBar_TotalServers", "Total Servers" },
                { "StatusBar_Running", "Running" },
                { "StatusBar_Memory", "Memory" },
                { "StatusBar_Java_Configured", "Java configured" },
                { "StatusBar_Java_NotConfigured", "Java not configured" },
                { "StatusBar_Version", "Version" },

                // Settings
                { "Settings_Title", "Settings" },
                { "Settings_Servers", "Servers" },
                { "Settings_Servers_Directory", "Servers Directory" },
                { "Settings_Servers_Browse", "Browse" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Add Java" },
                { "Settings_RAM_Min", "Memory and Application" },
                { "Settings_RAM_Min_Label", "Min RAM (MB)" },
                { "Settings_RAM_Max_Label", "Max RAM (MB)" },
                { "Settings_RAM_Min_Desc", "Initial memory amount" },
                { "Settings_RAM_Max_Desc", "Maximum memory amount" },
                { "Settings_App", "Application" },
                { "Settings_CheckUpdates", "Check for Updates" },
                { "Settings_CheckUpdates_Desc", "Automatically check for updates" },
                { "Settings_Theme", "Theme" },
                { "Settings_Theme_Desc", "Select application theme" },
                { "Settings_Theme_System", "System Default" },
                { "Settings_Theme_Dark", "Dark" },
                { "Settings_Theme_Light", "Light" },
                { "Settings_Language", "Language" },
                { "Settings_Language_Desc", "Select application language" },
                { "Settings_Language_System", "System Default" },
                { "Settings_Language_System_Auto", "(auto)" },
                { "Settings_Language_English", "English" },
                { "Settings_Language_Russian", "Russian" },
                { "Settings_About", "About" },
                { "Settings_About_Version", "Version" },
                { "Settings_About_Description", "Minecraft Server Manager" },
                { "Settings_About_ModalLoaders", "Supported Mod Loaders:" },
                { "Settings_About_InDevelopment", "In Development" },
                { "Message_SettingsSaved", "Settings saved" },

                // CreateServer
                { "CreateServer_Title", "Create Server" },
                { "CreateServer_Name", "Server Name" },
                { "CreateServer_MinecraftVersion", "Minecraft Version" },
                { "CreateServer_ModLoader", "Mod Loader" },
                { "CreateServer_Folder", "Folder" },
                { "CreateServer_Browse", "Browse" },
                { "CreateServer_Create", "Create" },
                { "CreateServer_Cancel", "Cancel" },
                { "CreateServer_Filter_Stable", "Stable only" },
                { "CreateServer_Import", "Import" },

                // ServersPage
                { "ServersPage_Search", "Search..." },
                { "ServersPage_Filter_All", "All Types" },
                { "ServersPage_Filter_AllServers", "All Servers" },
                { "ServersPage_Filter_Running", "Running" },
                { "ServersPage_Filter_Stopped", "Stopped" },
                { "ServersPage_Create", "Create Server" },
                { "ServersPage_Port", "Port:" },
                { "ServersPage_OpenFolder", "Open Server Folder" },
                { "ServersPage_Delete", "Delete Server" },
                { "ServersPage_NoServers", "No Servers" },
                { "ServersPage_NoServers_Description", "Create your first server to get started" },

                // Common
                { "Common_Cancel", "Cancel" },
                { "Common_OK", "OK" },
                { "Common_Yes", "Yes" },
                { "Common_No", "No" },
                { "Common_Loading", "Loading..." },
                { "Common_None", "None" },
                { "Common_InDevelopment", "In Development" },

                // ServerDetail
                { "ServerDetail_Title", "Server Details" },
                { "ServerDetail_Console", "Console" },
                { "ServerDetail_Properties", "Properties" },
                { "ServerDetail_Mods", "Mods" },
                { "ServerDetail_Plugins", "Plugins" },
                { "ServerDetail_Start", "Start" },
                { "ServerDetail_Stop", "Stop" },
                { "ServerDetail_StartStop", "Start/Stop" },
                { "ServerDetail_Starting", "Starting..." },
                { "ServerDetail_Stopping", "Stopping..." },
                { "ServerDetail_Delete", "Delete" },
                { "ServerDetail_OpenFolder", "Open Folder" },
                { "ServerDetail_AutoRestart", "Auto-Restart" },
                { "ServerDetail_Java", "Java" },
                { "ServerDetail_RAM", "RAM" },
                { "ServerDetail_Port", "Port" },
                { "ServerDetail_Name", "Name" },
                { "ServerDetail_Version", "Version" },
                { "ServerDetail_ModLoader", "Mod Loader" },
                { "ServerDetail_Status", "Status" },
                { "ServerDetail_Status_Running", "Running" },
                { "ServerDetail_Status_Stopped", "Stopped" },
                { "ServerDetail_Console_Empty", "Console is empty, start the server" },
                { "ServerDetail_SendCommand", "Send" },
                { "ServerDetail_Mods_List", "Installed Mods List" },
                { "ServerDetail_Mods_OpenFolder", "Open mods folder" },
                { "ServerDetail_Mods_Refresh", "Refresh" },
                { "ServerDetail_Mods_Delete", "Delete" },
                { "ServerDetail_Plugins_List", "Installed Plugins List" },
                { "ServerDetail_Plugins_OpenFolder", "Open plugins folder" },
                { "ServerDetail_Plugins_Refresh", "Refresh" },
                { "ServerDetail_Plugins_Delete", "Delete" },

                // ServerProperties
                { "ServerProperties_Title", "Server Properties" },
                { "ServerProperties_Save", "Save" },
                { "ServerProperties_Cancel", "Cancel" },
                { "ServerProperties_General", "General" },
                { "ServerProperties_ServerPort", "Server Port" },
                { "ServerProperties_ServerIp", "Server IP" },
                { "ServerProperties_MaxPlayers", "Max Players" },
                { "ServerProperties_ViewDistance", "View Distance" },
                { "ServerProperties_SimulationDistance", "Simulation Distance" },
                { "ServerProperties_MOTD", "MOTD" },
                { "ServerProperties_Gamemode", "Game Mode" },
                { "ServerProperties_Gamemode_Survival", "Survival" },
                { "ServerProperties_Gamemode_Creative", "Creative" },
                { "ServerProperties_Gamemode_Adventure", "Adventure" },
                { "ServerProperties_Gamemode_Spectator", "Spectator" },
                { "ServerProperties_Difficulty", "Difficulty" },
                { "ServerProperties_Difficulty_Peaceful", "Peaceful" },
                { "ServerProperties_Difficulty_Easy", "Easy" },
                { "ServerProperties_Difficulty_Normal", "Normal" },
                { "ServerProperties_Difficulty_Hard", "Hard" },
                { "ServerProperties_Hardcore", "Hardcore" },
                { "ServerProperties_Hardcore_Content", "Hardcore mode" },
                { "ServerProperties_PvP", "PvP" },
                { "ServerProperties_PvP_Content", "Enable PvP" },
                { "ServerProperties_CommandBlocks", "Command Blocks" },
                { "ServerProperties_CommandBlocks_Content", "Enable command blocks" },
                { "ServerProperties_World", "World" },
                { "ServerProperties_LevelName", "Level Name" },
                { "ServerProperties_LevelSeed", "Level Seed" },
                { "ServerProperties_LevelType", "Level Type" },
                { "ServerProperties_LevelType_Normal", "Normal" },
                { "ServerProperties_LevelType_Flat", "Flat" },
                { "ServerProperties_LevelType_LargeBiomes", "Large Biomes" },
                { "ServerProperties_LevelType_Amplified", "Amplified" },
                { "ServerProperties_LevelType_SingleBiome", "Single Biome" },
                { "ServerProperties_MaxWorldSize", "Max World Size" },
                { "ServerProperties_SpawnStructures", "Spawn Structures" },
                { "ServerProperties_SpawnStructures_Content", "Generate structures" },
                { "ServerProperties_AllowNether", "Allow Nether" },
                { "ServerProperties_AllowNether_Content", "Allow Nether" },
                { "ServerProperties_Network", "Network" },
                { "ServerProperties_OnlineMode", "Online Mode" },
                { "ServerProperties_OnlineMode_Content", "Verify licenses (online-mode)" },
                { "ServerProperties_Whitelist", "Whitelist" },
                { "ServerProperties_Whitelist_Content", "Use whitelist" },
                { "ServerProperties_EnforceWhitelist", "Enforce Whitelist" },
                { "ServerProperties_EnforceWhitelist_Content", "Enforce whitelist" },
                { "ServerProperties_EnforceSecureProfile", "Enforce Secure Profile" },
                { "ServerProperties_EnforceSecureProfile_Content", "Require secure profile" },
                { "ServerProperties_AllowFlight", "Allow Flight" },
                { "ServerProperties_AllowFlight_Content", "Allow flights" },
                { "ServerProperties_RCON", "RCON" },
                { "ServerProperties_EnableRcon", "Enable RCON" },
                { "ServerProperties_EnableRcon_Content", "Enable RCON" },
                { "ServerProperties_RconPassword", "RCON Password" },
                { "ServerProperties_RconPort", "RCON Port" },
                { "ServerProperties_RconIp", "RCON IP" },
                { "ServerProperties_Advanced", "Advanced" },
                { "ServerProperties_SpawnProtection", "Spawn Protection" },
                { "ServerProperties_SpawnRadius", "Spawn Radius" },
                { "ServerProperties_OpPermissionLevel", "OP Permission Level" },
                { "ServerProperties_MaxTickTime", "Max Tick Time (ms)" },
                { "ServerProperties_NetworkCompression", "Network Compression" },
                { "ServerProperties_SpawnNPCs", "Spawn NPCs" },
                { "ServerProperties_SpawnAnimals", "Spawn Animals" },
                { "ServerProperties_SpawnMonsters", "Spawn Monsters" },
                { "ServerProperties_EnabledPacks", "Enabled Datapacks" },
                { "ServerProperties_DisabledPacks", "Disabled Datapacks" },
                { "ServerProperties_Reset", "Reset" },
                { "ServerProperties_Refresh", "Refresh" },
                { "ServerProperties_Performance", "Performance" },
                { "ServerProperties_Gameplay", "Gameplay" },

                // ServerDetail Settings
                { "ServerDetail_Settings_General", "Server name" },
                { "ServerDetail_Settings_General_Desc", "Server name in app" },
                { "ServerDetail_Settings_Port", "Port" },
                { "ServerDetail_Settings_Port_Desc", "Server port" },
                { "ServerDetail_Settings_RAM", "Memory Allocation" },
                { "ServerDetail_Settings_RAM_Min", "Min RAM (MB)" },
                { "ServerDetail_Settings_RAM_Min_Desc", "Minimum memory size for server" },
                { "ServerDetail_Settings_RAM_Max", "Max RAM (MB)" },
                { "ServerDetail_Settings_RAM_Max_Desc", "Maximum memory size for server" },
                { "ServerDetail_Settings_AutoRestart", "Auto-Restart" },
                { "ServerDetail_Settings_AutoRestart_Enable", "Enable auto-restart" },
                { "ServerDetail_Settings_AutoRestart_Desc", "Automatically restart server on stop" },
                { "ServerDetail_Settings_AutoRestart_Delay", "Restart delay (sec)" },
                { "ServerDetail_Settings_AutoRestart_Delay_Desc", "Delay before automatic restart" },
                { "ServerDetail_Settings_Java_Auto", "Auto-select Java version" },
                { "ServerDetail_Settings_Java_Auto_Desc", "Automatically select Java based on Minecraft version" },
                { "ServerDetail_Settings_Java_Version", "Java Version" },

                // Messages
                { "Message_ConfirmDelete", "Are you sure you want to delete the server?" },
                { "Message_Error", "Error" },
                { "Message_DeleteMod", "Delete" },
                { "Message_DeletePlugin", "Delete" },
                { "Message_RestartRequired", "Application restart is required to apply the new language." },

                // Errors
                { "Error_JavaNotFound", "Java not found" },
                { "Error_JavaIncompatible", "Incompatible Java version" },
                { "Error_ServerInstallFailed", "Server installation failed" },
                { "Error_PortInUse", "Port is already in use" },
                { "Error_OutOfMemory", "Out of memory" }
            },
            _ => []
        };
    }
}

/// <summary>
/// MarkupExtension для использования в XAML: {loc:Loc KeyName}
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return Key;

        return GetTranslation();
    }

    private string GetTranslation()
    {
        // Сначала пробуем получить из загруженных файлов
        if (LocalizationManager.TryGetTranslation(Key, out var value))
        {
            return value;
        }
        
        // Если файлы не загружены, используем default translations
        var defaultTranslations = LocalizationManager.GetDefaultTranslationsForCulture(LocalizationManager.CurrentCulture.Name);
        if (defaultTranslations != null && defaultTranslations.TryGetValue(Key, out var defaultValue))
        {
            return defaultValue;
        }
        
        // Fallback на английский
        var enTranslations = LocalizationManager.GetDefaultTranslationsForCulture("en");
        if (enTranslations != null && enTranslations.TryGetValue(Key, out var enValue))
        {
            return enValue;
        }
        
        return Key;
    }
}
