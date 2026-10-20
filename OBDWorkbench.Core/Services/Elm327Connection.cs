using System.Diagnostics;
using System.Text;
namespace OBDWorkbench.Services;

public enum ElmTransportKind { None, Serial, Tcp, Bluetooth, Usb }
public readonly record struct ElmCommandResponse(string Response, double FirstByteMs, double TotalMs)
{
    public double TailMs => Math.Max(0, TotalMs - FirstByteMs);
}

/// <summary>ELM command framing, prompt detection, timing and monitor mode, independent of device APIs.</summary>
public sealed class Elm327Connection : IDisposable
{
    private readonly Func<string, int, IObdTransport>? _serialFactory;
    private IObdTransport? _transport;
    private readonly object _sync = new();
    public Elm327Connection(Func<string, int, IObdTransport>? serialFactory = null) => _serialFactory = serialFactory;
    public ElmTransportKind TransportKind => _transport?.Kind ?? ElmTransportKind.None;
    public bool IsOpen => _transport?.IsOpen == true;
    public string? PortName => _transport?.PortName;
    public string TransportDescription => _transport?.Description ?? "Disconnected";

    /// <summary>Takes ownership of an already opened platform transport.</summary>
    public void AttachTransport(IObdTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!transport.IsOpen) throw new ArgumentException("Transport must be open.", nameof(transport));
        lock (_sync) { if (ReferenceEquals(_transport, transport)) return; Close(); _transport = transport; }
    }
    public void Open(string portName, int baudRate) => OpenSerial(portName, baudRate);
    public void OpenSerial(string portName, int baudRate)
    {
        Close();
        if (_serialFactory is null) throw new NotSupportedException("Supply a platform serial transport factory.");
        AttachTransport(_serialFactory(portName, baudRate));
    }
    public async Task OpenTcpAsync(string host, int port, int connectTimeoutMs = 3000)
    {
        Close();
        AttachTransport(await TcpObdTransport.ConnectAsync(host, port, connectTimeoutMs).ConfigureAwait(false));
    }
    public void Close()
    {
        lock (_sync) { var transport = _transport; _transport = null; transport?.Dispose(); }
    }
    public string SendCommand(string command, int timeoutMs = 2500) => SendCommandDetailed(command, timeoutMs).Response;
    public Task<ElmCommandResponse> SendCommandDetailedAsync(string command, int timeoutMs = 2500, CancellationToken token = default)
        => Task.Run(() => SendCommandDetailed(command, timeoutMs, token), token);
    public ElmCommandResponse SendCommandDetailed(string command, int timeoutMs = 2500, CancellationToken token = default)
    {
        lock (_sync)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOpen) throw new InvalidOperationException("ELM transport is not open.");
            string normalized = command.Trim().Replace("\r", "").Replace("\n", "");
            if (normalized.Length == 0) throw new ArgumentException("Command is empty.", nameof(command));
            _transport!.DiscardInput();
            _transport.Write(Encoding.ASCII.GetBytes(normalized + "\r"));
            var response = new StringBuilder(); var sw = Stopwatch.StartNew(); double? firstByteMs = null; bool promptReceived = false;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    byte[] bytes = _transport.ReadAvailable();
                    if (bytes.Length > 0)
                    {
                        firstByteMs ??= sw.Elapsed.TotalMilliseconds;
                        response.Append(Encoding.ASCII.GetString(bytes));
                        if (response.ToString().Contains('>')) { promptReceived = true; break; }
                    }
                    else Thread.Sleep(2);
                }
                catch (TimeoutException) { }
            }
            sw.Stop(); string text = response.ToString();
            if (text.Length == 0) throw new TimeoutException($"No response to '{normalized}' within {timeoutMs} ms.");
            if (!promptReceived) throw new TimeoutException($"Response to '{normalized}' did not reach the ELM prompt within {timeoutMs} ms. Partial response: {text.Trim()}");
            return new(text, firstByteMs ?? sw.Elapsed.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);
        }
    }
    public void MonitorAll(Action<string> onChunk, CancellationToken token)
    {
        lock (_sync)
        {
            if (!IsOpen) throw new InvalidOperationException("ELM transport is not open.");
            if (TransportKind == ElmTransportKind.Serial) _transport!.DiscardInput();
            _transport!.Write(Encoding.ASCII.GetBytes("ATMA\r"));
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] bytes = _transport.ReadAvailable();
                    if (bytes.Length > 0) onChunk(Encoding.ASCII.GetString(bytes)); else Thread.Sleep(10);
                }
            }
            finally { _transport.Write(Encoding.ASCII.GetBytes(" \r")); Thread.Sleep(100); }
        }
    }
    public string InterruptObdAndWaitForPrompt(int timeoutMs = 1200)
    {
        lock (_sync)
        {
            if (!IsOpen) return string.Empty;
            timeoutMs = Math.Max(100, timeoutMs);
            var response = new StringBuilder();

            // A command may have timed out at the PC while the ELM is still searching.
            // First consume anything that arrived late. If the prompt is already present,
            // the adapter is synchronized and no interrupt is needed.
            byte[] pending = _transport!.ReadAvailable();
            if (pending.Length > 0)
            {
                response.Append(Encoding.ASCII.GetString(pending));
                if (response.ToString().Contains('>')) return response.ToString();
            }

            // ELM327 documents that any single RS232 character interrupts an OBD
            // operation and that the interrupting character is discarded. A space is
            // benign if the clone was already idle because spaces are ignored.
            _transport.Write(new byte[] { (byte)' ' });
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                byte[] bytes = _transport.ReadAvailable();
                if (bytes.Length > 0)
                {
                    response.Append(Encoding.ASCII.GetString(bytes));
                    if (response.ToString().Contains('>')) break;
                }
                else Thread.Sleep(2);
            }
            return response.ToString();
        }
    }
    public string DrainInputUntilQuiet(int quietMs = 60, int maxWaitMs = 350)
    {
        lock (_sync)
        {
            if (!IsOpen) return string.Empty;
            quietMs = Math.Max(10, quietMs); maxWaitMs = Math.Max(quietMs, maxWaitMs);
            var drained = new StringBuilder(); var sw = Stopwatch.StartNew(); double lastDataMs = 0;
            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
                byte[] bytes = _transport!.ReadAvailable();
                if (bytes.Length > 0) { drained.Append(Encoding.ASCII.GetString(bytes)); lastDataMs = sw.Elapsed.TotalMilliseconds; }
                else if (sw.Elapsed.TotalMilliseconds - lastDataMs >= quietMs) break;
                else Thread.Sleep(2);
            }
            return drained.ToString();
        }
    }
    public static async Task<ElmCommandResponse> ProbeTcpElmAsync(string host, int port, int connectTimeoutMs = 900, int commandTimeoutMs = 1600)
    {
        using var probe = new Elm327Connection();
        await probe.OpenTcpAsync(host, port, connectTimeoutMs).ConfigureAwait(false);
        return await probe.SendCommandDetailedAsync("ATZ", commandTimeoutMs).ConfigureAwait(false);
    }
    public void Dispose() => Close();
}
