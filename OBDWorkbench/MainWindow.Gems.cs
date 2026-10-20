using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class MainWindow
{
    private bool _gemsMode;
    private readonly List<GemsReadResult> _gemsDealerReads = new();

    private async Task InitializeGemsAsync(CancellationToken token = default)
    {
        _ssm.Close();
        _ssmMode = false;
        _gemsMode = true;
        _td5Mode = false;
        _currentVehicleSignature = "";
        _currentVehicleProfile = null;
        _gemsDealerReads.Clear();

        StartupStatusChanged?.Invoke("GEMS • configuring ISO 14230 dealer session (5-baud init address DA)…");
        SetConnectionUi("Configuring Land Rover GEMS dealer session…", false);

        await RequireOkAsync("ATE0");
        await RequireOkAsync("ATS0");
        await RequireOkAsync("ATAL");
        await RequireOkAsync("ATH0");
        await RequireOkAsync("ATSP4");
        await RequireOkAsync("ATIIADA");
        await RequireOkAsync("ATSHC133F1");

        string init = await SendCommandAsync("ATSI", 5000, true, token);
        if (LooksLikeElmError(init))
            throw new InvalidOperationException("GEMS slow initialization was rejected by the adapter/ECU: " + CompactElm(init));

        string session = await SendCommandAsync("1002", 3500, true, token);
        if (!GemsProtocol.TryExtractPayload(session, "5002", out _))
            throw new InvalidOperationException("GEMS ECU did not accept diagnostic session 10 02. Response: " + CompactElm(session));

        string seedRaw = await SendCommandAsync("2701", 3500, true, token);
        if (!GemsProtocol.TryExtractSeed(seedRaw, out ushort seed))
            throw new InvalidOperationException("GEMS ECU did not return a SecurityAccess seed. Response: " + CompactElm(seedRaw));
        ushort key = GemsProtocol.CalculateSecurityKey(seed);
        string keyRaw = await SendCommandAsync($"2702{key:X4}", 3500, true, token);
        if (!GemsProtocol.TryExtractPayload(keyRaw, "6702", out string keyPayload))
            throw new InvalidOperationException("GEMS SecurityAccess key was not accepted. Response: " + CompactElm(keyRaw));

        _sessionLog.WriteLine($"[{DateTime.Now:O}] GEMS AUTH SUCCESS seed=0x{seed:X4} key=0x{key:X4} response={keyPayload}");
        EnsurePublicTestRecordingStarted("GEMS");
        await ReadGemsDealerSnapshotAsync(token);

        string stableEvidence = string.Join("|", _gemsDealerReads
            .Where(r => r.Request.StartsWith("2204", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Request)
            .Select(r => $"{r.Request}:{r.PayloadHex.Length}:{r.PayloadHex}"));
        if (string.IsNullOrWhiteSpace(stableEvidence))
            stableEvidence = string.Join("|", _gemsDealerReads.OrderBy(r => r.Request).Select(r => $"{r.Request}:{r.Positive}:{r.PayloadHex.Length}"));
        _ecuFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("GEMS|P4|DA|" + stableEvidence)))[..16];
        int positiveReads = _gemsDealerReads.Count(r => r.Positive);
        // No real GEMS vehicle has yet been validated against this Workbench implementation.
        // Preserve a stable fingerprint, but do not call any GEMS fingerprint "known" until
        // a returned public-test bundle has been reviewed and deliberately promoted.
        _ecuProfileName = positiveReads >= 3
            ? "GEMS dealer-session response family (unvalidated)"
            : "GEMS partial/unknown response family";
        _ecuProfileKnown = false;

        DetectedModules.Clear();
        DetectedModules.Add(new DetectedModule
        {
            Source = "Engine",
            Name = "Land Rover GEMS Engine ECU",
            Protocol = "ISO 14230 / 5-baud init DA / dealer session 10 02",
            SupportedPids = string.Join(" ", _gemsDealerReads.Where(r => r.Positive).Select(r => r.Request)),
            Notes = positiveReads >= 3
                ? "GEMS dealer session authenticated and known dealer reads answered. Fingerprint retained for public-test validation; writes remain disabled."
                : "GEMS dealer session authenticated, but response coverage is partial/different. Conservative read-only behavior is recommended."
        });
        DiscoverySummaryText.Text = $"Land Rover GEMS • dealer session authenticated • {_gemsDealerReads.Count(r => r.Positive)}/{_gemsDealerReads.Count} known reads answered";
        CalibrationText.Text = $"ECU fingerprint: {_ecuFingerprint} • {_ecuProfileName}";
        ProtocolText.Text = "ISO 14230-4 / GEMS dealer";
        PidPathText.Text = "Built-in Land Rover GEMS public-test catalog";
        ProtocolCombo.IsEnabled = false;
        SetTimingControlsEnabled(false);
        UseResponseCountCheck.IsChecked = false;
        UseResponseCountCheck.IsEnabled = false;
        StabilityTestButton.IsEnabled = false;
        BenchmarkButton.IsEnabled = false;
        ApplyTimingButton.IsEnabled = false;
        PollingButton.IsEnabled = false;
        ConnectButton.Content = "Disconnect";
        RawCommandBox.IsEnabled = true;
        RawSendButton.IsEnabled = true;
        if (EraseDtcsButton is not null) EraseDtcsButton.IsEnabled = false;
        DtcStatusText.Text = "GEMS DTC read uses a temporary generic ISO 9141 OBD session; GEMS clearing is disabled in this public-test preview.";
        SetConnectionUi("Land Rover GEMS ECU connected", true);
        _sessionLog.WriteLine($"[{DateTime.Now:O}] ECU_PROFILE family=GEMS fingerprint={_ecuFingerprint} profile={_ecuProfileName} known={_ecuProfileKnown}");
        _logger.LogEvent("ECU_PROFILE", _ecuFingerprint, source: "Engine", message: $"family=GEMS profile={_ecuProfileName} known={_ecuProfileKnown}");
        OnFirstPollingData();
        UpdateCompactStatus();
    }

    private async Task EnterGemsDealerSessionAsync(CancellationToken token = default)
    {
        await RequireOkAsync("ATE0");
        await RequireOkAsync("ATS0");
        await RequireOkAsync("ATAL");
        await RequireOkAsync("ATH0");
        await RequireOkAsync("ATSP4");
        await RequireOkAsync("ATIIADA");
        await RequireOkAsync("ATSHC133F1");
        string init = await SendCommandAsync("ATSI", 5000, true, token);
        if (LooksLikeElmError(init)) throw new InvalidOperationException("GEMS slow init failed: " + CompactElm(init));
        string session = await SendCommandAsync("1002", 3500, true, token);
        if (!GemsProtocol.TryExtractPayload(session, "5002", out _)) throw new InvalidOperationException("GEMS session 10 02 rejected: " + CompactElm(session));
        string seedRaw = await SendCommandAsync("2701", 3500, true, token);
        if (!GemsProtocol.TryExtractSeed(seedRaw, out ushort seed)) throw new InvalidOperationException("GEMS seed unavailable: " + CompactElm(seedRaw));
        ushort key = GemsProtocol.CalculateSecurityKey(seed);
        string keyRaw = await SendCommandAsync($"2702{key:X4}", 3500, true, token);
        if (!GemsProtocol.TryExtractPayload(keyRaw, "6702", out _)) throw new InvalidOperationException("GEMS security key rejected: " + CompactElm(keyRaw));
    }

    private async Task ReadGemsDealerSnapshotAsync(CancellationToken token = default)
    {
        _gemsDealerReads.Clear();
        LiveValues.Clear();
        _definitions = new List<PidDefinition>();
        foreach (GemsReadDefinition read in GemsProtocol.DealerReads)
        {
            token.ThrowIfCancellationRequested();
            string raw;
            bool positive = false;
            string payload = "";
            string display;
            try
            {
                raw = await SendCommandAsync(read.Request, 3500, true, token);
                string id = read.Request.Substring(2);
                positive = GemsProtocol.TryExtractPayload(raw, "62" + id, out payload);
                display = GemsProtocol.DecodeDisplay(read, raw);
            }
            catch (Exception ex)
            {
                raw = ex.Message;
                display = "Unavailable: " + ex.Message;
            }
            var result = new GemsReadResult(read.Request, read.Name, raw, payload, display, positive);
            _gemsDealerReads.Add(result);
            var def = new PidDefinition
            {
                Enabled = true,
                Name = read.Name,
                Request = "GEMS-" + read.Request,
                ResponseMode = "62",
                PID = read.Request.Substring(2),
                Formula = "GEMS",
                Units = "text",
                PollMs = 0,
                DefaultPriority = 1,
                Priority = 1,
                DiscoveryOnly = true,
                DisplayKind = "RawText",
                Category = "GEMS Dealer Data",
                Responders = "Engine",
                ExpectedResponses = 1,
                Notes = read.Notes
            };
            _definitions.Add(def);
            var live = new PidLiveValue { Definition = def, Source = "Engine", CanTrend = false };
            live.DisplayValue = display;
            live.Raw = raw;
            live.Units = "";
            LiveValues.Add(live);
            _logger.LogEvent(positive ? "GEMS_READ" : "GEMS_READ_UNAVAILABLE", "GEMS", read.Request, "Engine", raw, read.Name + "=" + display);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] GEMS READ request={read.Request} positive={positive} name={read.Name} display={display} raw={CompactElm(raw)}");
        }
        PidGrid.ItemsSource = _definitions;
    }

    private async Task RefreshGemsDtcsAsync(bool showTraffic, CancellationToken token = default)
    {
        if (!_elm.IsOpen) throw new InvalidOperationException("GEMS adapter is not connected.");
        bool resumeDealer = _gemsMode;
        try
        {
            await RequireOkAsync("ATE0");
            await RequireOkAsync("ATL0");
            await RequireOkAsync("ATH1");
            // Restore the normal OBD slow-init address before temporarily entering generic ISO 9141.
            await RequireOkAsync("ATIIA33");
            await RequireOkAsync("ATSP3");
            // Dealer mode set C1 33 F1 explicitly. Protocol changes do not reliably
            // reset a manually-set header on all ELM/STN implementations, so put the
            // standard ISO 9141 OBD request header back before Mode 03.
            await RequireOkAsync("ATSH686AF1");
            string raw = await SendCommandAsync("03", 6000, showTraffic, token, GetPostInteractiveQuietMs());
            var decoded = MultiEcuParser.DecodeMode03(raw);
            DetectedDtcs.Clear();
            DateTime now = DateTime.Now;
            foreach (DiagnosticTroubleCode d in decoded)
            {
                d.Module = "Land Rover GEMS Engine ECU";
                if (_dtcDatabase is not null)
                {
                    var dtcInfo = _dtcDatabase.Lookup(d.Code, "Land Rover", _currentVehicleSignature);
                    d.Description = dtcInfo.Description;
                    d.DefinitionSource = dtcInfo.Source;
                }
                else
                {
                    d.Description = "";
                    d.DefinitionSource = "Generic OBD-II / database unavailable";
                }
                d.FirstRead = now;
                d.LastRead = now;
                DetectedDtcs.Add(d);
            }
            DtcStatusText.Text = $"GEMS generic OBD DTCs • {decoded.Count} code(s) • {now:HH:mm:ss}";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] GEMS GENERIC DTC READ count={decoded.Count} raw={CompactElm(raw)}");
        }
        finally
        {
            if (resumeDealer)
            {
                try { await EnterGemsDealerSessionAsync(token); }
                catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] GEMS DEALER SESSION RESTORE FAILED {ex.Message}"); }
            }
        }
    }

    private static bool LooksLikeElmError(string response)
    {
        string s = response.ToUpperInvariant();
        return s.Contains("?") || s.Contains("UNABLE TO CONNECT") || s.Contains("BUS ERROR") || s.Contains("NO DATA") || s.Contains("ERROR");
    }

    private static string CompactElm(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace(">", " ").Trim();
}
