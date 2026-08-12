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
            bool isCurrentlyDark = app.ActualThemeVariant == ThemeVariant.Dark;
            var targetTheme = isCurrentlyDark ? ThemeVariant.Light : ThemeVariant.Dark;
            app.RequestedThemeVariant = targetTheme;
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(!isCurrentlyDark));
        }
        return Task.CompletedTask;
    }

    public string GetCurrentTheme()
    {
        if (Application.Current is { } app)
        {
            return app.ActualThemeVariant == ThemeVariant.Dark ? "Dark" : "Light";
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
