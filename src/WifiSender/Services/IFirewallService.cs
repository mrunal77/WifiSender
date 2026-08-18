using System;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed record FirewallStatus(bool IsBlocking, string WarningText, string FirewallType);

public interface IFirewallService
{
    Task<FirewallStatus> CheckFirewallAsync(CancellationToken cancellationToken = default);
    Task FixFirewallAsync(CancellationToken cancellationToken = default);
}
