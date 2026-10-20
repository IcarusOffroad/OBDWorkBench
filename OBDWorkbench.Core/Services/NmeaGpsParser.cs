using System.Globalization;

namespace OBDWorkbench.Services;

// Receiver-independent NMEA 0183 reader. Every exposed field has its own freshness.
public sealed class NmeaGpsParser
{
    private readonly Dictionary<string, (double Value, DateTime At, string Raw)> _values = new();
    private DateTime _fixAt;
    private bool _fix;
    public DateTime LastSentenceUtc { get; private set; }
    public string LastRaw { get; private set; } = "";
    public string RawFor(string key) => _values.TryGetValue(key, out var value) ? value.Raw : LastRaw;
    public string UtcTime { get; private set; } = "";
    public int RejectedSentences { get; private set; }
    public bool HasFix(DateTime now) => _fix && now - _fixAt <= TimeSpan.FromSeconds(3) &&
        _values.TryGetValue("LATITUDE", out var lat) && now - lat.At <= TimeSpan.FromSeconds(3) &&
        _values.TryGetValue("LONGITUDE", out var lon) && now - lon.At <= TimeSpan.FromSeconds(3);
    public Dictionary<string, double> Snapshot(DateTime now)
    {
        bool fix = HasFix(now);
        var result = new Dictionary<string, double> { ["FIX"] = fix ? 1 : 0 };
        foreach (var pair in _values)
            if (now - pair.Value.At <= TimeSpan.FromSeconds(3) &&
                (fix || pair.Key is "SATELLITES" or "HDOP")) result[pair.Key] = pair.Value.Value;
        return result;
    }
    public bool Accept(string line, DateTime now)
    {
        line = line.Trim();
        int star = line.IndexOf('*');
        if (line.Length > 1024 || !line.StartsWith('$') || star < 6 || star != line.Length - 3 ||
            !byte.TryParse(line.AsSpan(star + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte expected))
            return Reject();
        byte checksum = 0;
        for (int i = 1; i < star; i++) checksum ^= (byte)line[i];
        if (checksum != expected) return Reject();
        string[] f = line[1..star].Split(',');
        if (f[0].Length != 5) return Reject();
        string type = f[0][2..];
        if (type is not ("RMC" or "GGA" or "VTG")) return false;
        if ((type == "RMC" && f.Length < 10) || (type == "GGA" && f.Length < 11) || (type == "VTG" && f.Length < 9)) return Reject();
        LastSentenceUtc = now; LastRaw = line;
        void Put(string key, string text, double min, double max, double factor = 1)
        {
            _values.Remove(key);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && v >= min && v <= max)
                _values[key] = (v * factor, now, line);
        }
        void Position(string lat, string ns, string lon, string ew)
        {
            _values.Remove("LATITUDE"); _values.Remove("LONGITUDE");
            if (Coordinate(lat, ns, true, out double a) && Coordinate(lon, ew, false, out double b))
            { _values["LATITUDE"] = (a, now, line); _values["LONGITUDE"] = (b, now, line); }
        }
        if (type == "RMC")
        {
            UtcTime = f[1] + " UTC / date " + f[9];
            _fix = f[2] == "A" && !(f.Length > 12 && f[12] == "N"); _fixAt = now;
            if (!_fix) { ClearNavigation(); return true; }
            Position(f[3], f[4], f[5], f[6]);
            Put("SPEED", f[7], 0, 2000, 1.852);
            Put("COURSE", f[8], 0, 360);
        }
        else if (type == "GGA")
        {
            UtcTime = f[1] + " UTC";
            _fix = int.TryParse(f[6], out int q) && q is 1 or 2 or 3 or 4 or 5; _fixAt = now;
            Put("SATELLITES", f[7], 0, 200); Put("HDOP", f[8], 0, 1000);
            if (!_fix) { ClearNavigation(); return true; }
            Position(f[2], f[3], f[4], f[5]);
            Put("ALTITUDE", f[10] == "M" ? f[9] : "", -1500, 100000);
        }
        else if (HasFix(now))
        {
            // VTG without an established fix cannot make a receiver valid.
            if (f.Length > 9 && f[9] == "N") { _values.Remove("SPEED"); _values.Remove("COURSE"); return true; }
            Put("SPEED", f[8] == "K" ? f[7] : "", 0, 4000);
            Put("COURSE", f[2] == "T" ? f[1] : "", 0, 360);
        }
        return true;
    }
    private bool Reject() { RejectedSentences++; return false; }
    private void ClearNavigation()
    {
        foreach (string key in new[] { "SPEED", "COURSE", "LATITUDE", "LONGITUDE", "ALTITUDE" }) _values.Remove(key);
    }
    private static bool Coordinate(string text, string hemisphere, bool latitude, out double result)
    {
        result = 0;
        if (!(latitude ? hemisphere is "N" or "S" : hemisphere is "E" or "W") ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw) || !double.IsFinite(raw) || raw < 0) return false;
        double degrees = Math.Floor(raw / 100), minutes = raw - degrees * 100;
        result = degrees + minutes / 60;
        if (minutes >= 60 || result > (latitude ? 90 : 180)) return false;
        if (hemisphere is "S" or "W") result = -result;
        return true;
    }
}
