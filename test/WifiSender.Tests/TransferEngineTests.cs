using System;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using WifiSender.Transfer.Session;
using WifiSender.Transfer.Transports;

namespace WifiSender.Tests;

public class TransferEngineTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceFile;
    private readonly byte[] _payload;

    public TransferEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wifisender-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _sourceFile = Path.Combine(_root, "payload.bin");
        var rnd = new Random(42);
        _payload = new byte[4 * 1024 * 1024];
        rnd.NextBytes(_payload);
        File.WriteAllBytes(_sourceFile, _payload);
    }

    [Fact]
    public async Task Transfer_and_verify_file_content()
    {
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destDir);

        var transport = new TcpFileTransferTransport();
        var listener = await transport.ListenAsync(0, default);
        int port = listener.Port;

        var receiveTask = Task.Run(async () =>
        {
            await using var stream = await listener.AcceptAsync(default);
            if (stream == null) return;
            await using var session = await FileTransferSession.AcceptAsync(stream, new SessionOptions { ChunkSize = 256 * 1024, EnableResume = true, PathSelector = (_, name) => Path.Combine(destDir, name) }, default);
            var result = await session.ReceiveAsync(destDir, null, default);
            result.BytesTransferred.ShouldBe(_payload.Length);
        });

        await Task.Delay(50);
        await using var clientStream = await transport.ConnectAsync("127.0.0.1", port, default);
        await using var session = await FileTransferSession.ConnectAsync(clientStream, new SessionOptions { ChunkSize = 256 * 1024, EnableResume = true }, default);
        var sendResult = await session.SendAsync(new[] { new WifiSender.Transfer.Session.FileSource(_sourceFile, "test.bin") }, null, default);
        sendResult.BytesTransferred.ShouldBe(_payload.Length);

        await receiveTask;
        var receivedPath = Path.Combine(destDir, "test.bin");
        File.Exists(receivedPath).ShouldBeTrue();
        var receivedBytes = await File.ReadAllBytesAsync(receivedPath);
        receivedBytes.ShouldBe(_payload);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
