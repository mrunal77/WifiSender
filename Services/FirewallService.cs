using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed class FirewallService : IFirewallService
{
    private const int DiscoveryPort = 5556;

    public async Task<FirewallStatus> CheckFirewallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new FirewallStatus(false, "", "none");

            if (await IsUfwActiveAsync())
            {
                if (!await UfwHasPortAsync(DiscoveryPort))
                    return new FirewallStatus(true, $"Firewall (ufw) is blocking device discovery. Allow UDP port {DiscoveryPort} to scan for devices.", "ufw");
                return new FirewallStatus(false, "", "ufw");
            }

            if (await IsFirewalldActiveAsync())
            {
                if (!await FirewalldHasPortAsync(DiscoveryPort))
                    return new FirewallStatus(true, $"Firewall (firewalld) is blocking device discovery. Allow UDP port {DiscoveryPort} to scan for devices.", "firewalld");
                return new FirewallStatus(false, "", "firewalld");
            }

            return new FirewallStatus(false, "", "none");
        }
        catch
        {
            return new FirewallStatus(false, "", "none");
        }
    }

    public async Task FixFirewallAsync(CancellationToken cancellationToken = default)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "setup-firewall.sh");
        if (!File.Exists(scriptPath))
            scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scripts", "setup-firewall.sh");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Firewall script not found. Run: sudo scripts/setup-firewall.sh", scriptPath);

        if (await TryLaunchElevatedAsync("pkexec", $"\"{scriptPath}\""))
            return;
        if (await TryLaunchElevatedAsync("xdg-su", $"-c \"{scriptPath}\""))
            return;

        throw new InvalidOperationException($"Run manually in terminal: sudo {scriptPath}");
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

        if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            return File.Exists(name) ? name : null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var p in pathEnv.Split(Path.PathSeparator))
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
}
