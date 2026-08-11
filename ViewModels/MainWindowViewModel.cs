using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _isScanning;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    [ObservableProperty]
    private string _selectedDeviceIp = "";

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        SelectedDeviceIp = value?.IpAddress ?? "";
    }

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

    public string ThemeIcon => IsDarkTheme ? "🌙" : "☀️";

    public IBrush NavBarBackground => IsDarkTheme ? Brushes.Transparent : (IsNavBarScrolled ? Brushes.Transparent : Brushes.Transparent);

    public ObservableCollection<string> SelectedFiles { get; } = new();
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();

    public bool HasSelectedFiles => SelectedFiles.Count > 0;
    public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
    public bool HasConnectionTestResult => !string.IsNullOrEmpty(ConnectionTestResult);
    public bool HasCurrentFile => !string.IsNullOrEmpty(CurrentFileName);

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
                    Status = $"Found {DiscoveredDevices.Count} device(s)";
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
                    Status = "No devices found. Make sure receiver is running on other device.";
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
                IsSending = false;
                IsReceiving = false;
            });
        };

        _fileTransferService.TransferError += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Status = e.ErrorMessage;
                IsSending = false;
                IsReceiving = false;
            });
        };

        _fileTransferService.ConnectionStatusChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(() => ConnectionTestResult = e);
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
        && (!string.IsNullOrWhiteSpace(SelectedDeviceIp) || DiscoveredDevices.Any(d => d.IsSelected));

    [RelayCommand(CanExecute = nameof(CanSendFiles))]
    private async Task SendFiles(Window? window)
    {
        var devices = DiscoveredDevices.Where(d => d.IsSelected).ToList();
        if (devices.Count == 0 && !string.IsNullOrWhiteSpace(SelectedDeviceIp))
        {
            var ips = SelectedDeviceIp.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .ToList();
            devices = ips.Select(ip => new DiscoveredDevice { IpAddress = ip, Port = Port, IsSelected = true }).ToList();
        }
        if (devices.Count == 0)
        {
            Status = "Select a device or enter at least one IP address";
            return;
        }

        IsSending = true;
        Progress = 0;
        CurrentFileName = "";
        CurrentFileProgress = "";

        try
        {
            await _fileTransferService.SendFilesAsync(SelectedFiles, devices);
        }
        catch (OperationCanceledException)
        {
            Status = "Sending cancelled";
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
    }

    [RelayCommand]
    private async Task SelectFiles(Window? window)
    {
        var files = await _filePickerService.PickFilesAsync(window);
        SelectedFiles.Clear();
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
            Status = $"Selected {SelectedFiles.Count} file(s) ({FormatFileSize(totalSize)})";
        }
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
        var folder = await _filePickerService.PickDownloadFolderAsync(window);
        if (folder != null)
        {
            DownloadFolder = folder;
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

        await _discoveryService.ScanAsync();
    }

    [RelayCommand]
    private void SelectAllDevices()
    {
        foreach (var d in DiscoveredDevices)
            d.IsSelected = true;
    }

    [RelayCommand]
    private void ClearDeviceSelection()
    {
        foreach (var d in DiscoveredDevices)
            d.IsSelected = false;
    }

    [RelayCommand]
    private void SelectDevice(DiscoveredDevice? device)
    {
        if (device != null)
        {
            SelectedDevice = device;
            Status = $"Selected: {device.DisplayName}";
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        var selectedDevices = DiscoveredDevices.Where(d => d.IsSelected).ToList();
        if (selectedDevices.Count == 0)
        {
            ConnectionTestResult = "Select one or more receiver devices";
            return;
        }

        ConnectionTestResult = "Testing receivers...";

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
        }
        else
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

            LocalIp = await _localIpService.GetLocalIpAddressAsync();
            IsReceiving = true;
            Status = $"Listening on {LocalIp}:{port} (broadcasting to network)...";
            Progress = 0;

            _ = _discoveryService.StartRespondingAsync(port);
            await _fileTransferService.StartReceivingAsync(DownloadFolder, port);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
        Status = "Files cleared";
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        ContentOpacity = 0;
        await Task.Delay(200);
        await _themeService.ToggleThemeAsync();
        ContentOpacity = 1;
    }

    [RelayCommand]
    private async Task FixFirewall()
    {
        try
        {
            await _firewallService.FixFirewallAsync();
            Status = "Firewall configured successfully";
            await CheckFirewallAsync();
        }
        catch (Exception ex)
        {
            Status = $"Firewall fix failed: {ex.Message}";
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
