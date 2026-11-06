using System.Net;
using StayFlow.BuildingBlocks.Security;

namespace StayFlow.UnitTests;

/// <summary>
/// EPIC-53 — per-caller rate-limit partitioning.
///
/// These tests verify that every address variant maps to the right key so the
/// rate limiter actually isolates callers rather than sharing one global bucket.
/// </summary>
public class RateLimitPartitionKeyTests
{
    // ── Null / unknown ────────────────────────────────────────────────────────

    [Fact]
    public void ForClient_NullAddress_ReturnsUnknown()
    {
        var key = RateLimitPartitionKey.ForClient(null);
        Assert.Equal(RateLimitPartitionKey.Unknown, key);
    }

    [Fact]
    public void ForUserOrClient_NullUserIdNullAddress_ReturnsUnknown()
    {
        var key = RateLimitPartitionKey.ForUserOrClient(null, null);
        Assert.Equal(RateLimitPartitionKey.Unknown, key);
    }

    // ── IPv4 ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3.4",   "ip:1.2.3.4")]
    [InlineData("10.0.0.1",  "ip:10.0.0.1")]
    [InlineData("127.0.0.1", "ip:127.0.0.1")]
    public void ForClient_IPv4Address_ReturnsExpectedKey(string address, string expected)
    {
        var key = RateLimitPartitionKey.ForClient(IPAddress.Parse(address));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void ForClient_SameIPv4_ReturnsSameKey()
    {
        var a = RateLimitPartitionKey.ForClient(IPAddress.Parse("1.2.3.4"));
        var b = RateLimitPartitionKey.ForClient(IPAddress.Parse("1.2.3.4"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ForClient_DifferentIPv4_ReturnsDifferentKeys()
    {
        var a = RateLimitPartitionKey.ForClient(IPAddress.Parse("1.2.3.4"));
        var b = RateLimitPartitionKey.ForClient(IPAddress.Parse("1.2.3.5"));
        Assert.NotEqual(a, b);
    }

    // ── IPv4-mapped-to-IPv6 (::ffff:a.b.c.d) ────────────────────────────────
    // Kestrel returns these when the client connects over IPv4 to a dual-stack
    // socket. They must map back to IPv4 so the key matches a plain IPv4 caller.

    [Theory]
    [InlineData("::ffff:1.2.3.4", "ip:1.2.3.4")]
    [InlineData("::ffff:192.168.1.1", "ip:192.168.1.1")]
    public void ForClient_IPv4MappedToIPv6_UnwrapsToIPv4Key(string address, string expected)
    {
        var key = RateLimitPartitionKey.ForClient(IPAddress.Parse(address));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void ForClient_IPv4MappedIPv6_MatchesBareIPv4()
    {
        var mapped = RateLimitPartitionKey.ForClient(IPAddress.Parse("::ffff:1.2.3.4"));
        var bare   = RateLimitPartitionKey.ForClient(IPAddress.Parse("1.2.3.4"));
        Assert.Equal(bare, mapped);
    }

    // ── IPv6 /64 collapsing ──────────────────────────────────────────────────
    // A residential ISP hands each customer a /64 prefix; a single host can
    // rotate through 2^64 addresses in that prefix. We key on the /64 so the
    // whole prefix counts as one caller.

    [Fact]
    public void ForClient_IPv6_ContainsSlash64Suffix()
    {
        var key = RateLimitPartitionKey.ForClient(IPAddress.Parse("2001:db8::1"));
        Assert.EndsWith("/64", key);
    }

    [Theory]
    [InlineData("2001:db8::1",    "2001:db8::2")]    // different host ID, same /64
    [InlineData("fe80::aabb:1",   "fe80::aabb:9999")] // link-local, same /64
    public void ForClient_SameIPv6Prefix_ReturnsSameKey(string address1, string address2)
    {
        var a = RateLimitPartitionKey.ForClient(IPAddress.Parse(address1));
        var b = RateLimitPartitionKey.ForClient(IPAddress.Parse(address2));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ForClient_DifferentIPv6Prefix_ReturnsDifferentKeys()
    {
        // 2001:db8:1:: vs 2001:db8:2:: — different /64 prefixes
        var a = RateLimitPartitionKey.ForClient(IPAddress.Parse("2001:db8:1::1"));
        var b = RateLimitPartitionKey.ForClient(IPAddress.Parse("2001:db8:2::1"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForClient_IPv6_InterfaceIdBytesAreZero()
    {
        // The last 8 bytes of the /64-collapsed key should be zeroed.
        // We verify this by checking both interface variants map to the same key.
        var withHost = RateLimitPartitionKey.ForClient(IPAddress.Parse("2001:db8::cafe:babe"));
        var noHost   = RateLimitPartitionKey.ForClient(IPAddress.Parse("2001:db8::"));
        Assert.Equal(withHost, noHost);
    }

    // ── ForUserOrClient ───────────────────────────────────────────────────────

    [Fact]
    public void ForUserOrClient_WithUserId_ReturnsUserKey()
    {
        var userId = Guid.NewGuid();
        var key = RateLimitPartitionKey.ForUserOrClient(userId, IPAddress.Parse("1.2.3.4"));
        Assert.Equal($"user:{userId}", key);
    }

    [Fact]
    public void ForUserOrClient_NullUserId_FallsBackToIpKey()
    {
        var ipKey = RateLimitPartitionKey.ForClient(IPAddress.Parse("5.6.7.8"));
        var key   = RateLimitPartitionKey.ForUserOrClient(null, IPAddress.Parse("5.6.7.8"));
        Assert.Equal(ipKey, key);
    }

    [Fact]
    public void ForUserOrClient_DifferentUsers_SameIp_DifferentKeys()
    {
        var ip = IPAddress.Parse("10.0.0.1");
        var a  = RateLimitPartitionKey.ForUserOrClient(Guid.NewGuid(), ip);
        var b  = RateLimitPartitionKey.ForUserOrClient(Guid.NewGuid(), ip);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForUserOrClient_SameUser_DifferentIps_SameKey()
    {
        // A staff member at the desk, then at another terminal: same user key.
        var userId = Guid.NewGuid();
        var a = RateLimitPartitionKey.ForUserOrClient(userId, IPAddress.Parse("10.0.0.1"));
        var b = RateLimitPartitionKey.ForUserOrClient(userId, IPAddress.Parse("10.0.0.2"));
        Assert.Equal(a, b);
    }
}
