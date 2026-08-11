using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WifiSender.Services;

public sealed class FileTransferService : IFileTransferService, IDisposable
{
    private const int BufferSize = 1048576;
    private const int NetworkTimeoutMs = 30000;

    public event EventHandler<FileTransferProgress>? TransferProgress;
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferErrorEventArgs>? TransferError;
    public event EventHandler<string>? ConnectionStatusChanged;

    private TcpListener? _server;
    private CancellationTokenSource? _cts;
    private long _totalBytes;
    private long _completedBytes;
    private readonly object _progressLock = new();

    public async Task SendFilesAsync(IEnumerable<string> filePaths, IEnumerable<DiscoveredDevice> recipients, CancellationToken cancellationToken = default)
    {
        var devices = recipients.Where(d => d.IsSelected).ToList();
        if (devices.Count == 0)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs("Select one or more receiver devices", false));
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _totalBytes = 0;
        _completedBytes = 0;

        var fileEntries = new List<(string Path, string Name, long Size)>();
        foreach (var f in filePaths)
        {
            if (File.Exists(f))
            {
                var info = new FileInfo(f);
                fileEntries.Add((f, info.Name, info.Length));
                _totalBytes += info.Length;
            }
        }

        if (fileEntries.Count == 0)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs("No valid files to send", false));
            return;
        }

        var sendTasks = devices.Select(async (device, idx) =>
        {
            try
            {
                await SendToRecipientAsync(device.IpAddress, int.Parse(device.Port), idx, devices.Count, fileEntries, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TransferError?.Invoke(this, new TransferErrorEventArgs($"{device.IpAddress}:{device.Port}: {ex.Message}", false));
            }
        });

        try
        {
            await Task.WhenAll(sendTasks);
            TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(true, null, _completedBytes));
        }
        catch (OperationCanceledException)
        {
            TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(false, "Sending cancelled", _completedBytes));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async Task StartReceivingAsync(string downloadFolder, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadFolder))
        {
            downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadFolder))
                Directory.CreateDirectory(downloadFolder);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        _server = new TcpListener(IPAddress.Any, port);
        _server.Start();

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _server.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, downloadFolder, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs(ex.Message, true));
        }
        finally
        {
            _server?.Stop();
            _server = null;
        }
    }

    public Task StopReceivingAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _server?.Stop();
        return Task.CompletedTask;
    }

    public async Task TestConnectionAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ipAddress, port, cancellationToken).AsTask();
            var timeoutTask = Task.Delay(3000, cancellationToken);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
            {
                ConnectionStatusChanged?.Invoke(this, $"Connected to {ipAddress}:{port}");
            }
            else
            {
                ConnectionStatusChanged?.Invoke(this, $"Timeout connecting to {ipAddress}:{port}");
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusChanged?.Invoke(this, $"Failed to connect to {ipAddress}:{port}: {ex.Message}");
        }
    }

    private async Task SendToRecipientAsync(string recipientIp, int port, int recipientIndex, int recipientCount, List<(string Path, string Name, long Size)> fileEntries, CancellationToken ct)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.LingerState = new LingerOption(true, 30);
        client.SendBufferSize = BufferSize;
        client.ReceiveBufferSize = BufferSize;
        client.Client.SendTimeout = NetworkTimeoutMs;

        await client.ConnectAsync(recipientIp, port, ct);
        await using var stream = client.GetStream();

        foreach (var (filePath, _, fileSize) in fileEntries)
        {
            ct.ThrowIfCancellationRequested();
            string sendName = filePath;
            byte[] fileNameBytes = Encoding.UTF8.GetBytes(sendName);
            byte[] header = new byte[4 + fileNameBytes.Length + 8];
            BitConverter.GetBytes(fileNameBytes.Length).CopyTo(header, 0);
            fileNameBytes.CopyTo(header, 4);
            BitConverter.GetBytes(fileSize).CopyTo(header, 4 + fileNameBytes.Length);

            await stream.WriteAsync(header, ct);

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[BufferSize];
            long readSoFar = 0;
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                readSoFar += bytesRead;
                ReportSendProgress(bytesRead, readSoFar, fileSize, recipientIndex, recipientCount, sendName);
            }
        }

        byte[] endMarker = BitConverter.GetBytes((int)0);
        await stream.WriteAsync(endMarker, ct);
        await stream.FlushAsync(ct);

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            byte[] ackBuf = new byte[1];
            int ackRead = await ReadExactAsync(stream, ackBuf, readCts.Token);
            if (ackRead == 0 || ackBuf[0] != 0xFF)
                throw new IOException("Receiver did not acknowledge transfer");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for receiver acknowledgment");
        }
    }

    private async Task HandleClientAsync(TcpClient client, string downloadFolder, CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveBufferSize = BufferSize;
            client.SendBufferSize = BufferSize;
            client.Client.ReceiveTimeout = NetworkTimeoutMs;

            using var stream = client.GetStream();
            long totalReceived = 0;

            while (true)
            {
                byte[] lengthBuffer = new byte[4];
                int read = await ReadExactAsync(stream, lengthBuffer, ct);
                if (read == 0) break;

                int fileNameLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (fileNameLength == 0)
                {
                    try { await stream.WriteAsync(new byte[] { 0xFF }, ct); }
                    catch { }
                    break;
                }
                if (fileNameLength < 0 || fileNameLength > 4096)
                    throw new InvalidOperationException($"Invalid file name length: {fileNameLength}");

                byte[] fileNameBuffer = new byte[fileNameLength];
                int fileNameRead = await ReadExactAsync(stream, fileNameBuffer, ct);
                if (fileNameRead != fileNameLength)
                    throw new IOException("Connection closed while reading file name");

                string fileName = Encoding.UTF8.GetString(fileNameBuffer);
                string[] pathParts = fileName.Split(new[] { '/', '\\' }, StringSplitOptions.None);
                for (int i = 0; i < pathParts.Length; i++)
                {
                    foreach (char c in Path.GetInvalidFileNameChars())
                        pathParts[i] = pathParts[i].Replace(c, '_');
                }
                fileName = string.Join(Path.DirectorySeparatorChar, pathParts);

                byte[] sizeBuffer = new byte[8];
                int sizeRead = await ReadExactAsync(stream, sizeBuffer, ct);
                if (sizeRead != 8)
                    throw new IOException("Connection closed while reading file size");

                long fileSize = BitConverter.ToInt64(sizeBuffer, 0);
                if (fileSize < 0)
                    throw new InvalidOperationException($"Invalid file size: {fileSize}");

                string savePath = Path.Combine(downloadFolder, fileName);
                string? saveDir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                string originalPath = savePath;
                int counter = 1;
                while (File.Exists(savePath))
                {
                    string name = Path.GetFileNameWithoutExtension(originalPath);
                    string ext = Path.GetExtension(originalPath);
                    string newName = $"{name} ({counter}){ext}";
                    savePath = Path.Combine(saveDir ?? downloadFolder, newName);
                    counter++;
                }

                await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                byte[] buffer = new byte[BufferSize];
                long received = 0;

                while (received < fileSize)
                {
                    int toRead = (int)Math.Min(buffer.Length, fileSize - received);
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (bytesRead == 0) throw new IOException("Connection closed during transfer");

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    received += bytesRead;
                    totalReceived += bytesRead;

                    double fileProgress = (received * 100.0) / fileSize;
                    TransferProgress?.Invoke(this, new FileTransferProgress(
                        fileName, totalReceived, fileSize, fileProgress, 0, true));
                }

                TransferProgress?.Invoke(this, new FileTransferProgress(fileName, totalReceived, fileSize, 100, 0, true));
            }

            TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(true, null, totalReceived));
        }
        catch (OperationCanceledException)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs("Receive cancelled", true));
        }
        catch (Exception ex)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs(ex.Message, true));
        }
        finally
        {
            client.Close();
        }
    }

    private void ReportSendProgress(long sentDelta, long filePos, long fileSize, int recipientIdx, int recipientCount, string fileName)
    {
        long completed;
        lock (_progressLock)
        {
            _completedBytes += sentDelta;
            completed = _completedBytes;
        }
        double pct = Math.Min(completed * 100.0 / (_totalBytes * recipientCount), 100.0);
        TransferProgress?.Invoke(this, new FileTransferProgress(
            fileName, completed, _totalBytes * recipientCount, pct, 0, false));
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct = default)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _server?.Stop();
        GC.SuppressFinalize(this);
    }
}
