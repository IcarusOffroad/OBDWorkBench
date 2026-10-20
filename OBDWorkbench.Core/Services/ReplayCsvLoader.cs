using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public static class ReplayCsvLoader
{
    private static readonly HashSet<string> KLineMetadataColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ElapsedMs", "Cycle", "PollDurationMs", "Port", "Status", "Errors", "Retries", "RecoveredRetries", "Marker",
        "CaptureMode", "RPMIntervalMs", "RPMRate_Hz", "DTCReadStatus", "DTCFresh", "DTCSampleTms", "DTCAgeMs",
        "DTC_Current", "DTC_Logged", "DTC_Unknown", "DTC_TotalFlags", "DTC_NewCodes", "DTC_ClearedCodes",
        "DTC_BitmapHex", "Event", "CutoutCandidateCount", "MILState"
    };

    public static ReplaySession Load(IEnumerable<string> paths, string cacheRoot)
    {
        List<string> files = paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) return new ReplaySession();

        List<ReplaySourceSignature> signatures = files.Select(SignatureFor).ToList();
        string cacheDirectory = CacheDirectoryFor(signatures, cacheRoot);
        string manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        if (TryLoadManifest(manifestPath, signatures, out ReplayCacheManifest? cached))
            return SessionFromManifest(cached!, cacheDirectory, true);

        if (Directory.Exists(cacheDirectory))
        {
            try { Directory.Delete(cacheDirectory, true); }
            catch { cacheDirectory += "_" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture); }
        }
        Directory.CreateDirectory(cacheDirectory);

        var markers = new List<ReplayRecord>();
        var signals = new Dictionary<string, ReplaySignalDescriptor>(StringComparer.OrdinalIgnoreCase);
        DateTime? startUtc = null, endUtc = null;
        string vehicle = "";
        long sampleCount = 0, eventCount = 0, dtcCount = 0, skippedRows = 0;

        using var writer = new ReplayCacheStore.Writer(cacheDirectory);
        void Emit(ReplayRecord record)
        {
            if (!startUtc.HasValue || record.TimestampUtc < startUtc.Value) startUtc = record.TimestampUtc;
            if (!endUtc.HasValue || record.TimestampUtc > endUtc.Value) endUtc = record.TimestampUtc;
            if (string.IsNullOrWhiteSpace(vehicle) && !string.IsNullOrWhiteSpace(record.Vehicle)) vehicle = record.Vehicle;
            writer.Append(record);
            if (record.Kind == ReplayRecordKind.Sample && record.Value.HasValue)
            {
                sampleCount++;
                string key = ReplayCacheStore.ReplayKey(record);
                if (!signals.TryGetValue(key, out ReplaySignalDescriptor? d))
                {
                    d = new ReplaySignalDescriptor { Key = key, Source = string.IsNullOrWhiteSpace(record.Source) ? "Replay" : record.Source, Vehicle = record.Vehicle, Name = record.Name, Request = record.Request, Units = record.Units, NativeUnits = record.NativeUnits, Min = record.Value.Value, Max = record.Value.Value, SampleCount = 1 };
                    signals[key] = d;
                }
                else
                {
                    d.Min = Math.Min(d.Min, record.Value.Value); d.Max = Math.Max(d.Max, record.Value.Value); d.SampleCount++;
                    if (string.IsNullOrWhiteSpace(d.Units) && !string.IsNullOrWhiteSpace(record.Units)) d.Units = record.Units;
                    if (string.IsNullOrWhiteSpace(d.NativeUnits) && !string.IsNullOrWhiteSpace(record.NativeUnits)) d.NativeUnits = record.NativeUnits;
                }
            }
            else
            {
                markers.Add(record);
                if (record.Kind == ReplayRecordKind.Dtc) dtcCount++; else eventCount++;
            }
        }

        foreach (string file in files) skippedRows += LoadOne(file, Emit);
        ReplayCacheStore store = writer.FinalizeAndBuildCheckpoints();
        if (!startUtc.HasValue || !endUtc.HasValue) return new ReplaySession { Cache = store, SkippedRows = skippedRows };

        markers.Sort(ReplayCacheStore.CompareRecords);
        var manifest = new ReplayCacheManifest
        {
            Version = ReplayCacheStore.FormatVersion, CreatedUtc = DateTime.UtcNow, Sources = signatures, ChunkKeys = writer.ChunkKeys.ToList(),
            Signals = signals.Values.OrderBy(x => x.Source).ThenBy(x => x.Name).ToList(), Markers = markers, StartUtc = startUtc.Value, EndUtc = endUtc.Value,
            Vehicle = string.IsNullOrWhiteSpace(vehicle) ? "Recorded vehicle" : vehicle, SampleCount = sampleCount, EventCount = eventCount, DtcCount = dtcCount, SkippedRows = skippedRows
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions()), Encoding.UTF8);
        return SessionFromManifest(manifest, cacheDirectory, false);
    }

    private static ReplaySession SessionFromManifest(ReplayCacheManifest m, string cacheDirectory, bool reused)
    {
        var session = new ReplaySession { StartUtc = m.StartUtc, EndUtc = m.EndUtc, Vehicle = m.Vehicle, SampleCount = m.SampleCount, EventCount = m.EventCount, DtcCount = m.DtcCount, SkippedRows = m.SkippedRows, CacheWasReused = reused, Cache = new ReplayCacheStore(cacheDirectory, m.ChunkKeys) };
        session.Files.AddRange(m.Sources.Select(x => x.Path)); session.Signals.AddRange(m.Signals); session.Markers.AddRange(m.Markers.OrderBy(x => x.TimestampUtc)); session.CacheBytes = session.Cache.CacheBytes;
        return session;
    }

    private static bool TryLoadManifest(string manifestPath, List<ReplaySourceSignature> signatures, out ReplayCacheManifest? manifest)
    {
        manifest = null; if (!File.Exists(manifestPath)) return false;
        try
        {
            ReplayCacheManifest? m = JsonSerializer.Deserialize<ReplayCacheManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions());
            if (m is null || m.Version != ReplayCacheStore.FormatVersion || m.Sources.Count != signatures.Count) return false;
            for (int i = 0; i < signatures.Count; i++)
            {
                ReplaySourceSignature a = m.Sources[i], b = signatures[i];
                if (!string.Equals(Path.GetFullPath(a.Path), Path.GetFullPath(b.Path), StringComparison.OrdinalIgnoreCase) || a.Size != b.Size || a.LastWriteUtcTicks != b.LastWriteUtcTicks) return false;
            }
            string dir = Path.GetDirectoryName(manifestPath)!;
            if (m.ChunkKeys.Any(key => !File.Exists(Path.Combine(dir, $"chunk_{key}.bin")) || !File.Exists(Path.Combine(dir, $"checkpoint_{key}.bin")))) return false;
            manifest = m; return true;
        }
        catch { return false; }
    }

    private static ReplaySourceSignature SignatureFor(string path)
    {
        var i = new FileInfo(path); return new ReplaySourceSignature { Path = i.FullName, Size = i.Length, LastWriteUtcTicks = i.LastWriteTimeUtc.Ticks };
    }

    private static string CacheDirectoryFor(IEnumerable<ReplaySourceSignature> signatures, string cacheRoot)
    {
        string identity = string.Join("\n", signatures.Select(x => $"{x.Path.ToUpperInvariant()}|{x.Size}|{x.LastWriteUtcTicks}"));
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return Path.Combine(Path.GetFullPath(cacheRoot), key);
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static long LoadOne(string path, Action<ReplayRecord> emit)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return 0;
        List<string> headers = ParseCsvLine(headerLine);
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

        if (headerSet.Contains("RecordType") && headerSet.Contains("Timestamp") && headerSet.Contains("Name"))
        {
            return LoadObdLog(reader, headers, emit);
        }
        if (headerSet.Contains("TimestampUtc") && headerSet.Contains("Cycle") && (headerSet.Contains("RPM") || headers.Any(h => h.StartsWith("PID", StringComparison.OrdinalIgnoreCase))))
        {
            return LoadKLineLive(reader, headers, emit);
        }
        if (headerSet.Contains("TimestampLocal") && headerSet.Contains("Snapshot") && headerSet.Contains("Code") && headerSet.Contains("Change"))
        {
            return LoadKLineDtc(reader, headers, emit);
        }
        if (headerSet.Contains("TimestampUtc") && headerSet.Contains("Event") && headerSet.Contains("Details"))
        {
            return LoadKLineEvents(reader, headers, emit);
        }
        if ((headerSet.Contains("Timestamp") || headerSet.Contains("TimestampUtc") || headerSet.Contains("TimestampLocal")) && headerSet.Contains("Name") && headerSet.Contains("Value"))
        {
            return LoadGenericLong(reader, headers, emit);
        }
        return 0;
    }

    private static long LoadObdLog(StreamReader reader, List<string> headers, Action<ReplayRecord> emit)
    {
        long skipped = 0;
        foreach (var row in ReadRows(reader, headers))
        {
            if (!TryTimestamp(Get(row, "Timestamp"), out DateTime when)) { skipped++; continue; }
            string recordType = Get(row, "RecordType");
            if (recordType.Equals("Sample", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDouble(Get(row, "Value"), out double value)) { skipped++; continue; }
                emit(new ReplayRecord
                {
                    TimestampUtc = when,
                    Kind = ReplayRecordKind.Sample,
                    Source = BlankTo(Get(row, "Source"), "Unattributed"),
                    Vehicle = Get(row, "Vehicle"),
                    Name = Get(row, "Name"),
                    Request = Get(row, "Request"),
                    Value = value,
                    Units = Get(row, "Units"),
                    NativeValue = TryDouble(Get(row, "NativeValue"), out double native) ? native : null,
                    NativeUnits = Get(row, "NativeUnits"),
                    RttMs = Number(Get(row, "RTT_ms")),
                    FirstByteMs = Number(Get(row, "FirstByte_ms")),
                    TailMs = Number(Get(row, "Tail_ms")),
                    Raw = Get(row, "RawResponse"),
                    Status = Get(row, "Status"),
                    DisplayText = Get(row, "Message")
                });
            }
            else
            {
                emit(new ReplayRecord
                {
                    TimestampUtc = when,
                    Kind = ReplayRecordKind.Event,
                    Source = BlankTo(Get(row, "Source"), "Workbench"),
                    Vehicle = Get(row, "Vehicle"),
                    Request = Get(row, "Request"),
                    Raw = Get(row, "RawResponse"),
                    Status = BlankTo(Get(row, "Status"), recordType),
                    Message = Get(row, "Message")
                });
            }
        }
        return skipped;
    }

    private static long LoadGenericLong(StreamReader reader, List<string> headers, Action<ReplayRecord> emit)
    {
        long skipped = 0;
        foreach (var row in ReadRows(reader, headers))
        {
            string ts = FirstNonBlank(Get(row, "TimestampUtc"), Get(row, "Timestamp"), Get(row, "TimestampLocal"));
            if (!TryTimestamp(ts, out DateTime when) || !TryDouble(Get(row, "Value"), out double value)) { skipped++; continue; }
            emit(new ReplayRecord
            {
                TimestampUtc = when,
                Kind = ReplayRecordKind.Sample,
                Source = BlankTo(Get(row, "Source"), "Replay"),
                Vehicle = Get(row, "Vehicle"),
                Name = Get(row, "Name"),
                Request = Get(row, "Request"),
                Value = value,
                Units = Get(row, "Units"),
                Raw = Get(row, "RawResponse")
            });
        }
        return skipped;
    }

    private static long LoadKLineLive(StreamReader reader, List<string> headers, Action<ReplayRecord> emit)
    {
        long skipped = 0;
        foreach (var row in ReadRows(reader, headers))
        {
            string ts = FirstNonBlank(Get(row, "TimestampUtc"), Get(row, "TimestampLocal"));
            if (!TryTimestamp(ts, out DateTime when)) { skipped++; continue; }

            foreach (string column in headers)
            {
                if (!IsUsefulKLineNumericColumn(column)) continue;
                if (!TryDouble(Get(row, column), out double value)) continue;
                string unit = KLineUnit(column);
                DateTime sampleWhen = KLineSampleTimestamp(row, column, when);
                emit(new ReplayRecord
                {
                    TimestampUtc = sampleWhen,
                    Kind = ReplayRecordKind.Sample,
                    Source = "K-Line ECU",
                    Vehicle = "Land Rover TD5 capture",
                    Name = Humanize(column),
                    Request = KLineRequest(column),
                    Value = value,
                    Units = unit,
                    Status = Get(row, "Status"),
                    Message = Get(row, "Marker")
                });
            }

            string evt = Get(row, "Event");
            if (!string.IsNullOrWhiteSpace(evt))
            {
                emit(new ReplayRecord
                {
                    TimestampUtc = when,
                    Kind = ReplayRecordKind.Event,
                    Source = "K-Line ECU",
                    Vehicle = "Land Rover TD5 capture",
                    Status = evt,
                    Message = FirstNonBlank(Get(row, "Marker"), Get(row, "MILState"))
                });
            }
        }
        return skipped;
    }

    private static long LoadKLineDtc(StreamReader reader, List<string> headers, Action<ReplayRecord> emit)
    {
        long skipped = 0;
        foreach (var row in ReadRows(reader, headers))
        {
            if (!TryTimestamp(Get(row, "TimestampLocal"), out DateTime when)) { skipped++; continue; }
            emit(new ReplayRecord
            {
                TimestampUtc = when,
                Kind = ReplayRecordKind.Dtc,
                Source = "K-Line ECU",
                Vehicle = "Land Rover TD5 capture",
                DtcCode = Get(row, "Code"),
                DtcChange = BlankTo(Get(row, "Change"), "BASE"),
                Status = Get(row, "Status"),
                DtcDescription = Get(row, "Description"),
                Message = Get(row, "Observation"),
                Raw = Get(row, "FrameHex")
            });
        }
        return skipped;
    }

    private static long LoadKLineEvents(StreamReader reader, List<string> headers, Action<ReplayRecord> emit)
    {
        long skipped = 0;
        foreach (var row in ReadRows(reader, headers))
        {
            string ts = FirstNonBlank(Get(row, "TimestampUtc"), Get(row, "TimestampLocal"));
            if (!TryTimestamp(ts, out DateTime when)) { skipped++; continue; }
            emit(new ReplayRecord
            {
                TimestampUtc = when,
                Kind = ReplayRecordKind.Event,
                Source = "K-Line ECU",
                Vehicle = "Land Rover TD5 capture",
                Status = Get(row, "Event"),
                Message = Get(row, "Details"),
                Raw = Get(row, "MILState")
            });
        }
        return skipped;
    }

    private static bool IsUsefulKLineNumericColumn(string column)
    {
        if (column.StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase) || KLineMetadataColumns.Contains(column)) return false;
        if (Regex.IsMatch(column, @"^PID[0-9A-F]{2}_(Tms|Raw|FirstRx|RetryRx|RetryUsed|DataHex|DataLength)$", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(column, @"^(Byte\d+_Hex|Word\d+_(Unsigned|Signed))$", RegexOptions.IgnoreCase)) return false;
        if (column.Contains("_DataHex", StringComparison.OrdinalIgnoreCase) || column.EndsWith("_Ascii", StringComparison.OrdinalIgnoreCase)) return false;
        if (column.Contains("_Word", StringComparison.OrdinalIgnoreCase) && (column.EndsWith("_Raw", StringComparison.OrdinalIgnoreCase) || column.EndsWith("_Unsigned", StringComparison.OrdinalIgnoreCase) || column.EndsWith("_Signed", StringComparison.OrdinalIgnoreCase))) return false;
        if (column.Contains("_Bit", StringComparison.OrdinalIgnoreCase)) return false;
        return column.Equals("RPM", StringComparison.OrdinalIgnoreCase)
            || column.Contains("Speed", StringComparison.OrdinalIgnoreCase)
            || column.EndsWith("_V", StringComparison.OrdinalIgnoreCase)
            || column.EndsWith("_C", StringComparison.OrdinalIgnoreCase)
            || column.EndsWith("_kPa", StringComparison.OrdinalIgnoreCase)
            || column.EndsWith("_psi", StringComparison.OrdinalIgnoreCase)
            || column.EndsWith("_pct", StringComparison.OrdinalIgnoreCase)
            || column.Contains("mg_stroke", StringComparison.OrdinalIgnoreCase)
            || column.Contains("FuelDemand", StringComparison.OrdinalIgnoreCase)
            || column.Contains("AirMass", StringComparison.OrdinalIgnoreCase)
            || column.StartsWith("InjectorTrim", StringComparison.OrdinalIgnoreCase)
            || column is "BrakeSecondary" or "ClutchPressed" or "CruiseMaster" or "CruiseSetAccel" or "CruiseResume" or "ACFanReq" or "ACClutchReq" or "Range" or "BrakePressed"
            || column.Contains("Position", StringComparison.OrdinalIgnoreCase);
    }

    private static string KLineUnit(string name)
    {
        if (name.Equals("RPM", StringComparison.OrdinalIgnoreCase)) return "rpm";
        if (name.EndsWith("_mph", StringComparison.OrdinalIgnoreCase)) return "mph";
        if (name.EndsWith("_kmh", StringComparison.OrdinalIgnoreCase)) return "km/h";
        if (name.EndsWith("_V", StringComparison.OrdinalIgnoreCase)) return "V";
        if (name.EndsWith("_C", StringComparison.OrdinalIgnoreCase)) return "°C";
        if (name.EndsWith("_kPa", StringComparison.OrdinalIgnoreCase)) return "kPa";
        if (name.EndsWith("_psi", StringComparison.OrdinalIgnoreCase)) return "psi";
        if (name.EndsWith("_pct", StringComparison.OrdinalIgnoreCase)) return "%";
        if (name.Contains("mg_stroke", StringComparison.OrdinalIgnoreCase)) return "mg/stroke";
        if (name is "BrakeSecondary" or "ClutchPressed" or "CruiseMaster" or "CruiseSetAccel" or "CruiseResume" or "ACFanReq" or "ACClutchReq" or "Range" or "BrakePressed") return "state";
        return "";
    }

    private static string KLineRequest(string column)
    {
        string? pid = KLinePidForColumn(column);
        return pid is not null ? "KLINE:" + pid : "KLINE:" + column.ToUpperInvariant();
    }

    private static DateTime KLineSampleTimestamp(Dictionary<string, string> row, string column, DateTime rowTimestampUtc)
    {
        string? pid = KLinePidForColumn(column);
        if (pid is null) return rowTimestampUtc;
        if (!TryDouble(Get(row, "PID" + pid + "_Tms"), out double sampleMs)) return rowTimestampUtc;
        if (!TryDouble(Get(row, "ElapsedMs"), out double rowElapsedMs)) return rowTimestampUtc;
        double delta = sampleMs - rowElapsedMs;
        if (!double.IsFinite(delta) || Math.Abs(delta) > 120000) return rowTimestampUtc;
        return rowTimestampUtc.AddMilliseconds(delta);
    }

    private static string? KLinePidForColumn(string column)
    {
        Match match = Regex.Match(column, @"^PID([0-9A-F]{2})_", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        if (column.Equals("RPM", StringComparison.OrdinalIgnoreCase)) return "09";
        if (column.StartsWith("VehicleSpeed", StringComparison.OrdinalIgnoreCase)) return "0D";
        if (column is "Battery_V" or "Reference_V") return "10";
        if (column.Equals("InletAirTemp_C", StringComparison.OrdinalIgnoreCase)) return "03";
        if (column.Equals("FuelTemp_C", StringComparison.OrdinalIgnoreCase)) return "05";
        if (column.Equals("Coolant_C", StringComparison.OrdinalIgnoreCase)) return "1A";
        if (column.StartsWith("PedalTrack", StringComparison.OrdinalIgnoreCase) || column.Equals("SensorSupply_V", StringComparison.OrdinalIgnoreCase)) return "1B";
        if (column.StartsWith("BoostCandidate", StringComparison.OrdinalIgnoreCase)) return "1C";
        if (column.Contains("FuelDemand", StringComparison.OrdinalIgnoreCase) || column.Contains("AirMass", StringComparison.OrdinalIgnoreCase) ||
            column.StartsWith("InjectedFuel", StringComparison.OrdinalIgnoreCase) || column.StartsWith("FuelLimit", StringComparison.OrdinalIgnoreCase) ||
            column.StartsWith("IdleFuelDemand", StringComparison.OrdinalIgnoreCase)) return "1D";
        if (column is "BrakeSecondary" or "ClutchPressed" or "CruiseMaster" or "CruiseSetAccel" or "CruiseResume" or "ACFanReq" or "ACClutchReq" or "Range" or "BrakePressed") return "1E";
        if (column.Equals("Barometric_kPa", StringComparison.OrdinalIgnoreCase) || column.Equals("AmbientBaseline_kPa", StringComparison.OrdinalIgnoreCase)) return "23";
        if (column.Equals("EGR_Position_pct", StringComparison.OrdinalIgnoreCase)) return "37";
        if (column.Equals("Wastegate_Position_pct", StringComparison.OrdinalIgnoreCase)) return "38";
        if (column.StartsWith("InjectorTrim", StringComparison.OrdinalIgnoreCase)) return "40";
        return null;
    }

    private static string Humanize(string value)
    {
        string result = value.Replace('_', ' ');
        result = Regex.Replace(result, "([a-z])([A-Z])", "$1 $2");
        result = result.Replace(" kmh", "").Replace(" mph", "").Replace(" kPa", "").Replace(" psi", "").Replace(" pct", "").Replace(" mg stroke", "");
        return result.Trim();
    }

    private static IEnumerable<Dictionary<string, string>> ReadRows(StreamReader reader, List<string> headers)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            List<string> fields = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++) row[headers[i]] = i < fields.Count ? fields[i] : "";
            yield return row;
        }
    }

    private static string Get(Dictionary<string, string> row, string key) => row.TryGetValue(key, out string? value) ? value : "";
    private static string BlankTo(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string FirstNonBlank(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static double Number(string value) => TryDouble(value, out double parsed) ? parsed : 0;
    private static bool TryDouble(string value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryTimestamp(string value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTimeOffset dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime dt))
        {
            utc = dt.ToUniversalTime();
            return true;
        }
        return false;
    }

    public static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
