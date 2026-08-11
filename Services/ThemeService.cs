using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;

namespace WifiSender.Services;

public sealed class ThemeService : IThemeService
{
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public Task ToggleThemeAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current is { } app)
        {
            bool isDark = app.RequestedThemeVariant == ThemeVariant.Dark;
            app.RequestedThemeVariant = isDark ? ThemeVariant.Light : ThemeVariant.Dark;
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(!isDark));
        }
        return Task.CompletedTask;
    }

    public string GetCurrentTheme()
    {
        if (Application.Current is { } app)
        {
            return app.RequestedThemeVariant == ThemeVariant.Dark ? "Dark" : "Light";
        }
        return "Dark";
    }

    public bool IsSystemDarkTheme()
    {
        if (Application.Current is { } app)
        {
            return app.ActualThemeVariant == ThemeVariant.Dark;
        }
        return true;
    }
}
