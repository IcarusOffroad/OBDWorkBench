namespace OBDWorkbench.Services;

public sealed record DtcBank(string Name, int CurrentStart, int HistoryStart, int Length);

public sealed record DtcProfile(string Format, IReadOnlyList<DtcBank> Banks)
{
    public int[] Addresses => Banks
        .SelectMany(b => Enumerable.Range(b.CurrentStart, b.Length).Concat(Enumerable.Range(b.HistoryStart, b.Length)))
        .Distinct()
        .ToArray();

    public static DtcProfile FromInit(byte[] data)
    {
        if (data.Length < 39 || data[0] != 0xFF || (data[1] & 0xF0) != 0xA0 || data[2] != 0x10)
            throw new NotSupportedException("A complete Ax10xx SSM engine identification with at least 30 capability bytes is required before selecting a DTC layout.");

        bool Has(int flag, int bit) => SsmProtocol.CapabilitySupported(data, flag, bit);
        var banks = new List<DtcBank>();
        string format = Has(30, 8) ? "SUBARU" : "OBD";

        if (format == "SUBARU")
        {
            banks.Add(new("Base", 0x8E, 0xA4, 11));
        }
        else
        {
            if (Has(30, 5) || Has(30, 7)) banks.Add(new("Base", 0x8E, 0xAE, 32));
            if (Has(29, 1)) banks.Add(new("Extension F0", 0xF0, 0xF4, 4));
            var extensions = new[]
            {
                (40, 8, 0x123, 0x12B, 8),
                (40, 7, 0x150, 0x155, 5),
                (40, 6, 0x160, 0x165, 5),
                (40, 5, 0x174, 0x17B, 7),
                (51, 7, 0x1C1, 0x1C7, 6),
                (51, 7, 0x20A, 0x20E, 4),
                (51, 6, 0x263, 0x268, 5)
            };
            foreach (var (flag, bit, current, history, length) in extensions)
                if (Has(flag, bit)) banks.Add(new($"Extension {current:X}", current, history, length));
        }

        if (banks.Count == 0)
            throw new NotSupportedException("No known DTC banks are advertised by this SSM ECU. Raw BF data is retained; this is not a zero-fault result.");

        return new(format, banks);
    }
}

public sealed record DtcFlag(string Bank, string Code, string Description, string Address, int Bit, string Raw, bool Mapped);

public sealed class SsmDtcDecoder
{
    private readonly Dictionary<(string Format, int Address, int Bit), string> _codes = new();
    private readonly DtcDescriptionDatabase _descriptions;

    public SsmDtcDecoder(string dtcDataDirectory, string mapPath)
    {
        _descriptions = new DtcDescriptionDatabase(dtcDataDirectory);
        if (!File.Exists(mapPath)) throw new FileNotFoundException("SSM DTC map is missing.", mapPath);
        foreach (string raw in File.ReadLines(mapPath).Skip(1))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] fields = line.Split(',');
            if (fields.Length != 5) throw new InvalidDataException("Invalid SSM DTC map row: " + line);
            string format = fields[0].Trim();
            int current = Convert.ToInt32(fields[1].Trim(), 16);
            int history = Convert.ToInt32(fields[2].Trim(), 16);
            int bit = int.Parse(fields[3].Trim());
            string code = fields[4].Trim();
            foreach (int address in new[] { current, history })
            {
                var key = (format, address, bit);
                if (_codes.TryGetValue(key, out string? old) && !old.Equals(code, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Conflicting SSM DTC map entry for {format} {address:X6} bit {bit}.");
                _codes[key] = code;
            }
        }
    }

    public List<DtcFlag> Decode(DtcProfile profile, IReadOnlyDictionary<int, byte> values)
    {
        if (profile.Addresses.Any(a => !values.ContainsKey(a)))
            throw new InvalidDataException("Incomplete SSM fault snapshot; a partial bank cannot be interpreted as a zero-fault result.");

        var result = new List<DtcFlag>();
        foreach (DtcBank bank in profile.Banks)
        {
            foreach (var (start, label) in new[] { (bank.CurrentStart, "Current / temporary"), (bank.HistoryStart, "History / memorized") })
            {
                for (int address = start; address < start + bank.Length; address++)
                {
                    byte raw = values[address];
                    for (int bit = 1; bit <= 8; bit++)
                    {
                        if ((raw & (1 << (bit - 1))) == 0) continue;
                        bool mapped = _codes.TryGetValue((profile.Format, address, bit), out string? code);
                        string resolvedCode = code ?? $"UNMAPPED-{address:X6}-b{bit}";
                        string description;
                        if (mapped && resolvedCode.Length == 5 && "PBCU".Contains(resolvedCode[0]))
                            description = _descriptions.Lookup(resolvedCode, "Subaru", "").Description;
                        else if (mapped)
                            description = "Subaru code; verify description/status with a reference scanner";
                        else
                            description = "Unmapped SSM fault bit; raw address/bit retained for comparison";

                        result.Add(new DtcFlag(label, resolvedCode, description, address.ToString("X6"), bit, raw.ToString("X2"), mapped));
                    }
                }
            }
        }
        return result;
    }
}
