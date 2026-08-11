using System;
using System.Collections.Generic;

namespace WifiSender.FileTransfer.Models;

public enum TransferStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public sealed class TransferState
{
    public Guid TransferId { get; set; }
    public Guid FileId { get; set; }
    public long FileSize { get; set; }
    public int ChunkSize { get; set; }
    public IReadOnlyCollection<long> CompletedChunks { get; set; } = Array.Empty<long>();
    public string? FileHash { get; set; }
    public TransferStatus Status { get; set; }
}
