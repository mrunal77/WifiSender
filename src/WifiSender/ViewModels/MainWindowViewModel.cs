using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WifiSender.Models;
using WifiSender.Services;

namespace WifiSender.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileTransferService _fileTransferService;
    private readonly IFirewallService _firewallService;
    private readonly ILocalIpService _localIpService;
    private readonly IThemeService _themeService;
    private readonly IFilePickerService _filePickerService;
    private CancellationTokenSource? _toastCts;

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
    private string _transferSpeedText = "";

    [ObservableProperty]
    private string _estimatedTimeRemainingText = "";

    [ObservableProperty]
    private bool _isScanning;

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

    [ObservableProperty]
    private string _toastMessage = "";

    [ObservableProperty]
    private bool _isToastVisible;

    public string ThemeIcon => IsDarkTheme ? "🌙" : "☀️";

    public string VersionText
    {
        get
        {
            var version = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(version))
                return "";
            var plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
                version = version[..plusIndex];
            return $"v{version}";
        }
    }

    public IBrush NavBarBackground => IsDarkTheme ? Brushes.Transparent : Brushes.Transparent;

    public ObservableCollection<SelectedFileItem> SelectedFiles { get; } = new();
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();

    public bool HasSelectedFiles => SelectedFiles.Count > 0;
    public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
    public bool HasConnectionTestResult => !string.IsNullOrEmpty(ConnectionTestResult);
    public bool HasCurrentFile => !string.IsNullOrEmpty(CurrentFileName);

    public string SelectedFilesSummary
    {
        get
        {
            if (SelectedFiles.Count == 0) return "No files selected";
            long totalBytes = 0;
            foreach (var f in SelectedFiles)
                totalBytes += f.FileSize;

            return $"{SelectedFiles.Count} item{(SelectedFiles.Count == 1 ? "" : "s")} ({FormatFileSize(totalBytes)})";
        }
    }

    public MainWindowViewModel()
        : this(
            new DiscoveryService(new LocalIpService()),
            new FileTransferService(),
            new FirewallService(),
            new LocalIpService(),
            new ThemeService(),
            new FilePickerService())
    {
    }

    public MainWindowViewModel(
        IDiscoveryService discoveryService,
        IFileTransferService fileTransferService,
        IFirewallService firewallService,
        ILocalIpService localIpService,
        IThemeService themeService,
        IFilePickerService filePickerService)
    {
        _discoveryService = discoveryService;
        _fileTransferService = fileTransferService;
        _firewallService = firewallService;
        _localIpService = localIpService;
        _themeService = themeService;
        _filePickerService = filePickerService;

        _ = InitializeAsync();

        SelectedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelectedFiles));
            OnPropertyChanged(nameof(SelectedFilesSummary));
            SendFilesCommand.NotifyCanExecuteChanged();
        };

        DiscoveredDevices.CollectionChanged += (_, args) =>
        {
            OnPropertyChanged(nameof(HasDiscoveredDevices));
            if (args.NewItems != null)
                foreach (DiscoveredDevice d in args.NewItems)
                    d.PropertyChanged += OnDevicePropertyChanged;
            if (args.OldItems != null)
                foreach (DiscoveredDevice d in args.OldItems)
                    d.PropertyChanged -= OnDevicePropertyChanged;
        };

        _discoveryService.DeviceFound += (_, device) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!DiscoveredDevices.Any(d => d.IpAddress == device.IpAddress && d.Port == device.Port))
                {
                    DiscoveredDevices.Add(device);
                    Status = $"Discovered {DiscoveredDevices.Count} device(s)";
                    ShowToast($"Discovered device: {device.DisplayName}");
                }
            });
        };

        _discoveryService.ScanCompleted += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsScanning = false;
                if (DiscoveredDevices.Count == 0)
                {
                    Status = "No devices found. Make sure receiver is running on target device.";
                    _ = CheckFirewallAsync();
                }
                else
                {
                    Status = $"Found {DiscoveredDevices.Count} device(s)";
                    IsFirewallWarningVisible = false;
                }
            });
        };

        _discoveryService.ScanError += (_, ex) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = $"Scan error: {ex.Message}";
                IsScanning = false;
            });
        };

        _fileTransferService.TransferProgress += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentFileName = e.CurrentFileName ?? "";
                CurrentFileProgress = $"{FormatFileSize(e.BytesTransferred)} / {FormatFileSize(e.TotalBytes)}";
                Progress = e.Percentage;

                if (e.SpeedBytesPerSecond > 0)
                {
                    TransferSpeedText = $"{FormatFileSize((long)e.SpeedBytesPerSecond)}/s";
                    var remainingBytes = e.TotalBytes - e.BytesTransferred;
                    if (remainingBytes > 0)
                    {
                        var seconds = remainingBytes / e.SpeedBytesPerSecond;
                        EstimatedTimeRemainingText = $"~{Math.Max(1, (int)Math.Ceiling(seconds))}s remaining";
                    }
                    else
                    {
                        EstimatedTimeRemainingText = "Finishing...";
                    }
                }
                else
                {
                    TransferSpeedText = "";
                    EstimatedTimeRemainingText = "";
                }

                Status = e.IsReceiving
                    ? $"Receiving: {e.CurrentFileName} ({e.Percentage:F1}%)"
                    : $"Sending: {e.CurrentFileName} ({e.Percentage:F1}%)";
            });
        };

        _fileTransferService.TransferCompleted += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = e.Success ? "Transfer complete!" : $"Transfer failed: {e.ErrorMessage}";
                Progress = 100;
                CurrentFileName = "";
                CurrentFileProgress = "";
                TransferSpeedText = "";
                EstimatedTimeRemainingText = "";
                IsSending = false;
                IsReceiving = false;

                if (e.Success)
                    ShowToast("✨ Transfer completed successfully!");
                else if (!string.IsNullOrEmpty(e.ErrorMessage))
                    ShowToast($"⚠️ {e.ErrorMessage}");
            });
        };

        _fileTransferService.TransferError += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = e.ErrorMessage;
                IsSending = false;
                IsReceiving = false;
                TransferSpeedText = "";
                EstimatedTimeRemainingText = "";
                ShowToast($"❌ {e.ErrorMessage}");
            });
        };

        _fileTransferService.ConnectionStatusChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionTestResult = e;
                ShowToast(e);
            });
        };

        _themeService.ThemeChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(() => IsDarkTheme = e.IsDark);
        };
    }

    private async Task InitializeAsync()
    {
        LocalIp = await _localIpService.GetLocalIpAddressAsync();
        IsDarkTheme = _themeService.IsSystemDarkTheme();

        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            DownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) =>
            {
                if (app.RequestedThemeVariant == ThemeVariant.Default)
                    IsDarkTheme = _themeService.IsSystemDarkTheme();
            };
        }
        await CheckFirewallAsync();
    }

    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscoveredDevice.IsSelected))
            SendFilesCommand.NotifyCanExecuteChanged();
    }

    public bool CanSendFiles() =>
        SelectedFiles.Count > 0
        && !IsSending
        && DiscoveredDevices.Any(d => d.IsSelected);

    [RelayCommand(CanExecute = nameof(CanSendFiles))]
    private async Task SendFiles(Window? window)
    {
        var devices = DiscoveredDevices.Where(d => d.IsSelected).ToList();
        if (devices.Count == 0)
        {
            Status = "Select one or more receiver devices";
            ShowToast("Select at least one receiver device");
            return;
        }

        IsSending = true;
        Progress = 0;
        CurrentFileName = "";
        CurrentFileProgress = "";
        TransferSpeedText = "";
        EstimatedTimeRemainingText = "";

        try
        {
            var paths = SelectedFiles.Select(f => f.FilePath).ToList();
            await _fileTransferService.SendFilesAsync(paths, devices);
        }
        catch (OperationCanceledException)
        {
            Status = "Sending cancelled";
            ShowToast("Sending operation cancelled");
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            ShowToast($"Send error: {ex.Message}");
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void StopSending()
    {
        Status = "Stopping send...";
        ShowToast("Stopping file transmission...");
    }

    [RelayCommand]
    private async Task SelectFiles(Window? window)
    {
        var files = await _filePickerService.PickFilesAsync(window);
        if (files.Count == 0) return;

        SelectedFiles.Clear();
        foreach (var f in files)
            SelectedFiles.Add(new SelectedFileItem(f));

        Status = $"Selected {SelectedFiles.Count} file(s)";
        ShowToast($"Added {SelectedFiles.Count} file(s)");
    }

    [RelayCommand]
    private async Task SelectFolder(Window? window)
    {
        var folderPath = await _filePickerService.PickFolderAsync(window);
        if (folderPath == null) return;

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
        foreach (var f in files)
            SelectedFiles.Add(new SelectedFileItem(f));

        if (SelectedFiles.Count > 0)
        {
            Status = $"Selected {SelectedFiles.Count} file(s) from folder '{Path.GetFileName(folderPath)}'";
            ShowToast($"Loaded folder with {SelectedFiles.Count} files");
        }
    }

    [RelayCommand]
    private void RemoveSelectedFile(SelectedFileItem? fileItem)
    {
        if (fileItem != null && SelectedFiles.Contains(fileItem))
        {
            SelectedFiles.Remove(fileItem);
            Status = $"Removed {fileItem.FileName}";
            ShowToast($"Removed {fileItem.FileName}");
        }
    }

    [RelayCommand]
    private async Task CopyLocalIp(Window? window)
    {
        if (window != null && TopLevel.GetTopLevel(window) is { } topLevel && topLevel.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(LocalIp);
            ShowToast($"📋 Copied IP to clipboard ({LocalIp})");
        }
    }

    [RelayCommand]
    private void OpenDownloadFolder()
    {
        try
        {
            if (!Directory.Exists(DownloadFolder))
                Directory.CreateDirectory(DownloadFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = DownloadFolder,
                UseShellExecute = true
            });
            ShowToast($"Opened download folder");
        }
        catch (Exception ex)
        {
            ShowToast($"Could not open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SelectDownloadFolder(Window? window)
    {
        var folder = await _filePickerService.PickDownloadFolderAsync(window);
        if (folder != null)
        {
            DownloadFolder = folder;
            Status = $"Download folder: {DownloadFolder}";
            ShowToast($"Download folder set to {DownloadFolder}");
        }
    }

    [RelayCommand]
    private async Task ScanDevices()
    {
        if (IsScanning) return;

        IsScanning = true;
        DiscoveredDevices.Clear();
        Status = "Scanning for nearby devices...";
        ShowToast("🔍 Scanning network for devices...");

        await _discoveryService.ScanAsync();
    }

    [RelayCommand]
    private void SelectAllDevices()
    {
        foreach (var d in DiscoveredDevices)
            d.IsSelected = true;
        ShowToast("Selected all devices");
    }

    [RelayCommand]
    private void ClearDeviceSelection()
    {
        foreach (var d in DiscoveredDevices)
            d.IsSelected = false;
        ShowToast("Cleared device selections");
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        var selectedDevices = DiscoveredDevices.Where(d => d.IsSelected).ToList();
        if (selectedDevices.Count == 0)
        {
            ConnectionTestResult = "Select one or more receiver devices";
            ShowToast("Select at least one device to test");
            return;
        }

        ConnectionTestResult = "Testing connection to selected receivers...";
        ShowToast("Testing receiver connections...");

        foreach (var device in selectedDevices)
        {
            await _fileTransferService.TestConnectionAsync(device.IpAddress, int.Parse(device.Port));
        }
    }

    [RelayCommand]
    private async Task ToggleReceiving(Window? window)
    {
        if (IsReceiving)
        {
            await _fileTransferService.StopReceivingAsync();
            IsReceiving = false;
            Status = "Stopped listening";
            ShowToast("Stopped receiver service");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(DownloadFolder))
            {
                DownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(DownloadFolder))
                    Directory.CreateDirectory(DownloadFolder);
            }

            if (!int.TryParse(Port, out int port) || port <= 0 || port > 65535)
            {
                Status = "Invalid port number!";
                ShowToast("Please enter a valid port (1-65535)");
                return;
            }

            LocalIp = await _localIpService.GetLocalIpAddressAsync();
            IsReceiving = true;
            Status = $"Listening on {LocalIp}:{port}...";
            Progress = 0;
            ShowToast($"🚀 Listening for incoming transfers on port {port}");

            _ = _discoveryService.StartRespondingAsync(port);
            await _fileTransferService.StartReceivingAsync(DownloadFolder, port);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
        Status = "Files cleared";
        ShowToast("Cleared selected files");
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        ContentOpacity = 0;
        await Task.Delay(180);
        await _themeService.ToggleThemeAsync();
        ContentOpacity = 1;
        ShowToast(IsDarkTheme ? "🌙 Switched to Dark Mode" : "☀️ Switched to Light Mode");
    }

    [RelayCommand]
    private async Task FixFirewall()
    {
        try
        {
            await CheckFirewallAsync();

            if (!IsFirewallWarningVisible)
            {
                Status = "Firewall is already configured correctly";
                ShowToast("Firewall is configured correctly");
                return;
            }

            await _firewallService.FixFirewallAsync();
            Status = "Firewall configured successfully";
            ShowToast("🛡️ Firewall rule updated!");
            await CheckFirewallAsync();
        }
        catch (Exception ex)
        {
            Status = $"Firewall fix failed: {ex.Message}";
            ShowToast($"Firewall fix error: {ex.Message}");
        }
    }

    private async Task CheckFirewallAsync()
    {
        try
        {
            var result = await _firewallService.CheckFirewallAsync();
            IsFirewallWarningVisible = result.IsBlocking;
            FirewallWarningText = result.WarningText;
        }
        catch
        {
            IsFirewallWarningVisible = false;
        }
    }

    public void ShowToast(string message, int durationMs = 3000)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastMessage = message;
        IsToastVisible = true;

        Task.Delay(durationMs, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() => IsToastVisible = false);
            }
        }, TaskScheduler.Default);
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
