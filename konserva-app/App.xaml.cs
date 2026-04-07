using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text;
using System.Windows;

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
            $"{LocalizationManager.Get("App_StartupError")}:\n{ex.Message}\n\n" +
            LocalizationManager.Get("App_StartupErrorDetail"),
            LocalizationManager.Get("MsgTitle_Error"),
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

    private static async Task ShutdownStaticAsync(int exitCode)
    {
        await CleanupAsync();
        Current.Shutdown(exitCode);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled exception", e.Exception, "App");

        MessageBox.Show(
            $"{LocalizationManager.Get("App_UnhandledError")}:\n{e.Exception.Message}\n\n" +
            LocalizationManager.Get("App_UnhandledErrorDetail"),
            LocalizationManager.Get("MsgTitle_Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        // Не закрываем приложение - даём пользователю возможность сохранить данные
    }
}
