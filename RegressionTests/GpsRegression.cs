using OBDWorkbench.Services;

internal static class GpsRegression
{
    public static void Run(Action<bool, string> check)
    {
        static string Sentence(string body)
        {
            byte crc = 0; foreach (char c in body) crc ^= (byte)c;
            return "$" + body + "*" + crc.ToString("X2");
        }
        var now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var p = new NmeaGpsParser();
        string rmc = Sentence("GNRMC,120000,A,4000.0000,N,10500.0000,W,10.0,90.0,171026,,,A");
        check(p.Accept(rmc, now) && p.HasFix(now), "GPS GN talker RMC produces valid fix");
        var v = p.Snapshot(now);
        check(Math.Abs(v["SPEED"] - 18.52) < 1e-8 && v["LATITUDE"] == 40 && v["LONGITUDE"] == -105, "GPS knots conversion and signed coordinates");
        check(!p.HasFix(now.AddSeconds(4)) && !p.Snapshot(now.AddSeconds(4)).ContainsKey("SPEED"), "GPS fix and speed expire without fresh sentences");
        check(!p.Accept(rmc[..^2] + "00", now), "GPS rejects incorrect checksum");
        p.Accept(Sentence("GPGGA,120001,4000.0000,N,10500.0000,W,1,11,1.01,2500.5,M,0,M,,"), now.AddSeconds(1));
        check(p.Snapshot(now.AddSeconds(1))["SATELLITES"] == 11 && p.Snapshot(now.AddSeconds(1))["ALTITUDE"] == 2500.5, "GPS GGA altitude satellites and HDOP");
        p.Accept(Sentence("GPRMC,120002,V,,,,,,,171026,,,N"), now.AddSeconds(2));
        check(!p.HasFix(now.AddSeconds(2)) && !p.Snapshot(now.AddSeconds(2)).ContainsKey("SPEED"), "GPS invalid RMC immediately removes navigation values");
        p.Accept(Sentence("GPVTG,90,T,,M,10,N,18.52,K,A"), now.AddSeconds(2));
        check(!p.Snapshot(now.AddSeconds(2)).ContainsKey("SPEED"), "GPS VTG cannot establish valid fix by itself");
        p.Accept(rmc, now.AddSeconds(3));
        p.Accept(Sentence("GPGGA,120003,4000.0000,N,10500.0000,W,0,00,99.9,,M,0,M,,"), now.AddSeconds(3));
        check(!p.HasFix(now.AddSeconds(3)), "GPS no-fix GGA overrides earlier RMC fix");
        p.Accept(Sentence("GPRMC,120004,A,4061.0,N,10500.0,W,10,90,171026,,,A"), now.AddSeconds(4));
        check(!p.HasFix(now.AddSeconds(4)), "GPS rejects invalid coordinate minutes");
        p.Accept(Sentence("GPRMC,120005,A,4000.0,N,10500.0,W,,90,171026,,,A"), now.AddSeconds(5));
        check(!p.Snapshot(now.AddSeconds(5)).ContainsKey("SPEED"), "GPS missing speed is unavailable rather than zero");
        p.Accept(Sentence("GPRMC,120006,A,4000.0,S,10500.0,E,0,0,171026,,,A"), now.AddSeconds(6));
        check(p.Snapshot(now.AddSeconds(6))["LATITUDE"] == -40 && p.Snapshot(now.AddSeconds(6))["SPEED"] == 0, "GPS valid stationary speed remains zero");
        string path = Path.Combine(Path.GetTempPath(), "gps-settings-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new AppSettingsStore(path);
            store.Save(new AppSettingsData { GpsEnabled = true, GpsPort = "COM6", GpsBaud = 9600, GpsDataBits = 8, GpsParity = "None", GpsStopBits = "One" });
            var loaded = store.Load();
            check(loaded.GpsEnabled && loaded.GpsPort == "COM6" && loaded.GpsBaud == 9600 && loaded.GpsDataBits == 8 && loaded.GpsParity == "None" && loaded.GpsStopBits == "One", "GPS serial settings persist independently");
        }
        finally { File.Delete(path); }
    }
}
