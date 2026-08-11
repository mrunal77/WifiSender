using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WifiSender.FileTransfer.Interfaces;
using WifiSender.FileTransfer.Models;

namespace WifiSender.FileTransfer.Services;

public sealed class FileTransferManager : IFileTransferManager
{
    private readonly IFileStorage _storage;
    private readonly FileTransferOptions _options;
    private readonly ConcurrentDictionary<Guid, TransferState> _transfers = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _transferTokens = new();

    public FileTransferManager(
        IFileStorage storage,
        FileTransferOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<TransferResult> SendAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        var transferState = new TransferState
        {
            TransferId = request.TransferId,
            FileId = request.FileId,
            FileSize = request.FileSize,
            ChunkSize = GetEffectiveChunkSize(request.FileSize),
            FileHash = null,
            Status = TransferStatus.Running,
            CompletedChunks = Array.Empty<long>()
        };

        _transfers[transferState.TransferId] = transferState;
        _transferTokens[transferState.TransferId] = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var stopWatch = Stopwatch.StartNew();
        long totalTransferred = 0;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(request.DestinationAddress, request.DestinationPort, cancellationToken);
            using var stream = client.GetStream();
            stream.WriteTimeout = 30000;
            stream.ReadTimeout = 30000;

            await SendMetadataAsync(stream, request, cancellationToken);
            await foreach (var chunk in EnumerateChunksAsync(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SendChunkAsync(stream, chunk, cancellationToken);
                totalTransferred += chunk.Length;
                transferState.CompletedChunks = transferState.CompletedChunks.Append(chunk.ChunkIndex).ToArray();
            }

            var hash = await ComputeFileHashAsync(request.FilePath, transferState.ChunkSize, cancellationToken);
            transferState.FileHash = hash;
            stopWatch.Stop();
            transferState.Status = TransferStatus.Completed;
            return new TransferResult
            {
                TransferId = request.TransferId,
                Success = true,
                BytesTransferred = totalTransferred,
                Duration = stopWatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            transferState.Status = TransferStatus.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            transferState.Status = TransferStatus.Failed;
            return new TransferResult
            {
                TransferId = request.TransferId,
                Success = false,
                ErrorMessage = ex.Message,
                BytesTransferred = totalTransferred,
                Duration = stopWatch.Elapsed
            };
        }
        finally
        {
            _transferTokens.TryRemove(request.TransferId, out _);
        }
    }

    public Task<TransferResult> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task PauseAsync(Guid transferId, CancellationToken cancellationToken)
    {
        if (_transfers.TryGetValue(transferId, out var state))
        {
            state.Status = TransferStatus.Paused;
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(Guid transferId, CancellationToken cancellationToken)
    {
        if (_transfers.TryGetValue(transferId, out var state))
        {
            state.Status = TransferStatus.Running;
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid transferId, CancellationToken cancellationToken)
    {
        if (_transferTokens.TryRemove(transferId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        if (_transfers.TryGetValue(transferId, out var state))
        {
            state.Status = TransferStatus.Cancelled;
        }
        return Task.CompletedTask;
    }

    public Task<TransferStatus> GetStatusAsync(Guid transferId, CancellationToken cancellationToken)
    {
        if (_transfers.TryGetValue(transferId, out var state))
            return Task.FromResult(state.Status);
        return Task.FromResult(TransferStatus.Failed);
    }

    private int GetEffectiveChunkSize(long fileSize)
    {
        if (fileSize <= 4 * 1024 * 1024)
            return Math.Min(_options.ChunkSizeMB * 1024 * 1024, 1 * 1024 * 1024);
        if (fileSize <= 128 * 1024 * 1024)
            return Math.Min(_options.ChunkSizeMB * 1024 * 1024, 4 * 1024 * 1024);
        return Math.Min(_options.MaxChunkSizeMB * 1024 * 1024, 16 * 1024 * 1024);
    }

    private static async IAsyncEnumerable<FileChunk> EnumerateChunksAsync(TransferRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long chunkSize = request.FileSize <= 4 * 1024 * 1024 ? 256 * 1024 : 4 * 1024 * 1024;
        long totalChunks = (request.FileSize + chunkSize - 1) / chunkSize;
        for (long index = 0; index < totalChunks; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long offset = index * chunkSize;
            int length = (int)Math.Min(chunkSize, request.FileSize - offset);
            yield return new FileChunk(request.TransferId, request.FileId, index, offset, length, string.Empty);
        }
    }

    private static async Task SendMetadataAsync(Stream stream, TransferRequest request, CancellationToken cancellationToken)
    {
        var fileNameBytes = Encoding.UTF8.GetBytes(request.FileName);
        var fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
        var fileSizeBytes = BitConverter.GetBytes(request.FileSize);
        await stream.WriteAsync(fileNameLengthBytes.AsMemory(0, fileNameLengthBytes.Length), cancellationToken);
        await stream.WriteAsync(fileNameBytes.AsMemory(0, fileNameBytes.Length), cancellationToken);
        await stream.WriteAsync(fileSizeBytes.AsMemory(0, fileSizeBytes.Length), cancellationToken);
    }

    private static async Task SendChunkAsync(Stream stream, FileChunk chunk, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(chunk.Length);
        try
        {
            await using var fileStream = File.OpenRead(chunk.Hash);
            fileStream.Seek(chunk.Offset, SeekOrigin.Begin);
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, chunk.Length), cancellationToken);
            if (bytesRead != chunk.Length)
                throw new InvalidOperationException("Unexpected chunk length while reading file.");
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> ComputeFileHashAsync(string path, int chunkSize, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var hasher = SHA256.Create();
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)) > 0)
            {
                hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(hasher.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
