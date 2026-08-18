using System.Net;
using System.Net.Sockets;

namespace WifiSender.Transfer.Transports;

/// <summary>
/// Kernel TCP transport. Tuned with NoDelay and large socket buffers; no TLS (transport
/// encryption is optional via <see cref="QuicFileTransferTransport"/> or a pairing secret).
/// </summary>
public sealed class TcpFileTransferTransport : IFileTransferTransport
{
    private readonly int _socketBufferSize;

    public TcpFileTransferTransport(int socketBufferSize = 1 * 1024 * 1024)
    {
        _socketBufferSize = socketBufferSize;
    }

    public string Name => "tcp";

    public bool IsAvailable => true;

    public async Task<ITransportStream> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            client.NoDelay = true;
            client.SendBufferSize = _socketBufferSize;
            client.ReceiveBufferSize = _socketBufferSize;
            client.LingerState = new LingerOption(true, 30);

            await client.ConnectAsync(IPAddress.Parse(host), port, ct).ConfigureAwait(false);
            return new TcpTransportStream(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<ITransportListener> ListenAsync(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _socketBufferSize);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, _socketBufferSize);
        listener.Start(backlog: 16);
        return Task.FromResult<ITransportListener>(new TcpTransportListener(listener, _socketBufferSize));
    }

    private sealed class TcpTransportStream : ITransportStream
    {
        private readonly TcpClient _client;
        private bool _disposed;

        public TcpTransportStream(TcpClient client)
        {
            _client = client;
            client.NoDelay = true;
            client.SendBufferSize = 1 * 1024 * 1024;
            client.ReceiveBufferSize = 1 * 1024 * 1024;
        }

        public Stream Stream => _client.GetStream();

        public string TransportName => "tcp";

        public void Abort()
        {
            _client.Client?.LingerState = new LingerOption(true, 0);
            _client.Close();
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _client.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TcpTransportListener : ITransportListener
    {
        private readonly TcpListener _listener;
        private readonly int _socketBufferSize;

        public TcpTransportListener(TcpListener listener, int socketBufferSize)
        {
            _listener = listener;
            _socketBufferSize = socketBufferSize;
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<ITransportStream?> AcceptAsync(CancellationToken ct)
        {
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            client.NoDelay = true;
            client.SendBufferSize = _socketBufferSize;
            client.ReceiveBufferSize = _socketBufferSize;
            return new TcpTransportStream(client);
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
