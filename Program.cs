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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSION_MANAGER")))
        {
            // Some X11/libICE libraries warn when SESSION_MANAGER is not defined.
            // Set an empty value to suppress benign warnings when no session manager is present.
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
