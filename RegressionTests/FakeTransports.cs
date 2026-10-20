using System.Text;
using OBDWorkbench.Services;

internal sealed class FakeElmTransport : IObdTransport
{
    public bool IsOpen { get; private set; } = true;
    public ElmTransportKind Kind => ElmTransportKind.Usb;
    public string Description => "Test USB transport";
    public string? PortName => null;
    public List<string> Writes { get; } = new();
    public Queue<byte[]> Incoming { get; } = new();
    public string[] Reply { get; set; } = Array.Empty<string>();
    public bool Disposed { get; private set; }
    public Action? OnRead { get; set; }
    public void DiscardInput() => Incoming.Clear();
    public void Write(byte[] bytes)
    {
        Writes.Add(Encoding.ASCII.GetString(bytes));
        foreach (string chunk in Reply) Incoming.Enqueue(Encoding.ASCII.GetBytes(chunk));
    }
    public byte[] ReadAvailable() { OnRead?.Invoke(); return Incoming.TryDequeue(out var bytes) ? bytes : Array.Empty<byte>(); }
    public void Dispose() { IsOpen = false; Disposed = true; }
}

internal sealed class FakeKLineTransport : IKLineTransport
{
    public bool IsOpen { get; private set; }
    public string PortName { get; private set; } = "";
    public int Baud { get; private set; }
    public bool SawFastInit { get; private set; }
    public bool BadChecksum { get; set; }
    public byte[] LastFrame { get; private set; } = Array.Empty<byte>();
    public void Open(string portName, int baud) { IsOpen = true; PortName = portName; Baud = baud; }
    public void Close() { IsOpen = false; PortName = ""; }
    public Td5IoResult FastInitBreak(byte[] frame, int idleMs, double lowMs, double highMs, int listenMs)
    {
        SawFastInit = frame.SequenceEqual(new byte[] { 0x81, 0x13, 0xF7, 0x81, 0x0C }) && lowMs == 25 && highMs == 25;
        return Response(0xC1, 0xEF, 0x8F);
    }
    public Td5IoResult SendOnlyAdaptive(byte[] frame, int minListenMs, int maxListenMs, int quietMs)
    {
        LastFrame = frame.ToArray();
        return frame[1] switch
        {
            0x10 => Response(0x50, 0xA0),
            0x27 => Response(0x67, 0x01, 0x00, 0x00),
            0x21 => Response(0x61, frame[2], 0x12, 0x34),
            0x31 => Response(0x71, 0xDD),
            _ => throw new InvalidOperationException("Unexpected test request")
        };
    }
    private Td5IoResult Response(params byte[] data)
    {
        byte[] frame = new byte[data.Length + 2]; frame[0] = (byte)data.Length;
        data.CopyTo(frame, 1); frame[^1] = (byte)(frame.Take(frame.Length - 1).Sum(b => (int)b) & 255);
        if (BadChecksum) frame[^1] ^= 1;
        var result = new Td5IoResult { Rx = frame };
        result.RxChunks.Add(new Td5RxChunk { Ms = 12, Bytes = frame }); return result;
    }
    public void Dispose() => Close();
}
