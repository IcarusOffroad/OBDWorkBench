using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace OBDWorkbench.Services;

internal sealed class SupportBundleContext
{
    public string Family { get; init; } = "Unknown";
    public string EcuProfile { get; init; } = "Unidentified";
    public string EcuFingerprint { get; init; } = "";
    public bool EcuProfileKnown { get; init; }
    public string AdapterIdentity { get; init; } = "";
    public string Transport { get; init; } = "";
    public string ConnectionId { get; init; } = "";
    public string Protocol { get; init; } = "";
    public string SessionLogPath { get; init; } = "";
    public string CsvLogPath { get; init; } = "";
    public string SessionLogText { get; init; } = "";
    public string CsvLogText { get; init; } = "";
    public string Vin { get; init; } = "";
    public bool IncludeVin { get; init; }
    public string UserNotes { get; init; } = "";
    public string IdentificationText { get; init; } = "";
    public string ModuleCsv { get; init; } = "";
    public string DtcCsv { get; init; } = "";
    public string FamilyDataCsv { get; init; } = "";
    public string LiveSnapshotCsv { get; init; } = "";
    public string EventsCsv { get; init; } = "";
}

internal static class SupportBundleService
{
    public static string Create(SupportBundleContext context)
    {
        string root = Path.Combine(WindowsStoragePaths.Root, "SupportBundles");
        Directory.CreateDirectory(root);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string work = Path.Combine(root, "_work_" + stamp);
        Directory.CreateDirectory(work);
        string zip = Path.Combine(root, $"OBDWorkbench_Support_{stamp}.zip");

        try
        {
            File.WriteAllText(Path.Combine(work, "UserNotes.txt"), SanitizeText(context.UserNotes ?? "", context), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work, "Identification.txt"), SanitizeText(context.IdentificationText ?? "", context), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work, "DetectedModules.csv"), SanitizeText(context.ModuleCsv ?? "", context), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work, "DTC_Snapshot.csv"), SanitizeText(context.DtcCsv ?? "", context), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work, "LiveSnapshot.csv"), SanitizeText(context.LiveSnapshotCsv ?? "", context), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work, "Events.csv"), SanitizeText(context.EventsCsv ?? "", context), new UTF8Encoding(false));
            if (!string.IsNullOrWhiteSpace(context.FamilyDataCsv))
                File.WriteAllText(Path.Combine(work, "FamilyData.csv"), SanitizeText(context.FamilyDataCsv, context), new UTF8Encoding(false));

            string sessionStatus = WriteLogEvidence(work, "Session.log", context.SessionLogText, context.SessionLogPath, context);
            string csvStatus = WriteLogEvidence(work, "OBDLog.csv", context.CsvLogText, context.CsvLogPath, context);
            int liveRows = CsvDataRowCount(context.LiveSnapshotCsv ?? "");
            int eventRows = CsvDataRowCount(context.EventsCsv ?? "");
            int dtcRows = CsvDataRowCount(context.DtcCsv ?? "");
            string liveStatus = $"INCLUDED ({liveRows} row{(liveRows == 1 ? "" : "s")})";
            string eventStatus = $"INCLUDED ({eventRows} event{(eventRows == 1 ? "" : "s")})";
            string dtcStatus = $"INCLUDED ({dtcRows} DTC/fault row{(dtcRows == 1 ? "" : "s")})";

            string captureStatus =
                "OBD Workbench support-bundle capture status\r\n" +
                $"Created: {DateTime.Now:O}\r\n" +
                $"Family: {context.Family}\r\n" +
                $"ECU fingerprint: {context.EcuFingerprint}\r\n\r\n" +
                $"Session log: {sessionStatus}\r\n" +
                $"CSV recording: {csvStatus}\r\n" +
                $"Live snapshot: {liveStatus}\r\n" +
                $"Event markers: {eventStatus}\r\n" +
                $"DTC snapshot: {dtcStatus}\r\n" +
                $"Family evidence: {(string.IsNullOrWhiteSpace(context.FamilyDataCsv) ? "NOT APPLICABLE" : "INCLUDED")}\r\n\r\n" +
                "A missing session/CSV log is explicitly reported here; it is never silently treated as a complete bundle.\r\n";
            File.WriteAllText(Path.Combine(work, "CaptureStatus.txt"), SanitizeText(captureStatus, context), new UTF8Encoding(false));

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var manifest = new
            {
                bundleSchema = 2,
                createdLocal = DateTime.Now.ToString("O"),
                createdUtc = DateTime.UtcNow.ToString("O"),
                workbenchVersion = version,
                family = context.Family,
                ecuProfile = context.EcuProfile,
                ecuFingerprint = context.EcuFingerprint,
                ecuProfileKnown = context.EcuProfileKnown,
                adapterIdentity = context.AdapterIdentity,
                transport = context.Transport,
                connection = context.ConnectionId,
                protocol = context.Protocol,
                os = Environment.OSVersion.ToString(),
                dotnet = Environment.Version.ToString(),
                capture = new
                {
                    sessionLog = sessionStatus,
                    csvRecording = csvStatus,
                    liveSnapshotRows = liveRows,
                    eventRows,
                    dtcRows
                },
                privacy = new { vinIncluded = context.IncludeVin && !string.IsNullOrWhiteSpace(context.Vin), machineNameRemoved = true, userProfilePathRemoved = true }
            };
            File.WriteAllText(Path.Combine(work, "Manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

            string readme = "OBD Workbench diagnostic support bundle\r\n\r\n" +
                "This ZIP was created locally by OBD Workbench for compatibility/debugging reports.\r\n" +
                "CaptureStatus.txt states explicitly which logs and snapshots were included.\r\n" +
                "LiveSnapshot.csv preserves the values visible when the bundle was created, even if continuous CSV recording is unavailable.\r\n" +
                "Events.csv preserves Mark Event notes independently from the rolling log files.\r\n" +
                "By default the bundle removes the local Windows user-profile path, machine name, and VIN.\r\n" +
                "Attach this ZIP to the relevant GitHub issue instead of pasting large raw logs into the issue body.\r\n";
            File.WriteAllText(Path.Combine(work, "README.txt"), readme, new UTF8Encoding(false));

            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zip;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private static string WriteLogEvidence(string work, string outputName, string snapshotText, string sourcePath, SupportBundleContext context)
    {
        string destination = Path.Combine(work, outputName);
        if (!string.IsNullOrWhiteSpace(snapshotText))
        {
            File.WriteAllText(destination, SanitizeText(snapshotText, context), new UTF8Encoding(false));
            return $"INCLUDED from active logger snapshot ({Encoding.UTF8.GetByteCount(snapshotText)} bytes before sanitization)";
        }

        if (string.IsNullOrWhiteSpace(sourcePath)) return "NOT AVAILABLE (logger has no current file path)";
        if (!File.Exists(sourcePath)) return $"NOT AVAILABLE (source file {Path.GetFileName(sourcePath)} does not exist)";
        try
        {
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            File.WriteAllText(destination, SanitizeText(text, context), new UTF8Encoding(false));
            return $"INCLUDED from file fallback ({Encoding.UTF8.GetByteCount(text)} bytes before sanitization)";
        }
        catch (Exception ex)
        {
            return $"NOT AVAILABLE (could not snapshot {Path.GetFileName(sourcePath)}: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static int CsvDataRowCount(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return 0;
        string[] lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return Math.Max(0, lines.Length - 1);
    }

    private static string SanitizeText(string text, SupportBundleContext context)
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(user)) text = text.Replace(user, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Environment.MachineName)) text = text.Replace(Environment.MachineName, "<MACHINE_REDACTED>", StringComparison.OrdinalIgnoreCase);
        if (!context.IncludeVin)
        {
            if (!string.IsNullOrWhiteSpace(context.Vin)) text = text.Replace(context.Vin, "<VIN_REDACTED>", StringComparison.OrdinalIgnoreCase);
            text = Regex.Replace(text, @"(?<![A-Z0-9])[A-HJ-NPR-Z0-9]{17}(?![A-Z0-9])", "<VIN_REDACTED>", RegexOptions.IgnoreCase);
        }
        return text;
    }
}
