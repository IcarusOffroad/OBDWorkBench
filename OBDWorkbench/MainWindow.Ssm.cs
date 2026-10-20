using OBDWorkbench.Models;
using OBDWorkbench.Services;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace OBDWorkbench;

public partial class MainWindow
{
    private readonly SsmConnection _ssm = new(new OBDWorkbench.Platform.Windows.WindowsSsmTransport());
    private bool _ssmMode;
    private readonly Dictionary<string, SsmParameterDefinition> _ssmDefinitions = new();
    private DtcProfile? _ssmDtcProfile;
    private SsmDtcDecoder? _ssmDtcDecoder;
    private readonly Dictionary<string, DateTime> _ssmDtcFirstRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ssmPreviousDtcKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, byte> _ssmLastDtcValues = new();
    private bool _ssmClearAttempted;

    private async Task ConnectSsmAsync(string port)
    {
        _elm.Close();
        _td5.Close();
        _td5Mode = false;
        _gemsMode = false;
        _ssm.Traffic -= LogSsmTraffic;
        _ssm.Traffic += LogSsmTraffic;
        _ssmDtcProfile = null;
        _ssmDtcDecoder = null;
        _ssmDtcFirstRead.Clear();
        _ssmPreviousDtcKeys.Clear();
        _ssmLastDtcValues.Clear();
        StartupStatusChanged?.Invoke($"SSM: initializing {port} at 4800 baud…");
        await _ssm.OpenAsync(port, _connectCancellation?.Token ?? default);
        _ssmMode = true;
        _settings.LastSsmPort = port;
        _settings.LastSuccessfulPort = port;
        _settings.LastUsedPort = port;
        _settings.LastTransportIndex = 0;
        SaveSettingsAndGlobalConfiguration();
        _currentTransport = "Raw K-Line";
        _currentPort = port;
        _currentBaud = 4800;
        _currentConnectionId = port;
        _currentAdapterIdentity = "KKL / SSM raw serial";
        _confirmedProtocol = "SSM2_KLINE";
        string systemId = _ssm.InitData.Length >= 4 ? Convert.ToHexString(_ssm.InitData.Skip(1).Take(3).ToArray()) : "UNKNOWN";
        string romId = _ssm.InitData.Length >= 9 ? Convert.ToHexString(_ssm.InitData.Skip(4).Take(5).ToArray()) : "UNKNOWN";
        string capabilityHex = _ssm.InitData.Length > 9 ? Convert.ToHexString(_ssm.InitData.Skip(9).ToArray()) : "";
        string fingerprintCanonical = $"SSM2|BF|SYS={systemId}|ROM={romId}|CAP={capabilityHex}";
        _ecuFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintCanonical)))[..16];
        _ecuProfileKnown = systemId.Equals("A2100E", StringComparison.OrdinalIgnoreCase) && romId.Equals("2D1A255105", StringComparison.OrdinalIgnoreCase);
        _ecuProfileName = _ecuProfileKnown ? "SSM2 validated EZ30 development profile" : "SSM2 unrecognized ECU profile";
        _currentVehicleSignature = $"SSM-{systemId}-{romId}";
        HandleTrendVehicleDetected(_currentVehicleSignature);
        HandleDashboardVehicleDetected(_currentVehicleSignature);
        AdapterText.Text = _currentAdapterIdentity;
        TransportText.Text = "Raw K-Line";
        ProtocolText.Text = "Subaru SSM2 • 4800 baud • BF / A8";
        EnsurePublicTestRecordingStarted("SSM");
        LoadSsmDefinitions();
        DetectedModules.Clear();
        DetectedModules.Add(new DetectedModule
        {
            Source = "SSM10",
            Name = "Subaru Engine ECU",
            Protocol = "SSM2 K-Line",
            SupportedPids = string.Join(" ", _ssmDefinitions.Keys),
            Notes = $"BF advertised support; physical sensor validity is not established by capability flags. Profile={_ecuProfileName}; fingerprint={_ecuFingerprint}."
        });
        DiscoverySummaryText.Text = $"SSM engine ECU • {_ssm.Supported.Count} advertised parameters • fingerprint {_ecuFingerprint}";
        CalibrationText.Text = $"SSM System ID {systemId} • ROM {romId} • {_ecuProfileName}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM IDENT system={systemId} rom={romId} init={SsmProtocol.Hex(_ssm.InitData)} supported={_ssm.Supported.Count}");
        _sessionLog.WriteLine($"[{DateTime.Now:O}] ECU_PROFILE family=SSM fingerprint={_ecuFingerprint} profile={_ecuProfileName} known={_ecuProfileKnown} canonical={fingerprintCanonical}");
        _logger.LogEvent("ECU_PROFILE", _ecuFingerprint, source: "SSM10", message: $"family=SSM profile={_ecuProfileName} known={_ecuProfileKnown}");

        try
        {
            string mapPath = Path.Combine(AppContext.BaseDirectory, "ReferenceData", "SSM_DTC_MAP.csv");
            _ssmDtcProfile = DtcProfile.FromInit(_ssm.InitData);
            _ssmDtcDecoder = new SsmDtcDecoder(DataDirectory, mapPath);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM DTC PROFILE format={_ssmDtcProfile.Format} banks={_ssmDtcProfile.Banks.Count} addresses={_ssmDtcProfile.Addresses.Length}");
            foreach (DtcBank bank in _ssmDtcProfile.Banks)
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM DTC BANK name={bank.Name} current={bank.CurrentStart:X6} history={bank.HistoryStart:X6} length={bank.Length}");
            DtcStatusText.Text = _ecuProfileKnown
                ? "SSM DTC banks recognized. Read and guarded engine-memory reset are enabled."
                : "SSM DTC banks recognized. Read is enabled; clear is disabled for this unvalidated ECU fingerprint.";
        }
        catch (Exception ex)
        {
            _ssmDtcProfile = null;
            _ssmDtcDecoder = null;
            DtcStatusText.Text = "SSM DTC layout could not be verified for this ECU. Live data remains available; raw BF evidence is retained.";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM DTC PROFILE unavailable: {ex.Message}");
        }

        ProtocolCombo.IsEnabled = false;
        SetTimingControlsEnabled(false);
        UseResponseCountCheck.IsChecked = false;
        UseResponseCountCheck.IsEnabled = false;
        StabilityTestButton.IsEnabled = false;
        BenchmarkButton.IsEnabled = false;
        ApplyTimingButton.IsEnabled = false;
        RawCommandBox.IsEnabled = false;
        RawSendButton.IsEnabled = false;
        EraseDtcsButton.IsEnabled = _ssmDtcProfile is not null && _ecuProfileKnown;
        ReplayRefreshDtcButton.IsEnabled = _ssmDtcProfile is not null;
        ReplayDiscoveryButton.Content = "SSM capabilities";
        PollingButton.IsEnabled = true;
        ConnectButton.Content = "Disconnect";
        SetConnectionUi("Subaru SSM ECU connected", true);
        AppendRaw($"SSM BF initialization succeeded: {_ssm.Supported.Count} parameters. Sensor values are ECU-reported and unvalidated.\r\n");

        if (_ssmDtcProfile is not null)
        {
            try { await RefreshSsmDtcsAsync(false); }
            catch (Exception ex)
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM INITIAL DTC READ unavailable: {ex.Message}");
                DtcStatusText.Text = "SSM DTC layout recognized, but the initial bank read failed. Use Refresh DTCs and include a Support ZIP.";
            }
        }

        StartPolling();
        UpdateCompactStatus();
    }

    private void LogSsmTraffic(string line)
    {
        if (line.StartsWith("CLEAR WRITE:", StringComparison.Ordinal)) _ssmClearAttempted = true;
        _sessionLog.WriteLine($"[{DateTime.Now:O}] {line}");
    }

    private void LoadSsmDefinitions()
    {
        _ssmDefinitions.Clear();
        _definitions = _ssm.Supported.Select(p =>
        {
            string key = "SSM-" + p.CsvKey;
            _ssmDefinitions[key] = p;
            return new PidDefinition
            {
                Enabled = true,
                Name = p.Name,
                Request = key,
                PID = p.AddressText,
                ResponseMode = "E8",
                Units = p.Unit,
                Category = "SSM " + p.PollClass,
                PollMs = p.PollClass == SsmPollClass.Fast ? 250 : p.PollClass == SsmPollClass.Snapshot ? 10000 : 2000,
                Responders = "SSM10",
                Min = p.Unit == "°C" ? -40 : p.Name.Contains("Correction") || p.Name.Contains("Learning") || p.Name.Contains("Relative") ? -100 : 0,
                Max = p.ValueBit.HasValue ? 1 : p.Unit == "RPM" ? 8000 : p.Unit == "°C" ? 150 : p.Unit == "V" ? 20 : p.Unit == "km/h" ? 240 : 100,
                Notes = $"BF supported; sensor validity unverified. Address {p.AddressText}; raw bytes retained.",
                DisplayKind = p.ValueBit.HasValue ? "status" : ""
            };
        }).ToList();
        PidGrid.ItemsSource = _definitions;
        PidPathText.Text = "Built-in SSM v0.1.3 catalog • capability filtered";
        LiveValues.Clear();
        foreach (var d in _definitions)
            LiveValues.Add(new PidLiveValue
            {
                Definition = d,
                Source = "SSM10",
                CanTrend = CanTrendDefinition(d),
                TrendUnit = UnitConverter.TargetUnit(d.Units, _settings),
                ShowDetailedMetrics = DetailedPollMetricsCheck.IsChecked == true
            });
    }

    private async Task<List<DtcFlag>> RefreshSsmDtcsAsync(bool showTraffic, CancellationToken token = default)
    {
        DtcProfile profile = _ssmDtcProfile ?? throw new InvalidOperationException("This ECU does not have a verified SSM DTC-bank layout.");
        SsmDtcDecoder decoder = _ssmDtcDecoder ?? throw new InvalidOperationException("SSM DTC decoder is unavailable.");
        var result = await _ssm.ReadAsync(profile.Addresses, token);
        List<DtcFlag> flags = decoder.Decode(profile, result.Values);
        DateTime now = DateTime.Now;

        _ssmLastDtcValues.Clear();
        foreach (var pair in result.Values) _ssmLastDtcValues[pair.Key] = pair.Value;

        var keys = flags.Select(f => $"{f.Bank}|{f.Address}|{f.Bit}|{f.Code}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            if (!_ssmDtcFirstRead.ContainsKey(key))
            {
                _ssmDtcFirstRead[key] = now;
                _logger.LogEvent("SSM_DTC_FIRST_SEEN", _ecuFingerprint, source: "SSM10", message: key);
            }
        }
        foreach (string gone in _ssmPreviousDtcKeys.Except(keys, StringComparer.OrdinalIgnoreCase))
            _logger.LogEvent("SSM_DTC_CLEARED_OR_GONE", _ecuFingerprint, source: "SSM10", message: gone);

        DetectedDtcs.Clear();
        foreach (DtcFlag flag in flags.OrderBy(f => f.Bank).ThenBy(f => f.Code).ThenBy(f => f.Address).ThenBy(f => f.Bit))
        {
            string key = $"{flag.Bank}|{flag.Address}|{flag.Bit}|{flag.Code}";
            DetectedDtcs.Add(new DiagnosticTroubleCode
            {
                Source = "SSM10",
                Module = "Subaru Engine ECU",
                Code = flag.Code,
                Status = flag.Bank,
                Description = flag.Description,
                DefinitionSource = flag.Mapped ? "SSM DTC map" : "Unmapped SSM bit",
                FirstRead = _ssmDtcFirstRead[key],
                LastRead = now
            });
        }

        string bytes = string.Join(' ', result.Values.OrderBy(p => p.Key).Select(p => $"{p.Key:X6}:{p.Value:X2}"));
        string newKeys = string.Join(';', keys.Except(_ssmPreviousDtcKeys, StringComparer.OrdinalIgnoreCase));
        string goneKeys = string.Join(';', _ssmPreviousDtcKeys.Except(keys, StringComparer.OrdinalIgnoreCase));
        _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM DTC SNAPSHOT format={profile.Format} flags={flags.Count} bytes={result.Values.Count} rtt={result.RttMs:0.0}ms batch={_ssm.BatchLimit} NEW={newKeys} GONE={goneKeys} RAW={bytes}");
        _logger.LogEvent("SSM_DTC_SNAPSHOT", _ecuFingerprint, source: "SSM10", raw: bytes,
            message: $"format={profile.Format}; flags={flags.Count}; unmapped={flags.Count(f => !f.Mapped)}; batch={_ssm.BatchLimit}", rttMs: result.RttMs);
        _ssmPreviousDtcKeys.Clear();
        _ssmPreviousDtcKeys.UnionWith(keys);
        _lastDtcPollUtc = DateTime.UtcNow;

        DtcStatusText.Text = flags.Count == 0
            ? $"SSM {profile.Format} DTC banks read completely at {now:HH:mm:ss}: no set bits in the selected banks. This is not a statement about OBD permanent codes."
            : $"SSM {profile.Format} DTC banks • {flags.Count} set flag(s) • {flags.Count(f => !f.Mapped)} unmapped • {now:HH:mm:ss}.";
        if (showTraffic) AppendRaw($"SSM DTC read: {flags.Count} set flag(s), {result.Values.Count} bank bytes, {result.RttMs:0} ms.\r\n");
        return flags;
    }

    private async Task ClearSsmFaultsFromWorkbenchAsync()
    {
        if (_ssmDtcProfile is null || _ssmDtcDecoder is null)
        {
            SetError("SSM fault clearing is unavailable because this ECU's DTC-bank layout was not verified.");
            return;
        }
        if (!_ecuProfileKnown)
        {
            SetError("SSM fault clearing is disabled for an unvalidated ECU fingerprint. Read faults and create a Support ZIP instead.");
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(StartupOwner ?? this,
            "Reset SSM engine fault memory?\n\n" +
            "Ignition must be ON, engine OFF, and the vehicle stationary. Workbench will take a fresh pre-clear snapshot, verify zero RPM/speed twice, send ONE memory-reset request, reconnect, and read the DTC banks again.\n\n" +
            "This operation may also reset learned engine adaptations. It does not claim to clear OBD permanent codes.\n\n" +
            "Continue?",
            "Reset SSM engine memory", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        bool resumePolling = _pollCts is not null;
        if (resumePolling) StopPolling("SSM_DTC_CLEAR");
        _ssmClearAttempted = false;
        try
        {
            List<DtcFlag> before = await RefreshSsmDtcsAsync(false);
            byte[] identity = _ssm.InitData.Take(9).ToArray();
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM CLEAR REQUEST preFlags={before.Count} fingerprint={_ecuFingerprint}");
            _logger.LogEvent("SSM_DTC_CLEAR_REQUEST", _ecuFingerprint, source: "SSM10", message: $"preFlags={before.Count}; guarded one-shot B8 memory reset");

            string outcome;
            try
            {
                bool acknowledged = await _ssm.ClearEngineMemoryOnceAsync();
                outcome = acknowledged ? "ACKNOWLEDGED" : "UNEXPECTED ACKNOWLEDGEMENT — outcome unknown";
            }
            catch (Exception ex)
            {
                outcome = _ssmClearAttempted ? "OUTCOME UNKNOWN: " + ex.Message : "BLOCKED BEFORE CLEAR WRITE: " + ex.Message;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM CLEAR {outcome}");
                _logger.LogEvent("SSM_DTC_CLEAR_RESULT", _ecuFingerprint, source: "SSM10", message: outcome);
                if (!_ssmClearAttempted)
                {
                    DtcStatusText.Text = outcome;
                    SetError(outcome);
                    return;
                }
            }

            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM CLEAR RESULT {outcome}");
            _logger.LogEvent("SSM_DTC_CLEAR_RESULT", _ecuFingerprint, source: "SSM10", message: outcome);
            DtcStatusText.Text = outcome + "; waiting for ECU reconnect and verification…";
            await Task.Delay(1500);
            _ssm.Close();
            await _ssm.OpenAsync(_currentPort);
            if (!identity.SequenceEqual(_ssm.InitData.Take(9)))
                throw new InvalidDataException("SSM ECU identification changed after clear; post-clear comparison stopped.");

            _ssmDtcProfile = DtcProfile.FromInit(_ssm.InitData);
            List<DtcFlag> after = await RefreshSsmDtcsAsync(false);
            string result = after.Count == 0
                ? "Post-clear selected SSM banks contain no set flags. Recheck after a key cycle if validating persistence."
                : $"Post-clear: {after.Count} flag(s) remain or returned (before: {before.Count}).";
            DtcStatusText.Text = outcome + " • " + result;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM CLEAR VERIFY {result}");
            SetError("");
        }
        catch (Exception ex)
        {
            string message = _ssmClearAttempted
                ? "SSM clear outcome/verification problem: " + ex.Message + " Key-cycle, reconnect, and read DTCs before retrying."
                : "SSM clear blocked/failed before write: " + ex.Message;
            DtcStatusText.Text = message;
            SetError(message);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM CLEAR FAILED attempted={_ssmClearAttempted} {ex}");
            _logger.LogEvent("SSM_DTC_CLEAR_FAILED", _ecuFingerprint, source: "SSM10", message: message);
        }
        finally
        {
            _lastDtcPollUtc = DateTime.UtcNow;
            if (resumePolling && _ssm.IsOpen && _pollCts is null) StartPolling();
        }
    }

    private async Task RunSsmPollLoopAsync(CancellationToken token)
    {
        var last = new Dictionary<string, DateTime>();
        int failures = 0;
        while (!token.IsCancellationRequested && _ssm.IsOpen)
        {
            if (_ssmDtcProfile is not null && (DateTime.UtcNow - _lastDtcPollUtc).TotalSeconds >= 60)
            {
                try { await RefreshSsmDtcsAsync(false, token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] SSM PERIODIC DTC READ failed: {ex.Message}");
                    _lastDtcPollUtc = DateTime.UtcNow;
                }
            }

            var now = DateTime.UtcNow;
            var due = _definitions.Where(d => d.Enabled && (!last.TryGetValue(d.Request, out var at) || (now - at).TotalMilliseconds >= Math.Max(50, d.PollMs))).ToList();
            if (due.Count == 0) { await Task.Delay(20, token); continue; }
            try
            {
                var result = await _ssm.ReadAsync(due.SelectMany(d => _ssmDefinitions[d.Request].Addresses), token);
                token.ThrowIfCancellationRequested();
                RecordQueryMetrics(result.RttMs, 0, 0, DateTime.UtcNow);
                OnFirstPollingData();
                foreach (var d in due)
                {
                    var p = _ssmDefinitions[d.Request];
                    double? value = p.Decode(result.Values);
                    if (!value.HasValue) continue;
                    string raw = p.RawHex(result.Values);
                    string status = "ECU-reported; sensor validity unverified";
                    if (p.Name.Contains("Fuel Temperature", StringComparison.OrdinalIgnoreCase) && value == -40)
                        status = "Unvalidated: raw zero / -40 °C may indicate disconnected sensor";
                    UpdateLiveValue(d, value.Value, raw, result.RttMs, 0, 0, "SSM10");
                    var item = LiveValues.FirstOrDefault(v => ReferenceEquals(v.Definition, d));
                    if (item is not null) item.SampleStatus = status + " • Raw: " + raw;
                    string unit = UnitConverter.TargetUnit(d.Units, _settings);
                    double logValue = UnitConverter.Convert(value.Value, d.Units, unit);
                    if (CanTrendDefinition(d))
                    {
                        SignalOffsetSetting cal = GetSignalAdjustment(TrendKey(d, "SSM10"), unit);
                        logValue = logValue * cal.Gain + cal.Offset;
                    }
                    _logger.Log(d, value.Value, logValue, unit, ShortVehicleId(), raw, result.RttMs, 0, 0, "SSM10", sampleStatus: status);
                    last[d.Request] = DateTime.UtcNow;
                }
                failures = 0;
                RecordPollOutcome(true);
                _firstPollCompletion?.TrySetResult(true);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _queryErrors++;
                RecordPollOutcome(false);
                LogSsmTraffic("POLL ERROR " + ex.Message);
                _logger.LogEvent("SSM_POLL_ERROR", ShortVehicleId(), message: ex.Message);
                foreach (var item in LiveValues) item.DisplayValue = "STALE";
                if (++failures >= 5) { _linkLost = true; _ssm.Close(); break; }
                await Task.Delay(250, token);
            }
        }
    }
}
