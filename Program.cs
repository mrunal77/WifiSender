using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Runtime.InteropServices;

namespace WifiSender;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Disable DBus IME to avoid disposal errors from IBus not implementing Destroy method
            Environment.SetEnvironmentVariable("AVALONIA_IME_MODE", "None");
            
            // Some X11/libICE libraries warn when SESSION_MANAGER is not defined.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSION_MANAGER")))
                Environment.SetEnvironmentVariable("SESSION_MANAGER", string.Empty);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
