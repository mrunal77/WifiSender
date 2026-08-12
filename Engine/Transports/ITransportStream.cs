namespace WifiSender.Transfer.Transports;

/// <summary>A connected, duplex byte stream over which the framed protocol runs.</summary>
public interface ITransportStream : IAsyncDisposable
{
    /// <summary>The duplex stream. Reads and writes share this object.</summary>
    Stream Stream { get; }

    /// <summary>Human-readable transport name (e.g. "tcp", "quic").</summary>
    string TransportName { get; }

    /// <summary>Hard-closes the stream, discarding buffered data.</summary>
    void Abort();
}

/// <summary>A listener that yields inbound <see cref="ITransportStream"/> connections.</summary>
public interface ITransportListener : IAsyncDisposable
{
    /// <summary>Waits for an inbound connection. Returns null when the listener is closed.</summary>
    Task<ITransportStream?> AcceptAsync(CancellationToken ct);

    /// <summary>The port the listener is bound to (resolves 0 = ephemeral to the real port).</summary>
    int Port { get; }
}

/// <summary>
/// A transport capable of connecting to and listening for transfer sessions.
/// All transports speak the identical framed protocol so the engine is transport-agnostic.
/// </summary>
public interface IFileTransferTransport
{
    string Name { get; }

    /// <summary>True when the native/platform prerequisites for this transport are present.</summary>
    bool IsAvailable { get; }

    Task<ITransportStream> ConnectAsync(string host, int port, CancellationToken ct);

    Task<ITransportListener> ListenAsync(int port, CancellationToken ct);
}
