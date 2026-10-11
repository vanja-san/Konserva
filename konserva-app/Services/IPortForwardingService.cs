namespace Konserva.Services;

/// <summary>
/// Service for automatic port forwarding via UPnP/NAT-PMP.
/// </summary>
public interface IPortForwardingService
{
    /// <summary>
    /// Checks whether a UPnP router is available on the network.
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Creates a port forwarding rule (TCP) on the router.
    /// </summary>
    /// <param name="port">Port to forward.</param>
    /// <param name="description">Rule description (shown in the router admin UI).</param>
    Task<bool> CreateMappingAsync(int port, string description);

    /// <summary>
    /// Removes a port forwarding rule (TCP) from the router.
    /// </summary>
    Task<bool> DeleteMappingAsync(int port);

    /// <summary>
    /// Checks whether a port forwarding rule (TCP) exists on the router.
    /// </summary>
    /// <param name="port">Port to check.</param>
    /// <returns>true if the forwarding rule exists.</returns>
    Task<bool> CheckMappingAsync(int port);

    /// <summary>
    /// Gets the external (public) IP address through the UPnP router.
    /// </summary>
    Task<string?> GetExternalIpAsync();

    /// <summary>
    /// Returns the last known external IP without querying the router.
    /// </summary>
    string? TryGetCachedExternalIp();
}
