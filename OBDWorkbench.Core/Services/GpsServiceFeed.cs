using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Icarus.Gps;

namespace OBDWorkbench.Services;

public sealed record GpsServiceState(Snapshot? Snapshot, DateTimeOffset ReceivedUtc, string Status);

// Read-only subscription: never uses the service configuration/control pipe or COM port.
public sealed class GpsServiceFeed : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private GpsServiceState _state = new(null, default, "GPS service connecting");
    public GpsServiceState State => Volatile.Read(ref _state);
    public Task Completion { get; }
    public GpsServiceFeed(string pipeName = Wire.DataPipe)
    {
        Completion = Task.Run(() => RunAsync(pipeName));
    }
    private async Task RunAsync(string pipeName)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(3000, _stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                while (!_stop.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(5000);
                    string? line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    if (line is null) throw new IOException("GPS service disconnected");
                    Snapshot snapshot = Wire.Decode<Snapshot>(line);
                    var received = DateTimeOffset.UtcNow;
                    if (snapshot.ProtocolVersion != 1) throw new IOException("Unsupported GPS service protocol version " + snapshot.ProtocolVersion);
                    if (!GpsServiceValues.IsRecent(snapshot.PublishedUtc, received)) throw new IOException("GPS service snapshot is stale or clock differs");
                    Volatile.Write(ref _state, new(snapshot, received, "GPS service connected"));
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or JsonException or FormatException or UnauthorizedAccessException)
            {
                if (_stop.IsCancellationRequested) break;
                string message = ex is OperationCanceledException ? "GPS service heartbeat timed out" : "GPS service unavailable: " + ex.Message;
                Volatile.Write(ref _state, new(null, default, message));
            }
            try { await Task.Delay(2000, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        Volatile.Write(ref _state, new(null, default, "GPS service disconnected"));
    }
    public void Dispose() { _stop.Cancel(); }
}

public static class GpsServiceValues
{
    public static bool IsRecent(DateTimeOffset at, DateTimeOffset now) => at != default && (now - at).TotalSeconds is >= -5 and < 5;
    public static bool IsAvailable(GpsServiceState state, DateTimeOffset now) => state.Snapshot is { ProtocolVersion: 1 } s &&
        IsRecent(state.ReceivedUtc, now) && IsRecent(s.PublishedUtc, now);
    public static bool HasFix(Snapshot s) => s.Config is { Enabled: true } && s.PortOpen && s.DataFresh && s.FixValid &&
        s.Latitude is >= -90 and <= 90 && s.Longitude is >= -180 and <= 180;
    public static Dictionary<string, double> Map(GpsServiceState state, DateTimeOffset now)
    {
        var values = new Dictionary<string, double>();
        if (!IsAvailable(state, now)) return values;
        Snapshot s = state.Snapshot!;
        bool fix = HasFix(s);
        values["FIX"] = fix ? 1 : 0;
        if (!(s.Config is { Enabled: true } && s.PortOpen && s.DataFresh)) return values;
        void Add(string key, double? value, double min, double max)
        {
            if (value is double v && double.IsFinite(v) && v >= min && v <= max) values[key] = v;
        }
        Add("SATELLITES", s.SatellitesUsed, 0, 200);
        Add("HDOP", s.Hdop, 0, 1000);
        if (!fix) return values;
        Add("LATITUDE", s.Latitude, -90, 90); Add("LONGITUDE", s.Longitude, -180, 180);
        Add("SPEED", s.SpeedKph, 0, 4000); Add("COURSE", s.CourseDegrees, 0, 360);
        Add("ALTITUDE", s.AltitudeMeters, -1500, 100000);
        return values;
    }
    public static string Evidence(Snapshot s) => Wire.Encode(new
    {
        GpsSource = "Icarus GPS Service", s.ProtocolVersion, s.PublishedUtc, s.GpsUtc,
        s.LastNmeaUtc, s.LastFixUtc, s.State, s.PortOpen, s.DataFresh, s.FixValid,
        s.Latitude, s.Longitude, s.SpeedKph, s.CourseDegrees, s.AltitudeMeters, s.Hdop, s.SatellitesUsed
    });
}
