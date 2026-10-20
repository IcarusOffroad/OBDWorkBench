using System.Net.Sockets;
namespace OBDWorkbench.Services;

public sealed class TcpObdTransport : IObdTransport
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private bool _disposed;
    private TcpObdTransport(TcpClient client, string host, int port)
    {
        _client = client; _stream = client.GetStream();
        _stream.ReadTimeout = 100; _stream.WriteTimeout = 1500;
        Description = $"Wi-Fi/TCP {host}:{port}";
    }
    public bool IsOpen => !_disposed;
    public ElmTransportKind Kind => ElmTransportKind.Tcp;
    public string Description { get; }
    public string? PortName => null;
    public static async Task<TcpObdTransport> ConnectAsync(string host, int port, int timeoutMs = 3000, CancellationToken token = default)
    {
        var client = new TcpClient { NoDelay = true, ReceiveBufferSize = 4096, SendBufferSize = 4096 };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Math.Max(250, timeoutMs));
        try { await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false); return new(client, host, port); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { client.Dispose(); throw new TimeoutException($"TCP connection to {host}:{port} timed out after {timeoutMs} ms."); }
        catch { client.Dispose(); throw; }
    }
    public void Write(byte[] bytes) { _stream.Write(bytes, 0, bytes.Length); _stream.Flush(); }
    public byte[] ReadAvailable()
    {
        try
        {
            if (!_stream.DataAvailable) return Array.Empty<byte>();
            byte[] buffer = new byte[1024]; int count = _stream.Read(buffer, 0, buffer.Length);
            if (count <= 0) throw new IOException("TCP connection closed by the ELM adapter.");
            return buffer[..count];
        }
        catch (IOException ex) { throw new InvalidOperationException("TCP communication failed: " + ex.Message, ex); }
        catch (SocketException ex) { throw new InvalidOperationException("TCP communication failed: " + ex.Message, ex); }
    }
    public void DiscardInput() { for (int n = 0; n < 16 && _stream.DataAvailable; n++) _ = ReadAvailable(); }
    public void Dispose() { if (_disposed) return; _disposed = true; _stream.Dispose(); _client.Dispose(); }
}
