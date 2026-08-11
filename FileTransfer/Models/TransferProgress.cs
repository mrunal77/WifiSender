using System;

namespace WifiSender.FileTransfer.Models;

public sealed record TransferProgress(
    Guid TransferId,
    long BytesTransferred,
    long TotalBytes,
    double Percentage,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining);
