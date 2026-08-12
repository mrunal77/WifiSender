using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WifiSender.Transfer.Session;
using WifiSender.Transfer.Transports;

namespace WifiSender.Services;

/// <summary>
/// App-facing file transfer service built on the WifiSender.Transfer engine.
/// Handles multi-recipient sends, the receiver loop, and progress/speed reporting.
/// </summary>
public sealed class FileTransferService : IFileTransferService, IDisposable
{
    private const int PortListenBacklog = 16;

    public event EventHandler<FileTransferProgress>? TransferProgress;
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferErrorEventArgs>? TransferError;
    public event EventHandler<string>? ConnectionStatusChanged;

    private readonly TcpFileTransferTransport _transport = new();
    private CancellationTokenSource? _cts;
    private long _totalBytes;
    private long _completedBytes;
    private long _lastReportedBytes;
    private long _lastReportedTicks;
    private double _smoothedSpeed;
    private readonly object _lock = new();

    /// <summary>The port the receiver is bound to while <see cref="StartReceivingAsync"/> is active.</summary>
    public int? ListeningPort { get; private set; }

    public async Task SendFilesAsync(IEnumerable<string> filePaths, IEnumerable<DiscoveredDevice> recipients, CancellationToken cancellationToken = default)
    {
        var devices = recipients.Where(d => d.IsSelected).ToList();
        if (devices.Count == 0)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs("Select one or more receiver devices", false));
            return;
        }

        var fileEntries = new List<FileSource>();
        long totalBytes = 0;
        foreach (var f in filePaths)
        {
            var info = new FileInfo(f);
            if (!info.Exists) continue;
            fileEntries.Add(new FileSource(f, info.Name));
            totalBytes += info.Length;
        }

        if (fileEntries.Count == 0)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs("No valid files to send", false));
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        lock (_lock)
        {
            _totalBytes = totalBytes * devices.Count;
            _completedBytes = 0;
            _lastReportedBytes = 0;
            _lastReportedTicks = Stopwatch.GetTimestamp();
            _smoothedSpeed = 0;
        }

        var sendTasks = devices.Select(async device =>
        {
            var tracker = new ProgressTracker();
            var progress = new Progress<TransferProgress>(p =>
            {
                lock (_lock)
                {
                    _completedBytes += tracker.Add(p.TotalBytesTransferred);
                    ReportProgress(p.FileName, _completedBytes, _totalBytes, isReceiving: false);
                }
            });

            try
            {
                var result = await SendToRecipientAsync(device.IpAddress, int.Parse(device.Port), fileEntries, progress, token);
                lock (_lock)
                {
                    _completedBytes += tracker.Add(result.BytesTransferred);
                    ReportProgress(string.Empty, _completedBytes, _totalBytes, isReceiving: false);
                }
                return (Device: device, Success: true, Bytes: result.BytesTransferred);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TransferError?.Invoke(this, new TransferErrorEventArgs($"{device.IpAddress}:{device.Port}: {ex.Message}", false));
                return (Device: device, Success: false, Bytes: 0L);
            }
        });

        try
        {
            var outcomes = await Task.WhenAll(sendTasks);
            bool allOk = outcomes.All(o => o.Success);
            TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(allOk, allOk ? null : "One or more recipients failed", _completedBytes));
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

    private async Task<TransferResult> SendToRecipientAsync(
        string ipAddress, int port, IReadOnlyList<FileSource> files,
        IProgress<TransferProgress> progress, CancellationToken ct)
    {
        await using var stream = await _transport.ConnectAsync(ipAddress, port, ct);
        await using var session = await FileTransferSession.ConnectAsync(stream, CreateSenderOptions(), ct);
        return await session.SendAsync(files, progress, ct);
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

        await using var listener = await _transport.ListenAsync(port, token);
        ListeningPort = listener.Port;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var stream = await listener.AcceptAsync(token);
                if (stream == null)
                    break;
                _ = HandleClientAsync(stream, downloadFolder, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TransferError?.Invoke(this, new TransferErrorEventArgs(ex.Message, true));
        }
        finally
        {
            ListeningPort = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public Task StopReceivingAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task TestConnectionAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectTask = _transport.ConnectAsync(ipAddress, port, cancellationToken);
            var timeoutTask = Task.Delay(3000, cancellationToken);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
            {
                var stream = await connectTask;
                await stream.DisposeAsync();
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

    private async Task HandleClientAsync(ITransportStream stream, string downloadFolder, CancellationToken ct)
    {
        try
        {
            await using var session = await FileTransferSession.AcceptAsync(stream, CreateReceiverOptions(downloadFolder), ct);
            var progress = new Progress<TransferProgress>(p =>
            {
                double pct = p.FileLength > 0 ? Math.Min(p.FileBytesTransferred * 100.0 / p.FileLength, 100.0) : 100;
                ReportProgress(p.FileName, p.FileBytesTransferred, p.FileLength, isReceiving: true, pctOverride: pct);
            });
            var result = await session.ReceiveAsync(downloadFolder, progress, ct);
            TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(true, null, result.BytesTransferred));
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
            await stream.DisposeAsync();
        }
    }

    private static SessionOptions CreateSenderOptions() => new()
    {
        ChunkSize = 1 << 20,
        EnableResume = false,
    };

    private static SessionOptions CreateReceiverOptions(string downloadFolder) => new()
    {
        ChunkSize = 1 << 20,
        EnableResume = false,
        PathSelector = (_, name) => ResolveDestinationPath(downloadFolder, name),
    };

    /// <summary>Maps a sanitized remote name to a destination path, renaming on conflict like the legacy receiver.</summary>
    private static string ResolveDestinationPath(string downloadFolder, string name)
    {
        string path = Path.Combine(downloadFolder, name);
        if (!File.Exists(path))
            return path;

        string nameWithoutExt = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        int counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(downloadFolder, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        }
        return path;
    }

    private void ReportProgress(string fileName, long bytesTransferred, long totalBytes, bool isReceiving, double? pctOverride = null)
    {
        double speed = 0;
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            long deltaBytes = bytesTransferred - _lastReportedBytes;
            long deltaTicks = now - _lastReportedTicks;
            if (deltaTicks > 0)
            {
                double inst = deltaBytes * (double)Stopwatch.Frequency / deltaTicks;
                _smoothedSpeed = _smoothedSpeed == 0 ? inst : _smoothedSpeed * 0.7 + inst * 0.3;
                speed = _smoothedSpeed;
            }
            _lastReportedBytes = bytesTransferred;
            _lastReportedTicks = now;
        }

        double pct = pctOverride ?? (totalBytes > 0 ? Math.Min(bytesTransferred * 100.0 / totalBytes, 100.0) : 100);
        TransferProgress?.Invoke(this, new FileTransferProgress(
            fileName, bytesTransferred, totalBytes, pct, speed, isReceiving));
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Tracks cumulative bytes for a single recipient, yielding only the delta since the last report.</summary>
    private sealed class ProgressTracker
    {
        private long _last;

        public long Add(long cumulative)
        {
            if (cumulative <= _last)
                return 0;
            long delta = cumulative - _last;
            _last = cumulative;
            return delta;
        }
    }
}
