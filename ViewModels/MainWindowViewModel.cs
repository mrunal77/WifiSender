using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WifiSender.ViewModels;

public class DiscoveredDevice
{
    public string IpAddress { get; set; } = "";
    public string Port { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(DeviceName) ? $"{IpAddress}:{Port}" : $"{DeviceName} ({IpAddress})";
}

public partial class MainWindowViewModel : ObservableObject
{
    private TcpListener? _server;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private UdpClient? _udpScanner;
    private CancellationTokenSource? _scanCts;
    private const int BufferSize = 262144;
    private const int DiscoveryPort = 5556;
    private readonly FolderPickerOpenOptions _folderPickerOptions = new()
    {
        Title = "Select Download Folder",
        AllowMultiple = false
    };

    [ObservableProperty]
    private string _localIp = "0.0.0.0";

    [ObservableProperty]
    private string _port = "5555";

    [ObservableProperty]
    private string _recipientIp = "";

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isReceiving;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private string _downloadFolder = "";

    [ObservableProperty]
    private string _connectionTestResult = "";

    [ObservableProperty]
    private string _currentFileName = "";

    [ObservableProperty]
    private string _currentFileProgress = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    [ObservableProperty]
    private string _receiveButtonText = "START RECEIVING";

    [ObservableProperty]
    private string _receiveButtonColor = "#FF6D00";

    public ObservableCollection<string> SelectedFiles { get; } = new();
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();

    public MainWindowViewModel()
    {
        LocalIp = GetLocalIpAddress();
    }

    partial void OnIsReceivingChanged(bool value)
    {
        ReceiveButtonText = value ? "STOP" : "START RECEIVING";
        ReceiveButtonColor = value ? "#FF1744" : "#FF6D00";
    }

    private string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private string GetNetworkPrefix()
    {
        try
        {
            string ip = LocalIp;
            var parts = ip.Split('.');
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }
        catch
        {
            return "192.168.1";
        }
    }

    [RelayCommand]
    private async Task SelectFiles(Window? window)
    {
        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Files to Send",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        SelectedFiles.Clear();
        foreach (var file in files)
        {
            SelectedFiles.Add(file.Path.LocalPath);
        }

        if (SelectedFiles.Count > 0)
        {
            long totalSize = 0;
            foreach (var f in SelectedFiles)
            {
                if (File.Exists(f))
                    totalSize += new FileInfo(f).Length;
            }
            Status = $"Selected {SelectedFiles.Count} file(s) ({FormatFileSize(totalSize)})";
        }
    }

    [RelayCommand]
    private async Task SelectDownloadFolder(Window? window)
    {
        if (window == null) return;

        var folder = await window.StorageProvider.OpenFolderPickerAsync(_folderPickerOptions);
        if (folder.Count > 0)
        {
            DownloadFolder = folder[0].Path.LocalPath;
            Status = $"Download folder: {DownloadFolder}";
        }
    }

    [RelayCommand]
    private async Task ScanDevices()
    {
        if (IsScanning) return;

        IsScanning = true;
        DiscoveredDevices.Clear();
        Status = "Scanning for nearby devices...";

        _scanCts = new CancellationTokenSource();

        try
        {
            // Start UDP listener for responses
            _udpScanner = new UdpClient();
            _udpScanner.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpScanner.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _udpScanner.EnableBroadcast = true;

            // Send broadcast to discover devices
            string broadcastIp = $"{GetNetworkPrefix()}.255";
            string discoveryMsg = $"WIFISENDER_DISCOVERY|{LocalIp}|{Port}";

            for (int i = 0; i < 3; i++)
            {
                if (_scanCts.Token.IsCancellationRequested) break;
                byte[] data = Encoding.UTF8.GetBytes(discoveryMsg);
                await _udpScanner.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), DiscoveryPort));
                await Task.Delay(500);
            }

            // Listen for responses for limited time
            var endTime = DateTime.UtcNow.AddSeconds(5);
            
            while (DateTime.UtcNow < endTime && !_scanCts.Token.IsCancellationRequested)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_scanCts.Token, cts.Token);
                    
                    var result = await _udpScanner.ReceiveAsync(linkedCts.Token);
                    string response = Encoding.UTF8.GetString(result.Buffer);
                    
                    if (response.StartsWith("WIFISENDER_RESPONSE|"))
                    {
                        var parts = response.Split('|');
                        if (parts.Length >= 3)
                        {
                            var device = new DiscoveredDevice
                            {
                                IpAddress = parts[1],
                                Port = parts[2],
                                DeviceName = parts.Length > 3 ? parts[3] : ""
                            };

                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (!DiscoveredDevices.Any(d => d.IpAddress == device.IpAddress && d.Port == device.Port))
                                {
                                    DiscoveredDevices.Add(device);
                                    Status = $"Found {DiscoveredDevices.Count} device(s)";
                                }
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }

            if (DiscoveredDevices.Count == 0)
            {
                Status = "No devices found. Make sure receiver is running on other device.";
            }
            else
            {
                Status = $"Found {DiscoveredDevices.Count} device(s)";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _udpScanner?.Close();
            _udpScanner?.Dispose();
        }
    }

    [RelayCommand]
    private void SelectDevice(DiscoveredDevice? device)
    {
        if (device != null)
        {
            SelectedDevice = device;
            RecipientIp = device.IpAddress;
            if (!string.IsNullOrEmpty(device.Port))
                Port = device.Port;
            Status = $"Selected: {device.DisplayName}";
        }
    }

    [RelayCommand]
    private async Task SendFiles(Window? window)
    {
        if (SelectedFiles.Count == 0)
        {
            Status = "No files selected! Click 'Select Files' first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(RecipientIp))
        {
            Status = "Enter recipient IP address!";
            return;
        }

        if (!int.TryParse(Port, out int port) || port <= 0 || port > 65535)
        {
            Status = "Invalid port number!";
            return;
        }

        IsSending = true;
        Status = $"Connecting to {RecipientIp}:{port}...";
        Progress = 0;

        try
        {
            _client = new TcpClient();
            _client.SendBufferSize = BufferSize;
            _client.ReceiveBufferSize = BufferSize;
            await _client.ConnectAsync(RecipientIp, port);
            _stream = _client.GetStream();

            Status = "Connected! Sending files...";

            int totalFiles = SelectedFiles.Count;
            long totalBytes = 0;

            foreach (var f in SelectedFiles)
            {
                if (File.Exists(f))
                    totalBytes += new FileInfo(f).Length;
            }

            long sentTotal = 0;

            for (int i = 0; i < totalFiles; i++)
            {
                string filePath = SelectedFiles[i];

                if (!File.Exists(filePath))
                {
                    Status = $"File not found: {Path.GetFileName(filePath)}";
                    continue;
                }

                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                CurrentFileName = fileName;

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                byte[] fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
                byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);

                await _stream.WriteAsync(fileNameLengthBytes);
                await _stream.WriteAsync(fileNameBytes);
                await _stream.WriteAsync(fileSizeBytes);

                await using var fileStream = File.OpenRead(filePath);
                byte[] buffer = new byte[BufferSize];
                long sent = 0;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    await _stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    sent += bytesRead;
                    sentTotal += bytesRead;

                    double fileProgress = (sent * 100.0) / fileSize;
                    double totalProgress = (sentTotal * 100.0) / totalBytes;

                    Progress = totalProgress;
                    CurrentFileProgress = $"{FormatFileSize(sent)} / {FormatFileSize(fileSize)}";
                    Status = $"Sending: {fileName} ({fileProgress:F1}%)";
                }

                Status = $"Sent {i + 1}/{totalFiles}: {fileName}";
            }

            byte[] endMarker = BitConverter.GetBytes((int)0);
            await _stream.WriteAsync(endMarker);

            Status = $"All files sent successfully! ({FormatFileSize(totalBytes)})";
            Progress = 100;
            CurrentFileName = "";
            CurrentFileProgress = "";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsSending = false;
            _stream?.Close();
            _client?.Close();
        }
    }

    [RelayCommand]
    private async Task StartReceiving(Window? window)
    {
        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            DownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(DownloadFolder))
                Directory.CreateDirectory(DownloadFolder);
            Status = $"Using default folder: {DownloadFolder}";
        }

        if (!int.TryParse(Port, out int port) || port <= 0 || port > 65535)
        {
            Status = "Invalid port number!";
            return;
        }

        IsReceiving = true;
        Status = $"Listening on port {port}...";
        Progress = 0;
        _cts = new CancellationTokenSource();

        // Start discovery responder
        _ = RespondToDiscoveryAsync(port);

        try
        {
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();

            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _server.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped listening";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsReceiving = false;
        }
    }

    private async Task RespondToDiscoveryAsync(int filePort)
    {
        try
        {
            using var udpServer = new UdpClient();
            udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udpServer.EnableBroadcast = true;

            string hostName = Environment.MachineName;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpServer.ReceiveAsync(_cts.Token);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("WIFISENDER_DISCOVERY"))
                    {
                        string response = $"WIFISENDER_RESPONSE|{LocalIp}|{filePort}|{hostName}";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            client.ReceiveBufferSize = BufferSize;
            client.SendBufferSize = BufferSize;

            var remoteEndPoint = (IPEndPoint?)client.Client.RemoteEndPoint;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Connection from {remoteEndPoint?.Address}";
            });

            using var stream = client.GetStream();
            long totalReceived = 0;

            while (true)
            {
                byte[] lengthBuffer = new byte[4];
                int read = await ReadExactAsync(stream, lengthBuffer);
                if (read == 0) break;

                int fileNameLength = BitConverter.ToInt32(lengthBuffer, 0);

                if (fileNameLength == 0)
                    break;

                byte[] fileNameBuffer = new byte[fileNameLength];
                await ReadExactAsync(stream, fileNameBuffer);
                string fileName = Encoding.UTF8.GetString(fileNameBuffer);

                byte[] sizeBuffer = new byte[8];
                await ReadExactAsync(stream, sizeBuffer);
                long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

                CurrentFileName = fileName;

                string savePath = Path.Combine(DownloadFolder, fileName);
                string originalPath = savePath;
                int counter = 1;
                while (File.Exists(savePath))
                {
                    string name = Path.GetFileNameWithoutExtension(originalPath);
                    string ext = Path.GetExtension(originalPath);
                    savePath = Path.Combine(DownloadFolder, $"{name} ({counter}){ext}");
                    counter++;
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = $"Receiving: {fileName} ({FormatFileSize(fileSize)})";
                });

                await using var fileStream = File.Create(savePath);
                byte[] buffer = new byte[BufferSize];
                long received = 0;

                while (received < fileSize)
                {
                    int toRead = (int)Math.Min(buffer.Length, fileSize - received);
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    received += bytesRead;
                    totalReceived += bytesRead;

                    double fileProgress = (received * 100.0) / fileSize;
                    CurrentFileProgress = $"{FormatFileSize(received)} / {FormatFileSize(fileSize)}";

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Progress = fileProgress;
                    });
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = $"Received: {fileName}";
                    Progress = 100;
                });
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Transfer complete! ({FormatFileSize(totalReceived)} received)";
                Progress = 100;
                CurrentFileName = "";
                CurrentFileProgress = "";
            });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Receive error: {ex.Message}";
            });
        }
        finally
        {
            client.Close();
        }
    }

    private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    [RelayCommand]
    private void StopReceiving()
    {
        _cts?.Cancel();
        _scanCts?.Cancel();
        _server?.Stop();
        IsReceiving = false;
        IsScanning = false;
        Status = "Stopped";
    }

    [RelayCommand]
    private async Task ToggleReceiving(Window? window)
    {
        if (IsReceiving)
        {
            StopReceiving();
        }
        else
        {
            await StartReceiving(window);
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(RecipientIp))
        {
            ConnectionTestResult = "Enter IP address";
            return;
        }

        if (!int.TryParse(Port, out int port))
        {
            ConnectionTestResult = "Invalid port";
            return;
        }

        ConnectionTestResult = "Testing...";

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(RecipientIp, port);

            if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
            {
                ConnectionTestResult = $"✓ Connected to {RecipientIp}:{port}";
            }
            else
            {
                ConnectionTestResult = $"✗ Timeout - port not open";
            }
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"✗ {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
        Status = "Files cleared";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F2} {suffixes[suffixIndex]}";
    }
}
