using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace Konserva;

/// <summary>
/// Main application class
/// </summary>
public partial class App : Application
{
    // Single-instance
    private const string MutexName = "Global\\KonservaApp-9E7A4F5B-2D3C-4A1E-8B6F-9C0D1E2F3A4B";
    private const string PipeName = "KonservaApp-Pipe-9E7A4F5B";
    private static Mutex? _instanceMutex;
    private static CancellationTokenSource? _pipeCts;

    /// <summary>
    /// DI provider for accessing services.
    /// Use Ioc.Default.GetService&lt;T&gt;() in the rest of the application.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance check
        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex = null;
            BringExistingInstanceToFront();
            Environment.Exit(0);
            return;
        }

        // 1. Initialize the logger first
        Logger.Initialize();

        base.OnStartup(e);

        Logger.Info("Application starting...", "App");

        // 2. Localization initialization
        try
        {
            LocalizationManager.Initialize();
            Logger.Info("Localization initialized", "App");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to initialize localization: {ex.Message}", ex, "App");
        }

        // 3. Create the localization files if they do not exist (already done in Initialize)

        // 4. Register the encoding provider to support OEM encodings (866, etc.)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 5. Hooking up global exception handling
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

        // 6. Start asynchronous initialization
        StartupAsync().SafeFireAndForget(errorMessage: "App startup failed");
    }

    private static async Task StartupAsync()
    {
        try
        {
            Logger.Info("Services initializing...", "App");

            // DI setup
            var services = ConfigureServices();
            Services = services.BuildServiceProvider();
            Ioc.Default.ConfigureServices(Services);

            Logger.Info("DI container built", "App");

            // Service initialization
            await InitializeServicesAsync();

            Logger.Info("Services initialized successfully", "App");

            // Apply the language from the config
            try
            {
                var config = Services.GetRequiredService<IConfigService>().GetConfig();
                var language = config.Language ?? "System"; // Defaults to the system language

                // Determine the actual language
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

            // Show the main window (created via DI)
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Start the pipe server for single-instance IPC
            StartPipeServer();

            Logger.Info("Application started successfully", "App");
        }
        catch (Exception ex)
        {
            await HandleStartupErrorAsync(ex);
        }
    }

    /// <summary>
    /// Dependency Injection setup
    /// </summary>
    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // Base services
        services.AddSingleton<IDispatcher, WpfDispatcher>();
        services.AddSingleton<IModLoaderService, ModLoaderService>();

        // Services (singleton)
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
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IUpdateChecker>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new UpdateChecker(factory.CreateClient("UpdateChecker"));
        });
        services.AddSingleton<IAppUpdater>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new AppUpdater(factory.CreateClient("AppUpdater"));
        });

        // Mod update services (Modrinth)
        services.AddSingleton<IModRepositoryApi>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new ModrinthApi(factory.CreateClient("ModrinthApi"));
        });
        services.AddSingleton<IModFileDownloader>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new ModFileDownloader(factory.CreateClient("ModrinthApi"));
        });
        services.AddSingleton<IModUpdateService, ModUpdateService>();

        // ServersViewModel holds a subscription to an event of the IServerManager singleton,
        // so it must be a singleton as well: otherwise every visit to the server list page
        // would leave a "dangling" ViewModel behind.
        services.AddSingleton<ServersViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ServerDetailViewModel>();
        services.AddTransient<CreateServerViewModel>();
        services.AddSingleton<MainWindow>();

        // HttpClient for the API with a retry policy
        services.AddHttpClientWithDefaults<IMcVersionsApi, McVersionsApi>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(30);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        });

        // HttpClient for UpdateChecker (GitHub update check)
        services.AddHttpClientWithDefaults("UpdateChecker", options =>
        {
            options.Timeout = TimeSpan.FromSeconds(15);
            // The User-Agent will be overridden with the current version in UpdateChecker.CheckAsync()
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva");
            options.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        }, retryCount: 2);

        // HttpClient for AppUpdater (update downloads).
        // No retry: a partially downloaded archive cannot be resumed with the same request,
        // and the integrity check would reject a corrupted file anyway.
        // No redirects: the expected SHA-256 comes from the same unsigned manifest as the URL,
        // so an https->http downgrade would defeat the integrity check entirely.
        services.AddHttpClient("AppUpdater", options =>
        {
            options.Timeout = TimeSpan.FromMinutes(10);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        })
            .ConfigurePrimaryHttpMessageHandler(HttpClientDefaults.CreateStrictHandler);

        // HttpClient for McServerInstaller (server downloads) with a retry policy
        services.AddHttpClientWithDefaults("McServerInstaller", options =>
        {
            options.Timeout = TimeSpan.FromMinutes(5);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        });

        // HttpClient for the Modrinth API (mod updates)
        services.AddHttpClientWithDefaults("ModrinthApi", options =>
        {
            options.Timeout = TimeSpan.FromSeconds(30);
            options.DefaultRequestHeaders.UserAgent.ParseAdd("Konserva/1.0");
        }, retryCount: 2);

        return services;
        // end of ConfigureServices()
    }

    /// <summary>
    /// Service initialization
    /// </summary>
    private static Task InitializeServicesAsync()
    {
        Logger.Info("Services initialized (DI)", "App");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Startup error handling
    /// </summary>
    private static async Task HandleStartupErrorAsync(Exception ex)
    {
        Logger.Critical("Startup error", ex, "App");

        // The MessageBox must be on the UI thread (StartupAsync runs in the background)
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

    private static void BringExistingInstanceToFront()
    {
        try
        {
            // Send a signal through the named pipe — the first instance will raise the window
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(1000);
            var data = "bringtofront"u8.ToArray();
            pipeClient.Write(data, 0, data.Length);
            pipeClient.Flush();
        }
        catch (Exception ex)
        {
            Logger.Warning($"BringExistingInstanceToFront error: {ex.Message}", "App");
        }
    }

    private static void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await pipeServer.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(pipeServer);
                    var command = await reader.ReadLineAsync(token);

                    if (command == "bringtofront")
                    {
                        // Switch to the UI thread and raise the window
                        await Current.Dispatcher.InvokeAsync(() =>
                        {
                            var window = Current.MainWindow;
                            if (window == null) return;

                            window.ShowInTaskbar = true;

                            if (window.WindowState == WindowState.Minimized)
                                window.WindowState = WindowState.Normal;

                            window.Show();
                            window.Activate();

                            // Topmost toggle — guarantees bringing the window to the front
                            window.Topmost = true;
                            window.Topmost = false;

                            _ = window.Focus();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"PipeServer error: {ex.Message}", "App");
                }
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop the pipe server
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;

        // Release the mutex
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        // Wait for the servers to stop (otherwise Java processes stay in the background and block the port)
        try
        {
            // CleanupAsync runs on the thread pool (without a SynchronizationContext),
            // to rule out a deadlock when waiting synchronously on the UI thread.
            // 30s timeout — even if stopping the servers hangs, the application still closes.
            var cleanupTask = Task.Run(CleanupAsync);
            if (!cleanupTask.Wait(TimeSpan.FromSeconds(30)))
            {
                Logger.Warning("Cleanup timeout (30s) during shutdown, some servers may still be running", "App");
            }
            else
            {
                cleanupTask.GetAwaiter().GetResult(); // rethrow exceptions
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Cleanup error during shutdown: {ex.Message}", ex, "App");
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Resource cleanup
    /// </summary>
    private static async Task CleanupAsync()
    {
        try
        {
            // Stop all servers
            if (Services.GetRequiredService<IServerManager>() is McServerManager serverManager)
            {
                var servers = serverManager.GetServers();
                var runningServers = servers.Where(s => s.IsRunning).ToList();

                if (runningServers.Count > 0)
                {
                    Logger.Info($"Stopping {runningServers.Count} server(s) on shutdown", "App");

                    // Try to stop all servers with a timeout
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

                    // Force-kill the remaining Java processes from the server folders
                    foreach (var server in runningServers)
                    {
                        McServerManager.KillZombieProcesses(server.Path);
                    }
                }

                // Dispose of all processes
                foreach (var process in serverManager.GetProcesses())
                {
                    process?.Dispose();
                }

                // Write out any server state still sitting in the coalescing queue —
                // otherwise the last status change is lost on exit.
                serverManager.FlushPendingWrites();
            }

            // Dispose of the DI container
            if (Services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Flush the log channel last: without it the records describing this shutdown
            // are exactly the ones that never reach the disk.
            await Logger.ShutdownAsync();
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
        // Do not close the app — give the user a chance to save their data
    }
}
