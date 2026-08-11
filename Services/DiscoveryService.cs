using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed class DiscoveryService : IDiscoveryService, IDisposable
{
    private const int DiscoveryPort = 5556;
    private const int ScanTimeoutSeconds = 5;
    private const int BroadcastCount = 3;
    private const int BroadcastDelayMs = 500;

    public event EventHandler<DiscoveredDevice>? DeviceFound;
    public event EventHandler? ScanCompleted;
    public event EventHandler<Exception>? ScanError;

    private UdpClient? _udpScanner;
    private CancellationTokenSource? _scanCts;
    private readonly ILocalIpService _localIpService;

    public DiscoveryService(ILocalIpService localIpService)
    {
        _localIpService = localIpService;
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _scanCts.Token;

        try
        {
            _udpScanner = new UdpClient();
            _udpScanner.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpScanner.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _udpScanner.EnableBroadcast = true;

            var broadcasts = await _localIpService.GetBroadcastAddressesAsync(token);
            var localIp = await _localIpService.GetLocalIpAddressAsync(token);
            string discoveryMsg = $"WIFISENDER_DISCOVERY|{localIp}|5555";
            byte[] data = Encoding.UTF8.GetBytes(discoveryMsg);

            for (int i = 0; i < BroadcastCount; i++)
            {
                token.ThrowIfCancellationRequested();
                foreach (var broadcastIp in broadcasts)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await _udpScanner.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), DiscoveryPort));
                    }
                    catch (SocketException) { }
                }
                await Task.Delay(BroadcastDelayMs, token);
            }

            var endTime = DateTime.UtcNow.AddSeconds(ScanTimeoutSeconds);
            while (DateTime.UtcNow < endTime && !token.IsCancellationRequested)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                    var result = await _udpScanner.ReceiveAsync(linkedCts.Token);
                    string response = Encoding.UTF8.GetString(result.Buffer);

                    if (ParseDeviceMessage(response) is { } device)
                    {
                        DeviceFound?.Invoke(this, device);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) { }
                catch (SocketException ex)
                {
                    ScanError?.Invoke(this, ex);
                }
            }

            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ScanError?.Invoke(this, ex);
        }
        finally
        {
            _udpScanner?.Close();
            _udpScanner?.Dispose();
            _udpScanner = null;
        }
    }

    public async Task BroadcastPresenceAsync(int filePort, CancellationToken cancellationToken = default)
    {
        var localIp = await _localIpService.GetLocalIpAddressAsync(cancellationToken);
        string hostName = Environment.MachineName;
        string announce = $"WIFISENDER_ANNOUNCE|{localIp}|{filePort}|{hostName}";
        byte[] data = Encoding.UTF8.GetBytes(announce);

        var broadcasts = await _localIpService.GetBroadcastAddressesAsync(cancellationToken);
        foreach (var broadcastIp in broadcasts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var udp = new UdpClient();
                udp.EnableBroadcast = true;
                await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), DiscoveryPort));
            }
            catch (OperationCanceledException) { throw; }
            catch (SocketException) { }
        }
    }

    public async Task StartRespondingAsync(int filePort, CancellationToken cancellationToken = default)
    {
        using var udpServer = new UdpClient();
        udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        udpServer.EnableBroadcast = true;

        var localIp = await _localIpService.GetLocalIpAddressAsync(cancellationToken);
        string hostName = Environment.MachineName;
        var lastAnnounce = DateTime.MinValue;

        await BroadcastPresenceAsync(filePort, cancellationToken);
        lastAnnounce = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            if ((DateTime.UtcNow - lastAnnounce).TotalSeconds >= 2)
            {
                try
                {
                    await BroadcastPresenceAsync(filePort, cancellationToken);
                    lastAnnounce = DateTime.UtcNow;
                }
                catch (OperationCanceledException) { throw; }
            }

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                var result = await udpServer.ReceiveAsync(linkedCts.Token);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (message.StartsWith("WIFISENDER_DISCOVERY"))
                {
                    string response = $"WIFISENDER_RESPONSE|{localIp}|{filePort}|{hostName}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
    }

    public DiscoveredDevice? ParseDeviceMessage(string message)
    {
        if (!message.StartsWith("WIFISENDER_RESPONSE|") && !message.StartsWith("WIFISENDER_ANNOUNCE|"))
            return null;

        var parts = message.Split('|');
        if (parts.Length < 3)
            return null;

        return new DiscoveredDevice
        {
            IpAddress = parts[1],
            Port = parts[2],
            DeviceName = parts.Length > 3 ? parts[3] : ""
        };
    }

    public void Stop()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        _udpScanner?.Close();
        _udpScanner?.Dispose();
        _udpScanner = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
