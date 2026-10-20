namespace OBDWorkbench.Services;

/// <summary>A connected ELM byte stream. Protocol owns one reader and serializes
/// commands; ReadAvailable must return immediately when no bytes are buffered.
/// Writes must finish or throw within an implementation-defined finite timeout.
/// Dispose closes the transport. Platform factories handle device permissions and opening.</summary>
public interface IObdTransport : IDisposable
{
    bool IsOpen { get; }
    ElmTransportKind Kind { get; }
    string Description { get; }
    string? PortName { get; }
    void DiscardInput();
    void Write(byte[] bytes);
    byte[] ReadAvailable();
}
