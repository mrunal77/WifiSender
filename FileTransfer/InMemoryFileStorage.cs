using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WifiSender.FileTransfer.Interfaces;

namespace WifiSender.FileTransfer;

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly string _baseDirectory;

    public InMemoryFileStorage()
    {
        _baseDirectory = Path.Combine(AppContext.BaseDirectory, "transfer_storage");
        Directory.CreateDirectory(_baseDirectory);
    }

    public Task<Stream> OpenReadAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = SafePath(fileId);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true));
    }

    public Task<Stream> CreateWriteAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = SafePath(fileId);
        var directory = Path.GetDirectoryName(path);
        if (directory != null)
            Directory.CreateDirectory(directory);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true));
    }

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = SafePath(fileId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string> ResolveFilePathAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = SafePath(fileId);
        return Task.FromResult(path);
    }

    private string SafePath(string fileId)
    {
        var safeId = string.Join("_", fileId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeId))
            safeId = fileId.GetHashCode().ToString("x8");

        return Path.Combine(_baseDirectory, safeId + ".dat");
    }
}
