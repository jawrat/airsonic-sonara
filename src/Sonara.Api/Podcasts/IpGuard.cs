using System.Net;
using System.Net.Sockets;

namespace Sonara.Api.Podcasts;

/// <summary>
/// Decides whether an address is a normal public internet address. Podcast URLs come from users, so
/// without this the server could be tricked into fetching internal services (SSRF).
/// </summary>
public static class IpGuard
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0                                      // 0.0.0.0/8 "this network"
                     || b[0] == 10                                  // private
                     || (b[0] == 100 && (b[1] & 0xC0) == 64)        // 100.64/10 carrier-grade NAT
                     || (b[0] == 169 && b[1] == 254)                // link-local, cloud metadata
                     || (b[0] == 172 && (b[1] & 0xF0) == 16)        // 172.16/12 private
                     || (b[0] == 192 && b[1] == 168)                // private
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24 IETF protocol
                     || (b[0] == 198 && (b[1] & 0xFE) == 18)        // 198.18/15 benchmarking
                     || b[0] >= 224);                               // multicast and reserved
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                return false;
            if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7 unique local
            return true;
        }

        return false;
    }
}
