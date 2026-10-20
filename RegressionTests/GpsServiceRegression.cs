using Icarus.Gps;
using OBDWorkbench.Services;
using System.IO.Pipes;
using System.Text;

internal static class GpsServiceRegression
{
    public static async Task Run(Action<bool, string> check, bool skipIpc = false)
    {
        var now = DateTimeOffset.UtcNow;
        var s = new Snapshot { PublishedUtc = now, Config = new GpsConfig(), PortOpen = true, DataFresh = true, FixValid = true,
            Latitude = 40.12345678, Longitude = -105.12345678, SpeedKph = 18.52, AltitudeMeters = 2500.5,
            SatellitesUsed = 11, Hdop = 1.01, CourseDegrees = 90, GpsUtc = now.AddSeconds(-1),
            SatellitesInView = new() { ["GP"] = 12, ["GL"] = 9, ["GN"] = 21 } };
        GpsServiceState State(Snapshot snapshot) => new(snapshot, now, "Connected");
        var values = GpsServiceValues.Map(State(s), now);
        check(values["SPEED"] == 18.52 && values["LATITUDE"] == 40.12345678 && values["ALTITUDE"] == 2500.5, "GPS service preserves native speed position altitude and precision");
        check(values["SATELLITES"] == 11, "GPS service uses satellites used rather than summing overlapping in-view constellations");
        check(GpsServiceValues.Map(State(s with { FixValid = false }), now).Keys.All(x => x is "FIX" or "HDOP" or "SATELLITES"), "GPS service invalid fix suppresses navigation fields");
        check(!GpsServiceValues.Map(State(s with { SpeedKph = null }), now).ContainsKey("SPEED"), "GPS service null speed remains unavailable");
        check(GpsServiceValues.Map(State(s with { SpeedKph = 0 }), now)["SPEED"] == 0, "GPS service stationary speed is valid zero");
        check(!GpsServiceValues.Map(State(s with { Latitude = double.NaN }), now).ContainsKey("SPEED"), "GPS service invalid coordinates cannot establish valid fix");
        check(GpsServiceValues.Map(State(s with { PortOpen = false }), now).Count == 1 && GpsServiceValues.Map(State(s with { PortOpen = false }), now)["FIX"] == 0, "GPS service released receiver invalidates measurements despite cached fix");
        check(GpsServiceValues.Map(State(s with { Config = new GpsConfig { Enabled = false } }), now).Count == 1, "GPS service disabled receiver suppresses measurements");
        check(GpsServiceValues.Map(State(s with { DataFresh = false }), now).Count == 1, "GPS service stale receiver data suppresses measurements");
        check(GpsServiceValues.Map(State(s), now.AddSeconds(5)).Count == 0, "GPS service heartbeat expires after five seconds");
        check(GpsServiceValues.Map(State(s with { PublishedUtc = now.AddSeconds(-6) }), now).Count == 0, "GPS service old publication is rejected despite recent receipt");
        check(GpsServiceValues.Map(State(s with { PublishedUtc = now.AddSeconds(20) }), now).Count == 0, "GPS service future publication beyond clock tolerance is unavailable");
        check(GpsServiceValues.Map(State(s with { ProtocolVersion = 2 }), now).Count == 0, "GPS service incompatible protocol suppresses measurements");
        string evidence = GpsServiceValues.Evidence(s);
        check(evidence.Contains("PublishedUtc") && evidence.Contains("GpsUtc") && evidence.Contains("40.12345678"), "GPS service log evidence retains both clocks and full coordinates");
        string path = Path.Combine(Path.GetTempPath(), "gps-source-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new AppSettingsStore(path);
            store.Save(new AppSettingsData { GpsSource = "Service", GpsEnabled = true, GpsPort = "COM6" });
            check(store.Load().GpsSource == "Service" && store.Load().GpsPort == "COM6", "GPS source selection persists without losing COM settings");
            File.WriteAllText(path, "{\"GpsEnabled\":true,\"GpsPort\":\"COM6\"}");
            check(store.Load().GpsSource == "COM", "GPS r23 settings retain direct COM source on upgrade");
        }
        finally { File.Delete(path); }

        if (skipIpc) { Console.WriteLine("SKIP: 5 GPS named-pipe integration checks (--no-ipc)"); return; }

        static async Task WaitFor(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!predicate()) await Task.Delay(20, timeout.Token);
        }
        string name = "WorkbenchGpsTest-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var feed = new GpsServiceFeed(name);
        await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Wire.Encode(s with { PublishedUtc = DateTimeOffset.UtcNow }));
        await WaitFor(() => feed.State.Snapshot is not null);
        check(feed.State.Snapshot!.SpeedKph == 18.52, "GPS service client reads actual newline JSON named-pipe feed");
        await writer.WriteLineAsync(Wire.Encode(s with { ProtocolVersion = 2, PublishedUtc = DateTimeOffset.UtcNow }));
        await WaitFor(() => feed.State.Snapshot is null);
        check(feed.State.Status.Contains("protocol"), "GPS service client immediately clears incompatible feed");
        server.Disconnect();
        await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(8));
        await writer.WriteLineAsync(Wire.Encode(s with { PublishedUtc = DateTimeOffset.UtcNow }));
        await WaitFor(() => feed.State.Snapshot is not null);
        check(feed.State.Snapshot is not null, "GPS service client reconnects automatically");
        await WaitFor(() => feed.State.Snapshot is null);
        check(feed.State.Status.Contains("heartbeat"), "GPS service client clears stalled pipe on heartbeat timeout");
        feed.Dispose();
        await feed.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        check(feed.State.Snapshot is null, "GPS service cancellation completes and releases subscription");
    }
}
