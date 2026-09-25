using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;

namespace Konserva.Services;

/// <summary>
/// Расширения для стандартизации конфигурации HttpClient'ов
/// </summary>
internal static class HttpClientDefaults
{
    /// <summary>
    /// Создаёт стандартный SocketsHttpHandler с объединёнными соединениями,
    /// сжатием и лимитами.
    /// </summary>
    public static SocketsHttpHandler CreateDefaultHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 10,
            AutomaticDecompression = DecompressionMethods.All
        };
    }

    /// <summary>
    /// Добавляет именованный HttpClient с дефолтным SocketsHttpHandler
    /// и стандартной политикой повторных попыток (exponential + jitter).
    /// </summary>
    public static IHttpClientBuilder AddHttpClientWithDefaults(
        this IServiceCollection services,
        string name,
        Action<HttpClient> configureClient,
        int retryCount = 3)
    {
        return AddDefaultResilience(services.AddHttpClient(name, configureClient), retryCount);
    }

    /// <summary>
    /// Добавляет типизированный HttpClient с теми же настройками, что и
    /// именованный. Нужен для <c>AddHttpClient&lt;TClient, TImpl&gt;</c>.
    /// </summary>
    public static IHttpClientBuilder AddHttpClientWithDefaults<TClient, TImplementation>(
        this IServiceCollection services,
        Action<HttpClient> configureClient,
        int retryCount = 3)
        where TClient : class
        where TImplementation : class, TClient
    {
        return AddDefaultResilience(services.AddHttpClient<TClient, TImplementation>(configureClient), retryCount);
    }

    private static IHttpClientBuilder AddDefaultResilience(IHttpClientBuilder builder, int retryCount)
    {
        builder.ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler);

        if (retryCount > 0)
        {
            builder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = retryCount;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
            });
        }

        return builder;
    }
}
