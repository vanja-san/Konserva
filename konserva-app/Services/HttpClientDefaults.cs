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
    /// Добавляет HttpClient с дефолтным SocketsHttpHandler и стандартной
    /// политикой повторных попыток (exponential + jitter).
    /// </summary>
    public static IHttpClientBuilder AddHttpClientWithDefaults(
        this IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient = null,
        int retryCount = 3)
    {
        var builder = services.AddHttpClient(name, configureClient ?? (_ => { }))
            .ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler);

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
