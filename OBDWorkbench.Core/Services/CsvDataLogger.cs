using System;
using System.Globalization;
using System.IO;
using System.Text;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public sealed class CsvDataLogger : IDisposable
{
    private readonly object _sync = new();
    private StreamWriter? _writer;
    public string? CurrentPath { get; private set; }
    public bool IsLogging => _writer is not null;

    public string Start(string folder)
    {
        Stop();
        Directory.CreateDirectory(folder);
        CurrentPath = Path.Combine(folder, $"OBDLog_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
        var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
        _writer.WriteLine("Timestamp,Source,Vehicle,Name,Value,Units,NativeValue,NativeUnits,Request,RTT_ms,FirstByte_ms,Tail_ms,RawResponse,RecordType,Status,Message");
        _writer.Flush();
        return CurrentPath;
    }

    public void Log(PidDefinition definition, double nativeValue, double displayValue, string displayUnits, string vehicle, string rawResponse, double rttMs, double firstByteMs, double tailMs, string ecuSource, string displayText = "", string sampleStatus = "OK")
    {
        lock (_sync)
        {
            if (_writer is null) return;
            _writer.WriteLine(string.Join(',',
                Csv(DateTime.Now.ToString("O", CultureInfo.InvariantCulture)),
                Csv(ecuSource), Csv(vehicle),
                Csv(definition.Name),
                displayValue.ToString("0.########", CultureInfo.InvariantCulture), Csv(displayUnits),
                nativeValue.ToString("0.########", CultureInfo.InvariantCulture), Csv(definition.Units),
                Csv(definition.Request),
                rttMs.ToString("0.###", CultureInfo.InvariantCulture),
                firstByteMs.ToString("0.###", CultureInfo.InvariantCulture),
                tailMs.ToString("0.###", CultureInfo.InvariantCulture),
                Csv(NormalizeRaw(rawResponse)), Csv("Sample"), Csv(sampleStatus), Csv(displayText)));
            _writer.Flush();
        }
    }

    public void LogEvent(string status, string vehicle, string request = "", string source = "", string raw = "", string message = "",
        double? rttMs = null, double? firstByteMs = null, double? tailMs = null)
    {
        lock (_sync)
        {
            if (_writer is null) return;
            string Number(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
            _writer.WriteLine(string.Join(',', new[] { DateTime.Now.ToString("O", CultureInfo.InvariantCulture), source, vehicle,
                "", "", "", "", "", request, Number(rttMs), Number(firstByteMs), Number(tailMs), NormalizeRaw(raw), "Event", status, message }.Select(Csv)));
            _writer.Flush();
        }
    }

    public string SnapshotText()
    {
        lock (_sync)
        {
            _writer?.Flush();
            if (string.IsNullOrWhiteSpace(CurrentPath) || !File.Exists(CurrentPath)) return "";
            try
            {
                using var stream = new FileStream(CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch { return ""; }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose() => Stop();

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string NormalizeRaw(string raw) => raw.Replace("\r", " ").Replace("\n", " ").Trim();
}
