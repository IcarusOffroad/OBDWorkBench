namespace OBDWorkbench.Services;

/// <summary>Raw K-line capability; deliberately separate from ELM command streams.
/// Implementations must preserve 10400-baud and timed break/echo semantics.</summary>
public interface IKLineTransport : IDisposable
{
    bool IsOpen { get; }
    string PortName { get; }
    void Open(string portName, int baud);
    void Close();
    Td5IoResult FastInitBreak(byte[] frame, int idleMs, double lowMs, double highMs, int listenMs);
    Td5IoResult SendOnlyAdaptive(byte[] frame, int minListenMs, int maxListenMs, int quietMs);
}

public sealed class Td5IoResult
    {
        public byte[] Rx { get; set; } = Array.Empty<byte>();
        public List<Td5RxChunk> RxChunks { get; } = new();
    }

public sealed class Td5RxChunk
    {
        public double Ms { get; init; }
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
    }

