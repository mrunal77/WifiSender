namespace WifiSender.FileTransfer;

public sealed class FileTransferOptions
{
    public int ChunkSizeMB { get; set; } = 4;
    public int MaxChunkSizeMB { get; set; } = 16;
    public int MaxParallelStreams { get; set; } = 4;
    public int MaxConcurrentTransfers { get; set; } = 2;
    public int BufferPoolSizeMB { get; set; } = 128;
    public bool EnableCompression { get; set; } = true;
    public bool EnableQuic { get; set; } = true;
    public bool EnableTcpFallback { get; set; } = true;
    public bool EnableResume { get; set; } = true;
    public bool EnableIntegrityVerification { get; set; } = true;
    public int ChannelCapacity { get; set; } = 32;
    public int IncompleteTransferRetentionHours { get; set; } = 24;
}
