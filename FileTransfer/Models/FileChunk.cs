using System;

namespace WifiSender.FileTransfer.Models;

public sealed record FileChunk(
    Guid TransferId,
    Guid FileId,
    long ChunkIndex,
    long Offset,
    int Length,
    string Hash);
