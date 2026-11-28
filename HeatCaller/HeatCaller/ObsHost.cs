using System.Net;
using System.Net.NetworkInformation;

namespace HeatCaller;

/// <summary>
/// WHERE OBS IS: on this laptop, or on another PC (the Race video card's
/// "OBS on:" box). It decides the stream overlay's address: localhost when
/// OBS is here, the laptop's network address when OBS has to reach it over
/// the network.
///
/// WHY (28 Nov 2026, Asher, stream setup plan S2.8 / network review N0): only
/// the exact text "localhost" or "127.0.0.1" counted as this PC. Asher's box
/// said 127.0.0.2, which is this PC too, so OBS on the same laptop was given
/// the Wi-Fi address (http://192.168.1.142:8080/overlay/...). That worked until
/// the Wi-Fi changed, dropped, or Windows treated it as a Public network, and
/// then the overlay went blank with nothing saying why. Now any loopback
/// address (127.x.x.x, ::1), the laptop's own name, and any of the laptop's
/// own addresses count as this PC.
/// </summary>
internal static class ObsHost
{
    /// <summary>
    /// True when <paramref name="host"/> names this PC. A blank host is this PC
    /// (no OBS address typed, as before). <paramref name="machineName"/> and
    /// <paramref name="ownAddresses"/> are passed in so the rule can be tested
    /// without the real network.
    /// </summary>
    public static bool IsThisPc(string? host, string machineName, IEnumerable<IPAddress> ownAddresses)
    {
        var h = (host ?? string.Empty).Trim().TrimEnd('.');
        if (h.Length == 0)
        {
            return true;
        }

        if (h.StartsWith('[') && h.EndsWith(']'))
        {
            h = h[1..^1];
        }

        if (string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(h, out var address))
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            return ownAddresses.Any(a => (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).Equals(plain));
        }

        return machineName.Length > 0
            && (string.Equals(h, machineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h, machineName + ".local", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>IsThisPc against this laptop's real name and addresses.</summary>
    public static bool IsThisPc(string? host) => IsThisPc(host, Environment.MachineName, OwnAddresses());

    /// <summary>Every address on this laptop's network adapters (an empty list if they can't be read).</summary>
    private static IEnumerable<IPAddress> OwnAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
