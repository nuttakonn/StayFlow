using System.Net;
using System.Net.Sockets;

namespace StayFlow.BuildingBlocks.Security;

/// <summary>
/// Builds the partition key used by the rate limiter.
///
/// Rate limits must be scoped to a caller. A single un-partitioned bucket means
/// one noisy client exhausts the allowance for everybody — a self-inflicted
/// denial of service rather than a protection.
/// </summary>
public static class RateLimitPartitionKey
{
    /// <summary>Used when the remote address cannot be determined.</summary>
    public const string Unknown = "ip:unknown";

    /// <summary>
    /// Key for pre-authentication endpoints (login, register, refresh), where
    /// the caller's identity is not yet known and the address is all we have.
    /// </summary>
    public static string ForClient(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
        {
            return Unknown;
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (remoteAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Residential IPv6 clients are routinely delegated an entire /64.
            // Keying on the full address would let a single host rotate through
            // 2^64 addresses and bypass the limit completely, so collapse the
            // interface identifier and limit the prefix as one caller.
            var bytes = remoteAddress.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return $"ip:{new IPAddress(bytes)}/64";
        }

        return $"ip:{remoteAddress}";
    }

    /// <summary>
    /// Key for authenticated endpoints. Prefers the user id, because on-premise
    /// staff all share one NAT egress address — keying those requests on the
    /// address would put the whole property in a single bucket.
    /// </summary>
    public static string ForUserOrClient(Guid? userId, IPAddress? remoteAddress)
        => userId.HasValue ? $"user:{userId.Value}" : ForClient(remoteAddress);
}
