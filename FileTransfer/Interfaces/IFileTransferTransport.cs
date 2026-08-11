using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WifiSender.FileTransfer.Models;

namespace WifiSender.FileTransfer.Interfaces;

public interface IFileTransferTransport
{
    Task SendChunkAsync(FileChunk chunk, Stream data, CancellationToken cancellationToken);
    Task ReceiveChunkAsync(FileChunk chunk, Stream destination, CancellationToken cancellationToken);
}
