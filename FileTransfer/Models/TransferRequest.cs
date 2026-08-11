using System;

namespace WifiSender.FileTransfer.Models;

public sealed class TransferRequest
{
    public Guid TransferId { get; init; } = Guid.NewGuid();
    public Guid FileId { get; init; } = Guid.NewGuid();
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string DestinationAddress { get; init; } = string.Empty;
    public int DestinationPort { get; init; }
}
