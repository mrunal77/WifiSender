using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public interface ILocalIpService
{
    Task<string> GetLocalIpAddressAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetBroadcastAddressesAsync(CancellationToken cancellationToken = default);
}
