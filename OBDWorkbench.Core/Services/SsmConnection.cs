using System.Diagnostics;

namespace OBDWorkbench.Services;

public interface ISsmTransport : IDisposable
{
    bool IsOpen { get; }
    void Open(string portName, int baudRate = 4800);
    void Close();
    void PurgeRx();
    void Write(byte[] data);
    byte[] ReadForResponse(int totalTimeoutMs, Func<IReadOnlyList<byte>, bool>? stopWhen = null);
}

public sealed record SsmReadResult(Dictionary<int, byte> Values, double RttMs, string RawHex);

public sealed class SsmConnection(ISsmTransport transport) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsOpen => transport.IsOpen;
    public byte[] InitData { get; private set; } = [];
    public IReadOnlyList<SsmParameterDefinition> Supported { get; private set; } = [];
    public int BatchLimit { get; private set; } = 43;
    public event Action<string>? Traffic;

    public async Task OpenAsync(string port, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() => { token.ThrowIfCancellationRequested(); transport.Open(port, 4800); }, token).ConfigureAwait(false);
            BatchLimit = 43;
            var response = await ExchangeAsync(SsmProtocol.BuildInitRequest(), 0xFF, token).ConfigureAwait(false);
            if (response.Data.Length < 10) throw new InvalidDataException("Incomplete SSM capability response.");
            InitData = response.Data;
            Supported = SsmParameterCatalog.Supported(InitData);
            if (Supported.Count == 0) throw new InvalidDataException("ECU advertises no parameters from the SSM catalog.");
        }
        catch { transport.Close(); Supported = []; InitData = []; throw; }
        finally { gate.Release(); }
    }

    private async Task<SsmFrame> ExchangeAsync(byte[] tx, byte service, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            transport.PurgeRx();
            Traffic?.Invoke("SSM TX " + SsmProtocol.Hex(tx));
            transport.Write(tx);
            byte[] rx = transport.ReadForResponse(1500, bytes => SsmProtocol.ExtractFrames(bytes).Any(f => f.Destination == 0xF0 && f.Source == 0x10 && f.Data.FirstOrDefault() == service));
            token.ThrowIfCancellationRequested();
            Traffic?.Invoke("SSM RX " + SsmProtocol.Hex(rx));
            return SsmProtocol.ExtractFrames(rx).FirstOrDefault(f => f.Destination == 0xF0 && f.Source == 0x10 && f.Data.FirstOrDefault() == service)
                ?? throw new IOException("No valid SSM ECU response (timeout, framing, or checksum).");
        }, token).ConfigureAwait(false);
    }

    public async Task<SsmReadResult> ReadAsync(IEnumerable<int> requested, CancellationToken token = default)
    {
        int[] addresses = requested.Distinct().ToArray();
        await gate.WaitAsync(token).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var values = new Dictionary<int, byte>();
        var raw = new List<string>();
        try
        {
            for (int offset = 0; offset < addresses.Length;)
            {
                int count = Math.Min(BatchLimit, addresses.Length - offset);
                int[] batch = addresses.Skip(offset).Take(count).ToArray();
                try
                {
                    var frame = await ExchangeAsync(SsmProtocol.BuildA8Request(batch), 0xE8, token).ConfigureAwait(false);
                    foreach (var pair in SsmProtocol.DecodeA8Values(frame, batch)) values[pair.Key] = pair.Value;
                    raw.Add(frame.ToString());
                    offset += count;
                }
                catch (Exception ex) when (count > 1 && (ex is IOException || ex is InvalidDataException))
                {
                    BatchLimit = Math.Max(1, count / 2);
                    Traffic?.Invoke($"SSM batch fallback: {count} to {BatchLimit} addresses");
                }
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            return new(values, sw.Elapsed.TotalMilliseconds, string.Join(" | ", raw));
        }
        finally { gate.Release(); }
    }

    // Guarded engine-memory reset validated by the standalone SSM DTC probe.
    // No arbitrary writes and no retry of the B8 clear request are exposed here.
    public async Task<bool> ClearEngineMemoryOnceAsync(CancellationToken token = default)
    {
        _ = DtcProfile.FromInit(InitData);
        if (!SsmProtocol.CapabilitySupported(InitData, 1, 1))
            throw new InvalidOperationException("Engine speed is not advertised; cannot verify the required engine-off condition.");

        bool speedAvailable = SsmProtocol.CapabilitySupported(InitData, 2, 8);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            int[] addresses = speedAvailable ? [0x0E, 0x0F, 0x10] : [0x0E, 0x0F];
            for (int i = 0; i < 2; i++)
            {
                var frame = await ExchangeAsync(SsmProtocol.BuildA8Request(addresses), 0xE8, token).ConfigureAwait(false);
                var values = SsmProtocol.DecodeA8Values(frame, addresses);
                int rpmRaw = (values[0x0E] << 8) | values[0x0F];
                int rpm = rpmRaw / 4;
                int speed = speedAvailable ? values[0x10] : 0;
                Traffic?.Invoke($"SSM clear safety check {i + 1}/2: rpm={rpm} speedRaw={speed}");
                if (rpm != 0 || speed != 0)
                    throw new InvalidOperationException("Clear blocked: engine RPM or vehicle speed is not zero.");
                if (i == 0) await Task.Delay(250, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            Traffic?.Invoke("CLEAR WRITE: one attempt only; SSM engine memory/adaptations may reset; loss of acknowledgement means outcome unknown.");
            var reply = await ExchangeAsync(SsmProtocol.BuildPacket([0xB8, 0x00, 0x00, 0x60, 0x40]), 0xF8, token).ConfigureAwait(false);
            return reply.Data.SequenceEqual(new byte[] { 0xF8, 0x40 });
        }
        finally { gate.Release(); }
    }

    public void Close() { gate.Wait(); try { transport.Close(); } finally { gate.Release(); } }
    public void Dispose() { Close(); transport.Dispose(); }
}
