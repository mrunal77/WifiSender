using System;

namespace WifiSender.FileTransfer.Models;

public sealed class TransferResult
{
    public Guid TransferId { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long BytesTransferred { get; init; }
    public TimeSpan Duration { get; init; }
}
