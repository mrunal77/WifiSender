using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed class LocalIpService : ILocalIpService
{
    public async Task<string> GetLocalIpAddressAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GetLocalIpAddress(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetBroadcastAddressesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GetBroadcastAddresses(), cancellationToken).ConfigureAwait(false);
    }

    private static string GetLocalIpAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(unicastAddress.Address))
                {
                    return unicastAddress.Address.ToString();
                }
            }
        }

        return "0.0.0.0";
    }

    private static IReadOnlyList<string> GetBroadcastAddresses()
    {
        var broadcasts = new List<string>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (unicastAddress.IPv4Mask == null)
                    continue;

                var ipBytes = unicastAddress.Address.GetAddressBytes();
                var maskBytes = unicastAddress.IPv4Mask.GetAddressBytes();
                var broadcastBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                    broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

                var broadcast = new IPAddress(broadcastBytes).ToString();
                if (!broadcasts.Contains(broadcast))
                    broadcasts.Add(broadcast);
            }
        }

        if (broadcasts.Count == 0)
            broadcasts.Add("255.255.255.255");

        return broadcasts;
    }
}
