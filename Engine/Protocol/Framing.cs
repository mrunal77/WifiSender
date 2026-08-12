using System.Buffers;
using System.Buffers.Binary;
using System.Net;

namespace WifiSender.Transfer.Protocol;

/// <summary>Binary helpers for writing/reading the compact wire format.</summary>
public static class Wire
{
    public static void WriteUInt32BE(Span<byte> dest, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(dest, value);

    public static void WriteInt32BE(Span<byte> dest, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(dest, value);

    public static void WriteInt64BE(Span<byte> dest, long value) =>
        BinaryPrimitives.WriteInt64BigEndian(dest, value);

    public static uint ReadUInt32BE(ReadOnlySpan<byte> src) =>
        BinaryPrimitives.ReadUInt32BigEndian(src);

    public static int ReadInt32BE(ReadOnlySpan<byte> src) =>
        BinaryPrimitives.ReadInt32BigEndian(src);

    public static long ReadInt64BE(ReadOnlySpan<byte> src) =>
        BinaryPrimitives.ReadInt64BigEndian(src);

    public static Guid ReadGuidBE(ReadOnlySpan<byte> src) => new(src, bigEndian: true);

    /// <summary>Reads exactly <paramref name="buffer"/> bytes from the stream.</summary>
    /// <returns>The number of bytes read; equals <c>buffer.Length</c> unless the stream ends early.</returns>
    public static async ValueTask<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(total), ct).ConfigureAwait(false);
            if (read == 0)
                return total;
            total += read;
        }
        return total;
    }

    /// <summary>Reads exactly <paramref name="buffer"/> bytes or throws on an early stream end.</summary>
    public static async ValueTask ReadExactOrThrowAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int read = await ReadExactAsync(stream, buffer, ct).ConfigureAwait(false);
        if (read != buffer.Length)
            throw new EndOfStreamException("The remote end closed the connection prematurely.");
    }
}

/// <summary>Writes length-prefixed frames to a duplex stream.</summary>
public sealed class FrameWriter
{
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[5];

    public FrameWriter(Stream stream) => _stream = stream;

    public Stream Stream => _stream;

    public async ValueTask WriteAsync(FrameType type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        _header[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(_header.AsSpan(1, 4), payload.Length);
        await _stream.WriteAsync(_header.AsMemory(), ct).ConfigureAwait(false);
        if (payload.Length > 0)
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    /// <summary>Writes a <see cref="FrameType.Data"/> header followed by a payload already staged in memory.</summary>
    public async ValueTask WriteDataAsync(int payloadLength, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await WriteAsync(FrameType.Data, payload.Slice(0, payloadLength), ct).ConfigureAwait(false);
    }

    public async ValueTask WriteHeaderThenPayloadAsync(FrameType type, int payloadLength, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        _header[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(_header.AsSpan(1, 4), payloadLength);
        await _stream.WriteAsync(_header.AsMemory(), ct).ConfigureAwait(false);
        if (payloadLength > 0)
            await _stream.WriteAsync(payload.Slice(0, payloadLength), ct).ConfigureAwait(false);
    }

    public ValueTask FlushAsync(CancellationToken ct) => new(_stream.FlushAsync(ct));
}

/// <summary>Reads length-prefixed frames from a duplex stream.</summary>
public sealed class FrameReader
{
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[5];

    public FrameReader(Stream stream) => _stream = stream;

    /// <summary>Reads the frame header. Throws <see cref="EndOfStreamException"/> on a clean close.</summary>
    public async ValueTask<(FrameType Type, int Length)> ReadHeaderAsync(CancellationToken ct)
    {
        int read = await Wire.ReadExactAsync(_stream, _header, ct).ConfigureAwait(false);
        if (read == 0)
            return (FrameType.Ping, -1); // closed; caller treats Length &lt; 0 as EOF
        if (read != _header.Length)
            return (FrameType.Ping, -1);
        return ((FrameType)_header[0], BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(1, 4)));
    }

    public ValueTask ReadPayloadAsync(Memory<byte> destination, CancellationToken ct) =>
        Wire.ReadExactOrThrowAsync(_stream, destination, ct);

    /// <summary>Reads a frame payload into a pooled buffer. Caller must return it.</summary>
    public async ValueTask<(byte[] Buffer, int Length)> ReadPayloadOwnedAsync(int length, CancellationToken ct)
    {
        if (length < 0 || length > WireProtocol.MaxFramePayload)
            throw new InvalidDataException($"Invalid frame payload length: {length}");

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await Wire.ReadExactOrThrowAsync(_stream, buffer.AsMemory(0, length), ct).ConfigureAwait(false);
            return (buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}

/// <summary>Capability negotiation payload. Compact and fixed-shape.</summary>
public static class CapabilityCodec
{
    public static byte[] Encode(CapabilityFlags flags, int maxChunkBytes, HashAlgorithmId hashAlgo, string? pairingSecret)
    {
        byte[] payload = new byte[4 + 4 + 1 + 16 + 32];
        var span = payload.AsSpan();
        Wire.WriteUInt32BE(span, (uint)flags);
        Wire.WriteInt32BE(span.Slice(4), maxChunkBytes);
        span[8] = (byte)hashAlgo;

        // Nonce + HMAC for optional pairing.
        if (string.IsNullOrEmpty(pairingSecret))
            return payload;

        byte[] nonce = new byte[16];
        Random.Shared.NextBytes(nonce);
        nonce.CopyTo(span.Slice(9, 16));

        byte[] hmac = ComputeHmac(pairingSecret!, span.Slice(0, 9 + 16));
        hmac.CopyTo(span.Slice(25, 32));
        return payload;
    }

    public static (CapabilityFlags Flags, int MaxChunkBytes, HashAlgorithmId HashAlgo, bool Authorized) Decode(byte[] payload, string? pairingSecret)
    {
        if (payload.Length < 4 + 4 + 1)
            return (CapabilityFlags.None, 0, HashAlgorithmId.None, false);

        var span = payload.AsSpan();
        var flags = (CapabilityFlags)Wire.ReadUInt32BE(span);
        int maxChunk = Wire.ReadInt32BE(span.Slice(4));
        var hashAlgo = (HashAlgorithmId)span[8];

        bool authorized = true;
        if (!string.IsNullOrEmpty(pairingSecret))
        {
            if (payload.Length < 4 + 4 + 1 + 48)
                return (CapabilityFlags.None, 0, HashAlgorithmId.None, false);
            byte[] expected = ComputeHmac(pairingSecret, span.Slice(0, 9 + 16));
            authorized = payload.AsSpan(25, 32).SequenceEqual(expected);
        }

        return (flags, maxChunk, hashAlgo, authorized);
    }

    private static byte[] ComputeHmac(string secret, ReadOnlySpan<byte> data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(data.ToArray());
    }
}
