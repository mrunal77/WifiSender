using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
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
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _sendCts;
    private UdpClient? _udpScanner;
    private CancellationTokenSource? _scanCts;
    private const int BufferSize = 262144;
    private const int DiscoveryPort = 5556;
    private const int MinCompressSize = 4096;
    private static readonly HashSet<string> UncompressibleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".gz", ".bz2", ".xz", ".7z", ".rar",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".ico",
        ".mp4", ".mp3", ".avi", ".mov", ".mkv", ".wmv", ".flv", ".webm", ".wav", ".flac", ".aac", ".ogg",
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
    };
    public string? SelectedFolderRoot { get; set; }
    private readonly FolderPickerOpenOptions _folderPickerOptions = new()
    {
        Title = "Select Download Folder",
        AllowMultiple = false
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFilesCommand))]
    private string _localIp = "0.0.0.0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFilesCommand))]
    private string _port = "5555";

    partial void OnPortChanged(string value)
    {
        var clean = new string(value?.Where(char.IsDigit).ToArray() ?? []);
        if (clean.Length > 1)
            clean = clean.TrimStart('0');
        if (clean != value)
            Port = clean;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFilesCommand))]
    private string _recipientIp = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFilesCommand))]
    private string _recipientIps = "";

    partial void OnRecipientIpsChanged(string value)
    {
        RecipientIp = GetRecipientTargets(value).FirstOrDefault() ?? "";
    }

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiveButtonText))]
    private bool _isReceiving;

    public string ReceiveButtonText => IsReceiving ? "Stop" : "Start Receiving";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFilesCommand))]
    private bool _isSending;

    [ObservableProperty]
    private string _downloadFolder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionTestResult))]
    private string _connectionTestResult = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentFile))]
    private string _currentFileName = "";

    [ObservableProperty]
    private string _currentFileProgress = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isFirewallWarningVisible;

    [ObservableProperty]
    private string _firewallWarningText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(NavBarBackground))]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private double _contentOpacity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavBarBackground))]
    private bool _isNavBarScrolled;

    private static readonly IBrush LightNavBarCream = new SolidColorBrush(Color.Parse("#FFF8E7"));
    private static readonly IBrush LightNavBarBluish = new SolidColorBrush(Color.Parse("#B08B5CF6"));
    private static readonly IBrush DarkNavBarGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#2563EB"), 0),
            new GradientStop(Color.Parse("#7C3AED"), 0.55),
            new GradientStop(Color.Parse("#0EA5E9"), 1)
        }
    };

    public IBrush NavBarBackground => IsDarkTheme ? DarkNavBarGradient : (IsNavBarScrolled ? LightNavBarBluish : LightNavBarCream);

    public string ThemeIcon => IsDarkTheme ? "🌙" : "☀️";

    private bool IsSystemDarkTheme() =>
        Application.Current?.ActualThemeVariant is { } v && v == ThemeVariant.Dark;

    public ObservableCollection<string> SelectedFiles { get; } = new();
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();

    public bool HasSelectedFiles => SelectedFiles.Count > 0;
    public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
    public bool HasConnectionTestResult => !string.IsNullOrEmpty(ConnectionTestResult);
    public bool HasCurrentFile => !string.IsNullOrEmpty(CurrentFileName);

    public MainWindowViewModel()
    {
        LocalIp = GetLocalIpAddress();
        IsDarkTheme = IsSystemDarkTheme();
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }
        _ = CheckFirewallAsync();

        SelectedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelectedFiles));
            SendFilesCommand.NotifyCanExecuteChanged();
        };
        DiscoveredDevices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDiscoveredDevices));
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (Application.Current?.RequestedThemeVariant == ThemeVariant.Default)
        {
            IsDarkTheme = IsSystemDarkTheme();
        }
    }

    private string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            var ip = endPoint?.Address.ToString();
            if (!string.IsNullOrEmpty(ip) && !ip.StartsWith("127."))
                return ip;
        }
        catch
        {
        }

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

    private string GetNetworkPrefix()
    {
        try
        {
            string ip = LocalIp;
            if (string.IsNullOrEmpty(ip) || ip.StartsWith("127.") || ip.StartsWith("0."))
                return "192.168.1";
            var parts = ip.Split('.');
            if (parts.Length >= 3)
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            return "192.168.1";
        }
        catch
        {
            return "192.168.1";
        }
    }

    private IReadOnlyList<string> GetBroadcastAddresses()
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
        {
            broadcasts.Add("255.255.255.255");
        }

        return broadcasts;
    }

    private static bool TryParseDeviceMessage(string message, out DiscoveredDevice? device)
    {
        device = null;
        if (!message.StartsWith("WIFISENDER_RESPONSE|") && !message.StartsWith("WIFISENDER_ANNOUNCE|"))
            return false;

        var parts = message.Split('|');
        if (parts.Length < 3)
            return false;

        device = new DiscoveredDevice
        {
            IpAddress = parts[1],
            Port = parts[2],
            DeviceName = parts.Length > 3 ? parts[3] : ""
        };
        return true;
    }

    private async Task BroadcastPresenceAsync(UdpClient udp, int filePort, CancellationToken token)
    {
        string hostName = Environment.MachineName;
        string announce = $"WIFISENDER_ANNOUNCE|{LocalIp}|{filePort}|{hostName}";
        byte[] data = Encoding.UTF8.GetBytes(announce);

        foreach (var broadcastIp in GetBroadcastAddresses())
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), DiscoveryPort));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException)
            {
            }
        }
    }

    private IReadOnlyList<string> GetRecipientTargets()
    {
        return GetRecipientTargets(RecipientIps);
    }

    private static IReadOnlyList<string> GetRecipientTargets(string? value)
    {
        return (value ?? "")
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        SelectedFolderRoot = null;
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
    private async Task SelectFolder(Window? window)
    {
        if (window == null) return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder to Send",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].Path.LocalPath;
        string[] files;
        try
        {
            files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        SelectedFiles.Clear();
        SelectedFolderRoot = folderPath;
        foreach (var f in files)
            SelectedFiles.Add(f);

        if (SelectedFiles.Count > 0)
        {
            long totalSize = 0;
            foreach (var f in SelectedFiles)
            {
                if (File.Exists(f))
                    totalSize += new FileInfo(f).Length;
            }
            Status = $"Selected {SelectedFiles.Count} file(s) from folder '{Path.GetFileName(folderPath)}' ({FormatFileSize(totalSize)})";
            SendFilesCommand.NotifyCanExecuteChanged();
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

            var broadcastAddresses = GetBroadcastAddresses();
            Status = $"Scanning {broadcastAddresses.Count} network broadcast address(es)...";

            string discoveryMsg = $"WIFISENDER_DISCOVERY|{LocalIp}|{Port}";
            byte[] data = Encoding.UTF8.GetBytes(discoveryMsg);

            for (int i = 0; i < 3; i++)
            {
                if (_scanCts.Token.IsCancellationRequested) break;

                foreach (var broadcastIp in broadcastAddresses)
                {
                    if (_scanCts.Token.IsCancellationRequested) break;
                    await _udpScanner.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Parse(broadcastIp), DiscoveryPort));
                }

                await Task.Delay(500);
            }

            var endTime = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < endTime && !_scanCts.Token.IsCancellationRequested)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_scanCts.Token, timeoutCts.Token);

                    var result = await _udpScanner.ReceiveAsync(linkedCts.Token);
                    string response = Encoding.UTF8.GetString(result.Buffer);

                    if (TryParseDeviceMessage(response, out var device) && device != null)
                    {
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
                catch (OperationCanceledException) when (_scanCts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException ex)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Status = $"Socket error while scanning: {ex.Message}";
                    });
                }
            }

            if (DiscoveredDevices.Count == 0)
            {
                Status = "No devices found. Make sure receiver is running on other device.";
                await CheckFirewallAsync();
            }
            else
            {
                Status = $"Found {DiscoveredDevices.Count} device(s)";
                IsFirewallWarningVisible = false;
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
            RecipientIps = device.IpAddress;
            if (!string.IsNullOrEmpty(device.Port))
                Port = device.Port;
            Status = $"Selected: {device.DisplayName}";
        }
    }

    public bool CanSendFiles() =>
        SelectedFiles.Count > 0
        && GetRecipientTargets().Count > 0
        && int.TryParse(Port, out int p) && p > 0 && p <= 65535
        && !IsSending;

    [RelayCommand(CanExecute = nameof(CanSendFiles))]
    private async Task SendFiles(Window? window)
    {
        var recipients = GetRecipientTargets();
        if (recipients.Count == 0)
        {
            Status = "Enter one or more receiver IP addresses";
            return;
        }

        if (!int.TryParse(Port, out int port) || port <= 0 || port > 65535)
        {
            Status = "Invalid port number!";
            return;
        }

        IsSending = true;
        Progress = 0;
        CurrentFileName = "";
        CurrentFileProgress = "";
        _sendCts = new CancellationTokenSource();
        var sendToken = _sendCts.Token;

        try
        {
            for (int i = 0; i < recipients.Count; i++)
            {
                sendToken.ThrowIfCancellationRequested();

                var recipient = recipients[i];
                Progress = (i * 100.0) / recipients.Count;
                Status = $"[{i + 1}/{recipients.Count}] Connecting to {recipient}:{port}...";

                try
                {
                    await SendFilesToRecipientAsync(recipient, port, i, recipients.Count, sendToken);
                    Status = $"[{i + 1}/{recipients.Count}] Sent to {recipient}";
                }
                catch (OperationCanceledException)
                {
                    Status = $"Cancelled sending to {recipient}";
                    throw;
                }
                catch (Exception ex)
                {
                    Status = $"[{i + 1}/{recipients.Count}] Failed to send to {recipient}: {ex.Message}";
                }
            }

            Status = "Finished sending to all receivers";
            Progress = 100;
            CurrentFileName = "";
            CurrentFileProgress = "";
        }
        catch (OperationCanceledException)
        {
            Status = "Sending cancelled";
        }
        finally
        {
            IsSending = false;
            _sendCts?.Dispose();
            _sendCts = null;
        }
    }

    [RelayCommand]
    private void StopSending()
    {
        _sendCts?.Cancel();
        Status = "Stopping send...";
    }

    private string GetSendFileName(string filePath)
    {
        if (SelectedFolderRoot != null && filePath.StartsWith(SelectedFolderRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = filePath.Substring(SelectedFolderRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length > 0)
            {
                string folderName = Path.GetFileName(SelectedFolderRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return Path.Combine(folderName, relative);
            }
        }
        return Path.GetFileName(filePath);
    }

    private async Task SendFilesToRecipientAsync(string recipientIp, int port, int recipientIndex, int recipientCount, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var client = new TcpClient();
        client.SendBufferSize = BufferSize;
        client.ReceiveBufferSize = BufferSize;
        await client.ConnectAsync(recipientIp, port, ct);

        await using var stream = client.GetStream();

        Status = $"[{recipientIndex + 1}/{recipientCount}] Connected to {recipientIp}. Sending files...";

        int totalFiles = SelectedFiles.Count;
        long totalBytes = 0;

        foreach (var f in SelectedFiles)
        {
            if (File.Exists(f))
                totalBytes += new FileInfo(f).Length;
        }

        if (totalBytes == 0)
            throw new InvalidOperationException("No selected files could be sent.");

        long sentTotal = 0;
        double recipientBaseProgress = (recipientIndex * 100.0) / recipientCount;

        for (int i = 0; i < totalFiles; i++)
        {
            string filePath = SelectedFiles[i];

            if (!File.Exists(filePath))
            {
                Status = $"[{recipientIndex + 1}/{recipientCount}] File not found: {Path.GetFileName(filePath)}";
                continue;
            }

            ct.ThrowIfCancellationRequested();

            string fileName = GetSendFileName(filePath);
            long fileSize = new FileInfo(filePath).Length;

            CurrentFileName = fileName;

            byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
            byte[] fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
            byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);

            await stream.WriteAsync(fileNameLengthBytes, ct);
            await stream.WriteAsync(fileNameBytes, ct);
            await stream.WriteAsync(fileSizeBytes, ct);

            // Compress file data with Deflate (fastest algorithm)
            string ext = Path.GetExtension(filePath);
            bool shouldCompress = fileSize >= MinCompressSize && !UncompressibleExtensions.Contains(ext);

            byte[]? compressedData = null;
            if (shouldCompress)
            {
                using (var ms = new MemoryStream())
                {
                    await using (var fileStream = File.OpenRead(filePath))
                    await using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest))
                        await fileStream.CopyToAsync(deflate);
                    compressedData = ms.ToArray();
                }
            }

            bool useCompression = compressedData != null && compressedData.Length < fileSize;
            long wireSize = useCompression ? compressedData!.Length : fileSize;

            await stream.WriteAsync(new[] { (byte)(useCompression ? 1 : 0) }, ct);
            await stream.WriteAsync(BitConverter.GetBytes(wireSize), ct);

            if (useCompression)
            {
                await stream.WriteAsync(compressedData, ct);
                sentTotal += fileSize;
            }
            else
            {
                await using var fileStream = File.OpenRead(filePath);
                byte[] buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await stream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    sentTotal += bytesRead;
                }
            }

            double totalProgress = recipientBaseProgress + ((sentTotal * 100.0) / totalBytes / recipientCount);
            Progress = totalProgress;
            CurrentFileProgress = $"Receiver {recipientIndex + 1}/{recipientCount}: {FormatFileSize(fileSize)} / {FormatFileSize(fileSize)}";
            Status = $"[{recipientIndex + 1}/{recipientCount}] Sent {i + 1}/{totalFiles}: {fileName} to {recipientIp}";
        }

        byte[] endMarker = BitConverter.GetBytes((int)0);
        await stream.WriteAsync(endMarker, ct);
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

        LocalIp = GetLocalIpAddress();
        IsReceiving = true;
        Status = $"Listening on {LocalIp}:{port} (broadcasting to network)...";
        Progress = 0;
        _cts = new CancellationTokenSource();

        _ = RunDiscoveryServiceAsync(port);

        try
        {
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();

            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _server.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client, _cts.Token);
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

    private async Task RunDiscoveryServiceAsync(int filePort)
    {
        try
        {
            using var udpServer = new UdpClient();
            udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udpServer.EnableBroadcast = true;

            string hostName = Environment.MachineName;
            var token = _cts!.Token;
            var lastAnnounce = DateTime.MinValue;

            await BroadcastPresenceAsync(udpServer, filePort, token);
            lastAnnounce = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - lastAnnounce).TotalSeconds >= 2)
                {
                    await BroadcastPresenceAsync(udpServer, filePort, token);
                    lastAnnounce = DateTime.UtcNow;
                }

                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    var result = await udpServer.ReceiveAsync(linkedCts.Token);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("WIFISENDER_DISCOVERY"))
                    {
                        string response = $"WIFISENDER_RESPONSE|{LocalIp}|{filePort}|{hostName}";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException ex)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Status = $"Socket error in discovery service: {ex.Message}";
                    });
                }
                catch (Exception ex)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Status = $"Discovery service error: {ex.Message}";
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Discovery service failed: {ex.Message}";
            });
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
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
                int read = await ReadExactAsync(stream, lengthBuffer, ct);
                if (read == 0) break;

                int fileNameLength = BitConverter.ToInt32(lengthBuffer, 0);

                if (fileNameLength == 0)
                    break;

                byte[] fileNameBuffer = new byte[fileNameLength];
                await ReadExactAsync(stream, fileNameBuffer, ct);
                string fileName = Encoding.UTF8.GetString(fileNameBuffer);

                byte[] sizeBuffer = new byte[8];
                await ReadExactAsync(stream, sizeBuffer, ct);
                long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

                byte[] flagBuffer = new byte[1];
                await ReadExactAsync(stream, flagBuffer, ct);
                bool isCompressed = flagBuffer[0] == 1;

                byte[] wireSizeBuffer = new byte[8];
                await ReadExactAsync(stream, wireSizeBuffer, ct);
                long wireSize = BitConverter.ToInt64(wireSizeBuffer, 0);

                CurrentFileName = fileName;

                string savePath = Path.Combine(DownloadFolder, fileName);
                string? saveDir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                string originalPath = savePath;
                int counter = 1;
                while (File.Exists(savePath))
                {
                    string name = Path.GetFileNameWithoutExtension(originalPath);
                    string ext = Path.GetExtension(originalPath);
                    string newName = $"{name} ({counter}){ext}";
                    savePath = Path.Combine(saveDir ?? DownloadFolder, newName);
                    counter++;
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = $"Receiving: {fileName} ({FormatFileSize(fileSize)})";
                });

                if (isCompressed)
                {
                    byte[] compressedData = new byte[wireSize];
                    await ReadExactAsync(stream, compressedData, ct);

                    await using var fileStream = File.Create(savePath);
                    using var ms = new MemoryStream(compressedData);
                    await using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    long received = 0;
                    while ((bytesRead = await deflate.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        received += bytesRead;
                        totalReceived += bytesRead;

                        double fileProgress = (received * 100.0) / fileSize;
                        CurrentFileProgress = $"{FormatFileSize(received)} / {FormatFileSize(fileSize)}";

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Progress = fileProgress;
                        });
                    }
                }
                else
                {
                    await using var fileStream = File.Create(savePath);
                    byte[] buffer = new byte[BufferSize];
                    long remaining = wireSize;
                    long received = 0;
                    while (remaining > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                        if (bytesRead == 0) break;

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        received += bytesRead;
                        totalReceived += bytesRead;
                        remaining -= bytesRead;

                        double fileProgress = (received * 100.0) / fileSize;
                        CurrentFileProgress = $"{FormatFileSize(received)} / {FormatFileSize(fileSize)}";

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Progress = fileProgress;
                        });
                    }
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
        catch (OperationCanceledException)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = "Receive cancelled";
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

    private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct = default)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
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
        var recipients = GetRecipientTargets();
        if (recipients.Count == 0)
        {
            ConnectionTestResult = "Enter one or more receiver IP addresses";
            return;
        }

        if (!int.TryParse(Port, out int port) || port <= 0 || port > 65535)
        {
            ConnectionTestResult = "Invalid port";
            return;
        }

        ConnectionTestResult = "Testing receivers...";

        var results = new List<string>();
        foreach (var recipient in recipients)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(recipient, port);

                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                {
                    results.Add($"OK - connected to {recipient}:{port}");
                }
                else
                {
                    results.Add($"Timeout - port not open on {recipient}:{port}");
                }
            }
            catch (Exception ex)
            {
                results.Add($"Failed - {recipient}:{port}: {ex.Message}");
            }
        }

        ConnectionTestResult = string.Join(Environment.NewLine, results);
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
        SelectedFolderRoot = null;
        Status = "Files cleared";
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        ContentOpacity = 0;
        await Task.Delay(200);
        IsDarkTheme = !IsDarkTheme;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }
        ContentOpacity = 1;
    }

    private async Task CheckFirewallAsync()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IsFirewallWarningVisible = false;
                return;
            }

            if (await IsUfwActiveAsync())
            {
                if (!await UfwHasPortAsync(DiscoveryPort))
                {
                    FirewallWarningText = "Firewall (ufw) is blocking device discovery. " +
                        $"Allow UDP port {DiscoveryPort} to scan for devices.";
                    IsFirewallWarningVisible = true;
                    return;
                }
                IsFirewallWarningVisible = false;
                return;
            }

            if (await IsFirewalldActiveAsync())
            {
                if (!await FirewalldHasPortAsync(DiscoveryPort))
                {
                    FirewallWarningText = "Firewall (firewalld) is blocking device discovery. " +
                        $"Allow UDP port {DiscoveryPort} to scan for devices.";
                    IsFirewallWarningVisible = true;
                    return;
                }
                IsFirewallWarningVisible = false;
                return;
            }

            IsFirewallWarningVisible = false;
        }
        catch
        {
            IsFirewallWarningVisible = false;
        }
    }

    private static async Task<string> RunCommandAsync(string fileName, string args)
    {
        try
        {
            var exe = GetExecutablePath(fileName);
            if (exe == null)
                return "";

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            try { proc.Start(); } catch { return ""; }
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output;
        }
        catch
        {
            return "";
        }
    }

    private static async Task<bool> IsUfwActiveAsync()
    {
        string output = await RunCommandAsync("ufw", "status");
        return output.Contains("Status: active");
    }

    private static async Task<bool> UfwHasPortAsync(int port)
    {
        string output = await RunCommandAsync("ufw", "status verbose");
        return output.Contains($"{port}/udp") || output.Contains($"{port}");
    }

    private static async Task<bool> IsFirewalldActiveAsync()
    {
        string output = await RunCommandAsync("firewall-cmd", "--state");
        return output.Trim() == "running";
    }

    private static async Task<bool> FirewalldHasPortAsync(int port)
    {
        string output = await RunCommandAsync("firewall-cmd", "--list-ports");
        return output.Contains($"{port}/udp");
    }

    [RelayCommand]
    private async Task FixFirewall()
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "setup-firewall.sh");
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scripts", "setup-firewall.sh");
        }

        if (!File.Exists(scriptPath))
        {
            Status = "Firewall script not found. Run: sudo scripts/setup-firewall.sh";
            return;
        }

        // Try pkexec (Polkit) first
        if (await TryLaunchElevatedAsync("pkexec", $"\"{scriptPath}\""))
            return;

        // Fall back to xdg-su (LXDE/XFCE)
        if (await TryLaunchElevatedAsync("xdg-su", $"-c \"{scriptPath}\""))
            return;

        // Last fallback: show manual instruction
        Status = $"Run manually in terminal: sudo {scriptPath}";
    }

    private static async Task<bool> TryLaunchElevatedAsync(string tool, string arguments)
    {
        try
        {
            var exe = GetExecutablePath(tool);
            if (exe == null)
                return false;

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = true
                }
            };
            try { proc.Start(); } catch { return false; }
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutablePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // If a path was provided, return it if the file exists
        if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            return File.Exists(name) ? name : null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        var parts = pathEnv.Split(Path.PathSeparator);
        foreach (var p in parts)
        {
            try
            {
                var candidate = Path.Combine(p, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        return null;
    }

    public static string FormatFileSize(long bytes)
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
