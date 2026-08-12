using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WifiSender.Transfer.Transports;

/// <summary>
/// QUIC transport backed by <see cref="System.Net.Quic"/> (MsQuic). Provides TLS 1.3,
/// per-connection multiplexing and connection migration. Availability depends on the
/// platform shipping or bundling libmsquic.
/// </summary>
public sealed class QuicFileTransferTransport : IFileTransferTransport
{
    public const string Alpn = "wifisender";

    private readonly X509Certificate2 _certificate;

    public QuicFileTransferTransport()
    {
        _certificate = CreateSelfSignedCertificate();
    }

    public string Name => "quic";

    public bool IsAvailable => QuicConnection.IsSupported;

    public async Task<ITransportStream> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse(host), port),
            DefaultStreamErrorCode = 0x01,
            DefaultCloseErrorCode = 0x10,
            IdleTimeout = TimeSpan.FromMinutes(10),
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
                // Certificate is self-signed and generated on the fly; pairing uses the app
                // level HMAC handshake rather than PKI. Validation is intentionally permissive.
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
                TargetHost = host,
            },
        }, ct).ConfigureAwait(false);

        var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
        return new QuicTransportStream(connection, stream);
    }

    public async Task<ITransportListener> ListenAsync(int port, CancellationToken ct)
    {
        var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Any, port),
            ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
            ConnectionOptionsCallback = (connection, hello, ct) =>
            {
                var options = new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
                    },
                    DefaultStreamErrorCode = 0x01,
                    DefaultCloseErrorCode = 0x10,
                    MaxInboundBidirectionalStreams = 8,
                    IdleTimeout = TimeSpan.FromMinutes(10),
                };
                return ValueTask.FromResult(options);
            },
        }, ct).ConfigureAwait(false);

        return new QuicTransportListener(listener, _certificate);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=WifiSender", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    }

    private sealed class QuicTransportStream : ITransportStream
    {
        private readonly QuicConnection _connection;
        private readonly QuicStream _stream;
        private bool _disposed;

        public QuicTransportStream(QuicConnection connection, QuicStream stream)
        {
            _connection = connection;
            _stream = stream;
        }

        public Stream Stream => _stream;

        public string TransportName => "quic";

        public void Abort()
        {
            try
            {
                _stream.Abort(QuicAbortDirection.Both, errorCode: 0x02);
            }
            catch
            {
                // already closed
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                // Flush buffered writes and signal the end of the write side.
                await _stream.WriteAsync(ReadOnlyMemory<byte>.Empty, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore; still attempt to close the connection below
            }
            finally
            {
                _stream.Dispose();
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class QuicTransportListener : ITransportListener
    {
        private readonly QuicListener _listener;
        private readonly X509Certificate2 _certificate;
        private bool _disposed;

        public QuicTransportListener(QuicListener listener, X509Certificate2 certificate)
        {
            _listener = listener;
            _certificate = certificate;
        }

        public int Port => ((IPEndPoint)_listener.LocalEndPoint).Port;

        public async Task<ITransportStream?> AcceptAsync(CancellationToken ct)
        {
            var connection = await _listener.AcceptConnectionAsync(ct).ConfigureAwait(false);
            var stream = await connection.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
            return new QuicTransportStream(connection, stream);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            await _listener.DisposeAsync().ConfigureAwait(false);
            _certificate.Dispose();
        }
    }
}
