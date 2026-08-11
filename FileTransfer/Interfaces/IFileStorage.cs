using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.FileTransfer.Interfaces;

public interface IFileStorage
{
    Task<Stream> OpenReadAsync(string fileId, CancellationToken cancellationToken);
    Task<Stream> CreateWriteAsync(string fileId, CancellationToken cancellationToken);
    Task DeleteAsync(string fileId, CancellationToken cancellationToken);
    Task<string> ResolveFilePathAsync(string fileId, CancellationToken cancellationToken);
}
