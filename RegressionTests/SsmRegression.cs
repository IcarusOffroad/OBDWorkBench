using OBDWorkbench.Services;
using OBDWorkbench.Models;

internal static class SsmRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        check(SsmProtocol.SelfTest().StartsWith("PASS"), "SSM framing, checksum, echo and capability baseline");
        check(SsmParameterCatalog.SelfTest().StartsWith("PASS"), "SSM catalog baseline");
        check(SignalScaleDefaults.StartsAtZero("SSM-engine_speed", "Engine Speed"), "SSM Engine Speed autoscale starts at zero");
        check(SignalScaleDefaults.StartsAtZero("SSM-vehicle_speed", "Vehicle Speed"), "SSM Vehicle Speed autoscale starts at zero");
        check(SignalScaleDefaults.StartsAtZero("010D", "Vehicle Speed"), "OBD vehicle speed autoscale starts at zero");

        byte[] init = BuildInit();
        DtcProfile profile = DtcProfile.FromInit(init);
        check(profile.Format == "OBD" && profile.Addresses.Length == 64, "SSM DTC base capability selects complete OBD current/history banks");
        check(profile.Banks[0].CurrentStart == 0x8E && profile.Banks[0].HistoryStart == 0xAE, "SSM DTC base bank boundaries");

        byte[] knownEz30Init = Convert.FromHexString("FFA2100E2D1A255105FFEAAFA2224503FE00180020CE54FBF0948000E00000000000DC0000451C000000000000000000800000000000000000");
        DtcProfile knownEz30Profile = DtcProfile.FromInit(knownEz30Init);
        check(knownEz30Profile.Format == "OBD" && knownEz30Profile.Banks.Count == 3 && knownEz30Profile.Addresses.Length == 88,
            "known A2100E / 2D1A255105 BF vector selects expected SSM DTC banks");

        byte[] legacy = (byte[])init.Clone();
        legacy[38] = 0x80;
        DtcProfile legacyProfile = DtcProfile.FromInit(legacy);
        check(legacyProfile.Format == "SUBARU" && legacyProfile.Addresses.Length == 22 && legacyProfile.Banks[0].HistoryStart == 0xA4,
            "SSM Subaru-format DTC history offset differs from OBD format");

        byte[] extended = (byte[])init.Clone();
        extended[48] = 0xF0;
        extended[59] = 0x60;
        extended[37] = 1;
        check(DtcProfile.FromInit(extended).Banks.Count == 9, "SSM DTC extension banks are selected by BF capabilities");

        foreach (byte[] bad in new[] { new byte[10], new byte[39], init.Take(38).ToArray() })
        {
            bool blocked = false;
            try { DtcProfile.FromInit(bad); } catch (NotSupportedException) { blocked = true; }
            check(blocked, "SSM truncated/unsupported identification is not treated as a zero-fault result");
        }
        byte[] noBanks = (byte[])init.Clone();
        noBanks[38] = 0;
        bool noBanksBlocked = false;
        try { DtcProfile.FromInit(noBanks); } catch (NotSupportedException) { noBanksBlocked = true; }
        check(noBanksBlocked, "SSM identification advertising no known DTC bank refuses interpretation");

        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        string mapPath = Path.Combine(AppContext.BaseDirectory, "ReferenceData", "SSM_DTC_MAP.csv");
        var decoder = new SsmDtcDecoder(dataDir, mapPath);
        var values = profile.Addresses.ToDictionary(a => a, _ => (byte)0);
        check(decoder.Decode(profile, values).Count == 0, "complete zero SSM DTC snapshot has no set flags");
        values[0x8E] = 1;
        values[0xAE] = 1;
        List<DtcFlag> flags = decoder.Decode(profile, values);
        check(flags.Count == 2 && flags.All(f => f.Code == "P0335") && flags.Select(f => f.Bank).Distinct().Count() == 2,
            "SSM P0335 maps independently in current and history banks");
        values[0x8E] = 3;
        check(decoder.Decode(profile, values).Any(f => !f.Mapped && f.Bit == 2), "SSM unknown set DTC bits are retained");
        values.Remove(0x8F);
        bool partialRejected = false;
        try { decoder.Decode(profile, values); } catch (InvalidDataException) { partialRejected = true; }
        check(partialRejected, "partial SSM DTC snapshot cannot report clear");

        var fake = new FakeSsmTransport(init);
        using var connection = new SsmConnection(fake);
        await connection.OpenAsync("fake");
        check(fake.Baud == 4800 && connection.Supported.Count > 0, "SSM initialization uses 4800 and capability filter");
        var result = await connection.ReadAsync(Enumerable.Range(0, 48));
        check(fake.ReadSizes.SequenceEqual(new[] { 43, 5 }) && result.Values.Count == 48, "SSM 48 addresses split 43 plus 5");
        check(result.Values[47] == 47, "SSM batched addresses retain mapping");
        fake.ReadSizes.Clear(); fake.Limit = 10;
        result = await connection.ReadAsync(Enumerable.Range(0, 48));
        check(connection.BatchLimit <= 10 && result.Values.Count == 48, "SSM retries with smaller batches on rejected read");
        fake.BadChecksum = true;
        bool rejected = false;
        try { await connection.ReadAsync(new[] { 8 }); } catch (IOException) { rejected = true; }
        check(rejected, "SSM rejects bad checksum instead of publishing values");
        fake.BadChecksum = false;

        fake.Rpm = 900;
        bool blockedClear = false;
        try { await connection.ClearEngineMemoryOnceAsync(); } catch (InvalidOperationException) { blockedClear = true; }
        check(blockedClear && fake.ClearWrites == 0, "SSM running engine blocks memory-reset write");
        fake.Rpm = 0; fake.Speed = 2;
        try { await connection.ClearEngineMemoryOnceAsync(); } catch (InvalidOperationException) { }
        check(fake.ClearWrites == 0, "SSM moving vehicle blocks memory-reset write");
        fake.Speed = 0; fake.CorruptReads = true;
        try { await connection.ClearEngineMemoryOnceAsync(); } catch (IOException) { }
        check(fake.ClearWrites == 0, "SSM bad safety-read checksum blocks memory-reset write");
        fake.CorruptReads = false;
        int samples = fake.EngineChecks;
        check(await connection.ClearEngineMemoryOnceAsync(), "SSM exact F8 40 acknowledgement is accepted");
        check(fake.EngineChecks - samples == 2, "SSM clear performs two fresh engine-off/stationary checks");
        check(fake.LastWrite!.SequenceEqual(new byte[] { 0x80, 0x10, 0xF0, 0x05, 0xB8, 0, 0, 0x60, 0x40, 0xDD }),
            "SSM memory-reset packet address/value/checksum are exact");
        fake.DropClearAck = true;
        int beforeWrites = fake.ClearWrites;
        try { await connection.ClearEngineMemoryOnceAsync(); } catch (IOException) { }
        check(fake.ClearWrites == beforeWrites + 1, "SSM lost clear acknowledgement never retries the write");
        fake.DropClearAck = false; fake.WrongClearAck = true;
        check(!await connection.ClearEngineMemoryOnceAsync(), "SSM wrong clear acknowledgement is not accepted");
        fake.WrongClearAck = false;

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        beforeWrites = fake.ClearWrites;
        try { await connection.ClearEngineMemoryOnceAsync(cancel.Token); } catch (OperationCanceledException) { }
        check(fake.ClearWrites == beforeWrites, "SSM cancelled clear sends no write");

        connection.Close();
        check(!fake.IsOpen, "SSM closes transport");

        string folder = Path.Combine(Path.GetTempPath(), "ssm-regression-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string path;
            using (var logger = new CsvDataLogger())
            {
                path = logger.Start(folder);
                logger.Log(new PidDefinition { Name = "Engine Speed", Request = "SSM-engine_speed", Units = "RPM" }, 800, 800, "RPM", "SSM-test", "0C 80", 100, 0, 0, "SSM10", sampleStatus: "ECU-reported; sensor validity unverified");
                string snapshot = logger.SnapshotText();
                check(snapshot.Contains("Engine Speed") && snapshot.Contains("sensor validity unverified"), "active CSV logger snapshot captures SSM live evidence");
            }
            using (var sessionLogger = new SessionDiagnosticLogger())
            {
                sessionLogger.Start(folder);
                sessionLogger.WriteLine("SSM TX 80 10 F0 01 BF 40");
                check(sessionLogger.SnapshotText().Contains("SSM TX 80 10 F0 01 BF 40"), "active session logger snapshot captures raw SSM traffic");
            }
            var session = ReplayCsvLoader.Load(new[] { path }, Path.Combine(folder, "cache"));
            check(session.Signals.Any(s => s.Name == "Engine Speed"), "SSM Workbench CSV is accepted by replay");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static byte[] BuildInit()
    {
        var data = new byte[60];
        data[0] = 0xFF;
        data[1] = 0xA2;
        data[2] = 0x10;
        data[9] = 0x01;  // flag 1 bit 1: engine speed
        data[10] = 0x80; // flag 2 bit 8: vehicle speed
        data[38] = 0x10; // flag 30 bit 5: OBD-format base DTC bank
        return data;
    }
}

internal sealed class FakeSsmTransport : ISsmTransport
{
    private readonly byte[] _init;
    private byte[] _tx = [];
    public bool IsOpen { get; private set; }
    public int Baud, Writes, Rpm, Speed, ClearWrites, EngineChecks;
    public int Limit = 43;
    public bool BadChecksum, DropClearAck, WrongClearAck, CorruptReads;
    public byte[]? LastWrite;
    public List<int> ReadSizes = new();

    public FakeSsmTransport(byte[] init) => _init = init;

    public void Open(string portName, int baudRate = 4800) { IsOpen = true; Baud = baudRate; }
    public void Close() => IsOpen = false;
    public void Dispose() => Close();
    public void PurgeRx() { }
    public void Write(byte[] data)
    {
        _tx = data;
        Writes++;
        if (_tx.Length > 4 && _tx[4] == 0xB8) { ClearWrites++; LastWrite = data; }
    }

    public byte[] ReadForResponse(int timeout, Func<IReadOnlyList<byte>, bool>? stopWhen = null)
    {
        byte[] data;
        if (_tx[4] == 0xBF)
        {
            data = _init;
        }
        else if (_tx[4] == 0xB8)
        {
            if (DropClearAck) return _tx;
            data = new byte[] { 0xF8, WrongClearAck ? (byte)0 : (byte)0x40 };
        }
        else
        {
            int count = (_tx[3] - 2) / 3;
            ReadSizes.Add(count);
            if (count > Limit) return _tx;
            data = new byte[count + 1];
            data[0] = 0xE8;
            for (int i = 0; i < count; i++)
            {
                int address = (_tx[6 + i * 3] << 16) | (_tx[7 + i * 3] << 8) | _tx[8 + i * 3];
                if (address == 0x0E) data[i + 1] = (byte)((Rpm * 4) >> 8);
                else if (address == 0x0F) data[i + 1] = (byte)(Rpm * 4);
                else if (address == 0x10) data[i + 1] = (byte)Speed;
                else data[i + 1] = (byte)(address & 0xFF);
            }
            if (count <= 3) EngineChecks++;
        }

        byte[] frame = new byte[data.Length + 5];
        frame[0] = 0x80; frame[1] = 0xF0; frame[2] = 0x10; frame[3] = (byte)data.Length;
        data.CopyTo(frame, 4);
        frame[^1] = SsmProtocol.Checksum(frame.AsSpan(0, frame.Length - 1));
        if (BadChecksum || (CorruptReads && _tx[4] == 0xA8)) frame[^1]++;
        return _tx.Concat(frame).ToArray();
    }
}
