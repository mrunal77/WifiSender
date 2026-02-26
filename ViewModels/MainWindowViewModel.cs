using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WifiSender.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private TcpListener? _server;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
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

    public ObservableCollection<string> SelectedFiles { get; } = new();

    public MainWindowViewModel()
    {
        LocalIp = GetLocalIpAddress();
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

    [RelayCommand]
    private void SelectFiles(Window? window)
    {
        if (window == null) return;

        var files = window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Files to Send",
            AllowMultiple = true
        }).Result;

        SelectedFiles.Clear();
        foreach (var file in files)
        {
            SelectedFiles.Add(file.Path.LocalPath);
        }

        if (SelectedFiles.Count > 0)
            Status = $"Selected {SelectedFiles.Count} file(s)";
    }

    [RelayCommand]
    private void SelectDownloadFolder(Window? window)
    {
        if (window == null) return;

        var folder = window.StorageProvider.OpenFolderPickerAsync(_folderPickerOptions).Result;
        if (folder.Count > 0)
        {
            DownloadFolder = folder[0].Path.LocalPath;
            Status = $"Download folder: {DownloadFolder}";
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
            await _client.ConnectAsync(RecipientIp, port);
            _stream = _client.GetStream();

            Status = "Connected! Sending files...";

            int totalFiles = SelectedFiles.Count;
            for (int i = 0; i < totalFiles; i++)
            {
                string filePath = SelectedFiles[i];
                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                // Send header: filename|size|
                string header = $"{fileName}|{fileSize}|";
                byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
                await _stream.WriteAsync(headerBytes);

                // Send file data
                await using var fileStream = File.OpenRead(filePath);
                byte[] buffer = new byte[65536];
                long sent = 0;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    await _stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    sent += bytesRead;
                    Progress = ((i * 100.0) + (sent * 100.0 / fileSize)) / totalFiles;
                }

                Status = $"Sent {i + 1}/{totalFiles}: {fileName}";
            }

            // Send end marker
            byte[] endMarker = System.Text.Encoding.UTF8.GetBytes("__END__");
            await _stream.WriteAsync(endMarker);

            Status = "All files sent successfully!";
            Progress = 100;
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

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Connection from {((IPEndPoint)client.Client.RemoteEndPoint!).Address}";
            });

            using var stream = client.GetStream();
            using var reader = new BinaryReader(stream);

            while (true)
            {
                // Read header
                string header = "";
                while (!header.Contains('|'))
                {
                    int b = reader.ReadByte();
                    if (b == -1) return;
                    header += (char)b;
                }

                if (header == "__END__")
                    break;

                var parts = header.TrimEnd('|').Split('|');
                if (parts.Length < 2) continue;

                string fileName = parts[0];
                long fileSize = long.Parse(parts[1]);

                string savePath = Path.Combine(DownloadFolder, fileName);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = $"Receiving: {fileName}";
                });

                // Receive file data
                await using var fileStream = File.Create(savePath);
                byte[] buffer = new byte[65536];
                long received = 0;

                while (received < fileSize)
                {
                    int toRead = (int)Math.Min(buffer.Length, fileSize - received);
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                    if (bytesRead == 0) break;
                    
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    received += bytesRead;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Progress = (received * 100.0) / fileSize;
                    });
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = $"Received: {fileName}";
                });
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = "Transfer complete!";
                Progress = 100;
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

    [RelayCommand]
    private void StopReceiving()
    {
        _cts?.Cancel();
        _server?.Stop();
        IsReceiving = false;
        Status = "Stopped";
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
}
