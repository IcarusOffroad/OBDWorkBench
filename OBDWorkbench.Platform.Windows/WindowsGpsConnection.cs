using System.Collections.Concurrent;
using System.IO.Ports;
using OBDWorkbench.Services;

namespace OBDWorkbench.Platform.Windows;

// A read-only serial connection independent of every diagnostic transport.
public sealed class WindowsGpsConnection : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private readonly ConcurrentQueue<string> _lines = new();
    public volatile bool IsOpen;
    private string _status = "GPS disabled";
    public string Status => _status;
    public bool TryRead(out string line) => _lines.TryDequeue(out line!);
    public void Start(AppSettingsData settings)
    {
        var previous = _worker;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        string port = settings.GpsPort;
        int baud = settings.GpsBaud, bits = settings.GpsDataBits;
        string parity = settings.GpsParity, stop = settings.GpsStopBits;
        _worker = Task.Run(async () =>
        {
            if (previous is not null) await previous;
            while (_lines.TryDequeue(out _)) { }
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    _status = "GPS connecting: " + port;
                    using var serial = new SerialPort(port, baud, Enum.Parse<Parity>(parity), bits, Enum.Parse<StopBits>(stop))
                    { ReadTimeout = 500, Handshake = Handshake.None, DtrEnable = false, RtsEnable = false };
                    serial.Open(); IsOpen = true; _status = "GPS connected; waiting for NMEA";
                    string pending = "";
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            int b = serial.ReadByte();
                            if (b == 10)
                            {
                                if (pending.Length > 0) { _lines.Enqueue(pending.TrimEnd('\r')); while (_lines.Count > 500) _lines.TryDequeue(out _); }
                                pending = "";
                            }
                            else if (b == '$') pending = "$";
                            else if (pending.Length < 1024) pending += (char)b;
                            else pending = "";
                        }
                        catch (TimeoutException) { }
                    }
                }
                catch (Exception ex) { _status = "GPS disconnected: " + ex.Message; }
                finally { IsOpen = false; }
                try { await Task.Delay(3000, cts.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    public void Dispose() { _cts?.Cancel(); }
}
