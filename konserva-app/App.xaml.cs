using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Resilience;
using Polly;
using System.Net;
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
    /// Главное окно приложения
    /// </summary>
    public static new MainWindow MainWindow =>
        _serviceProvider?.GetRequiredService<MainWindow>()
        ?? throw new InvalidOperationException("App not initialized");

    /// <summary>
    /// Сервис провайдер
    /// </summary>
    public static IServiceProvider? ServiceProvider => _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Сначала инициализируем логгер
        Logger.Initialize();

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
            var services = ConfigureServices();
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

            // Показываем главное окно (создаётся через DI)
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
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
    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // Сервисы (Singleton)
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IServerStorageService, ServerStorageService>();
        services.AddSingleton<IServerManager, McServerManager>();
        services.AddSingleton<IPortForwardingService, PortForwardingService>();
        services.AddSingleton<IJavaManagementService, JavaManagementService>();
        services.AddSingleton<IServerInstaller>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var configService = sp.GetService<IConfigService>();
            var httpClient = httpClientFactory.CreateClient("McServerInstaller");
            return new McServerInstaller(httpClient, configService);
        });
        services.AddSingleton<MainWindow>();

        // HttpClient для API с retry политикой
        services.AddHttpClient<IMcVersionsApi, McVersionsApi>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(30);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 10,
                AutomaticDecompression = DecompressionMethods.All
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
            });

        // HttpClient для UpdateChecker (проверка обновлений GitHub)
        services.AddHttpClient("UpdateChecker", options =>
        {
            options.Timeout = TimeSpan.FromSeconds(15);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
            options.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 10,
                AutomaticDecompression = DecompressionMethods.All
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
            });

        // HttpClient для AppUpdater (скачивание обновлений)
        services.AddHttpClient("AppUpdater", options =>
        {
            options.Timeout = TimeSpan.FromMinutes(10);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 10,
                AutomaticDecompression = DecompressionMethods.All
            });

        // HttpClient для McServerInstaller (скачивание серверов) с retry политикой
        services.AddHttpClient("McServerInstaller", options =>
        {
            options.Timeout = TimeSpan.FromMinutes(5);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 10,
                AutomaticDecompression = DecompressionMethods.All
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
            });

        return services;
        // конец ConfigureServices()
    }

    /// <summary>
    /// Инициализация сервисов
    /// </summary>
    private static async Task InitializeServicesAsync()
    {
        try
        {
            var httpClientFactory = _serviceProvider?.GetService<IHttpClientFactory>();

            // Инициализация UpdateChecker
            var updateCheckHttpClient = httpClientFactory?.CreateClient("UpdateChecker")
                ?? new HttpClient();
            UpdateChecker.Initialize(updateCheckHttpClient);
            Logger.Info("UpdateChecker initialized", "App");

            // Инициализация AppUpdater
            var updateHttpClient = httpClientFactory?.CreateClient("AppUpdater")
                ?? new HttpClient();
            AppUpdater.Initialize(updateHttpClient);
            Logger.Info("AppUpdater initialized", "App");
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

        // MessageBox должен быть на UI потоке (StartupAsync выполняется в фоне)
        await Current.Dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show(
                $"{LocalizationManager.Get("App_StartupError")}:\n{ex.Message}\n\n" +
                LocalizationManager.Get("App_StartupErrorDetail"),
                LocalizationManager.Get("MsgTitle_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });

        await ShutdownStaticAsync(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Дожидаемся остановки серверов (иначе Java процессы останутся в фоне и заблокируют порт)
        try
        {
            Task.Run(async () => await CleanupAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Error($"Cleanup error during shutdown: {ex.Message}", ex, "App");
        }

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
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var stopTasks = runningServers.Select(s =>
                        serverManager.StopServerAsync(s.Id, cts.Token));

                    try
                    {
                        await Task.WhenAll(stopTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warning("Timeout stopping servers, force killing remaining", "App");
                    }

                    // Принудительно убиваем оставшиеся Java процессы из папок серверов
                    foreach (var server in runningServers)
                    {
                        McServerManager.KillZombieProcesses(server.Path);
                    }
                }

                // Dispose всех процессов
                foreach (var process in serverManager.GetProcesses())
                {
                    process?.Dispose();
                }
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
            Logger.Error($"Cleanup error: {ex.Message}", ex, "App");
        }
    }

    private static async Task ShutdownStaticAsync(int exitCode)
    {
        await CleanupAsync();
        await Current.Dispatcher.InvokeAsync(() => Current.Shutdown(exitCode));
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
