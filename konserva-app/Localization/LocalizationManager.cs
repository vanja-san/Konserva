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
                { "Settings_Servers_Dir", "Папка серверов" },
                { "Settings_Servers_DirDesc", "Где будет создан сервер" },
                { "Settings_Servers_Browse", "Обзор" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Desc", "Расположение Java" },
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
                { "CreateServer_Filter_Stable", "Показывать тестовые версии" },
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

                // MessageBox Buttons
                { "MsgBtn_OK", "ОК" },
                { "MsgBtn_Yes", "Да" },
                { "MsgBtn_No", "Нет" },
                { "MsgBtn_Cancel", "Отмена" },
                { "MsgBtn_Delete", "Удалить" },

                // MessageBox Titles
                { "MsgTitle_Info", "Информация" },
                { "MsgTitle_Warning", "Предупреждение" },
                { "MsgTitle_Error", "Ошибка" },
                { "MsgTitle_Confirm", "Подтверждение" },
                { "MsgTitle_DeleteServer", "Удаление сервера" },

                // MessageBox Info Messages
                { "MsgInfo_Title", "Информация" },

                // MessageBox Warning Messages
                { "MsgWarning_Title", "Предупреждение" },

                // MessageBox Error Messages
                { "MsgError_Title", "Ошибка" },

                // MessageBox Confirm Messages
                { "MsgConfirm_Title", "Подтверждение" },

                // MessageBox Delete Messages
                { "MsgDel_Title", "Удаление сервера" },
                { "MsgDel_Confirm", "Вы уверены, что хотите удалить сервер \"{0}\"?" },
                { "MsgDel_WillBeDeleted", "Будут удалены:" },
                { "MsgDel_ServerFiles", "Все файлы сервера" },
                { "MsgDel_ConfigFiles", "Конфигурационные файлы" },
                { "MsgDel_WorldSaves", "Мир и сохранения" },
                { "MsgDel_LogsBackups", "Логи и бэкапы" },
                { "MsgDel_Irreversible", "Это действие необратимо!" },

                // ServerDetail
                { "ServerDetail_Title", "Детали сервера" },
                { "ServerDetail_Console", "Консоль" },
                { "ServerDetail_Properties", "Файл server.properties" },
                { "ServerDetail_Settings", "Настройки сервера" },
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
                { "ServerProperties_ServerPort_Desc", "Должен быть открыт для сети" },
                { "ServerProperties_ServerIp", "IP сервера" },
                { "ServerProperties_ServerIp_Desc", "Меняйте, если только знаете что делаете" },
                { "ServerProperties_MaxPlayers", "Макс. игроков" },
                { "ServerProperties_MaxPlayers_Desc", "Макс. количество игроков на сервере" },
                { "ServerProperties_ViewDistance", "Дальность прорисовки" },
                { "ServerProperties_ViewDistance_Desc", "Видимость чанков клиеном" },
                { "ServerProperties_SimulationDistance", "Дальность симуляции" },
                { "ServerProperties_SimulationDistance_Desc", "Расстояние обновления мобов и процессов мира" },
                { "ServerProperties_MOTD", "MOTD" },
                { "ServerProperties_MOTD_Desc", "Описание для сервера" },
                { "ServerProperties_Gamemode", "Режим игры" },
                { "ServerProperties_Gamemode_Desc", "Режим по умолчанию для новых игроков" },
                { "ServerProperties_Gamemode_Survival", "Выживание" },
                { "ServerProperties_Gamemode_Creative", "Творчество" },
                { "ServerProperties_Gamemode_Adventure", "Приключение" },
                { "ServerProperties_Gamemode_Spectator", "Наблюдатель" },
                { "ServerProperties_Difficulty", "Сложность" },
                { "ServerProperties_Difficulty_Desc", "Чем выше сложность, тем сложнее" },
                { "ServerProperties_Difficulty_Peaceful", "Мирный" },
                { "ServerProperties_Difficulty_Easy", "Лёгкий" },
                { "ServerProperties_Difficulty_Normal", "Обычный" },
                { "ServerProperties_Difficulty_Hard", "Сложный" },
                { "ServerProperties_Hardcore", "Хардкор" },
                { "ServerProperties_Hardcore_Desc", "Смерть навсегда" },
                { "ServerProperties_PvP", "PvP" },
                { "ServerProperties_PvP_Desc", "Могут ли игроки атаковать друг друга" },
                { "ServerProperties_CommandBlocks", "Командные блоки" },
                { "ServerProperties_CommandBlocks_Desc", "Разрешает командные блоки в мире" },
                { "ServerProperties_World", "Мир" },
                { "ServerProperties_LevelName", "Имя мира" },
                { "ServerProperties_LevelName_Desc", "Имя папки с сохранением мира" },
                { "ServerProperties_LevelSeed", "Сид (seed)" },
                { "ServerProperties_LevelSeed_Desc", "Код генерации мира. Пусто = случайный" },
                { "ServerProperties_LevelType", "Тип мира" },
                { "ServerProperties_LevelType_Desc", "Тип генерации ландшафта" },
                { "ServerProperties_LevelType_Normal", "Обычный" },
                { "ServerProperties_LevelType_Flat", "Плоский" },
                { "ServerProperties_LevelType_LargeBiomes", "Крупные биомы" },
                { "ServerProperties_LevelType_Amplified", "Амплифицированный" },
                { "ServerProperties_LevelType_SingleBiome", "Один биом" },
                { "ServerProperties_MaxWorldSize", "Макс. размер мира" },
                { "ServerProperties_MaxWorldSize_Desc", "Макс. радиус мира в блоках" },
                { "ServerProperties_SpawnStructures", "Генерация структур" },
                { "ServerProperties_SpawnStructures_Desc", "Деревни, храмы, крепости и другие постройки" },
                { "ServerProperties_AllowNether", "Доступ в Нижний мир" },
                { "ServerProperties_AllowNether_Desc", "Разрешить портал в Нижний мир" },
                { "ServerProperties_Network", "Сеть" },
                { "ServerProperties_OnlineMode", "Проверка лицензии" },
                { "ServerProperties_OnlineMode_Desc", "Отключите для пиратских клиентов" },
                { "ServerProperties_Whitelist", "Белый список" },
                { "ServerProperties_Whitelist_Desc", "Только игроки из списка смогут зайти на сервер" },
                { "ServerProperties_EnforceWhitelist", "Принудительный whitelist" },
                { "ServerProperties_EnforceWhitelist_Desc", "Кикать игроков, удалённых из whitelist" },
                { "ServerProperties_EnforceSecureProfile", "Безопасный профиль" },
                { "ServerProperties_EnforceSecureProfile_Desc", "Требовать от клиентов подписанный профиль" },
                { "ServerProperties_AllowFlight", "Полёты" },
                { "ServerProperties_AllowFlight_Desc", "Разрешить полёт для модов" },
                { "ServerProperties_RCON", "RCON" },
                { "ServerProperties_EnableRcon", "Включить RCON" },
                { "ServerProperties_EnableRcon_Desc", "Удалённое управление сервером по сети" },
                { "ServerProperties_RconPassword", "Пароль RCON" },
                { "ServerProperties_RconPassword_Desc", "Пароль для подключения к RCON" },
                { "ServerProperties_RconPort", "Порт RCON" },
                { "ServerProperties_RconPort_Desc", "Порт для RCON-подключений" },
                { "ServerProperties_RconIp", "RCON IP" },
                { "ServerProperties_RconIp_Desc", "IP для RCON. По умолчанию — все интерфейсы" },
                { "ServerProperties_Advanced", "Дополнительно" },
                { "ServerProperties_SpawnProtection", "Защита спавна" },
                { "ServerProperties_SpawnProtection_Desc", "Радиус защиты вокруг спавна. 0 = отключить" },
                { "ServerProperties_SpawnRadius", "Радиус спавна" },
                { "ServerProperties_SpawnRadius_Desc", "Появление игрока относительно точки спавна" },
                { "ServerProperties_OpPermissionLevel", "Уровень прав OP" },
                { "ServerProperties_OpPermissionLevel_Desc", "Макс. уровень доступа (1–4). 4 = все команды" },
                { "ServerProperties_MaxTickTime", "Макс. время тика" },
                { "ServerProperties_MaxTickTime_Desc", "Макс. время тика в мс. (-1 = отключить)" },
                { "ServerProperties_NetworkCompression", "Сжатие сети" },
                { "ServerProperties_NetworkCompression_Desc", "Порог сжатия пакетов" },
                { "ServerProperties_SpawnNPCs", "Спавн NPC" },
                { "ServerProperties_SpawnNPCs_Desc", "Появление жителей деревень" },
                { "ServerProperties_SpawnAnimals", "Спавн животных" },
                { "ServerProperties_SpawnAnimals_Desc", "Появление коров, свиней и других животных" },
                { "ServerProperties_SpawnMonsters", "Спавн монстров" },
                { "ServerProperties_SpawnMonsters_Desc", "Появление зомби, скелетов и криперов" },
                { "ServerProperties_EnabledPacks", "Включенные датапаки" },
                { "ServerProperties_EnabledPacks_Desc", "Список включённых датапаков через запятую" },
                { "ServerProperties_DisabledPacks", "Отключённые датапаки" },
                { "ServerProperties_DisabledPacks_Desc", "Список отключённых датапаков через запятую" },
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

                // Server Logs
                { "Log_ServerStarted", "Сервер полностью загрузился" },
                { "Log_JavaVersionMismatch", "НЕСОВМЕСТИМОСТЬ ВЕРСИИ JAVA!" },
                { "Log_JavaVersionMismatch_Detail", "Для Minecraft {0} требуется Java {1} или выше" },
                { "Log_JavaFound", "Найдена Java: {0} (версия {1})" },
                { "Log_JavaPath", "Путь к Java: {0}" },
                { "Log_Solution", "Решение:" },
                { "Log_Solution_Install_Java", "1. Установите Java {0}: https://adoptium.net/" },
                { "Log_Solution_Add_Java", "2. Укажите путь к Java {0} в настройках приложения" },
                { "Log_Solution_Older_Minecraft", "3. Или выберите более старую версию Minecraft" },
                { "Log_JavaNotFound", "JAVA НЕ НАЙДЕНА!" },
                { "Log_JavaNotFound_Path", "Путь к Java: {0}" },
                { "Log_JavaNotFound_Error", "Ошибка: {0}" },
                { "Log_Solution_Install_Java_General", "1. Установите Java: https://adoptium.net/" },
                { "Log_Solution_Add_Java_Settings", "2. Добавьте Java в настройках приложения (кнопка 'Добавить Java')" },
                { "Log_Solution_Check_PATH", "3. Убедитесь, что Java добавлена в PATH" },
                { "Log_CriticalError", "Критическая ошибка запуска!" },
                { "Log_ErrorType", "Тип: {0}" },
                { "Log_ErrorMessage", "Сообщение: {0}" },
                { "Log_ErrorOutput", "Вывод ошибки: {0}" },
                { "Log_ServerStoppedWithCode", "Сервер завершился с кодом ошибки: {0}" },
                { "Log_ServerConfigProblem", "Это может означать проблему с конфигурацией" },
                { "Log_MemoryProblem", "или нехватку памяти." },
                { "Log_TimeoutForceKill", "Таймаут остановки, принудительное завершение..." },
                { "Log_ForceKillAttempt", "Попытка принудительного завершения..." },
                { "Log_ServerNotRunning", "Попытка отправки команды на остановленный сервер: {0}" },
                { "Log_JavaFromSettingsNotFound", "Java из настроек сервера не найдена: {0}" },
                { "Log_JavaVersionNotFound_TryDefault", "Не найдена Java {0}+, пробуем Java по умолчанию" },
                { "Log_JavaDefaultNotFound", "Java по умолчанию не найдена: {0}" },
                { "Log_UsingJavaFromPATH", "Используем Java из PATH" },

                // Console log - startup info
                { "Log_ServerId", "ID сервера: {0}" },
                { "Log_ModLoader", "Модлоадер: {0}" },
                { "Log_MinecraftVersion", "Версия Minecraft: {0}" },
                { "Log_ServerFolder", "Папка сервера: {0}" },
                { "Log_LaunchType", "Тип запуска: {0}" },
                { "Log_JarFile", "JAR файл: {0} ({1} MB)" },
                { "Log_JavaVersion", "Java версия: {0}" },
                { "Log_JavaPath_Info", "Java путь: {0}" },
                { "Log_JavaArgs", "Аргументы Java: {0}" },
                { "Log_WorkingDirectory", "Рабочая директория: {0}" },
                { "Log_LaunchingProcess", "Запуск процесса..." },
                { "Log_WaitingForStartup", "Ожидание запуска сервера..." },
                { "Log_LaunchCancelled", "Запуск отменён" },
                { "Log_EulaAccepted", "EULA принята" },
                { "Log_JavaVersionCompatible", "Версия Java совместима (требуется {0}+)" },
                { "Log_ProcessStarted", "Процесс запущен, PID: {0}" },
                { "Log_LaunchForceCancelled", "Запуск отменён принудительно" },
                { "Log_ProcessForceKilled", "Процесс завершён принудительно (сервер ещё не загрузился)" },
                { "Log_ExitError", "Ошибка завершения: {0}" },
                { "Log_StoppingServer", "Остановка сервера..." },
                { "Log_SendingStopCommand", "Отправка команды 'stop'..." },
                { "Log_WaitingForProcessExit", "Ожидание завершения процесса (60 сек)..." },
                { "Log_ProcessForceKilledAfterTimeout", "Процесс завершен принудительно" },
                { "Log_ServerStoppedSuccessfully", "Сервер успешно остановлен" },
                { "Log_StopCancelled", "Остановка отменена" },
                { "Log_KillFailedOnCancel", "Не удалось завершить процесс при отмене: {0}" },
                { "Log_StopError", "Ошибка остановки: {0}" },
                { "Log_CommandSent", "CMD] > {0}" },
                { "Log_CommandSendError", "Ошибка отправки команды '{0}': {1}" },
                { "Log_OldLogDeleted", "Удалён старый лог: latest.log" },
                { "Log_LogMoved", "Перемещён заблокированный лог: latest.log -> {0}" },
                { "Log_CleanupFailed", "Не удалось очистить старые логи: {0}" },
                { "Log_ConfigUnavailable", "Конфигурация недоступна, используем Java из PATH" },
                { "Log_UsingServerJava", "Используем Java из настроек сервера: {0}" },
                { "Log_AutoSelectJava", "Автовыбор Java: требуется Java {0}+ для Minecraft {1}" },
                { "Log_JavaSelected", "Выбрана Java: {0}" },
                { "Log_UsingJava", "Используем Java: {0}" },
                { "Log_AutoRestart", "Авто-рестарт через" },
                { "Log_Seconds", "сек" },
                { "Log_ServerFullyLoaded", "Сервер полностью загрузился" },
                { "Log_ServerStarting", "Запуск сервера: {0}" },
                { "Log_KillFailed", "Не удалось завершить процесс: {0}" },
                { "Log_ServerStoppedCode0", "Сервер остановлен (код выхода: 0)" },
                { "Log_MonitorCancelled", "Мониторинг процесса отменён" },
                { "Log_MonitorError", "Ошибка мониторинга процесса: {0}" },

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
                { "Error_OutOfMemory", "Недостаточно памяти" },

                // Java Error Dialog
                { "JavaError_Title", "Ошибка Java" },
                { "JavaError_Required", "Требуется установить или обновить Java" },
                { "JavaError_DownloadText", "Скачайте последнюю версию Java с официального сайта:" },
                { "JavaError_DownloadBtn", "Скачать Java (adoptium.net)" },

                // Common UI
                { "Common_NotSelected", "Не выбрано" },
                { "Common_Default", "По умолчанию" },
                { "Common_Unknown", "Неизвестно" },
                { "Common_Preparing", "Подготовка..." },

                // Settings Page
                { "Settings_SelectServerFolder", "Выберите папку для серверов" },
                { "Settings_SelectJava", "Выберите java.exe или javaw.exe" },
                { "Settings_JavaFilter", "Java executable|java.exe|JavaW executable|javaw.exe" },
                { "Settings_JavaAdded", "Java добавлена" },
                { "Settings_JavaVersion", "Версия" },
                { "Settings_JavaPath", "Путь" },
                { "Settings_JavaInvalid", "Не удалось добавить Java. Проверьте путь к файлу.\n\nУбедитесь, что выбранный файл является java.exe или javaw.exe" },

                // ServerPropertiesEditor
                { "Props_Loaded", "Свойства загружены" },
                { "Props_NotLoaded", "Ошибка: файл не загружен" },
                { "Props_Saved", "Свойства сохранены!" },
                { "Props_SaveError", "Ошибка сохранения" },
                { "Props_Reset", "Свойства сброшены" },

                // ServerDetail
                { "ServerDetail_OperationError", "Не удалось выполнить операцию" },
                { "ServerDetail_OpenFolderError", "Не удалось открыть папку сервера" },
                { "ServerDetail_PropsLoadError", "Ошибка загрузки свойств" },
                { "ServerDetail_ModsLoadError", "Ошибка загрузки модов" },
                { "ServerDetail_PluginsLoadError", "Ошибка загрузки плагинов" },
                { "ServerDetail_ModsFolderNotFound", "Папка mods не найдена" },
                { "ServerDetail_PluginsFolderNotFound", "Папка plugins не найдена" },
                { "ServerDetail_FolderOpenError", "Не удалось открыть папку" },
                { "ServerDetail_DeleteModConfirm", "Удалить мод \"{0}\"?\n\nФайл: {1}" },
                { "ServerDetail_DeleteModTitle", "Удаление мода" },
                { "ServerDetail_DeletePluginConfirm", "Удалить плагин \"{0}\"?\n\nФайл: {1}" },
                { "ServerDetail_DeletePluginTitle", "Удаление плагина" },
                { "ServerDetail_ModDeleteError", "Ошибка удаления мода" },
                { "ServerDetail_PluginDeleteError", "Ошибка удаления плагина" },
                { "ServerDetail_DeleteServerError", "Ошибка при удалении сервера" },
                { "ServerDetail_JavaDefault", "По умолчанию" },
                { "ServerDetail_JavaNotSelected", "не выбрана" },

                // ServersPage
                { "ServersPage_AppNotInitialized", "Ошибка: приложение не инициализировано" },
                { "ServersPage_OpenFolderWarning", "Не удалось открыть папку сервера" },
                { "ServersPage_DeleteError", "Ошибка при удалении сервера" },
                { "ServersPage_OperationError", "Не удалось выполнить операцию" },

                // MainWindow
                { "MainWindow_Error", "Ошибка" },
                { "MainWindow_JavaVersions", "версий Java" },

                // App
                { "App_StartupError", "Ошибка инициализации приложения" },
                { "App_StartupErrorDetail", "Проверьте логи в %AppData%\\Konserva\\Logs" },
                { "App_UnhandledError", "Необработанное исключение" },
                { "App_UnhandledErrorDetail", "Приложение будет закрыто." }
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
                { "Settings_Servers_Dir", "Servers Directory" },
                { "Settings_Servers_DirDesc", "Directory for saving server" },
                { "Settings_Servers_Browse", "Browse" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Desc", "Directory for Java" },
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
                { "CreateServer_Filter_Stable", "Show pre-releases" },
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

                // Server Logs
                { "Log_ServerStarted", "Server fully loaded" },
                { "Log_JavaVersionMismatch", "JAVA VERSION MISMATCH!" },
                { "Log_JavaVersionMismatch_Detail", "Minecraft {0} requires Java {1} or higher" },
                { "Log_JavaFound", "Found Java: {0} (version {1})" },
                { "Log_JavaPath", "Java path: {0}" },
                { "Log_Solution", "Solution:" },
                { "Log_Solution_Install_Java", "1. Install Java {0}: https://adoptium.net/" },
                { "Log_Solution_Add_Java", "2. Specify Java {0} path in application settings" },
                { "Log_Solution_Older_Minecraft", "3. Or select an older Minecraft version" },
                { "Log_JavaNotFound", "JAVA NOT FOUND!" },
                { "Log_JavaNotFound_Path", "Java path: {0}" },
                { "Log_JavaNotFound_Error", "Error: {0}" },
                { "Log_Solution_Install_Java_General", "1. Install Java: https://adoptium.net/" },
                { "Log_Solution_Add_Java_Settings", "2. Add Java in application settings ('Add Java' button)" },
                { "Log_Solution_Check_PATH", "3. Make sure Java is added to PATH" },
                { "Log_CriticalError", "Critical startup error!" },
                { "Log_ErrorType", "Type: {0}" },
                { "Log_ErrorMessage", "Message: {0}" },
                { "Log_ErrorOutput", "Error output: {0}" },
                { "Log_ServerStoppedWithCode", "Server exited with error code: {0}" },
                { "Log_ServerConfigProblem", "This may indicate a configuration problem" },
                { "Log_MemoryProblem", "or insufficient memory." },
                { "Log_TimeoutForceKill", "Stop timeout, force killing..." },
                { "Log_ForceKillAttempt", "Attempting force kill..." },
                { "Log_ServerNotRunning", "Attempting to send command to stopped server: {0}" },
                { "Log_JavaFromSettingsNotFound", "Java from server settings not found: {0}" },
                { "Log_JavaVersionNotFound_TryDefault", "Java {0}+ not found, trying default Java" },
                { "Log_JavaDefaultNotFound", "Default Java not found: {0}" },
                { "Log_UsingJavaFromPATH", "Using Java from PATH" },

                // Console log - startup info
                { "Log_ServerId", "Server ID: {0}" },
                { "Log_ModLoader", "ModLoader: {0}" },
                { "Log_MinecraftVersion", "Minecraft Version: {0}" },
                { "Log_ServerFolder", "Server Folder: {0}" },
                { "Log_LaunchType", "Launch Type: {0}" },
                { "Log_JarFile", "JAR File: {0} ({1} MB)" },
                { "Log_JavaVersion", "Java Version: {0}" },
                { "Log_JavaPath_Info", "Java Path: {0}" },
                { "Log_JavaArgs", "Java Arguments: {0}" },
                { "Log_WorkingDirectory", "Working Directory: {0}" },
                { "Log_LaunchingProcess", "Launching process..." },
                { "Log_WaitingForStartup", "Waiting for server startup..." },
                { "Log_LaunchCancelled", "Launch cancelled" },
                { "Log_EulaAccepted", "EULA accepted" },
                { "Log_JavaVersionCompatible", "Java version is compatible (requires {0}+)" },
                { "Log_ProcessStarted", "Process started, PID: {0}" },
                { "Log_LaunchForceCancelled", "Launch force cancelled" },
                { "Log_ProcessForceKilled", "Process force killed (server not yet loaded)" },
                { "Log_ExitError", "Exit error: {0}" },
                { "Log_StoppingServer", "Stopping server..." },
                { "Log_SendingStopCommand", "Sending 'stop' command..." },
                { "Log_WaitingForProcessExit", "Waiting for process to exit (60 sec)..." },
                { "Log_ProcessForceKilledAfterTimeout", "Process force killed after timeout" },
                { "Log_ServerStoppedSuccessfully", "Server stopped successfully" },
                { "Log_StopCancelled", "Stop cancelled" },
                { "Log_KillFailedOnCancel", "Failed to kill process on cancel: {0}" },
                { "Log_StopError", "Stop error: {0}" },
                { "Log_CommandSent", "CMD] > {0}" },
                { "Log_CommandSendError", "Failed to send command '{0}': {1}" },
                { "Log_OldLogDeleted", "Old log deleted: latest.log" },
                { "Log_LogMoved", "Locked log moved: latest.log -> {0}" },
                { "Log_CleanupFailed", "Failed to clean old logs: {0}" },
                { "Log_ConfigUnavailable", "Config unavailable, using Java from PATH" },
                { "Log_UsingServerJava", "Using Java from server settings: {0}" },
                { "Log_AutoSelectJava", "Auto-selecting Java: requires Java {0}+ for Minecraft {1}" },
                { "Log_JavaSelected", "Selected Java: {0}" },
                { "Log_UsingJava", "Using Java: {0}" },
                { "Log_AutoRestart", "Auto-restart in" },
                { "Log_Seconds", "sec" },
                { "Log_ServerFullyLoaded", "Server fully loaded" },
                { "Log_ServerStarting", "Starting server: {0}" },
                { "Log_KillFailed", "Failed to kill process: {0}" },
                { "Log_ServerStoppedCode0", "Server stopped (exit code: 0)" },
                { "Log_MonitorCancelled", "Process monitoring cancelled" },
                { "Log_MonitorError", "Process monitoring error: {0}" },

                // MessageBox Buttons
                { "MsgBtn_OK", "OK" },
                { "MsgBtn_Yes", "Yes" },
                { "MsgBtn_No", "No" },
                { "MsgBtn_Cancel", "Cancel" },
                { "MsgBtn_Delete", "Delete" },

                // MessageBox Titles
                { "MsgTitle_Info", "Information" },
                { "MsgTitle_Warning", "Warning" },
                { "MsgTitle_Error", "Error" },
                { "MsgTitle_Confirm", "Confirm" },
                { "MsgTitle_DeleteServer", "Delete Server" },

                // MessageBox Info Messages
                { "MsgInfo_Title", "Information" },

                // MessageBox Warning Messages
                { "MsgWarning_Title", "Warning" },

                // MessageBox Error Messages
                { "MsgError_Title", "Error" },

                // MessageBox Confirm Messages
                { "MsgConfirm_Title", "Confirm" },

                // MessageBox Delete Messages
                { "MsgDel_Title", "Delete Server" },
                { "MsgDel_Confirm", "Are you sure you want to delete server \"{0}\"?" },
                { "MsgDel_WillBeDeleted", "The following will be deleted:" },
                { "MsgDel_ServerFiles", "All server files" },
                { "MsgDel_ConfigFiles", "Configuration files" },
                { "MsgDel_WorldSaves", "World and saves" },
                { "MsgDel_LogsBackups", "Logs and backups" },
                { "MsgDel_Irreversible", "This action is irreversible!" },

                // ServerDetail
                { "ServerDetail_Title", "Server Details" },
                { "ServerDetail_Console", "Console" },
                { "ServerDetail_Properties", "File server.properties" },
                { "ServerDetail_Settings", "Server Settings" },
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
                { "ServerProperties_ServerPort_Desc", "Connection port. Forward it on your router" },
                { "ServerProperties_ServerIp", "Server IP" },
                { "ServerProperties_ServerIp_Desc", "Leave blank to bind to all network interfaces" },
                { "ServerProperties_MaxPlayers", "Max Players" },
                { "ServerProperties_MaxPlayers_Desc", "How many players can be on the server at once" },
                { "ServerProperties_ViewDistance", "View Distance" },
                { "ServerProperties_ViewDistance_Desc", "How many chunks clients can see" },
                { "ServerProperties_SimulationDistance", "Simulation Distance" },
                { "ServerProperties_SimulationDistance_Desc", "How far mobs and world processes update" },
                { "ServerProperties_MOTD", "MOTD" },
                { "ServerProperties_MOTD_Desc", "Text players see in the server list" },
                { "ServerProperties_Gamemode", "Game Mode" },
                { "ServerProperties_Gamemode_Desc", "Default game mode for new players" },
                { "ServerProperties_Gamemode_Survival", "Survival" },
                { "ServerProperties_Gamemode_Creative", "Creative" },
                { "ServerProperties_Gamemode_Adventure", "Adventure" },
                { "ServerProperties_Gamemode_Spectator", "Spectator" },
                { "ServerProperties_Difficulty", "Difficulty" },
                { "ServerProperties_Difficulty_Desc", "Mob damage, hunger, and other survival mechanics" },
                { "ServerProperties_Difficulty_Peaceful", "Peaceful" },
                { "ServerProperties_Difficulty_Easy", "Easy" },
                { "ServerProperties_Difficulty_Normal", "Normal" },
                { "ServerProperties_Difficulty_Hard", "Hard" },
                { "ServerProperties_Hardcore", "Hardcore" },
                { "ServerProperties_Hardcore_Desc", "Death is permanent. Cannot be disabled on a running server" },
                { "ServerProperties_PvP", "PvP" },
                { "ServerProperties_PvP_Desc", "Whether players can attack each other" },
                { "ServerProperties_CommandBlocks", "Command Blocks" },
                { "ServerProperties_CommandBlocks_Desc", "Enables command blocks in the world" },
                { "ServerProperties_World", "World" },
                { "ServerProperties_LevelName", "Level Name" },
                { "ServerProperties_LevelName_Desc", "Name of the world save folder" },
                { "ServerProperties_LevelSeed", "Level Seed" },
                { "ServerProperties_LevelSeed_Desc", "World generation code. Leave blank for random" },
                { "ServerProperties_LevelType", "Level Type" },
                { "ServerProperties_LevelType_Desc", "Landscape generation type" },
                { "ServerProperties_LevelType_Normal", "Normal" },
                { "ServerProperties_LevelType_Flat", "Flat" },
                { "ServerProperties_LevelType_LargeBiomes", "Large Biomes" },
                { "ServerProperties_LevelType_Amplified", "Amplified" },
                { "ServerProperties_LevelType_SingleBiome", "Single Biome" },
                { "ServerProperties_MaxWorldSize", "Max World Size" },
                { "ServerProperties_MaxWorldSize_Desc", "Maximum world radius in blocks" },
                { "ServerProperties_SpawnStructures", "Spawn Structures" },
                { "ServerProperties_SpawnStructures_Desc", "Villages, temples, strongholds, and other world builds" },
                { "ServerProperties_AllowNether", "Allow Nether" },
                { "ServerProperties_AllowNether_Desc", "Allow Nether portal" },
                { "ServerProperties_Network", "Network" },
                { "ServerProperties_OnlineMode", "Online Mode" },
                { "ServerProperties_OnlineMode_Desc", "Disable for cracked clients" },
                { "ServerProperties_Whitelist", "Whitelist" },
                { "ServerProperties_Whitelist_Desc", "Only players on the list can join the server" },
                { "ServerProperties_EnforceWhitelist", "Enforce Whitelist" },
                { "ServerProperties_EnforceWhitelist_Desc", "Kick players removed from the whitelist" },
                { "ServerProperties_EnforceSecureProfile", "Enforce Secure Profile" },
                { "ServerProperties_EnforceSecureProfile_Desc", "Require signed profile from clients" },
                { "ServerProperties_AllowFlight", "Allow Flight" },
                { "ServerProperties_AllowFlight_Desc", "Allow flying. Enable for flight mods" },
                { "ServerProperties_RCON", "RCON" },
                { "ServerProperties_EnableRcon", "Enable RCON" },
                { "ServerProperties_EnableRcon_Desc", "Remote server management over network" },
                { "ServerProperties_RconPassword", "RCON Password" },
                { "ServerProperties_RconPassword_Desc", "Password for RCON connection" },
                { "ServerProperties_RconPort", "RCON Port" },
                { "ServerProperties_RconPort_Desc", "Port for RCON connections" },
                { "ServerProperties_RconIp", "RCON IP" },
                { "ServerProperties_RconIp_Desc", "IP for RCON. Default = all interfaces" },
                { "ServerProperties_Advanced", "Advanced" },
                { "ServerProperties_SpawnProtection", "Spawn Protection" },
                { "ServerProperties_SpawnProtection_Desc", "Protection radius around spawn. 0 = disabled" },
                { "ServerProperties_SpawnRadius", "Spawn Radius" },
                { "ServerProperties_SpawnRadius_Desc", "Where players spawn relative to the spawn point" },
                { "ServerProperties_OpPermissionLevel", "OP Permission Level" },
                { "ServerProperties_OpPermissionLevel_Desc", "Maximum access level (1–4). 4 = all commands" },
                { "ServerProperties_MaxTickTime", "Max Tick Time" },
                { "ServerProperties_MaxTickTime_Desc", "Max tick time in ms (-1 = disabled)" },
                { "ServerProperties_NetworkCompression", "Network Compression" },
                { "ServerProperties_NetworkCompression_Desc", "Packet compression threshold" },
                { "ServerProperties_SpawnNPCs", "Spawn NPCs" },
                { "ServerProperties_SpawnNPCs_Desc", "Villager spawning" },
                { "ServerProperties_SpawnAnimals", "Spawn Animals" },
                { "ServerProperties_SpawnAnimals_Desc", "Cow, pig, and other animal spawning" },
                { "ServerProperties_SpawnMonsters", "Spawn Monsters" },
                { "ServerProperties_SpawnMonsters_Desc", "Zombie, skeleton, and creeper spawning" },
                { "ServerProperties_EnabledPacks", "Enabled Datapacks" },
                { "ServerProperties_EnabledPacks_Desc", "List of enabled datapacks, comma-separated" },
                { "ServerProperties_DisabledPacks", "Disabled Datapacks" },
                { "ServerProperties_DisabledPacks_Desc", "List of disabled datapacks, comma-separated" },
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
                { "Error_OutOfMemory", "Out of memory" },

                // Java Error Dialog
                { "JavaError_Title", "Java Error" },
                { "JavaError_Required", "Java installation or update is required" },
                { "JavaError_DownloadText", "Download the latest Java version from the official website:" },
                { "JavaError_DownloadBtn", "Download Java (adoptium.net)" },

                // Common UI
                { "Common_NotSelected", "Not selected" },
                { "Common_Default", "Default" },
                { "Common_Unknown", "Unknown" },
                { "Common_Preparing", "Preparing..." },

                // Settings Page
                { "Settings_SelectServerFolder", "Select servers folder" },
                { "Settings_SelectJava", "Select java.exe or javaw.exe" },
                { "Settings_JavaFilter", "Java executable|java.exe|JavaW executable|javaw.exe" },
                { "Settings_JavaAdded", "Java added" },
                { "Settings_JavaVersion", "Version" },
                { "Settings_JavaPath", "Path" },
                { "Settings_JavaInvalid", "Failed to add Java. Check the file path.\n\nMake sure the selected file is java.exe or javaw.exe" },

                // ServerPropertiesEditor
                { "Props_Loaded", "Properties loaded" },
                { "Props_NotLoaded", "Error: file not loaded" },
                { "Props_Saved", "Properties saved!" },
                { "Props_SaveError", "Save error" },
                { "Props_Reset", "Properties reset" },

                // ServerDetail
                { "ServerDetail_OperationError", "Failed to perform operation" },
                { "ServerDetail_OpenFolderError", "Failed to open server folder" },
                { "ServerDetail_PropsLoadError", "Error loading properties" },
                { "ServerDetail_ModsLoadError", "Error loading mods" },
                { "ServerDetail_PluginsLoadError", "Error loading plugins" },
                { "ServerDetail_ModsFolderNotFound", "Mods folder not found" },
                { "ServerDetail_PluginsFolderNotFound", "Plugins folder not found" },
                { "ServerDetail_FolderOpenError", "Failed to open folder" },
                { "ServerDetail_DeleteModConfirm", "Delete mod \"{0}\"?\n\nFile: {1}" },
                { "ServerDetail_DeleteModTitle", "Delete Mod" },
                { "ServerDetail_DeletePluginConfirm", "Delete plugin \"{0}\"?\n\nFile: {1}" },
                { "ServerDetail_DeletePluginTitle", "Delete Plugin" },
                { "ServerDetail_ModDeleteError", "Error deleting mod" },
                { "ServerDetail_PluginDeleteError", "Error deleting plugin" },
                { "ServerDetail_DeleteServerError", "Error deleting server" },
                { "ServerDetail_JavaDefault", "Default" },
                { "ServerDetail_JavaNotSelected", "not selected" },

                // ServersPage
                { "ServersPage_AppNotInitialized", "Error: application not initialized" },
                { "ServersPage_OpenFolderWarning", "Failed to open server folder" },
                { "ServersPage_DeleteError", "Error deleting server" },
                { "ServersPage_OperationError", "Failed to perform operation" },

                // MainWindow
                { "MainWindow_Error", "Error" },
                { "MainWindow_JavaVersions", "Java versions" },

                // App
                { "App_StartupError", "Application initialization error" },
                { "App_StartupErrorDetail", "Check logs in %AppData%\\Konserva\\Logs" },
                { "App_UnhandledError", "Unhandled exception" },
                { "App_UnhandledErrorDetail", "Application will be closed." }
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
