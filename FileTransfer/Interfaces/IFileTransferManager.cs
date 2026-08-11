using System;
using System.Threading;
using System.Threading.Tasks;
using WifiSender.FileTransfer.Models;

namespace WifiSender.FileTransfer.Interfaces;

public interface IFileTransferManager
{
    Task<TransferResult> SendAsync(TransferRequest request, CancellationToken cancellationToken);
    Task<TransferResult> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken);
    Task PauseAsync(Guid transferId, CancellationToken cancellationToken);
    Task ResumeAsync(Guid transferId, CancellationToken cancellationToken);
    Task CancelAsync(Guid transferId, CancellationToken cancellationToken);
    Task<TransferStatus> GetStatusAsync(Guid transferId, CancellationToken cancellationToken);
}
