using System.Diagnostics;
using System.Security.Cryptography;
using WifiSender.Services;
using WifiSender.Transfer.Session;
using WifiSender.Transfer.Transports;

// Loopback smoke test + benchmark for the framed transfer engine over TCP.
// Usage: dotnet run -- <size-mb> <chunk-kb>
int sizeMb = args.Length > 0 ? int.Parse(args[0]) : 16;
int chunkKb = args.Length > 1 ? int.Parse(args[1]) : 1024;

string root = Path.Combine(Path.GetTempPath(), "wifisender-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string source = Path.Combine(root, "payload.bin");
string destDir = Path.Combine(root, "received");
Directory.CreateDirectory(destDir);

byte[] payload = RandomNumberGenerator.GetBytes(sizeMb * 1024 * 1024);
await File.WriteAllBytesAsync(source, payload);

var options = new SessionOptions { ChunkSize = chunkKb * 1024, EnableResume = true };
var transport = new TcpFileTransferTransport();

Console.WriteLine($"payload={sizeMb} MiB, chunk={chunkKb} KiB, sha256={SHA256.HashData(payload)[..8]:x2}…");

int failures = 0;
async Task Check(string name, Func<Task> fn)
{
    try
    {
        await fn();
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"  FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

await Check("transfer", () => TransferAsync(source, destDir, "full.bin", options, null));
string received = Path.Combine(destDir, "full.bin");
await Check("bytes identical", () => Task.FromResult(AssertBytes(payload, received)));

// Resume: pre-create a partial prefix, retransfer, verify assembled file hashes correctly.
string partial = Path.Combine(destDir, "resumed.bin");
await File.WriteAllBytesAsync(partial, payload.AsSpan(0, payload.Length / 2).ToArray());
await Check("resume", () => TransferAsync(source, destDir, "resumed.bin", options, null));
await Check("resumed bytes identical", () => Task.FromResult(AssertBytes(payload, partial)));

// Corruption: pre-existing wrong content must be rejected by the whole-file hash.
string corrupt = Path.Combine(destDir, "corrupt.bin");
await File.WriteAllBytesAsync(corrupt, new byte[payload.Length]);
await Check("hash mismatch rejected", async () =>
{
    try
    {
        await TransferAsync(source, destDir, "corrupt.bin", options, null);
        throw new Exception("expected rejection, but transfer succeeded");
    }
    catch (TransferException)
    {
        // expected: peer rejected the file
    }
});

// Pairing: wrong secret on the client must be refused.
await Check("pairing rejects wrong secret", async () =>
{
    var gated = options with { PairingSecret = "hunter2" };
    try
    {
        await TransferAsync(source, destDir, "nope.bin", gated, "wrong-secret");
        throw new Exception("expected UnauthorizedAccessException");
    }
    catch (UnauthorizedAccessException)
    {
    }
});

// Multi-file + progress reporting sanity.
await Check("multi-file progress", async () =>
{
    var progress = new Progress<TransferProgress>(p => { });
    await TransferAsync(source, destDir, "m1.bin", options, null, progress);
});

// App service layer: FileTransferService send/receive over the engine, including
// conflict rename (" (1)") and progress events.
await Check("service loopback", async () =>
{
    var receiver = new FileTransferService();
    var recvTask = receiver.StartReceivingAsync(destDir, 0);
    try
    {
        int port = 0;
        for (int i = 0; i < 100 && port == 0; i++)
        {
            await Task.Delay(50);
            port = receiver.ListeningPort ?? 0;
        }
        if (port == 0)
            throw new Exception("service receiver did not bind");

        var sender = new FileTransferService();
        int progressEvents = 0;
        sender.TransferProgress += (_, _) => progressEvents++;

        var device = new DiscoveredDevice { IpAddress = "127.0.0.1", Port = port.ToString(), IsSelected = true };
        await sender.SendFilesAsync(new[] { source }, new[] { device });

        // Second send of the same file must not overwrite (conflict rename -> "payload (1).bin").
        await sender.SendFilesAsync(new[] { source }, new[] { device });

        string first = Path.Combine(destDir, "payload.bin");
        string renamed = Path.Combine(destDir, "payload (1).bin");
        if (!File.Exists(first) || !File.Exists(renamed))
            throw new Exception("conflict rename did not produce two files");
        await AssertBytes(payload, first);
        await AssertBytes(payload, renamed);
        if (progressEvents == 0)
            throw new Exception("no progress events raised");
    }
    finally
    {
        await receiver.StopReceivingAsync();
        try { await recvTask; } catch (OperationCanceledException) { }
    }
});

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

static async Task AssertBytes(byte[] expected, string path)
{
    byte[] actual = await File.ReadAllBytesAsync(path);
    if (expected.Length != actual.Length)
        throw new Exception($"length mismatch: expected {expected.Length}, got {actual.Length}");
    if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        throw new Exception("content mismatch");
}

static async Task TransferAsync(string source, string destDir, string remoteName, SessionOptions serverOptions, string? clientSecret = null, IProgress<TransferProgress>? progress = null)
{
    var listener = await new TcpFileTransferTransport().ListenAsync(0, default);
    try
    {
        var serverTask = Task.Run(async () =>
        {
            var stream = await listener.AcceptAsync(default) ?? throw new Exception("no connection");
            await using var session = await FileTransferSession.AcceptAsync(stream, serverOptions, default);
            return await session.ReceiveAsync(destDir, progress, default);
        });

        await using var client = await new TcpFileTransferTransport().ConnectAsync("127.0.0.1", listener.Port, default);
        var clientOptions = serverOptions with { PairingSecret = clientSecret };
        await using var session = await FileTransferSession.ConnectAsync(client, clientOptions, default);
        TransferResult result;
        Exception? clientError = null;
        try
        {
            result = await session.SendAsync(new[] { new FileSource(source, remoteName) }, progress, default);
        }
        catch (Exception ex)
        {
            clientError = ex;
            result = null!;
        }

        try
        {
            await serverTask;
        }
        catch when (clientError is not null)
        {
            // Peer-side failures mirror client-side failures; surface only the client's.
        }

        if (clientError is not null)
            throw clientError;
        if (!result.Success)
            throw new Exception("client reported failure");

        double mbps = result.BytesTransferred / (result.Duration.TotalSeconds * 1024 * 1024);
        Console.WriteLine($"       ({result.BytesTransferred:N0} bytes in {result.Duration.TotalSeconds:N2}s = {mbps:N1} MiB/s)");
    }
    finally
    {
        await listener.DisposeAsync();
    }
}
