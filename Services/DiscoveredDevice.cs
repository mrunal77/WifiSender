using System.ComponentModel;

namespace WifiSender.Services;

public sealed class DiscoveredDevice : INotifyPropertyChanged
{
    public string IpAddress { get; set; } = "";
    public string Port { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(DeviceName) ? $"{IpAddress}:{Port}" : $"{DeviceName} ({IpAddress})";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
