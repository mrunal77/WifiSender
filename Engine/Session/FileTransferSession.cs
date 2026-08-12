using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using WifiSender.Transfer.Protocol;
using WifiSender.Transfer.Transports;

namespace WifiSender.Transfer.Session;

/// <summary>
/// A single negotiated transfer session over a connected transport stream.
/// <list type="bullet">
/// <item>ConnectAsync/AcceptAsync perform the capability + pairing handshake.</item>
/// <item>SendAsync streams local files to the peer (client side).</item>
/// <item>ReceiveAsync writes inbound files to a destination directory (server side).</item>
/// </list>
/// Sessions are single-use: one send or one receive per handshake.
/// </summary>
public sealed class FileTransferSession : IAsyncDisposable
{
    private const int SmallFrameLimit = 1 << 20;

    private readonly ITransportStream _transport;
    private readonly Stream _stream;
    private readonly FrameWriter _writer;
    private readonly FrameReader _reader;
    private readonly SessionOptions _options;
    private readonly bool _isSender;

    private int _negotiatedChunk;
    private CapabilityFlags _negotiatedFlags;
    private int _state; // 0 = idle, 1 = transferring, 2 = finished

    private FileTransferSession(ITransportStream transport, SessionOptions options, bool isSender)
    {
        _transport = transport;
        _stream = transport.Stream;
        _writer = new FrameWriter(_stream);
        _reader = new FrameReader(_stream);
        _options = options;
        _isSender = isSender;
    }

    /// <summary>Initiates the client side of the handshake (capability exchange + optional pairing).</summary>
    public static Task<FileTransferSession> ConnectAsync(ITransportStream transport, SessionOptions options, CancellationToken ct = default)
    {
        var session = new FileTransferSession(transport, options, isSender: true);
        return HandshakeAsync(session, ct);
    }

    /// <summary>Accepts the server side of the handshake.</summary>
    public static Task<FileTransferSession> AcceptAsync(ITransportStream transport, SessionOptions options, CancellationToken ct = default)
    {
        var session = new FileTransferSession(transport, options, isSender: false);
        return HandshakeAsync(session, ct);
    }

    private static async Task<FileTransferSession> HandshakeAsync(FileTransferSession session, CancellationToken ct)
    {
        try
        {
            await session.RunHandshakeAsync(ct).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunHandshakeAsync(CancellationToken ct)
    {
        var myFlags = ComputeLocalFlags();
        if (_isSender)
        {
            byte[] payload = CapabilityCodec.Encode(myFlags, _options.ChunkSize, HashAlgorithmId.Sha256, _options.PairingSecret);
            await _writer.WriteAsync(FrameType.Capability, payload, ct).ConfigureAwait(false);

            var (type, len) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
            switch (type)
            {
                case FrameType.CapabilityAck:
                {
                    var ack = new byte[CheckLen(type, len)];
                    await _reader.ReadPayloadAsync(ack, ct).ConfigureAwait(false);
                    var (flags, chunk, algo, _) = CapabilityCodec.Decode(ack, null);
                    if (algo != HashAlgorithmId.Sha256)
                        throw new TransferException("Peer does not support SHA-256 hashing.");
                    var required = RequiredFlags();
                    if ((flags & required) != required)
                        throw new TransferException($"Peer rejected required capabilities (negotiated {flags}).");
                    _negotiatedFlags = flags;
                    _negotiatedChunk = Math.Clamp(chunk, 1, Math.Max(1, _options.ChunkSize));
                    break;
                }
                case FrameType.Ack:
                {
                    var ack = new byte[CheckLen(type, len)];
                    await _reader.ReadPayloadAsync(ack, ct).ConfigureAwait(false);
                    var (status, message) = SessionCodec.DecodeAck(ack);
                    if (status == AckStatus.Unauthorized)
                        throw new UnauthorizedAccessException(message ?? "Pairing rejected.");
                    throw new TransferException($"Peer rejected the session: {message}");
                }
                default:
                    throw new TransferException($"Unexpected frame {type} during handshake.");
            }
        }
        else
        {
            var (type, len) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
            if (type != FrameType.Capability)
                throw new TransferException($"Expected Capability frame, got {type}.");
            var caps = new byte[CheckLen(type, len)];
            await _reader.ReadPayloadAsync(caps, ct).ConfigureAwait(false);
            var (flags, chunk, algo, authorized) = CapabilityCodec.Decode(caps, _options.PairingSecret);

            if (!authorized || algo != HashAlgorithmId.Sha256)
            {
                await _writer.WriteAsync(FrameType.Ack, SessionCodec.EncodeAck(AckStatus.Unauthorized, "Pairing failed."), CancellationToken.None)
                    .ConfigureAwait(false);
                throw new UnauthorizedAccessException("Pairing failed.");
            }

            CapabilityFlags negotiated = flags & ComputeLocalFlags();
            _negotiatedFlags = negotiated;
            _negotiatedChunk = Math.Clamp(chunk, 1, Math.Max(1, _options.ChunkSize));
            await _writer.WriteAsync(FrameType.CapabilityAck,
                CapabilityCodec.Encode(negotiated, _negotiatedChunk, HashAlgorithmId.Sha256, null), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Client side: streams each local file to the peer, resuming where the receiver already has bytes.</summary>
    public async Task<TransferResult> SendAsync(
        IReadOnlyList<FileSource> files,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_isSender)
            throw new InvalidOperationException("This session is a receiver; use ReceiveAsync.");
        EnsureCanTransfer();
        var started = Stopwatch.StartNew();
        var results = new List<FileTransferResult>(files.Count);
        long totalBytes = 0;
        long totalSent = 0;

        try
        {
            foreach (var f in files)
            {
                var info = new FileInfo(f.Path);
                if (!info.Exists)
                    throw new TransferException($"File not found: {f.Path}");
                totalBytes += info.Length;
            }

            await _writer.WriteAsync(FrameType.TransferStart, SessionCodec.EncodeTransferStart(files.Count), ct).ConfigureAwait(false);

            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];
                var info = new FileInfo(file.Path);
                string remoteName = string.IsNullOrEmpty(file.RemoteName) ? info.Name : file.RemoteName;

                await _writer.WriteAsync(FrameType.FileMeta, SessionCodec.EncodeFileMeta(new FileMeta(remoteName, info.Length)), ct)
                    .ConfigureAwait(false);

                long resumeOffset = 0;
                if (info.Length > 0 && _negotiatedFlags.HasFlag(CapabilityFlags.SupportsResume))
                {
                    var (rtype, rlen) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
                    if (rtype == FrameType.Cancel)
                        throw new TransferException("The peer cancelled the transfer.");
                    if (rtype != FrameType.ResumeInfo || rlen != 8)
                        throw new TransferException($"Expected ResumeInfo frame, got {rtype}.");
                    var rbuf = new byte[8];
                    await _reader.ReadPayloadAsync(rbuf, ct).ConfigureAwait(false);
                    resumeOffset = Math.Clamp(SessionCodec.DecodeResumeInfo(rbuf), 0, info.Length);
                }

                var (fileResult, bytesSent) = await SendFileAsync(
                    file.Path, remoteName, info.Length, resumeOffset, i, files.Count, totalBytes, totalSent, progress, ct).ConfigureAwait(false);
                totalSent += bytesSent;
                results.Add(fileResult);

                if (fileResult.Status != AckStatus.Ok)
                {
                    await SendCancelAsync().ConfigureAwait(false);
                    throw new TransferException($"Peer rejected '{remoteName}': {fileResult.Message}");
                }
            }

            await _writer.WriteAsync(FrameType.TransferEnd, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
            var (finalStatus, finalMessage) = await ReadAckAsync(ct).ConfigureAwait(false);
            if (finalStatus != AckStatus.Ok)
                throw new TransferException($"Batch rejected: {finalMessage}");

            started.Stop();
            _state = 2;
            return new TransferResult(true, results.Count, totalSent, started.Elapsed, results);
        }
        catch (OperationCanceledException)
        {
            await SendCancelAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not TransferException and not UnauthorizedAccessException)
        {
            await SendCancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<(FileTransferResult Result, long BytesSent)> SendFileAsync(
        string path, string remoteName, long length, long resumeOffset,
        int index, int count, long totalBytes, long totalSent,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, _negotiatedChunk));
        long sentForFile = resumeOffset;
        long skipRemaining = resumeOffset;

        progress?.Report(new TransferProgress(index, count, remoteName, length, sentForFile, totalBytes, totalSent + sentForFile));

        try
        {
            while (true)
            {
                int read = await fs.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                hasher.AppendData(buffer, 0, read);

                if (skipRemaining > 0)
                {
                    if (skipRemaining >= read)
                    {
                        skipRemaining -= read;
                        continue;
                    }
                    int toSend = read - (int)skipRemaining;
                    await _writer.WriteAsync(FrameType.Data, buffer.AsMemory((int)skipRemaining, toSend), ct).ConfigureAwait(false);
                    sentForFile += toSend;
                    skipRemaining = 0;
                }
                else
                {
                    await _writer.WriteAsync(FrameType.Data, buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    sentForFile += read;
                }

                progress?.Report(new TransferProgress(index, count, remoteName, length, sentForFile, totalBytes, totalSent + sentForFile));
            }

            byte[] hash = hasher.GetHashAndReset();
            await _writer.WriteAsync(FrameType.FileEnd, hash, ct).ConfigureAwait(false);
            var (status, message) = await ReadAckAsync(ct).ConfigureAwait(false);
            return (new FileTransferResult(remoteName, status, message), sentForFile - resumeOffset);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Server side: writes inbound files into <paramref name="destinationDirectory"/>.</summary>
    public async Task<TransferResult> ReceiveAsync(
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_isSender)
            throw new InvalidOperationException("This session is a sender; use SendAsync.");
        EnsureCanTransfer();
        Directory.CreateDirectory(destinationDirectory);
        var started = Stopwatch.StartNew();
        var results = new List<FileTransferResult>();
        long totalBytes = 0;
        long totalReceived = 0;

        try
        {
            var (ttype, tlen) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
            if (ttype == FrameType.Cancel)
                throw new TransferException("The peer cancelled the transfer.");
            if (ttype != FrameType.TransferStart)
                throw new TransferException($"Expected TransferStart frame, got {ttype}.");
            var tbuf = new byte[CheckLen(ttype, tlen)];
            await _reader.ReadPayloadAsync(tbuf, ct).ConfigureAwait(false);
            int fileCount = SessionCodec.DecodeTransferStart(tbuf);

            for (int i = 0; i < fileCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (mtype, mlen) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
                if (mtype == FrameType.Cancel)
                    throw new TransferException("The peer cancelled the transfer.");
                if (mtype != FrameType.FileMeta)
                    throw new TransferException($"Expected FileMeta frame, got {mtype}.");
                var mbuf = new byte[CheckLen(mtype, mlen)];
                await _reader.ReadPayloadAsync(mbuf, ct).ConfigureAwait(false);
                var meta = SessionCodec.DecodeFileMeta(mbuf);
                totalBytes += meta.Size;

                string safeName = SanitizeRemoteName(meta.Name);
                string path = _options.PathSelector?.Invoke(destinationDirectory, safeName)
                    ?? Path.Combine(destinationDirectory, safeName);
                long resumeOffset = 0;
                if (meta.Size > 0 && _negotiatedFlags.HasFlag(CapabilityFlags.SupportsResume) && _options.EnableResume)
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                        resumeOffset = Math.Min(info.Length, meta.Size);
                }

                await _writer.WriteAsync(FrameType.ResumeInfo, SessionCodec.EncodeResumeInfo(resumeOffset), ct).ConfigureAwait(false);

                var (fileResult, received) = await ReceiveFileAsync(
                    path, meta, resumeOffset, i, fileCount, totalBytes, totalReceived, progress, ct).ConfigureAwait(false);
                totalReceived += received;
                results.Add(fileResult);

                if (fileResult.Status != AckStatus.Ok)
                    throw new TransferException($"File '{meta.Name}' failed verification: {fileResult.Message}");
            }

            var (etype, elen) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
            if (etype == FrameType.Cancel)
                throw new TransferException("The peer cancelled the transfer.");
            if (etype != FrameType.TransferEnd)
                throw new TransferException($"Expected TransferEnd frame, got {etype}.");
            if (elen > 0)
            {
                var ebuf = new byte[CheckLen(etype, elen)];
                await _reader.ReadPayloadAsync(ebuf, ct).ConfigureAwait(false);
            }

            await _writer.WriteAsync(FrameType.Ack, SessionCodec.EncodeAck(AckStatus.Ok), ct).ConfigureAwait(false);

            started.Stop();
            _state = 2;
            return new TransferResult(true, results.Count, totalReceived, started.Elapsed, results);
        }
        catch (OperationCanceledException)
        {
            await SendCancelAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not TransferException and not UnauthorizedAccessException)
        {
            await SendCancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<(FileTransferResult Result, long BytesReceived)> ReceiveFileAsync(
        string path, FileMeta meta, long resumeOffset,
        int index, int count, long totalBytes, long totalReceived,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, _negotiatedChunk));
        long received = resumeOffset;

        await using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.SequentialScan);
        if (fs.Length > meta.Size)
            fs.SetLength(meta.Size);
        if (fs.Length < resumeOffset)
            fs.SetLength(resumeOffset);

        if (resumeOffset > 0)
        {
            // Hash the already-present prefix so the final FileEnd hash covers the whole file.
            fs.Position = 0;
            int read;
            while ((read = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                hasher.AppendData(buffer, 0, read);
        }
        fs.Position = resumeOffset;

        progress?.Report(new TransferProgress(index, count, meta.Name, meta.Size, received, totalBytes, totalReceived + received));

        try
        {
            while (true)
            {
                var (type, len) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
                if (type == FrameType.Cancel)
                    throw new TransferException("The peer cancelled the transfer.");
                if (type == FrameType.Data)
                {
                    if (len < 0 || len > buffer.Length)
                        throw new TransferException($"Oversized Data frame ({len} bytes).");
                    await _reader.ReadPayloadAsync(buffer.AsMemory(0, len), ct).ConfigureAwait(false);
                    await fs.WriteAsync(buffer.AsMemory(0, len), ct).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, len);
                    received += len;
                    progress?.Report(new TransferProgress(index, count, meta.Name, meta.Size, received, totalBytes, totalReceived + received));
                }
                else if (type == FrameType.FileEnd)
                {
                    if (len != 32)
                        throw new TransferException("Malformed FileEnd frame.");
                    var hashBuf = new byte[32];
                    await _reader.ReadPayloadAsync(hashBuf, ct).ConfigureAwait(false);
                    byte[] expected = SessionCodec.DecodeFileEnd(hashBuf);
                    byte[] actual = hasher.GetHashAndReset();
                    bool match = CryptographicOperations.FixedTimeEquals(actual, expected);
                    var status = match ? AckStatus.Ok : AckStatus.HashMismatch;
                    var message = match ? null : "SHA-256 mismatch.";
                    await _writer.WriteAsync(FrameType.Ack, SessionCodec.EncodeAck(status, message), ct).ConfigureAwait(false);
                    return (new FileTransferResult(meta.Name, status, message), received - resumeOffset);
                }
                else
                {
                    throw new TransferException($"Unexpected frame {type} while receiving file data.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask<(AckStatus Status, string? Message)> ReadAckAsync(CancellationToken ct)
    {
        var (type, payload) = await ReadSmallFrameAsync(ct).ConfigureAwait(false);
        if (type != FrameType.Ack)
            throw new TransferException($"Expected Ack frame, got {type}.");
        var (status, message) = SessionCodec.DecodeAck(payload);
        return (status, message);
    }

    private async ValueTask<(FrameType Type, byte[] Payload)> ReadSmallFrameAsync(CancellationToken ct)
    {
        var (type, len) = await ReadHeaderOrThrowAsync(ct).ConfigureAwait(false);
        if (type == FrameType.Cancel)
            throw new TransferException("The peer cancelled the transfer.");
        int bounded = CheckLen(type, len);
        var payload = new byte[bounded];
        if (bounded > 0)
            await _reader.ReadPayloadAsync(payload, ct).ConfigureAwait(false);
        return (type, payload);
    }

    private async ValueTask<(FrameType Type, int Length)> ReadHeaderOrThrowAsync(CancellationToken ct)
    {
        var (type, len) = await _reader.ReadHeaderAsync(ct).ConfigureAwait(false);
        if (len < 0)
            throw new TransferException("Connection closed by peer.");
        return (type, len);
    }

    private static int CheckLen(FrameType type, int len)
    {
        if (len < 0 || len > SmallFrameLimit)
            throw new TransferException($"Oversized frame {type} ({len} bytes).");
        return len;
    }

    private CapabilityFlags ComputeLocalFlags() =>
        CapabilityFlags.SupportsSha256
        | (_options.EnableResume ? CapabilityFlags.SupportsResume : 0)
        | _options.ExtraCapabilities;

    private CapabilityFlags RequiredFlags() =>
        CapabilityFlags.SupportsSha256
        | (_options.EnableResume ? CapabilityFlags.SupportsResume : 0);

    private void EnsureCanTransfer()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("This session has already been used.");
    }

    private async ValueTask SendCancelAsync()
    {
        try
        {
            await _writer.WriteAsync(FrameType.Cancel, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
            await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private static string SanitizeRemoteName(string name)
    {
        name = name.Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            throw new InvalidDataException($"Unsafe file name from peer: '{name}'.");
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public async ValueTask DisposeAsync()
    {
        if (_state != 2)
        {
            // Try to let the peer know, but don't block disposal on a dead socket.
            await SendCancelAsync().ConfigureAwait(false);
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
