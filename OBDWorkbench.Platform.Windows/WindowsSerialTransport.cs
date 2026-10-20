using System.IO.Ports;
using System.Text;
using OBDWorkbench.Services;
namespace OBDWorkbench.Platform.Windows;

public sealed class WindowsSerialTransport : IObdTransport
{
    private readonly SerialPort _port;
    private WindowsSerialTransport(SerialPort port) => _port = port;
    public bool IsOpen => _port.IsOpen;
    public ElmTransportKind Kind => ElmTransportKind.Serial;
    public string? PortName => _port.PortName;
    public string Description => $"Serial {_port.PortName} @ {_port.BaudRate}";
    public static IObdTransport Open(string portName, int baudRate)
    {
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = false, RtsEnable = false,
            NewLine = "\r", Encoding = Encoding.ASCII, ReadTimeout = 100, WriteTimeout = 1500
        };
        try { port.Open(); port.DiscardInBuffer(); port.DiscardOutBuffer(); return new WindowsSerialTransport(port); }
        catch { port.Dispose(); throw; }
    }
    public void DiscardInput() => _port.DiscardInBuffer();
    public void Write(byte[] bytes) => _port.Write(bytes, 0, bytes.Length);
    public byte[] ReadAvailable() => _port.BytesToRead > 0 ? Encoding.ASCII.GetBytes(_port.ReadExisting()) : Array.Empty<byte>();
    public void Dispose() { try { if (_port.IsOpen) _port.Close(); } finally { _port.Dispose(); } }
    public static string[] GetPortNames() => SerialPort.GetPortNames()
        .OrderBy(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[3..], out int n) ? n : int.MaxValue)
        .ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
}
