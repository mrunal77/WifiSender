namespace WifiSender.Transfer.Protocol;

/// <summary>
/// Frame envelope: every message on the wire is [FrameType:1][PayloadLength:4 big-endian][Payload].
/// All integers are transmitted big-endian. Frames carry compact binary payloads (no JSON for data).
/// </summary>
public enum FrameType : byte
{
    /// <summary>Client -&gt; server. Negotiates capabilities.</summary>
    Capability = 1,

    /// <summary>Server -&gt; client. Accepts the negotiated capabilities.</summary>
    CapabilityAck = 2,

    /// <summary>Client -&gt; server. Begins a multi-file transfer batch.</summary>
    TransferStart = 3,

    /// <summary>Client -&gt; server. Metadata for a single file.</summary>
    FileMeta = 4,

    /// <summary>Server -&gt; client. Reports how many bytes of the file are already present (resume).</summary>
    ResumeInfo = 5,

    /// <summary>Client -&gt; server. Raw file payload bytes.</summary>
    Data = 6,

    /// <summary>Client -&gt; server. Signals the file payload is finished and carries the whole-file hash.</summary>
    FileEnd = 7,

    /// <summary>Client -&gt; server. Signals the batch is finished.</summary>
    TransferEnd = 8,

    /// <summary>Server -&gt; client. Final status for a file or the whole transfer.</summary>
    Ack = 9,

    /// <summary>Either direction. Aborts the session.</summary>
    Cancel = 10,

    /// <summary>Either direction. Optional liveness probe.</summary>
    Ping = 11,

    /// <summary>Either direction. Reply to <see cref="Ping"/>.</summary>
    Pong = 12,
}

[Flags]
public enum CapabilityFlags : uint
{
    None = 0,
    SupportsSha256 = 1 << 0,
    SupportsResume = 1 << 1,
    SupportsCompression = 1 << 2,
    SupportsMultipleFiles = 1 << 3,
}

public enum HashAlgorithmId : byte
{
    None = 0,
    Sha256 = 1,
}

public enum AckStatus : byte
{
    Ok = 0,
    HashMismatch = 1,
    IoError = 2,
    Cancelled = 3,
    ProtocolError = 4,
    Busy = 5,
    Unauthorized = 6,
    Unsupported = 7,
}

/// <summary>Version of the wire protocol this build speaks.</summary>
public static class WireProtocol
{
    public const byte Version = 1;

    /// <summary>Maximum length of any frame payload, guards against oversized lengths.</summary>
    public const int MaxFramePayload = 64 * 1024 * 1024;

    /// <summary>Maximum length of an encoded file name.</summary>
    public const int MaxFileNameLength = 4096;

    /// <summary>Maximum length of a status message carried by an Ack frame.</summary>
    public const int MaxStatusMessageLength = 1024;

    public static readonly byte[] Magic = { 0x57, 0x53, 0x46, 0x54 }; // "WSFT"
}
