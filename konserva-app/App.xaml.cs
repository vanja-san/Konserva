using Konserva.Services;
using Konserva.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Windows;
using Konserva.Localization;

namespace Konserva;

/// <summary>
/// Главный класс приложения
/// </summary>
public partial class App : Application
{
    private static IServiceProvider? _serviceProvider;
    private static Logger? _logger;

    /// <summary>
    /// Сервис конфигурации
    /// </summary>
    public static IConfigService ConfigService =>
        _serviceProvider?.GetRequiredService<IConfigService>()
        ?? throw new InvalidOperationException("App not initialized");

    /// <summary>
    /// Менеджер серверов
    /// </summary>
    public static IServerManager ServerManager =>
        _serviceProvider?.GetRequiredService<IServerManager>()
        ?? throw new InvalidOperationException("App not initialized");

    /// <summary>
    /// Сервис провайдер
    /// </summary>
    public static IServiceProvider? ServiceProvider => _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Сначала инициализируем логгер
        Logger.Initialize();
        _logger = new Logger();

        base.OnStartup(e);

        Logger.Info("Application starting...", "App");

        // 2. Инициализация локализации
        try
        {
            LocalizationManager.Initialize();
            Logger.Info("Localization initialized", "App");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to initialize localization: {ex.Message}", ex, "App");
        }

        // 3. Создаём файлы локализации если не существуют (уже сделано в Initialize)
        var i18nPath = System.IO.Path.Combine(AppContext.BaseDirectory, "i18n");

        // 4. Регистрируем провайдер кодировок для поддержки OEM кодировок (866 и др.)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 5. Перехват глобальных исключений
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.Critical($"AppDomain exception: {ex?.Message}", ex, "App");
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Logger.Error($"Unobserved task exception: {args.Exception?.Message}", args.Exception, "App");
            args.SetObserved();
        };

        // 6. Запускаем асинхронную инициализацию
        _ = StartupAsync();
    }

    private static async Task StartupAsync()
    {
        try
        {
            Logger.Info("Services initializing...", "App");

            // Настройка DI
            var services = ConfigureServices;
            _serviceProvider = services.BuildServiceProvider();

            Logger.Info("DI container built", "App");

            // Инициализация сервисов
            await InitializeServicesAsync();

            Logger.Info("Services initialized successfully", "App");

            // Применяем язык из конфига
            try
            {
                var config = ConfigService.GetConfig();
                var language = config.Language ?? "System"; // По умолчанию - язык системы
                
                // Определяем фактический язык
                string actualLanguage;
                if (language == "System")
                {
                    var systemLanguage = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    actualLanguage = systemLanguage == "ru" ? "ru" : "en";
                }
                else
                {
                    actualLanguage = language;
                }
                
                LocalizationManager.SetLanguage(actualLanguage);
                Logger.Info($"Applied language from config: {language} ({actualLanguage})", "App");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply language from config: {ex.Message}", ex, "App");
            }

            // Показываем главное окно
            var mainWindow = new MainWindow();
            mainWindow.Show();

            Logger.Info("Application started successfully", "App");
        }
        catch (Exception ex)
        {
            await HandleStartupErrorAsync(ex);
        }
    }

    /// <summary>
    /// Настройка Dependency Injection
    /// </summary>
    private static IServiceCollection ConfigureServices
    {
        get
        {
            var services = new ServiceCollection();

            // Сервисы (Singleton)
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IServerStorageService, ServerStorageService>();
            services.AddSingleton<IServerManager, McServerManager>();
            services.AddSingleton<IMcVersionsApi, McVersionsApi>();
            services.AddSingleton<IJavaManagementService, JavaManagementService>();

            // HttpClient для API
            services.AddHttpClient<IMcVersionsApi, McVersionsApi>();

            return services;
        }
    }

    /// <summary>
    /// Инициализация сервисов
    /// </summary>
    private static async Task InitializeServicesAsync()
    {
        try
        {
            // Инициализация API версий
            var versionsApi = _serviceProvider?.GetService<IMcVersionsApi>();
            
            // Инициализация McServerInstaller (требуется для установки серверов)
            var httpClient = _serviceProvider?.GetService<HttpClient>() 
                ?? new HttpClient();
            var configService = _serviceProvider?.GetService<IConfigService>();
            McServerInstaller.Initialize(httpClient, configService);
            
            Logger.Info("McServerInstaller initialized", "App");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Service initialization error: {ex.Message}", "App");
        }
    }

    /// <summary>
    /// Обработка ошибки запуска
    /// </summary>
    private static async Task HandleStartupErrorAsync(Exception ex)
    {
        Logger.Critical("Startup error", ex, "App");

        MessageBox.Show(
            $"Ошибка инициализации приложения:\n{ex.Message}\n\n" +
            $"Проверьте логи в %AppData%\\Konserva\\Logs",
            "Ошибка запуска",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        await ShutdownStaticAsync(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ = CleanupAsync();

        base.OnExit(e);
    }

    /// <summary>
    /// Очистка ресурсов
    /// </summary>
    private static async Task CleanupAsync()
    {
        try
        {
            // Остановка всех серверов
            if (_serviceProvider?.GetService<IServerManager>() is McServerManager serverManager)
            {
                var servers = serverManager.GetServers();
                var runningServers = servers.Where(s => s.IsRunning).ToList();

                if (runningServers.Count > 0)
                {
                    Logger.Info($"Stopping {runningServers.Count} server(s) on shutdown", "App");

                    // Пытаемся остановить все серверы с таймаутом
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var stopTasks = runningServers.Select(s =>
                        serverManager.StopServerAsync(s.Id, cts.Token));

                    try
                    {
                        await Task.WhenAll(stopTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warning("Timeout stopping servers, continuing shutdown", "App");
                    }
                }

                // Dispose всех процессов
                foreach (var process in serverManager.GetProcesses())
                {
                    process?.Dispose();
                }
            }

            // Очистка пула HttpClient
            if (_logger != null)
            {
                await _logger.DisposeAsync();
            }

            // Очистка DI контейнера
            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _serviceProvider = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
        }
    }

    private async Task ShutdownAsync(int exitCode)
    {
        await CleanupAsync();
        Shutdown(exitCode);
    }

    private static async Task ShutdownStaticAsync(int exitCode)
    {
        await CleanupAsync();
        Current.Shutdown(exitCode);
    }

    /// <summary>
    /// Возвращает словарь переводов для указанной культуры.
    /// </summary>
    private static Dictionary<string, string> GetTranslationsForCulture(string culture)
    {
        return culture switch
        {
            "ru" => new Dictionary<string, string>
            {
                { "MainWindow_Title", "Konserva — Менеджер серверов Minecraft" },
                { "MainWindow_Servers", "Серверы" },
                { "MainWindow_Settings", "Настройки" },
                { "MainWindow_CreateServer", "Создать сервер" },
                { "StatusBar_TotalServers", "Всего серверов" },
                { "StatusBar_Running", "Запущено" },
                { "StatusBar_Memory", "Память" },
                { "StatusBar_Java_Configured", "Java настроена" },
                { "StatusBar_Java_NotConfigured", "Java не настроена" },
                { "StatusBar_Version", "Версия" },
                { "Settings_Title", "Настройки" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Добавить Java" },
                { "Settings_Servers", "Серверы" },
                { "Settings_Servers_Directory", "Папка серверов" },
                { "Settings_Servers_Browse", "Обзор" },
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
                { "Settings_About", "О программе" },
                { "Settings_About_Version", "Версия" },
                { "Settings_About_Description", "Менеджер серверов Minecraft" },
                { "Settings_About_ModalLoaders", "Поддерживаемые модлоадеры:" },
                { "Settings_About_InDevelopment", "В разработке" },
                { "Message_SettingsSaved", "Настройки сохранены" },
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
                { "ServersPage_Search", "Поиск..." },
                { "ServersPage_Filter_All", "Все типы" },
                { "ServersPage_Filter_AllServers", "Все серверы" },
                { "ServersPage_Filter_Running", "Запущен" },
                { "ServersPage_Filter_Stopped", "Остановлен" },
                { "ServersPage_Create", "Создать сервер" },
                { "Common_Cancel", "Отмена" },
                { "ModLoader_Vanilla", "Vanilla" },
                { "ModLoader_Forge", "Forge" },
                { "ModLoader_NeoForge", "NeoForge" },
                { "ModLoader_Fabric", "Fabric" },
                { "ModLoader_Quilt", "Quilt" },
                { "ModLoader_Paper", "Paper" },
                { "ModLoader_Purpur", "Purpur" },
                { "ModLoader_Spigot", "Spigot" }
            },
            "en" => new Dictionary<string, string>
            {
                { "MainWindow_Title", "Konserva — Minecraft Server Manager" },
                { "MainWindow_Servers", "Servers" },
                { "MainWindow_Settings", "Settings" },
                { "MainWindow_CreateServer", "Create Server" },
                { "StatusBar_TotalServers", "Total Servers" },
                { "StatusBar_Running", "Running" },
                { "StatusBar_Memory", "Memory" },
                { "StatusBar_Java_Configured", "Java configured" },
                { "StatusBar_Java_NotConfigured", "Java not configured" },
                { "StatusBar_Version", "Version" },
                { "Settings_Title", "Settings" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Add Java" },
                { "Settings_Servers", "Servers" },
                { "Settings_Servers_Directory", "Servers Directory" },
                { "Settings_Servers_Browse", "Browse" },
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
                { "Settings_About", "About" },
                { "Settings_About_Version", "Version" },
                { "Settings_About_Description", "Minecraft Server Manager" },
                { "Settings_About_ModalLoaders", "Supported Mod Loaders:" },
                { "Settings_About_InDevelopment", "In Development" },
                { "Message_SettingsSaved", "Settings saved" },
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
                { "ServersPage_Search", "Search..." },
                { "ServersPage_Filter_All", "All Types" },
                { "ServersPage_Filter_AllServers", "All Servers" },
                { "ServersPage_Filter_Running", "Running" },
                { "ServersPage_Filter_Stopped", "Stopped" },
                { "ServersPage_Create", "Create Server" },
                { "Common_Cancel", "Cancel" },
                { "ModLoader_Vanilla", "Vanilla" },
                { "ModLoader_Forge", "Forge" },
                { "ModLoader_NeoForge", "NeoForge" },
                { "ModLoader_Fabric", "Fabric" },
                { "ModLoader_Quilt", "Quilt" },
                { "ModLoader_Paper", "Paper" },
                { "ModLoader_Purpur", "Purpur" },
                { "ModLoader_Spigot", "Spigot" }
            },
            _ => new Dictionary<string, string>()
        };
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled exception", e.Exception, "App");

        MessageBox.Show(
            $"Необработанное исключение:\n{e.Exception.Message}\n\n" +
            $"Приложение будет закрыто.",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        // Не закрываем приложение - даём пользователю возможность сохранить данные
    }
}
