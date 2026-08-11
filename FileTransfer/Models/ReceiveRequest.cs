using System;

namespace WifiSender.FileTransfer.Models;

public sealed class ReceiveRequest
{
    public Guid TransferId { get; init; } = Guid.NewGuid();
    public string DownloadDirectory { get; init; } = string.Empty;
    public int ListenPort { get; init; }
}
