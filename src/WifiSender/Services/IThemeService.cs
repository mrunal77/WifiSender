using System;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed record ThemeChangedEventArgs(bool IsDark);

public interface IThemeService
{
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
    Task ToggleThemeAsync(CancellationToken cancellationToken = default);
    string GetCurrentTheme();
    bool IsSystemDarkTheme();
}
