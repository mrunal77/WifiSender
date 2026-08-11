using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WifiSender.FileTransfer.Interfaces;
using WifiSender.FileTransfer.Models;

namespace WifiSender.FileTransfer.Transports;

public sealed class TcpFileTransferTransport : IFileTransferTransport
{
    private readonly int _bufferSize;

    public TcpFileTransferTransport(int bufferSize = 64 * 1024)
    {
        _bufferSize = bufferSize;
    }

    public Task SendChunkAsync(FileChunk chunk, Stream data, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ReceiveChunkAsync(FileChunk chunk, Stream destination, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
