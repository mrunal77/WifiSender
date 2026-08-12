using WifiSender.Transfer.Protocol;

namespace WifiSender.Transfer.Session;

/// <summary>A local file to send, optionally renamed on the remote end.</summary>
public sealed record FileSource(string Path, string RemoteName);

/// <summary>Outcome for a single file within a batch.</summary>
public sealed record FileTransferResult(string Name, AckStatus Status, string? Message);

/// <summary>Aggregate outcome of a whole transfer batch.</summary>
public sealed record TransferResult(
    bool Success,
    int FileCount,
    long BytesTransferred,
    TimeSpan Duration,
    IReadOnlyList<FileTransferResult> Files);

/// <summary>Progress report emitted during send/receive. <see cref="TotalBytes"/> grows as metadata arrives.</summary>
public sealed record TransferProgress(
    int FileIndex,
    int FileCount,
    string FileName,
    long FileLength,
    long FileBytesTransferred,
    long TotalBytes,
    long TotalBytesTransferred);

/// <summary>Configuration for a session, shared by sender and receiver.</summary>
public sealed record SessionOptions
{
    /// <summary>When set, both ends must present it to pair (HMAC over the capability exchange).</summary>
    public string? PairingSecret { get; init; }

    /// <summary>Preferred payload size per Data frame. Negotiated down if the peer is smaller.</summary>
    public int ChunkSize { get; init; } = 1 << 20;

    /// <summary>Whether to attempt resume of partially received files.</summary>
    public bool EnableResume { get; init; } = true;

    /// <summary>
    /// Server-side hook mapping <c>(destinationDirectory, sanitizedName)</c> to the destination file
    /// path. Defaults to <c>Path.Combine</c>. Lets hosts rename on name conflicts or organize
    /// inbound files into subfolders.
    /// </summary>
    public Func<string, string, string>? PathSelector { get; init; }

    /// <summary>Extra capability flags the local end is willing to offer.</summary>
    public CapabilityFlags ExtraCapabilities { get; init; } = CapabilityFlags.None;
}

/// <summary>Raised when the peer rejects the session or a transfer step fails.</summary>
public sealed class TransferException : Exception
{
    public TransferException(string message) : base(message) { }
    public TransferException(string message, Exception inner) : base(message, inner) { }
}
