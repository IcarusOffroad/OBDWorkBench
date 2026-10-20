using OBDWorkbench.Services;
using OBDWorkbench.Models;

int tests = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); tests++; Console.WriteLine("PASS: " + name); }
await SsmRegression.Run(Check);
GpsRegression.Run(Check);
bool skipIpc = args.Contains("--no-ipc");
await GpsServiceRegression.Run(Check, skipIpc);

var offsetSettingsPath = Path.Combine(Path.GetTempPath(), "obd-offset-settings-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var offsetStore = new AppSettingsStore(offsetSettingsPath);
    var offsetSettings = new AppSettingsData();
    offsetSettings.SignalOffsets["0123|Manifold Absolute Pressure|D1"] = new SignalOffsetSetting { Gain = 1.06, Offset = -14.7, Unit = "psi", DecimalPlaces = 2 };
    offsetSettings.DashboardGridColumns = 4;
    offsetSettings.CalculatedSignals.Add(new CalculatedSignalSetting { Id = "boost", Name = "Boost", SourceKey = "0123|Manifold Absolute Pressure|D1", SourceName = "Manifold Absolute Pressure", SourceUnit = "psi", Units = "psi", Formula = "A-B", B = 12.3, DecimalPlaces = 1 });
    offsetStore.Save(offsetSettings);
    AppSettingsData offsetReloaded = offsetStore.Load();
    Check(offsetReloaded.SignalOffsets.TryGetValue("0123|Manifold Absolute Pressure|D1", out SignalOffsetSetting? loadedOffset) &&
          loadedOffset is not null && Math.Abs(loadedOffset.Gain - 1.06) < 0.000001 && Math.Abs(loadedOffset.Offset + 14.7) < 0.000001 && loadedOffset.Unit == "psi" && loadedOffset.DecimalPlaces == 2,
          "signal gain/offset/precision persist with display unit");
    Check(offsetReloaded.DashboardGridColumns == 4 && offsetReloaded.CalculatedSignals.Count == 1 && offsetReloaded.CalculatedSignals[0].Formula == "A-B",
          "dashboard grid and calculated signal persist");
}
finally
{
    try { if (File.Exists(offsetSettingsPath)) File.Delete(offsetSettingsPath); } catch { }
}
Check(CalculatedExpression.TryEvaluate("(A - B) * (C / D)", 212, 32, 5, 9, out double calcC, out _) && Math.Abs(calcC - 100) < 0.000001, "calculated expression evaluates Fahrenheit to Celsius example");
Check(CalculatedExpression.TryEvaluate("A * B", 100, 1.06, 0, 0, out double calcGain, out _) && Math.Abs(calcGain - 106) < 0.000001, "calculated expression supports gain-style multiplication");
Check(!CalculatedExpression.TryEvaluate("A / D", 1, 0, 0, 0, out _, out _), "calculated expression rejects division by zero");
Check(!CalculatedExpression.TryEvaluate("System.IO.File.Delete(A)", 1, 0, 0, 0, out _, out _), "calculated expression rejects arbitrary code/text");
Check(SignalScaleDefaults.StartsAtZero("010D", "Vehicle Speed"), "vehicle speed uses zero baseline");
Check(SignalScaleDefaults.StartsAtZero("010C", "Engine speed"), "RPM uses zero baseline even with alternate label");
Check(SignalScaleDefaults.StartsAtZero("01 11", "Position"), "Throttle PID uses zero baseline with spaced request");
Check(SignalScaleDefaults.StartsAtZero("custom", "Throttle Position B"), "Additional throttle channel uses zero baseline");
Check(!SignalScaleDefaults.StartsAtZero("0106", "Short Fuel Trim B1"), "Signed fuel trim retains automatic minimum");
Check(!SignalScaleDefaults.StartsAtZero("0105", "Coolant Temperature"), "Temperature retains automatic minimum");
var clock = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
var playback = new TrendPlayback();
playback.SetConnected(true, clock);
Check(playback.EndUtc(clock.AddSeconds(10)) == clock.AddSeconds(10), "live window follows time");
playback.Back(clock, 60, clock.AddMinutes(-10));
Check(playback.EndUtc(clock.AddSeconds(20)) == clock.AddSeconds(-30), "back half-window holds 90-to-30-second history");
Check(!playback.IsLive, "history navigation freezes display");
playback.Forward(clock, 60);
Check(playback.IsLive && playback.EndUtc(clock) == clock, "forward reaching present resumes live");
playback.Pause(clock);
Check(playback.EndUtc(clock.AddMinutes(1)) == clock, "pause holds absolute end time");
playback.Live(clock.AddMinutes(1));
Check(playback.EndUtc(clock.AddMinutes(1)) == clock.AddMinutes(1), "play returns to live edge");
playback.SetConnected(false, clock.AddMinutes(2));
Check(playback.EndUtc(clock.AddHours(2)) == clock.AddMinutes(2), "disconnect freezes view indefinitely");
playback.Back(clock.AddHours(2), 60, clock.AddMinutes(-10));
Check(playback.EndUtc(clock.AddHours(2)) == clock.AddSeconds(90), "offline back navigates retained samples");
playback.Forward(clock.AddHours(2), 60);
Check(playback.EndUtc(clock.AddHours(2)) == clock.AddMinutes(2), "offline forward stops at disconnect edge");
playback.Live(clock.AddHours(2));
Check(playback.EndUtc(clock.AddHours(2)) == clock.AddMinutes(2), "offline live button cannot erase final window");
playback.SetConnected(true, clock.AddHours(3));
Check(playback.EndUtc(clock.AddHours(3)) == clock.AddHours(3), "reconnect resumes following live edge");
playback.Back(clock, 60, clock.AddSeconds(-10));
Check(playback.EndUtc(clock) == clock, "short history clamps navigation to first available window");
const string bitmap = "48 6B D1 41 00 BE 3E B8 10 89\r48 6B 18 41 00 80 00 80 00 0C\r>";
const string cal = """
48 6B D1 49 04 01 35 36 30 34 A1
48 6B D1 49 04 02 31 36 33 34 A1
48 6B D1 49 04 03 41 46 00 00 5B
48 6B D1 49 04 04 00 00 00 00 D5
48 6B 18 49 04 01 35 36 30 34 E8
48 6B 18 49 04 02 31 33 38 36 EC
48 6B 18 49 04 03 41 42 00 00 9E
48 6B 18 49 04 04 00 00 00 49 65
""";
var ids = MultiEcuParser.DecodeMode09TextBySource(cal, 4);
Check(ids["D1"] == "56041634AF", "captured PCM CALID has no checksum ASCII");
Check(ids["18"] == "56041386AB", "TCM CALID separate; NUL padding cannot become ID text");
var reordered = string.Join('\n', cal.Split('\n').Reverse());
Check(MultiEcuParser.DecodeMode09TextBySource(reordered, 4)["D1"] == ids["D1"], "legacy sequence ordering");
Check(MultiEcuParser.DecodeMode09TextBySource(cal.Replace("48 6B D1 49 04 02", "48 6B D1 49 04 06"), 4).ContainsKey("D1") == false, "incomplete CALID rejected");
var support = MultiEcuParser.DecodeSupportedPids(bitmap);
const string fragmentedSubaru = "SEARCHING...\r48 \r6B \rE1 \r41 \r00 \rBF \rBE \rA8 \r11 \r0B \r>";
var fragmentedSupport = MultiEcuParser.DecodeSupportedPids(fragmentedSubaru);
Check(fragmentedSupport.ContainsKey("E1") && fragmentedSupport["E1"].Contains(0x0C) && fragmentedSupport["E1"].Contains(0x11), "Bluetooth byte-per-line ISO 9141 response is reconstructed");
const string busInitJoinedSubaru = "BUS INIT: OK48 \r6B \rE1 \r41 \r00 \rBF \rBE \rA8 \r11 \r0B \r>";
var busInitJoinedSupport = MultiEcuParser.DecodeSupportedPids(busInitJoinedSubaru);
Check(busInitJoinedSupport.ContainsKey("E1") && busInitJoinedSupport["E1"].Contains(0x0C) && busInitJoinedSupport["E1"].Contains(0x11), "BUS INIT OK joined to first header byte is reconstructed");
Check(MultiEcuParser.ParseMessages("48 6B E1 44 14\r>").Any(m => m.Source == "E1" && m.Payload.Length == 1 && m.Payload[0] == 0x44), "single-byte Mode 04 acknowledgement is parsed");
Check(MultiEcuParser.ParseMessages("48\r6B\rE1\r44\r14\r>").Any(m => m.Source == "E1" && m.Payload.Length == 1 && m.Payload[0] == 0x44), "fragmented single-byte Mode 04 acknowledgement is parsed");
Check(MultiEcuParser.ParseMessages("48 6B 18 44 0F\r>").Where(m => m.Payload.Length > 0 && m.Payload[0] == 0x44).Select(m => m.Source).SequenceEqual(new[]{"18"}), "Mode 04 acknowledgement retains its source module");
const string lateMode03AfterRpm = "48 6B D1 41 0C 10 00 00\r48 6B 18 43 00 00 4B\r>";
var mixedServices = MultiEcuParser.ParseMessages(lateMode03AfterRpm).ToList();
Check(mixedServices.Any(m => m.Source == "D1" && m.Payload.Length >= 2 && m.Payload[0] == 0x41 && m.Payload[1] == 0x0C) && mixedServices.Any(m => m.Source == "18" && m.Payload.Length >= 1 && m.Payload[0] == 0x43), "late multi-module Mode 03 frame remains distinguishable from following RPM response");
const string completedMode03 = "48 6B D1 43 01 32 01 41 01 38 75\r\n48 6B 18 43 00 00 00 00 00 00 0E\r\n>";
var completedMode03Sources = MultiEcuParser.ParseMessages(completedMode03).Where(m => m.Payload.Length > 0 && m.Payload[0] == 0x43).Select(m => m.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
Check(completedMode03Sources.SetEquals(new[]{"D1","18"}), "combined initial plus delayed Mode 03 traffic preserves both XJ responders");
Check(support["D1"].SetEquals(new byte[] {1,3,4,5,6,7,11,12,13,14,15,17,19,20,21,28}), "D1 BE3EB810 exact advertised set");
Check(support["18"].SetEquals(new byte[] {1,17}), "18 80008000 exact advertised set");
const string nissanBitmap = "48 6B 10 41 00 BF 9F B9 90 AB";
var nissanSupport = MultiEcuParser.DecodeSupportedPids(nissanBitmap);
Check(nissanSupport["10"].Contains(0x10) && nissanSupport["10"].Contains(0x18) && nissanSupport["10"].Contains(0x19), "1996 300ZX bitmap advertises MAF and both Bank 2 O2 PIDs");
var database = PidDatabase.Load(Path.Combine(AppContext.BaseDirectory, "PID_database.csv"));
Check(database.Single(d => d.Request == "010C").DefaultPriority == 5 && database.Single(d => d.Request == "0111").DefaultPriority == 5, "PID database preserves default polling priorities");
Check(database.Any(d => d.Request == "0108" && !d.DerivedOnly) && database.Any(d => d.Request == "0109" && !d.DerivedOnly), "bank 2 fuel-trim PIDs are present");
Check(database.Any(d => d.Request == "0110" && !d.DerivedOnly), "MAF PID 10 is present");
Check(database.Any(d => d.Request == "0118" && !d.DerivedOnly) && database.Any(d => d.Request == "0118" && d.DerivedOnly), "PID 18 exposes B2S1 voltage and trim from one request");
Check(database.Any(d => d.Request == "0119" && !d.DerivedOnly) && database.Any(d => d.Request == "0119" && d.DerivedOnly), "PID 19 exposes B2S2 voltage and trim from one request");
Check(nissanSupport["10"].All(pid => database.Any(d => d.Request == $"01{pid:X2}")), "database covers every Mode 01 PID advertised by the 1996 300ZX capture");
Check(database.Any(d => d.Request == "0124" && !d.DerivedOnly) && database.Any(d => d.Request == "0124" && d.DerivedOnly), "PID 24 exposes equivalence ratio and voltage from one request");
Check(database.Any(d => d.Request == "0128" && !d.DerivedOnly) && database.Any(d => d.Request == "0128" && d.DerivedOnly), "PID 28 exposes equivalence ratio and voltage from one request");
var dtcCount = database.Single(d => d.Name == "DTC Count");
Check(dtcCount.DerivedOnly && ObdParser.TryDecode(dtcCount, "48 6B E1 41 01 82 07 00 00 00", out double dtcCountValue, out _) && dtcCountValue == 2, "DTC count derived from monitor-status byte without MIL bit");
var fuel1 = database.Single(d => d.Name == "Fuel System 1");
var fuel2 = database.Single(d => d.Name == "Fuel System 2");
Check(ObdParser.TryDecode(fuel1, "48 6B E1 41 03 01 02 00", out double fuel1Value, out _) && StandardPidDisplay.Describe(fuel1, fuel1Value) == "Open loop (cold)", "fuel system 1 split decode");
Check(fuel2.DerivedOnly && ObdParser.TryDecode(fuel2, "48 6B E1 41 03 01 02 00", out double fuel2Value, out _) && StandardPidDisplay.Describe(fuel2, fuel2Value) == "Closed loop", "fuel system 2 split decode from shared request");
var o2b1s2 = database.Single(d => d.Name == "Oxygen Sensor B1S2 Voltage");
var trimB1s2 = database.Single(d => d.Name == "Short Fuel Trim B1S2");
Check(ObdParser.TryDecode(o2b1s2, "48 6B E1 41 15 64 80 00", out double o2v, out _) && Math.Abs(o2v - 0.5) < 0.0001, "PID 15 voltage decoded as its own value");
Check(trimB1s2.DerivedOnly && ObdParser.TryDecode(trimB1s2, "48 6B E1 41 15 64 80 00", out double o2trim, out _) && Math.Abs(o2trim) < 0.0001, "PID 15 trim decoded from same response");
var maf = database.Single(d => d.Request == "0110" && !d.DerivedOnly);
Check(ObdParser.TryDecode(maf, "48 6B 10 41 10 01 F4 00", out double mafValue, out _) && Math.Abs(mafValue - 5.0) < 0.000001, "PID 10 MAF decode");
var o2B2s1 = database.Single(d => d.Request == "0118" && !d.DerivedOnly);
var trimB2s1 = database.Single(d => d.Request == "0118" && d.DerivedOnly);
Check(ObdParser.TryDecode(o2B2s1, "48 6B 10 41 18 C8 80 00", out double o2B2s1Value, out _) && Math.Abs(o2B2s1Value - 1.0) < 0.000001, "PID 18 B2S1 voltage decode");
Check(ObdParser.TryDecode(trimB2s1, "48 6B 10 41 18 C8 80 00", out double trimB2s1Value, out _) && Math.Abs(trimB2s1Value) < 0.000001, "PID 18 B2S1 trim shared decode");
var o2B2s2 = database.Single(d => d.Request == "0119" && !d.DerivedOnly);
var trimB2s2 = database.Single(d => d.Request == "0119" && d.DerivedOnly);
Check(ObdParser.TryDecode(o2B2s2, "48 6B 10 41 19 64 FF 00", out double o2B2s2Value, out _) && Math.Abs(o2B2s2Value - 0.5) < 0.000001, "PID 19 B2S2 voltage decode");
Check(ObdParser.TryDecode(trimB2s2, "48 6B 10 41 19 64 FF 00", out double trimB2s2Value, out _) && Math.Abs(trimB2s2Value - 99.21875) < 0.000001, "PID 19 B2S2 trim shared decode");
var eqB1 = database.Single(d => d.Name == "Equivalence Ratio B1S1");
var wbV1 = database.Single(d => d.Name == "Wideband O2 B1S1 Voltage");
Check(ObdParser.TryDecode(eqB1, "48 6B E1 41 24 7F E0 00 00 00", out double eq, out _) && Math.Abs(eq - 0.9990234375) < 0.000001, "PID 24 equivalence-ratio formula");
Check(wbV1.DerivedOnly && ObdParser.TryDecode(wbV1, "48 6B E1 41 24 7F E0 10 00 00", out double wbv, out _) && Math.Abs(wbv - 0.5) < 0.000001, "PID 24 wideband voltage shares one request");
Check(support["D1"].All(pid => database.Any(d => d.Request == $"01{pid:X2}")), "database covers all 16 D1 PIDs");
Check(MultiEcuParser.DecodeSupportedPids("48 6B 18 41 00 80 00 80 00 0C").ContainsKey("D1") == false, "TCM-only PID 00 cannot prove XJ PCM D1 recovered");
var catalog = new ResponderCatalog(); catalog.AddBitmap(bitmap);
Check(catalog.Command("010C", true) == "010C1", "RPM count one");
Check(catalog.Command("0111", true) == "01112", "throttle count two");
Check(catalog.Command("0101", true) == "01012", "monitor status count two");
catalog.UseSpacedCountSyntax = true;
Check(catalog.Command("010C", true) == "01 0C 1", "canonical spaced response-count command supported");
catalog.UseSpacedCountSyntax = false;
Check(catalog.Command("012F", true) == "012F", "unknown PID never uses guessed count");
catalog.MarkUncertain("0111");
Check(catalog.Command("0111", true) == "0111", "missing responder disables early termination");
Check(!catalog.Revalidate("0111", new[]{"D1","18"}, true, true), "one complete reply does not restore count");
Check(!catalog.Revalidate("0111", new[]{"D1","18"}, true, true), "two complete replies do not restore count");
Check(!catalog.Revalidate("0111", new[]{"D1"}, true, true), "missing module resets revalidation streak");
Check(!catalog.Revalidate("0111", new[]{"D1","18"}, true, false), "count-limited reply cannot revalidate");
catalog.Revalidate("0111", new[]{"D1","18"}, true, true);
catalog.Revalidate("0111", new[]{"D1","18"}, true, true);
Check(catalog.Revalidate("0111", new[]{"D1","18"}, true, true) && catalog.Command("0111", true) == "01112", "three complete unrestricted replies restore both responders");
catalog.MarkUncertain("0111");
Check(!catalog.Revalidate("0111", new[]{"Unattributed"}, true, true), "unattributed reply never restores source count");
catalog.Observe("010C", new[] {"18"});
Check(catalog.Command("010C", true) == "010C2", "new observed ECU increases count");
catalog.Clear(); Check(catalog.ResponseCount("010C") == 0, "new session clears evidence");
var cvns = MultiEcuParser.DecodeCvnBySource("48 6B D1 49 06 01 A8 CF 02 D4 21\n48 6B 18 49 06 01 41 3E E6 44 C4\n48 6B 18 49 06 02 49 54 5D EC 04");
Check(cvns["D1"] == "A8CF02D4" && cvns["18"] == "413EE644, 49545DEC", "CVNs preserve source and embedded 49");
Check(MultiEcuParser.DecodeMode03("48 6B D1 43 01 32 01 41 01 38 75").Select(x => x.Code).SequenceEqual(new[]{"P0132","P0141","P0138"}), "DTC parser excludes checksum");
var dtcDb = new DtcDescriptionDatabase(Path.Combine(AppContext.BaseDirectory, "Data"));
Check(dtcDb.Lookup("P0183", "", "").Description.Contains("Fuel temperature", StringComparison.OrdinalIgnoreCase), "generic DTC description lookup");
Check(dtcDb.Lookup("P0335", "", "").Source == "Generic OBD-II", "generic DTC definition source");
Check(ObdParser.TryDecode(database.Single(d => d.Request == "0101" && !d.DerivedOnly), "48 6B D1 41 01 81 07 00 00 00", out double monitorValue, out _) && monitorValue == 0x81070000u, "MIL-on monitor word does not overflow signed Int32");
var rpm = database.Single(d => d.Request == "010C");
Check(!ObdParser.TryDecode(rpm, "48 6B D1 41 0C 10 00 00\n48 6B 18 41 0C 20 00 00", out _, out _), "ambiguous sources cannot silently decode");
Check(!ObdParser.TryDecode(rpm, "48 6B D1 41 0C 10 65", out _, out _), "checksum cannot supply missing RPM byte");
Check(ObdParser.TryDecode(rpm, "48 6B D1 41 0C 10 00 00", out double v, out _) && v == 1024, "RPM formula on framed response");
Check(ObdParser.TryDecode(rpm, "7E8 04 41 0C 10 00 00 00 00", out v, out _) && v == 1024, "CAN single frame length excludes padding");
const string canVin = "7E8 10 14 49 02 01 31 48 47\n7E9 03 41 05 60 00 00 00 00\n7E8 21 43 4D 38 32 36 33 33\n7E8 22 41 30 30 34 33 35 32";
Check(MultiEcuParser.DecodeMode09TextBySource(canVin, 2)["7E8"] == "1HGCM82633A004352", "CAN VIN reassembly");
Check(MultiEcuParser.ParseMessages(canVin.Replace("7E8 21", "7E8 23")).All(m => m.Source != "7E8"), "out of sequence CAN continuation rejected");
Check(!TimingPolicy.MateriallyFaster(198,200) && !TimingPolicy.MateriallyFaster(199,200), "1-2ms timing ties favor conservative");
Check(TimingPolicy.MateriallyFaster(180,200), "material timing improvement accepted");
Check(StandardPidDisplay.Describe("0114", 0x64FF) == "0.500 V; trim N/A", "O2 FF trim not misreported as real trim");
Check(StandardPidDisplay.Describe("0103", 0x0200) == "System 1: Closed loop; System 2: Not used", "fuel status decoded");
Check(StandardPidDisplay.Describe("0101", 0x81070000)!.StartsWith("MIL ON; 1 DTC(s)"), "MIL and DTC count decoded");
// Simulate a 285ms adapter plus the existing 200ms quiet delay, without hardware.
var schedule = new PollScheduler();
var simulated = database.Where(d => d.Enabled).ToList();
var seen = new Dictionary<string, List<DateTime>>();
DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), start = now;
while ((now - start).TotalSeconds < 240)
{
    var due = schedule.Next(simulated, now, 200, out int wait);
    if (due is null) { now = now.AddMilliseconds(Math.Max(1, wait)); continue; }
    if (due.DiscoveryOnly) throw new Exception("Scheduler returned a discovery-only PID.");
    if (due.DerivedOnly) throw new Exception("Scheduler returned a derived-only PID.");
    now = now.AddMilliseconds(285);
    schedule.Complete(due.Request, now, 285);
    if (!seen.TryGetValue(due.Request, out var times)) seen[due.Request] = times = new();
    times.Add(now);
    now = now.AddMilliseconds(200);
}
Check(seen.ContainsKey("0105") && seen["0105"].Count >= 3, "coolant gets regular slots under slow-link load");
Check(seen.ContainsKey("0101") && seen["0101"].Count >= 3, "monitor status is not starved");
Check(seen.ContainsKey("0111") && seen["0111"].Count >= 120, "throttle live lane receives frequent service on a saturated link");
Check(seen.ContainsKey("010C") && seen["010C"].Count >= 120, "RPM live lane receives frequent service on a saturated link");
int totalScheduled = seen.Values.Sum(x => x.Count);
int liveScheduled = seen.GetValueOrDefault("010C", new()).Count + seen.GetValueOrDefault("0111", new()).Count;
Check(totalScheduled > 0 && liveScheduled >= totalScheduled * 0.70, "default priority-5 PIDs receive most saturated-link opportunities");
Check(!seen.ContainsKey("0113") && !seen.ContainsKey("011C"), "static metadata is not repeatedly polled");
Check(simulated.Where(d => !d.DiscoveryOnly && !d.DerivedOnly).All(d => d.EffectivePollMs >= d.PollMs), "capacity planning never promises rates above request");
schedule.Reset();
Check(schedule.Next(simulated, now, 200, out _) is not null, "restart creates a fresh initial sampling pass");
string folder = Path.Combine(Path.GetTempPath(), "obd-regression-" + Guid.NewGuid());
using (var log = new CsvDataLogger())
{
    string path = log.Start(folder);
    log.Log(rpm, 1024, 1024, "rpm", "test", "41 0C 10 00", 200, 150, 50, "D1");
    foreach(string status in new[]{"NO DATA","RECOVERY_START","RECOVERY_SUCCESS","LINK_LOST"})
        log.LogEvent(status,"test","010C","D1","NO DATA", "Quoted \"error\", detail");
    log.Stop();
    var lines = File.ReadAllLines(path);
    Check(lines.Length == 6 && lines[0].EndsWith("RecordType,Status,Message"), "CSV samples and events share schema");
    Check(lines[1].Contains("\"D1\"") && lines[2].Contains("\"NO DATA\""), "CSV preserves ECU and NO DATA");
    Check(lines[2].Contains("\"Quoted \"\"error\"\", detail\""), "CSV event escaping");
}
Directory.Delete(folder,true);

var replayTestDirectory = Path.Combine(Path.GetTempPath(), "OBDReplayTest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(replayTestDirectory);
ReplaySession? replayTestSession = null;
try
{
    string csv = Path.Combine(replayTestDirectory, "samples.csv");
    File.WriteAllText(csv, "Timestamp,Name,Value,Source,Request,Units\n" +
        "2026-09-21T12:00:00Z,Engine RPM,800,D1,010C,rpm\n" +
        "2026-09-21T12:00:30Z,Engine RPM,1500,D1,010C,rpm\n" +
        "2026-09-21T12:01:00Z,Engine RPM,2200,D1,010C,rpm\n");
    string original = File.ReadAllText(csv);
    replayTestSession = ReplayCsvLoader.Load(new[] { csv }, Path.Combine(replayTestDirectory, "cache"));
    var cache = replayTestSession.Cache!;
    Check(replayTestSession.SampleCount == 3 && cache.ChunkCount == 3, "replay indexes samples across chunk boundaries");
    Check(cache.ReadRange(clock, clock.AddMinutes(1)).Count == 3, "replay includes samples on chunk boundaries");
    Check(cache.FindNextTimestamp(clock) == clock.AddSeconds(30), "replay forward step crosses chunk boundary");
    Check(cache.FindPreviousTimestamp(clock.AddMinutes(1)) == clock.AddSeconds(30), "replay backward step crosses chunk boundary");
    var checkpoint = cache.LoadCheckpoint(clock.AddSeconds(45));
    var restored = checkpoint.Signals.Select(s => s.LastSample)
        .Concat(cache.ReadRange(checkpoint.TimestampUtc, clock.AddSeconds(45)))
        .Where(s => s.Kind == ReplayRecordKind.Sample).OrderBy(s => s.TimestampUtc).Last();
    Check(restored.Value == 1500, "replay seek reconstructs latest sample without future data");
    Check(ReplayCsvLoader.Load(new[] { csv }, Path.Combine(replayTestDirectory, "cache")).CacheWasReused, "replay reopens unchanged cache");
    Check(File.ReadAllText(csv) == original, "replay leaves source CSV unchanged");
}
finally
{
    Directory.Delete(replayTestDirectory, true);
}

var fakeElm = new FakeElmTransport { Reply = new[] { "41 0C ", "10 00\r", ">" } };
using (var elm = new Elm327Connection())
{
    elm.AttachTransport(fakeElm);
    fakeElm.Incoming.Enqueue(System.Text.Encoding.ASCII.GetBytes("STALE>"));
    var reply = elm.SendCommandDetailed(" 010C\r\n ", 100);
    Check(reply.Response == "41 0C 10 00\r>" && fakeElm.Writes.Single() == "010C\r", "shared ELM normalizes command, discards stale input and assembles fragments");
    Check(reply.FirstByteMs >= 0 && reply.TotalMs >= reply.FirstByteMs && reply.TailMs >= 0, "shared ELM preserves response timing metrics");
    fakeElm.Reply = new[] { "41 0C" };
    bool partialTimeout = false;
    try { elm.SendCommandDetailed("010C", 15); } catch (TimeoutException ex) { partialTimeout = ex.Message.Contains("Partial response"); }
    Check(partialTimeout, "shared ELM rejects incomplete responses without prompt");
    fakeElm.Reply = Array.Empty<string>();
    bool emptyTimeout = false;
    try { elm.SendCommandDetailed("010C", 15); } catch (TimeoutException ex) { emptyTimeout = ex.Message.Contains("No response"); }
    Check(emptyTimeout, "shared ELM rejects silent adapter");
    using var cancellation = new CancellationTokenSource();
    fakeElm.OnRead = () => cancellation.Cancel();
    bool cancelled = false;
    try { await elm.SendCommandDetailedAsync("010C", 1000, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "shared ELM asynchronous command can be cancelled during response wait");
    fakeElm.OnRead = null;
    fakeElm.Reply = new[] { "OK\r>" };
    Check(elm.SendCommand("ATZ", 100) == "OK\r>", "shared ELM usable after timeout/cancellation");
    using var monitoring = new CancellationTokenSource();
    string captured = "";
    elm.MonitorAll(chunk => { captured += chunk; monitoring.Cancel(); }, monitoring.Token);
    Check(captured.Contains("OK") && fakeElm.Writes.TakeLast(2).SequenceEqual(new[] { "ATMA\r", " \r" }), "shared monitor sends ATMA and stop sequence");
}
Check(fakeElm.Disposed, "ELM connection disposes owned transport");

var fakeKLine = new FakeKLineTransport();
using (var td5 = new Td5KLineConnection(fakeKLine))
{
    await td5.OpenAndAuthenticateAsync("test-usb");
    Check(td5.IsAuthenticated && fakeKLine.Baud == 10400 && fakeKLine.SawFastInit, "portable TD5 session authenticates through injected K-line transport");
    var pid = await td5.ReadPidAsync(0x09);
    Check(pid.Data.SequenceEqual(new byte[] { 0x12, 0x34 }), "portable TD5 parser receives PID bytes through transport boundary");
    fakeKLine.BadChecksum = true;
    bool rejected = false;
    try { await td5.ReadPidAsync(0x09); } catch (System.IO.IOException) { rejected = true; }
    Check(rejected, "portable TD5 rejects corrupt response checksum");
}
Check(!fakeKLine.IsOpen, "TD5 closes injected transport on disposal");

Check(GemsProtocol.CalculateSecurityKey(0x1234) == 0x1ADC, "GEMS seed/key multiplication test vector");
var clearTransport = new FakeKLineTransport();
using (var td5Clear = new Td5KLineConnection(clearTransport))
{
    await td5Clear.OpenAndAuthenticateAsync("test-clear");
    Td5CommandResult cleared = await td5Clear.ClearFaultMemoryOnceAsync();
    byte[] expectedClear = new byte[] { 0x14,0x31,0xDD,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0x22 };
    Check(cleared.Positive && clearTransport.LastFrame.SequenceEqual(expectedClear), "TD5 clear sends validated 31/DD frame exactly once");
    Check(cleared.ResponseFrame.SequenceEqual(new byte[] { 0x02, 0x71, 0xDD, 0x50 }), "TD5 clear accepts checksum-valid 71/DD acknowledgement");
}

string storageFolder = Path.Combine(Path.GetTempPath(), "obd-portable-settings-" + Guid.NewGuid());
try
{
    var settingsStore = new AppSettingsStore(Path.Combine(storageFolder, "settings.json"));
    var settings = settingsStore.Load(); settings.LastWifiHost = "test-host"; settingsStore.Save(settings);
    Check(settingsStore.Load().LastWifiHost == "test-host", "portable settings use caller-provided storage path");
}
finally { if (Directory.Exists(storageFolder)) Directory.Delete(storageFolder, true); }
Check(typeof(ObdParser).Assembly.GetName().Name == "OBDWorkbench.Core", "tests exercise shared Core assembly rather than linked source copies");
Check(!typeof(ObdParser).Assembly.GetReferencedAssemblies().Any(a => a.Name is "PresentationFramework" or "PresentationCore" or "System.IO.Ports"), "Core assembly has no WPF or Windows serial dependency");

if (!skipIpc)
{
var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
listener.Start();
try
{
    int tcpPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        using var timeout = new CancellationTokenSource(3000);
        var command = new System.Text.StringBuilder(); byte[] buffer = new byte[1];
        while (!command.ToString().EndsWith("\r"))
        {
            int count = await stream.ReadAsync(buffer, timeout.Token);
            if (count == 0) throw new System.IO.IOException("Test connection closed");
            command.Append((char)buffer[0]);
        }
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("ELM327 "), timeout.Token);
        await Task.Delay(10, timeout.Token);
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("v1.5\r>"), timeout.Token);
        return command.ToString();
    });
    using var networkElm = new Elm327Connection();
    await networkElm.OpenTcpAsync("127.0.0.1", tcpPort);
    var tcpResponse = await networkElm.SendCommandDetailedAsync("ATI", 2000);
    Check(tcpResponse.Response == "ELM327 v1.5\r>" && await server == "ATI\r", "portable TCP transport works with shared ELM protocol over a real loopback socket");
}
finally { listener.Stop(); }
}
else Console.WriteLine("SKIP: 1 TCP loopback integration check (--no-ipc)");
Console.WriteLine($"{tests} regression checks passed.");
