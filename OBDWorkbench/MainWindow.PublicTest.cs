using System.IO;
using System.Globalization;
using System.Text;
using System.Windows;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class MainWindow
{
    private sealed record SupportEventMarker(DateTime TimestampLocal, string Family, string Fingerprint, string Note);
    private readonly List<SupportEventMarker> _supportEventMarkers = new();

    private void EnsurePublicTestRecordingStarted(string family)
    {
        if (!_autoCsvEnabled || _logger.IsLogging) return;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBDWorkbench", "Logs");
            string path = _logger.Start(folder);
            LoggingButton.Content = "■ Recording";
            LogPathText.Text = path;
            _logger.LogEvent("RECORDING_START", string.IsNullOrWhiteSpace(_ecuFingerprint) ? family : _ecuFingerprint, message: $"Automatic public-test capture started during {family} initialization.");
            _sessionLog.WriteLine($"[{DateTime.Now:O}] RECORDING_START_EARLY family={family} path={path}");
        }
        catch (Exception ex)
        {
            _autoCsvEnabled = false;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] RECORDING_START_EARLY_FAILED family={family} {ex.Message}");
        }
    }

    private void MarkEvent_Click(object sender, RoutedEventArgs e)
    {
        string note = EventMarkerBox.Text.Trim();
        if (note.Length == 0)
        {
            SetError("Type a short event note first, for example 'Disconnected MAF' or 'MIL came on'.");
            return;
        }
        string family = _ssmMode ? "SSM" : _td5Mode ? "TD5" : _gemsMode ? "GEMS" : "OBDII";
        string fingerprint = string.IsNullOrWhiteSpace(_ecuFingerprint) ? ShortVehicleId() : _ecuFingerprint;
        DateTime now = DateTime.Now;
        _supportEventMarkers.Add(new SupportEventMarker(now, family, fingerprint, note));
        _sessionLog.WriteLine($"[{now:O}] USER_EVENT family={family} fingerprint={fingerprint} note={note}");
        _logger.LogEvent("USER_EVENT", fingerprint, message: note);
        AppendRaw($"*** USER EVENT {now:HH:mm:ss.fff}: {note} ***\r\n");
        EventMarkerBox.Clear();
        SetError("");
    }

    private void OpenHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string doc = _ssmMode || _startupConnectionFamily == StartupConnectionFamily.Ssm
                ? "SSM_CONNECTION.md"
                : _td5Mode || _startupConnectionFamily == StartupConnectionFamily.Td5
                    ? "TD5_CONNECTION.md"
                    : _gemsMode || _startupConnectionFamily == StartupConnectionFamily.Gems
                        ? "GEMS_CONNECTION.md"
                        : "QUICK_START.md";
            string path = Path.Combine(AppContext.BaseDirectory, "Docs", doc);
            if (!File.Exists(path)) throw new FileNotFoundException("Documentation file is missing.", path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { SetError("Could not open local documentation: " + ex.Message); }
    }

    private void CreateSupportBundle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string family = _ssmMode || _startupConnectionFamily == StartupConnectionFamily.Ssm ? "SSM"
                : _td5Mode || _startupConnectionFamily == StartupConnectionFamily.Td5 ? "TD5"
                : _gemsMode || _startupConnectionFamily == StartupConnectionFamily.Gems ? "GEMS" : "OBDII";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SUPPORT_BUNDLE_START family={family} fingerprint={_ecuFingerprint}");
            _logger.LogEvent("SUPPORT_BUNDLE_START", string.IsNullOrWhiteSpace(_ecuFingerprint) ? ShortVehicleId() : _ecuFingerprint, message: "Capturing support bundle evidence.");

            string identification = BuildSupportIdentification(family);
            string modules = BuildModuleCsv();
            string dtcs = BuildDtcCsv();
            string familyData = BuildFamilyDataCsv(family);
            string liveSnapshot = BuildLiveSnapshotCsv();
            string events = BuildEventsCsv(family);
            string sessionSnapshot = _sessionLog.SnapshotText();
            string csvSnapshot = _logger.SnapshotText();
            var ctx = new SupportBundleContext
            {
                Family = family,
                EcuProfile = _ecuProfileName,
                EcuFingerprint = _ecuFingerprint,
                EcuProfileKnown = _ecuProfileKnown,
                AdapterIdentity = _currentAdapterIdentity,
                Transport = _currentTransport,
                ConnectionId = _currentConnectionId,
                Protocol = ProtocolText.Text,
                SessionLogPath = _sessionLog.CurrentPath ?? "",
                CsvLogPath = _logger.CurrentPath ?? "",
                SessionLogText = sessionSnapshot,
                CsvLogText = csvSnapshot,
                Vin = _lastDiscovery?.Vin ?? "",
                IncludeVin = false,
                UserNotes = EventMarkerBox.Text.Trim(),
                IdentificationText = identification,
                ModuleCsv = modules,
                DtcCsv = dtcs,
                FamilyDataCsv = familyData,
                LiveSnapshotCsv = liveSnapshot,
                EventsCsv = events
            };
            string zip = SupportBundleService.Create(ctx);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SUPPORT_BUNDLE_CREATED family={family} fingerprint={_ecuFingerprint} path={zip}");
            SetError("");
            MessageBoxResult open = MessageBox.Show(StartupOwner ?? this,
                "Diagnostic support ZIP created:\n\n" + zip +
                "\n\nCaptureStatus.txt reports exactly which logs/snapshots were included. The bundle redacts the local Windows profile path, machine name, and VIN by default. Open the folder now?",
                "Support ZIP created", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (open == MessageBoxResult.Yes)
            {
                string? folder = Path.GetDirectoryName(zip);
                if (!string.IsNullOrWhiteSpace(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { SetError("Could not create support ZIP: " + ex.Message); }
    }

    private string BuildSupportIdentification(string family)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OBD Workbench public-test identification snapshot");
        sb.AppendLine($"Created: {DateTime.Now:O}");
        sb.AppendLine($"Family: {family}");
        sb.AppendLine($"Profile: {_ecuProfileName}");
        sb.AppendLine($"Fingerprint: {_ecuFingerprint}");
        sb.AppendLine($"Known profile: {_ecuProfileKnown}");
        sb.AppendLine($"Adapter: {_currentAdapterIdentity}");
        sb.AppendLine($"Transport: {_currentTransport}");
        sb.AppendLine($"Connection: {_currentConnectionId}");
        sb.AppendLine($"Protocol: {ProtocolText.Text}");
        sb.AppendLine($"Discovery: {DiscoverySummaryText.Text}");
        sb.AppendLine($"Calibration/identity: {CalibrationText.Text}");
        if (family == "SSM")
        {
            string systemId = _ssm.InitData.Length >= 4 ? Convert.ToHexString(_ssm.InitData.Skip(1).Take(3).ToArray()) : "UNKNOWN";
            string romId = _ssm.InitData.Length >= 9 ? Convert.ToHexString(_ssm.InitData.Skip(4).Take(5).ToArray()) : "UNKNOWN";
            sb.AppendLine($"SSM System ID: {systemId}");
            sb.AppendLine($"SSM ROM ID: {romId}");
            sb.AppendLine($"SSM BF raw data: {SsmProtocol.Hex(_ssm.InitData)}");
            sb.AppendLine($"SSM advertised parameters: {_ssm.Supported.Count}");
            sb.AppendLine($"SSM A8 batch limit: {_ssm.BatchLimit}");
            sb.AppendLine($"SSM DTC profile: {_ssmDtcProfile?.Format ?? "unavailable"}");
        }
        if (family == "TD5")
            foreach (var p in _td5StaticConfig.OrderBy(x => x.Key)) sb.AppendLine($"TD5 PID {p.Key:X2}: {p.Value}");
        return sb.ToString();
    }

    private string BuildModuleCsv()
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Source,Name,Protocol,SupportedPids,CalibrationId,CVN,Notes\r\n");
        foreach (var m in DetectedModules)
            sb.AppendLine(string.Join(',', new[] { m.Source, m.Name, m.Protocol, m.SupportedPids, m.CalibrationId, m.Cvn, m.Notes }.Select(Q)));
        return sb.ToString();
    }

    private string BuildDtcCsv()
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Source,Module,Code,Status,Description,DefinitionSource,FirstRead,LastRead\r\n");
        foreach (var d in DetectedDtcs)
            sb.AppendLine(string.Join(',', new[] { d.Source, d.Module, d.Code, d.Status, d.Description, d.DefinitionSource, d.FirstRead.ToString("O", CultureInfo.InvariantCulture), d.LastRead.ToString("O", CultureInfo.InvariantCulture) }.Select(Q)));
        return sb.ToString();
    }

    private string BuildLiveSnapshotCsv()
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("CapturedAt,Source,Signal,Request,DisplayValue,Units,NumericValue,UnadjustedNumericValue,DisplayGain,DisplayOffset,DecimalPlaces,IsCalculated,CalculatedId,TrendUnit,Raw,SampleStatus,Updated,RTT_ms,FirstByte_ms,Tail_ms\r\n");
        string captured = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
        foreach (var v in LiveValues.OrderBy(x => x.Source).ThenBy(x => x.Definition.Name))
        {
            string number = v.LatestNumericValue?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";
            sb.AppendLine(string.Join(',', new[]
            {
                captured, v.Source, v.Definition.Name, v.Definition.Request, v.DisplayValue, v.Units, number,
                v.UnadjustedNumericValue?.ToString("0.########", CultureInfo.InvariantCulture) ?? "",
                v.DisplayGain.ToString("0.########", CultureInfo.InvariantCulture),
                v.DisplayOffset.ToString("0.########", CultureInfo.InvariantCulture),
                v.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                v.IsCalculated ? "true" : "false", v.CalculatedId, v.TrendUnit,
                v.Raw, v.SampleStatus, v.Updated?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                v.LastRttMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
                v.LastFirstByteMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
                v.LastTailMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? ""
            }.Select(Q)));
        }
        return sb.ToString();
    }

    private string BuildEventsCsv(string family)
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Timestamp,Family,Fingerprint,Note\r\n");
        IEnumerable<SupportEventMarker> rows = _supportEventMarkers.Where(e => e.Family.Equals(family, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(_ecuFingerprint))
            rows = rows.Where(e => string.IsNullOrWhiteSpace(e.Fingerprint) || e.Fingerprint.Equals(_ecuFingerprint, StringComparison.OrdinalIgnoreCase));
        foreach (var e in rows.OrderBy(e => e.TimestampLocal))
            sb.AppendLine(string.Join(',', new[] { e.TimestampLocal.ToString("O", CultureInfo.InvariantCulture), e.Family, e.Fingerprint, e.Note }.Select(Q)));
        return sb.ToString();
    }

    private string BuildFamilyDataCsv(string family)
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        if (family == "SSM")
        {
            var sb = new StringBuilder("Kind,Key,Value\r\n");
            string systemId = _ssm.InitData.Length >= 4 ? Convert.ToHexString(_ssm.InitData.Skip(1).Take(3).ToArray()) : "UNKNOWN";
            string romId = _ssm.InitData.Length >= 9 ? Convert.ToHexString(_ssm.InitData.Skip(4).Take(5).ToArray()) : "UNKNOWN";
            sb.AppendLine(string.Join(',', new[] { "Identity", "SystemId", systemId }.Select(Q)));
            sb.AppendLine(string.Join(',', new[] { "Identity", "RomId", romId }.Select(Q)));
            sb.AppendLine(string.Join(',', new[] { "Identity", "BfRawData", SsmProtocol.Hex(_ssm.InitData) }.Select(Q)));
            sb.AppendLine(string.Join(',', new[] { "Capability", "AdvertisedCount", _ssm.Supported.Count.ToString(CultureInfo.InvariantCulture) }.Select(Q)));
            sb.AppendLine(string.Join(',', new[] { "Capability", "A8BatchLimit", _ssm.BatchLimit.ToString(CultureInfo.InvariantCulture) }.Select(Q)));
            foreach (var def in _ssm.Supported.OrderBy(x => x.FlagByte).ThenBy(x => x.FlagBit).ThenBy(x => x.Name))
                sb.AppendLine(string.Join(',', new[] { "Parameter", def.CsvKey, $"{def.Name}; address={def.AddressText}; flag={def.FlagByte}-{def.FlagBit}; class={def.PollClass}; unit={def.Unit}" }.Select(Q)));
            if (_ssmDtcProfile is not null)
            {
                sb.AppendLine(string.Join(',', new[] { "DtcProfile", "Format", _ssmDtcProfile.Format }.Select(Q)));
                foreach (var bank in _ssmDtcProfile.Banks)
                    sb.AppendLine(string.Join(',', new[] { "DtcBank", bank.Name, $"current={bank.CurrentStart:X6}; history={bank.HistoryStart:X6}; length={bank.Length}" }.Select(Q)));
                foreach (var pair in _ssmLastDtcValues.OrderBy(p => p.Key))
                    sb.AppendLine(string.Join(',', new[] { "DtcByte", pair.Key.ToString("X6"), pair.Value.ToString("X2") }.Select(Q)));
            }
            return sb.ToString();
        }
        if (family == "GEMS")
        {
            var sb = new StringBuilder("Request,Name,Positive,Payload,Display,RawResponse\r\n");
            foreach (var r in _gemsDealerReads)
                sb.AppendLine(string.Join(',', new[] { r.Request, r.Name, r.Positive.ToString(), r.PayloadHex, r.DisplayValue, r.RawResponse }.Select(Q)));
            return sb.ToString();
        }
        if (family == "TD5")
        {
            var sb = new StringBuilder("Kind,Key,Value\r\n");
            foreach (var p in _td5StaticConfig.OrderBy(x => x.Key)) sb.AppendLine(string.Join(',', new[] { "Identity", p.Key.ToString("X2"), p.Value }.Select(Q)));
            foreach (string code in _td5PreviousDtcCodes.OrderBy(Td5DtcSortKey))
            {
                Td5FaultDefinition f = Td5FaultCatalog.Lookup(code);
                sb.AppendLine(string.Join(',', new[] { "Fault", code, $"{f.Status}; {f.Description}; {f.Confidence}; {f.Evidence}" }.Select(Q)));
            }
            return sb.ToString();
        }
        return "";
    }
}
