using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class MainWindow
{
    private readonly Td5KLineConnection _td5 = new(new OBDWorkbench.Platform.Windows.WindowsKLineTransport());
    private bool _td5Mode;
    private readonly Dictionary<string, DateTime> _td5DtcFirstRead = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _td5PreviousDtcCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, string> _td5StaticConfig = new();
    private const string ValidatedEarlyTd5Fingerprint = "11547216AB25EBE0";

    private bool ConnectionIsOpen => _ssmMode ? _ssm.IsOpen : _td5Mode ? _td5.IsOpen : _elm.IsOpen;

    private async Task<bool> TryConnectTd5Async(string portName, string elmFailure)
    {
        try
        {
            StartupStatusChanged?.Invoke($"{portName}: probing native Land Rover TD5 K-Line…");
            AppendRaw($"{elmFailure}\r\nTrying native Land Rover TD5 K-Line at 10400 baud…\r\n");
            SetConnectionUi("Probing Land Rover TD5 native K-Line…", false);
            await _td5.OpenAndAuthenticateAsync(portName, _connectCancellation?.Token ?? default);

            _ssm.Close();
            _ssmMode = false;
            _gemsMode = false;
            _td5Mode = true;
            _currentTransport = "Raw K-Line";
            _currentPort = portName;
            _currentBaud = 10400;
            _currentConnectionId = portName;
            _currentAdapterIdentity = "Raw K-Line / VAG KKL-compatible";
            AdapterText.Text = _currentAdapterIdentity;
            TransportText.Text = "Raw K-Line";
            ProtocolText.Text = "Land Rover TD5 native K-Line • 10400 baud • Service 0x21";
            _confirmedProtocol = "TD5_NATIVE_KLINE";
            _currentVehicleSignature = "LANDROVER-TD5-NATIVE";
            HandleTrendVehicleDetected(_currentVehicleSignature);
            HandleDashboardVehicleDetected(_currentVehicleSignature);
            _currentVehicleProfile = new VehicleProfile
            {
                Signature = _currentVehicleSignature,
                DisplayName = "Land Rover Defender TD5",
                Protocol = ProtocolText.Text,
                ProtocolNumber = "TD5_NATIVE_KLINE",
                PreferredProtocolNumber = "TD5_NATIVE_KLINE",
                ManufacturerFamily = "Land Rover",
                SupportedPidEvidence = string.Join(" ", Td5SignalCatalog.ConfirmedPids.Select(p => p.ToString("X2"))),
                FirstSeenUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                CalibrationAccepted = true,
                UseResponseCount = false,
                InterRequestDelayMs = GetInterRequestDelayMs()
            };
            _settings.VehicleProfiles[_currentVehicleSignature] = _currentVehicleProfile;
            _settings.LastTd5Port = portName;
            _settings.LastSuccessfulPort = portName;
            _settings.LastUsedPort = portName;
            _settings.LastTransportIndex = 0;
            SaveSettingsAndGlobalConfiguration();

            LoadTd5Definitions();
            DetectedModules.Clear();
            DetectedModules.Add(new DetectedModule
            {
                Source = "Engine",
                Name = "Land Rover TD5 Engine ECU",
                Protocol = "Native K-Line 10400 / Service 0x21",
                SupportedPids = string.Join(" ", Td5SignalCatalog.ConfirmedPids.Select(p => p.ToString("X2"))),
                Notes = "Auto-discovered using TD5 StartCommunication, diagnostic session and SecurityAccess seed/key."
            });
            DiscoverySummaryText.Text = "Land Rover TD5 • native raw K-Line • engine ECU authenticated";
            CalibrationText.Text = "Static TD5 identification/configuration values are exposed in Live Data and PIDs.";

            EnsurePublicTestRecordingStarted("TD5");
            await ReadTd5StaticSnapshotsAsync();
            try { await RefreshTd5DtcsAsync(false); } catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 INITIAL DTC READ unavailable: {ex.Message}"); }

            ProtocolCombo.IsEnabled = false;
            SetTimingControlsEnabled(false);
            UseResponseCountCheck.IsChecked = false;
            UseResponseCountCheck.IsEnabled = false;
            StabilityTestButton.IsEnabled = false;
            BenchmarkButton.IsEnabled = false;
            ApplyTimingButton.IsEnabled = false;
            PollingButton.IsEnabled = true;
            ConnectButton.Content = "Disconnect";
            RawCommandBox.IsEnabled = false;
            RawSendButton.IsEnabled = false;
            if (EraseDtcsButton is not null) EraseDtcsButton.IsEnabled = _ecuProfileKnown;
            DtcStatusText.Text = DtcStatusText.Text + (_ecuProfileKnown
                ? " • Clear validated for this TD5 fingerprint"
                : " • Clear disabled: unrecognized TD5 fingerprint; create a Support ZIP for analysis");
            SetConnectionUi("Land Rover TD5 ECU connected", true);
            AppendRaw("TD5 native K-Line authentication succeeded. OBDWorkbench is using raw serial K-Line, not ELM commands.\r\n");
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 CONNECT SUCCESS port={portName} protocol=native-kline-10400 security=unlocked");

            StartPolling();
            if (_pollCts is not null && _firstPollCompletion is not null)
                await Task.WhenAny(_firstPollCompletion.Task, Task.Delay(1500));
            if (_settings.AutoCollapseConnectionPanel && _pollCts is not null)
                SetConnectionPanelCollapsed(true);
            UpdateCompactStatus();
            return true;
        }
        catch (Exception ex)
        {
            _td5Mode = false;
            _td5.Close();
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 AUTO-DISCOVERY FAILED port={portName} {ex.Message}");
            AppendRaw("TD5 native K-Line probe failed: " + ex.Message + "\r\n");
            return false;
        }
    }

    private void LoadTd5Definitions()
    {
        _definitions = Td5SignalCatalog.CreateDefinitions();
        PidGrid.ItemsSource = _definitions;
        PidPathText.Text = "Built-in Land Rover TD5 native K-Line catalog";
        LiveValues.Clear();
        foreach (PidDefinition definition in _definitions.Where(d => d.Enabled))
        {
            LiveValues.Add(new PidLiveValue
            {
                Definition = definition,
                Source = "Engine",
                ShowDetailedMetrics = DetailedPollMetricsCheck.IsChecked == true,
                CanTrend = CanTrendDefinition(definition),
                TrendUnit = UnitConverter.TargetUnit(definition.Units, _settings)
            });
        }
        _lastCompletedUtc.Clear();
        foreach (PidDefinition definition in _definitions.Where(d => d.Enabled && !d.DerivedOnly))
            _lastCompletedUtc[definition.Request] = DateTime.MinValue;
        _scheduler.Reset();
    }

    private async Task ReadTd5StaticSnapshotsAsync()
    {
        _td5StaticConfig.Clear();
        foreach (byte pid in new byte[] { 0x0E, 0x32, 0x33 })
        {
            try
            {
                Td5PidReadResult result = await _td5.ReadPidAsync(pid);
                ApplyTd5Result(result);
                string ascii = new string(result.Data.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray()).TrimEnd(' ', '.');
                _td5StaticConfig[pid] = ascii;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 IDENT pid={pid:X2} ascii={ascii} raw={result.RawHex}");
            }
            catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 static PID {pid:X2} unavailable: {ex.Message}"); }
        }

        string canonical = $"TD5|A0|0E={_td5StaticConfig.GetValueOrDefault((byte)0x0E, "")}|32={_td5StaticConfig.GetValueOrDefault((byte)0x32, "")}|33={_td5StaticConfig.GetValueOrDefault((byte)0x33, "")}";
        _ecuFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))[..16];
        _ecuProfileKnown = _ecuFingerprint.Equals(ValidatedEarlyTd5Fingerprint, StringComparison.OrdinalIgnoreCase);
        _ecuProfileName = _ecuProfileKnown ? "TD5 early/10P validated profile" : "TD5 unrecognized configuration";
        CalibrationText.Text = $"TD5 fingerprint: {_ecuFingerprint} • {_ecuProfileName}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] ECU_PROFILE family=TD5 fingerprint={_ecuFingerprint} profile={_ecuProfileName} known={_ecuProfileKnown} canonical={canonical}");
        _logger.LogEvent("ECU_PROFILE", _ecuFingerprint, source: "Engine", message: $"family=TD5 profile={_ecuProfileName} known={_ecuProfileKnown}");
    }

    private async Task RunTd5PollLoopAsync(CancellationToken token)
    {
        int consecutiveFailures = 0;
        while (!token.IsCancellationRequested && _td5.IsOpen)
        {
            PidDefinition? due = SelectNextPid(DateTime.UtcNow, out int waitMs);
            if (due is null)
            {
                await Task.Delay(waitMs, token);
                continue;
            }

            byte pid = Td5SignalCatalog.PidFromRequest(due.Request);
            try
            {
                Td5PidReadResult result = await _td5.ReadPidAsync(pid, token);
                DateTime completed = DateTime.UtcNow;
                _lastCompletedUtc[due.Request] = completed;
                _scheduler.Complete(due.Request, completed, result.RttMs);
                RecordQueryMetrics(result.RttMs, result.FirstByteMs, result.TailMs, completed);
                ApplyTd5Result(result);
                consecutiveFailures = 0;
                RecordPollOutcome(true);
                _firstPollCompletion?.TrySetResult(true);

                if (pid == 0x3B)
                    ApplyTd5DtcFrame(result, false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _queryErrors++;
                RecordPollOutcome(false);
                _scheduler.Complete(due.Request, DateTime.UtcNow, 0);
                _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 POLL ERROR pid={pid:X2} consecutive={consecutiveFailures} {ex.Message}");
                _logger.LogEvent("TD5_POLL_ERROR", ShortVehicleId(), due.Request, "Engine", message: ex.Message);
                foreach (PidLiveValue item in LiveValues.Where(v => v.Definition.Request.Equals(due.Request, StringComparison.OrdinalIgnoreCase)))
                {
                    item.DisplayValue = "ERROR";
                    item.ResetPerformance();
                }
                UpdateBusStatsUi();

                if (consecutiveFailures >= 5)
                {
                    try
                    {
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 SESSION RECOVERY attempting health/authentication after {consecutiveFailures} failures.");
                        await _td5.RecoverSessionAsync(token);
                        consecutiveFailures = 0;
                        _recoveryCount++;
                        UpdateLinkHealthUi();
                    }
                    catch (Exception recoverEx)
                    {
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 SESSION RECOVERY FAILED {recoverEx.Message}");
                        _linkLost = true;
                        break;
                    }
                }
            }

            int quiet = Math.Max(5, GetInterRequestDelayMs());
            if (!token.IsCancellationRequested && quiet > 0)
                await Task.Delay(quiet, token);
        }
    }

    private void ApplyTd5Result(Td5PidReadResult result)
    {
        string request = $"TD5-{result.Pid:X2}";
        List<PidDefinition> defs = _definitions.Where(d => d.Enabled && d.Request.Equals(request, StringComparison.OrdinalIgnoreCase)).ToList();
        bool firstData = false;
        foreach (PidDefinition definition in defs)
        {
            if (!Td5SignalCatalog.TryDecode(definition, result.Data, out double? numeric, out string display))
                continue;
            PidLiveValue? item = LiveValues.FirstOrDefault(v => ReferenceEquals(v.Definition, definition) && v.Source == "Engine");
            if (item is null)
            {
                item = new PidLiveValue { Definition = definition, Source = "Engine", ShowDetailedMetrics = DetailedPollMetricsCheck.IsChecked == true, CanTrend = CanTrendDefinition(definition), TrendUnit = UnitConverter.TargetUnit(definition.Units, _settings) };
                LiveValues.Add(item);
            }

            if (numeric.HasValue)
            {
                UpdateLiveValue(definition, numeric.Value, result.RawHex, result.RttMs, result.FirstByteMs, result.TailMs, "Engine");
                if ((definition.Units.Equals("status", StringComparison.OrdinalIgnoreCase) || definition.Units.Equals("state", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(display))
                    item.DisplayValue = display;
                string targetUnit = UnitConverter.TargetUnit(definition.Units, _settings);
                double logValue = UnitConverter.Convert(numeric.Value, definition.Units, targetUnit);
                if (CanTrendDefinition(definition))
                {
                    SignalOffsetSetting cal = GetSignalAdjustment(TrendKey(definition, "Engine"), targetUnit);
                    logValue = logValue * cal.Gain + cal.Offset;
                }
                _logger.Log(definition, numeric.Value, logValue, targetUnit, ShortVehicleId(), result.RawHex, result.RttMs, result.FirstByteMs, result.TailMs, "Engine", display);
                firstData = true;
            }
            else
            {
                item.DisplayValue = display;
                item.Raw = result.RawHex;
                item.Units = definition.Units.Equals("text", StringComparison.OrdinalIgnoreCase) || definition.Units.Equals("hex", StringComparison.OrdinalIgnoreCase) ? "" : definition.Units;
                item.CanTrend = false;
                item.LatestNumericValue = null;
                item.RecordSample(DateTime.Now, result.RttMs, result.FirstByteMs, result.TailMs);
                _logger.LogEvent("TD5_RAW_SAMPLE", ShortVehicleId(), definition.Request, "Engine", result.RawHex, definition.Name + "=" + display, result.RttMs, result.FirstByteMs, result.TailMs);
                firstData = true;
            }
        }
        if (firstData) OnFirstPollingData();
    }

    private async Task RefreshTd5DtcsAsync(bool showTraffic, CancellationToken token = default)
    {
        await _td5.EnsureAuthenticatedAsync(token);
        Td5PidReadResult result = await _td5.ReadPidAsync(0x3B, token);
        ApplyTd5Result(result);
        ApplyTd5DtcFrame(result, true);
        if (showTraffic) AppendRaw($">> TD5 21 3B\r\n<< {result.RawHex}\r\n");
    }

    private void ApplyTd5DtcFrame(Td5PidReadResult result, bool manualRefresh)
    {
        if (result.Data.Length < 35) return;
        DateTime now = DateTime.Now;
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 35; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((result.Data[i] & (1 << bit)) != 0)
                    active.Add($"{i + 1}-{bit + 1}");

        foreach (string code in active)
        {
            string key = "Engine|" + code;
            if (!_td5DtcFirstRead.ContainsKey(key)) _td5DtcFirstRead[key] = now;
        }

        DetectedDtcs.Clear();
        foreach (string code in active.OrderBy(Td5DtcSortKey))
        {
            Td5FaultDefinition fault = Td5FaultCatalog.Lookup(code);
            string description = string.IsNullOrWhiteSpace(fault.Evidence) ? fault.Description : fault.Description + " • " + fault.Evidence;
            DetectedDtcs.Add(new DiagnosticTroubleCode
            {
                Source = "Engine",
                Module = "Land Rover TD5 Engine ECU",
                Code = code,
                Status = fault.Status,
                Description = description,
                DefinitionSource = fault.Source + " • " + fault.Confidence,
                FirstRead = _td5DtcFirstRead["Engine|" + code],
                LastRead = now
            });
        }

        List<string> added = active.Except(_td5PreviousDtcCodes, StringComparer.OrdinalIgnoreCase).OrderBy(Td5DtcSortKey).ToList();
        List<string> cleared = _td5PreviousDtcCodes.Except(active, StringComparer.OrdinalIgnoreCase).OrderBy(Td5DtcSortKey).ToList();
        _td5PreviousDtcCodes = active;
        int current = active.Count(c => Td5DtcLookup(c).status == "Current");
        int logged = active.Count(c => Td5DtcLookup(c).status == "Logged");
        int unknown = active.Count - current - logged;
        string change = (added.Count > 0 || cleared.Count > 0) ? $" NEW: {string.Join(",", added)}  CLEARED: {string.Join(",", cleared)}" : "";
        DtcStatusText.Text = $"TD5 fault flags • Current {current} • Logged {logged} • Unknown {unknown} • Total {active.Count} • {now:HH:mm:ss}.{change}";
        _lastDtcPollUtc = DateTime.UtcNow;
        if (manualRefresh || added.Count > 0 || cleared.Count > 0)
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 DTC BITMAP current={current} logged={logged} unknown={unknown} total={active.Count} new={string.Join(';', added)} cleared={string.Join(';', cleared)} raw={result.RawHex}");
    }

    private async Task ClearTd5FaultsFromWorkbenchAsync()
    {
        if (!_td5.IsOpen) { SetError("Connect to a TD5 ECU before clearing faults."); return; }
        if (!_ecuProfileKnown)
        {
            SetError($"TD5 clearing is disabled for unrecognized fingerprint {_ecuFingerprint}. Reading/logging remains available; create a Support ZIP so this ECU flavor can be reviewed.");
            return;
        }

        bool resume = _pollCts is not null;
        if (resume) StopPolling("TD5_DTC_CLEAR");
        try
        {
            await _td5.EnsureAuthenticatedAsync();
            Td5PidReadResult before = await _td5.ReadPidAsync(0x3B);
            ApplyTd5DtcFrame(before, true);
            int beforeCount = before.Data.Take(35).Sum(b => Enumerable.Range(0, 8).Count(bit => (b & (1 << bit)) != 0));
            MessageBoxResult confirmation = MessageBox.Show(StartupOwner ?? this,
                $"Clear TD5 fault memory?\n\nECU fingerprint: {_ecuFingerprint} ({_ecuProfileName})\nCurrently asserted bitmap flags: {beforeCount}\n\n" +
                "Workbench will save the pre-clear bitmap, send the validated 31/DD routine exactly once, then reread the fault bitmap several times. Current physical faults may immediately return.",
                "Clear TD5 fault memory", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) return;

            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 CLEAR PRE fingerprint={_ecuFingerprint} flags={beforeCount} raw={before.RawHex}");
            _logger.LogEvent("TD5_CLEAR_PRE", _ecuFingerprint, "TD5-3B", "Engine", before.RawHex, $"flags={beforeCount}");
            Td5CommandResult clear = await _td5.ClearFaultMemoryOnceAsync();
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 CLEAR ONE_SHOT positive={clear.Positive} request={Td5KLineConnection.Hex(clear.Request)} response={Td5KLineConnection.Hex(clear.ResponseFrame)} raw={clear.RawHex}");
            _logger.LogEvent(clear.Positive ? "TD5_CLEAR_ACK" : "TD5_CLEAR_NO_ACK", _ecuFingerprint, "31DD", "Engine", clear.RawHex,
                $"request={Td5KLineConnection.Hex(clear.Request)} response={Td5KLineConnection.Hex(clear.ResponseFrame)}");
            if (!clear.Positive)
            {
                SetError("The TD5 clear command was sent once, but a positive acknowledgement was not recognized. It was NOT retried. Read faults again before taking any further action.");
                return;
            }

            _td5DtcFirstRead.Clear();
            foreach (int delay in new[] { 250, 1500, 4500 })
            {
                await Task.Delay(delay == 250 ? 250 : delay == 1500 ? 1250 : 3000);
                Td5PidReadResult after = await _td5.ReadPidAsync(0x3B);
                ApplyTd5DtcFrame(after, true);
                _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 CLEAR POST target_ms={delay} raw={after.RawHex}");
                _logger.LogEvent("TD5_CLEAR_POST", _ecuFingerprint, "TD5-3B", "Engine", after.RawHex, $"target_ms={delay}");
            }
            SetError("");
        }
        catch (Exception ex)
        {
            SetError("TD5 clear test failed: " + ex.Message);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TD5 CLEAR ERROR {ex}");
        }
        finally
        {
            if (resume && _td5.IsOpen) StartPolling();
        }
    }

    private async Task RunTd5DiscoveryAsync()
    {
        await _td5.EnsureAuthenticatedAsync();
        await ReadTd5StaticSnapshotsAsync();
        await RefreshTd5DtcsAsync(false);
        DiscoverySummaryText.Text = $"Land Rover TD5 • Engine ECU • {Td5SignalCatalog.ConfirmedPids.Length} confirmed Service 0x21 local identifiers";
        CalibrationText.Text = $"TD5 fingerprint: {_ecuFingerprint} • {_ecuProfileName} • unknown values remain visible as raw data.";
    }

    private static int Td5DtcSortKey(string code)
    {
        int dash = code.IndexOf('-');
        if (dash > 0 && int.TryParse(code.Substring(0, dash), out int group) && int.TryParse(code.Substring(dash + 1), out int bit))
            return group * 16 + bit;
        return int.MaxValue;
    }

    private static (string status, string description) Td5DtcLookup(string code)
    {
        Td5FaultDefinition d = Td5FaultCatalog.Lookup(code);
        return (d.Status, d.Description);
    }
}
