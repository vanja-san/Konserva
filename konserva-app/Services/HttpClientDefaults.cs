using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;

namespace Konserva.Services;

/// <summary>
/// Extensions that standardize the configuration of HttpClient instances
/// </summary>
internal static class HttpClientDefaults
{
    /// <summary>
    /// Creates a default SocketsHttpHandler with connection pooling,
    /// decompression and limits.
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
    /// Creates a handler that refuses to follow redirects at all.
    /// <para>
    /// Used for the self-update download: the reference checksum arrives in the same
    /// unsigned manifest as the URL, so a redirect that downgrades https:// to http://
    /// would let an on-path attacker serve both the payload and a matching hash.
    /// Ordinary API clients keep following redirects — several of the version APIs
    /// legitimately redirect to a CDN.
    /// </para>
    /// </summary>
    public static SocketsHttpHandler CreateStrictHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 2,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false
        };
    }

    /// <summary>
    /// Registers a named HttpClient with the default SocketsHttpHandler
    /// and a standard retry policy (exponential + jitter).
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
    /// Registers a typed HttpClient with the same settings as the named one.
    /// Required for <c>AddHttpClient&lt;TClient, TImpl&gt;</c>.
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
