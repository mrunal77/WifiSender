using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed record FileTransferProgress(
    string? CurrentFileName,
    long BytesTransferred,
    long TotalBytes,
    double Percentage,
    double SpeedBytesPerSecond,
    bool IsReceiving);

public sealed record TransferCompletedEventArgs(
    bool Success,
    string? ErrorMessage,
    long BytesTransferred);

public sealed record TransferErrorEventArgs(
    string ErrorMessage,
    bool IsReceiving);

public interface IFileTransferService
{
    event EventHandler<FileTransferProgress>? TransferProgress;
    event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    event EventHandler<TransferErrorEventArgs>? TransferError;
    event EventHandler<string>? ConnectionStatusChanged;

    Task SendFilesAsync(IEnumerable<string> filePaths, IEnumerable<DiscoveredDevice> recipients, CancellationToken cancellationToken = default);
    Task StartReceivingAsync(string downloadFolder, int port, CancellationToken cancellationToken = default);
    Task StopReceivingAsync(CancellationToken cancellationToken = default);
    Task TestConnectionAsync(string ipAddress, int port, CancellationToken cancellationToken = default);
}
