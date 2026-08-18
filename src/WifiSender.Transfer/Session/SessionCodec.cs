using System.Text;
using WifiSender.Transfer.Protocol;

namespace WifiSender.Transfer.Session;

/// <summary>Metadata for a single file within a transfer batch.</summary>
public sealed record FileMeta(string Name, long Size);

/// <summary>Codecs for the session-level frame payloads (FileMeta, ResumeInfo, FileEnd, Ack).</summary>
public static class SessionCodec
{
    // FileMeta: [nameLen:4][name:utf8][size:8]
    public static byte[] EncodeFileMeta(FileMeta meta)
    {
        if (string.IsNullOrEmpty(meta.Name) || meta.Name.Length > WireProtocol.MaxFileNameLength)
            throw new ArgumentException("Invalid file name.", nameof(meta));
        if (meta.Size < 0)
            throw new ArgumentException("Invalid file size.", nameof(meta));

        int nameBytes = Encoding.UTF8.GetByteCount(meta.Name);
        var payload = new byte[4 + nameBytes + 8];
        var span = payload.AsSpan();
        Wire.WriteInt32BE(span, nameBytes);
        Encoding.UTF8.GetBytes(meta.Name, span.Slice(4, nameBytes));
        Wire.WriteInt64BE(span.Slice(4 + nameBytes), meta.Size);
        return payload;
    }

    public static FileMeta DecodeFileMeta(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            throw new InvalidDataException("Malformed FileMeta frame.");
        int nameLen = Wire.ReadInt32BE(payload);
        if (nameLen < 0 || nameLen > WireProtocol.MaxFileNameLength || 4 + nameLen + 8 > payload.Length)
            throw new InvalidDataException("Malformed FileMeta frame.");
        string name = Encoding.UTF8.GetString(payload.Slice(4, nameLen));
        long size = Wire.ReadInt64BE(payload.Slice(4 + nameLen));
        return new FileMeta(name, size);
    }

    // ResumeInfo: [offset:8]
    public static byte[] EncodeResumeInfo(long offset)
    {
        var payload = new byte[8];
        Wire.WriteInt64BE(payload, offset);
        return payload;
    }

    public static long DecodeResumeInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 8)
            throw new InvalidDataException("Malformed ResumeInfo frame.");
        return Wire.ReadInt64BE(payload);
    }

    // TransferStart: [count:4]
    public static byte[] EncodeTransferStart(int count)
    {
        var payload = new byte[4];
        Wire.WriteInt32BE(payload, count);
        return payload;
    }

    public static int DecodeTransferStart(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 4)
            throw new InvalidDataException("Malformed TransferStart frame.");
        int count = Wire.ReadInt32BE(payload);
        if (count < 0 || count > 100_000)
            throw new InvalidDataException($"Unreasonable file count: {count}");
        return count;
    }

    // FileEnd: [hash:32]
    public static byte[] EncodeFileEnd(byte[] hash)
    {
        if (hash.Length != 32)
            throw new ArgumentException("Expected a SHA-256 hash.", nameof(hash));
        return (byte[])hash.Clone();
    }

    public static byte[] DecodeFileEnd(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 32)
            throw new InvalidDataException("Malformed FileEnd frame (expected SHA-256 hash).");
        return payload.ToArray();
    }

    // Ack: [status:1][messageLen:4][message:utf8]
    public static byte[] EncodeAck(AckStatus status, string? message = null)
    {
        message ??= string.Empty;
        if (message.Length > WireProtocol.MaxStatusMessageLength)
            message = message[..WireProtocol.MaxStatusMessageLength];
        int messageBytes = Encoding.UTF8.GetByteCount(message);
        var payload = new byte[1 + 4 + messageBytes];
        var span = payload.AsSpan();
        span[0] = (byte)status;
        Wire.WriteInt32BE(span.Slice(1), messageBytes);
        Encoding.UTF8.GetBytes(message, span.Slice(5));
        return payload;
    }

    public static (AckStatus Status, string Message) DecodeAck(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5)
            throw new InvalidDataException("Malformed Ack frame.");
        var status = (AckStatus)payload[0];
        int len = Wire.ReadInt32BE(payload.Slice(1));
        if (len < 0 || 5 + len > payload.Length)
            throw new InvalidDataException("Malformed Ack frame.");
        string message = Encoding.UTF8.GetString(payload.Slice(5, len));
        return (status, message);
    }
}
