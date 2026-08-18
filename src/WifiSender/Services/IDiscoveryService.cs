using System;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public interface IDiscoveryService
{
    event EventHandler<DiscoveredDevice>? DeviceFound;
    event EventHandler? ScanCompleted;
    event EventHandler<Exception>? ScanError;

    Task ScanAsync(CancellationToken cancellationToken = default);
    Task BroadcastPresenceAsync(int filePort, CancellationToken cancellationToken = default);
    Task StartRespondingAsync(int filePort, CancellationToken cancellationToken = default);
    DiscoveredDevice? ParseDeviceMessage(string message);
    void Stop();
}
