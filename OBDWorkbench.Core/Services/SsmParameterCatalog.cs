using System.Globalization;
using System.Text;

namespace OBDWorkbench.Services;

public enum SsmPollClass
{
    Fast,
    Slow,
    Switch,
    Snapshot
}

public sealed class SsmParameterDefinition
{
    public required string Name { get; init; }
    public required int Address { get; init; }
    public int Width { get; init; } = 1;
    public required int FlagByte { get; init; }
    public required int FlagBit { get; init; }
    public required SsmPollClass PollClass { get; init; }
    public required string Unit { get; init; }
    public int Decimals { get; init; } = 2;
    public int? ValueBit { get; init; }
    public required Func<IReadOnlyDictionary<int, byte>, double?> Decoder { get; init; }

    public IEnumerable<int> Addresses
    {
        get
        {
            for (int i = 0; i < Width; i++) yield return Address + i;
        }
    }

    public string AddressText => Width == 1 ? $"{Address:X6}" : $"{Address:X6}-{Address + Width - 1:X6}";

    public bool Supported(byte[] initData) => SsmProtocol.CapabilitySupported(initData, FlagByte, FlagBit);

    public double? Decode(IReadOnlyDictionary<int, byte> values) => Decoder(values);

    public string Format(double? value)
    {
        if (!value.HasValue) return "--";
        if (ValueBit.HasValue) return value.Value >= 0.5 ? "ON" : "off";
        string format = Decimals <= 0 ? "0" : "0." + new string('0', Decimals);
        return value.Value.ToString(format, CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit);
    }

    public string RawHex(IReadOnlyDictionary<int, byte> values)
    {
        var bytes = new List<string>();
        foreach (int address in Addresses)
        {
            if (!values.TryGetValue(address, out byte b)) return "--";
            bytes.Add(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        string raw = string.Join(" ", bytes);
        if (ValueBit.HasValue) raw += $"  bit{ValueBit.Value}";
        return raw;
    }

    public string CsvKey
    {
        get
        {
            var sb = new StringBuilder();
            foreach (char c in Name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }
    }
}

public static class SsmParameterCatalog
{
    private static byte? B(IReadOnlyDictionary<int, byte> v, int a)
        => v.TryGetValue(a, out byte b) ? b : null;

    private static int? U16(IReadOnlyDictionary<int, byte> v, int a)
    {
        byte? hi = B(v, a);
        byte? lo = B(v, a + 1);
        return hi.HasValue && lo.HasValue ? (hi.Value << 8) | lo.Value : null;
    }

    private static SsmParameterDefinition U8(string name, int address, int flagByte, int flagBit,
        SsmPollClass pollClass, string unit, int decimals, Func<byte, double> convert)
        => new()
        {
            Name = name, Address = address, FlagByte = flagByte, FlagBit = flagBit,
            PollClass = pollClass, Unit = unit, Decimals = decimals,
            Decoder = v => B(v, address) is byte b ? convert(b) : null
        };

    private static SsmParameterDefinition U16Def(string name, int address, int flagByte, int flagBit,
        SsmPollClass pollClass, string unit, int decimals, Func<int, double> convert)
        => new()
        {
            Name = name, Address = address, Width = 2, FlagByte = flagByte, FlagBit = flagBit,
            PollClass = pollClass, Unit = unit, Decimals = decimals,
            Decoder = v => U16(v, address) is int x ? convert(x) : null
        };

    private static SsmParameterDefinition Bool(string name, int address, int flagByte, int flagBit, int valueBit)
        => new()
        {
            Name = name, Address = address, FlagByte = flagByte, FlagBit = flagBit,
            PollClass = SsmPollClass.Switch, Unit = string.Empty, Decimals = 0, ValueBit = valueBit,
            Decoder = v => B(v, address) is byte b ? (((b >> (valueBit - 1)) & 1) != 0 ? 1.0 : 0.0) : null
        };

    // Standard SSM base definitions. Conversion formulae and BF flag locations follow
    // RomRaider's SSM base logging definition / the published SSM2 protocol notes.
    // We intentionally do not infer unknown non-zero BF bits.
    public static readonly IReadOnlyList<SsmParameterDefinition> All = new List<SsmParameterDefinition>
    {
        // BF flag byte 1
        U8("Engine Load", 0x000007, 1, 8, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U8("Coolant Temperature", 0x000008, 1, 7, SsmPollClass.Slow, "°C", 1, b => b - 40.0),
        U8("Air/Fuel Correction #1", 0x000009, 1, 6, SsmPollClass.Fast, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Air/Fuel Learning #1", 0x00000A, 1, 5, SsmPollClass.Slow, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Air/Fuel Correction #2", 0x00000B, 1, 4, SsmPollClass.Fast, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Air/Fuel Learning #2", 0x00000C, 1, 3, SsmPollClass.Slow, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Manifold Absolute Pressure", 0x00000D, 1, 2, SsmPollClass.Fast, "psi", 3, b => b * 37.0 / 255.0),
        U16Def("Engine Speed", 0x00000E, 1, 1, SsmPollClass.Fast, "RPM", 0, x => x / 4.0),

        // BF flag byte 2
        U16Def("Front O2 Sensor #2", 0x00001A, 2, 1, SsmPollClass.Snapshot, "V", 3, x => x * 0.005),
        U16Def("Rear O2 Sensor", 0x000018, 2, 2, SsmPollClass.Slow, "V", 3, x => x * 0.005),
        U16Def("Front O2 Sensor #1", 0x000016, 2, 3, SsmPollClass.Snapshot, "V", 3, x => x * 0.005),
        U8("Throttle Opening Angle", 0x000015, 2, 4, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U16Def("Mass Air Flow", 0x000013, 2, 5, SsmPollClass.Fast, "g/s", 2, x => x / 100.0),
        U8("Intake Air Temperature", 0x000012, 2, 6, SsmPollClass.Slow, "°C", 1, b => b - 40.0),
        U8("Ignition Timing", 0x000011, 2, 7, SsmPollClass.Fast, "°BTDC", 1, b => (b - 128.0) / 2.0),
        U8("Vehicle Speed", 0x000010, 2, 8, SsmPollClass.Fast, "km/h", 0, b => b),

        // BF flag byte 3
        U8("Atmospheric Pressure", 0x000023, 3, 1, SsmPollClass.Slow, "psi", 3, b => b * 37.0 / 255.0),
        U8("Knock Correction", 0x000022, 3, 2, SsmPollClass.Fast, "°", 1, b => (b - 128.0) / 2.0),
        U8("Fuel Injector #2 Pulse Width", 0x000021, 3, 3, SsmPollClass.Fast, "ms", 3, b => b * 256.0 / 1000.0),
        U8("Fuel Injector #1 Pulse Width", 0x000020, 3, 4, SsmPollClass.Fast, "ms", 3, b => b * 256.0 / 1000.0),
        U8("Differential Pressure Sensor Voltage", 0x00001F, 3, 5, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),
        U8("Throttle Sensor Voltage", 0x00001E, 3, 6, SsmPollClass.Fast, "V", 3, b => b * 0.02),
        U8("Air Flow Sensor Voltage", 0x00001D, 3, 7, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),
        U8("Battery Voltage", 0x00001C, 3, 8, SsmPollClass.Slow, "V", 2, b => b * 0.08),

        // BF flag byte 4
        U8("Manifold Relative Pressure", 0x000024, 4, 8, SsmPollClass.Fast, "psi", 3, b => (b - 128.0) * 37.0 / 255.0),
        U8("Pressure Differential Sensor", 0x000025, 4, 7, SsmPollClass.Snapshot, "psi", 3, b => (b - 128.0) * 37.0 / 255.0),
        U8("Fuel Tank Pressure", 0x000026, 4, 6, SsmPollClass.Slow, "psi", 4, b => (b - 128.0) * 35.0 / 10000.0),
        U8("CO Adjustment", 0x000027, 4, 5, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),
        U8("Learned Ignition Timing", 0x000028, 4, 4, SsmPollClass.Snapshot, "°BTDC", 1, b => (b - 128.0) / 2.0),
        U8("Accelerator Opening Angle", 0x000029, 4, 3, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 256.0),
        U8("Fuel Temperature", 0x00002A, 4, 2, SsmPollClass.Slow, "°C", 1, b => b - 40.0),
        U8("Front O2 Heater #1", 0x00002B, 4, 1, SsmPollClass.Snapshot, "A", 3, b => b * 1004.0 / 25600.0),

        // BF flag byte 5
        U8("Rear O2 Heater Current", 0x00002C, 5, 8, SsmPollClass.Snapshot, "A", 3, b => b * 1004.0 / 25600.0),
        U8("Front O2 Heater #2", 0x00002D, 5, 7, SsmPollClass.Snapshot, "A", 3, b => b * 1004.0 / 25600.0),
        U8("Fuel Level Voltage", 0x00002E, 5, 6, SsmPollClass.Slow, "V", 3, b => b * 0.02),
        U8("Primary Wastegate Duty Cycle", 0x000030, 5, 4, SsmPollClass.Snapshot, "%", 2, b => b * 100.0 / 255.0),
        U8("Secondary Wastegate Duty Cycle", 0x000031, 5, 3, SsmPollClass.Snapshot, "%", 2, b => b * 100.0 / 255.0),
        U8("CPC Valve Duty Ratio", 0x000032, 5, 2, SsmPollClass.Slow, "%", 2, b => b * 100.0 / 255.0),
        U8("Tumble Valve Position Sensor Right", 0x000033, 5, 1, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),

        // BF flag byte 6
        U8("Tumble Valve Position Sensor Left", 0x000034, 6, 8, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),
        U8("Idle Speed Control Valve Duty Ratio", 0x000035, 6, 7, SsmPollClass.Slow, "%", 2, b => b / 2.0),
        U8("Air/Fuel Lean Correction", 0x000036, 6, 6, SsmPollClass.Snapshot, "%", 2, b => b * 100.0 / 255.0),
        U8("Air/Fuel Heater Duty", 0x000037, 6, 5, SsmPollClass.Snapshot, "%", 2, b => b * 100.0 / 255.0),
        U8("Idle Speed Control Valve Step", 0x000038, 6, 4, SsmPollClass.Snapshot, "steps", 0, b => b),
        U8("Number Of Ex. Gas Recirc Steps", 0x000039, 6, 3, SsmPollClass.Slow, "steps", 0, b => b),
        U8("Alternator Duty", 0x00003A, 6, 2, SsmPollClass.Snapshot, "%", 1, b => b),
        U8("Fuel Pump Duty", 0x00003B, 6, 1, SsmPollClass.Slow, "%", 2, b => b * 100.0 / 255.0),

        // BF flag byte 7
        U8("Intake VVT Advance Angle Right", 0x00003C, 7, 8, SsmPollClass.Fast, "°", 1, b => b - 50.0),
        U8("Intake VVT Advance Angle Left", 0x00003D, 7, 7, SsmPollClass.Fast, "°", 1, b => b - 50.0),
        U8("Intake OCV Duty Right", 0x00003E, 7, 6, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U8("Intake OCV Duty Left", 0x00003F, 7, 5, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U8("Intake OCV Current Right", 0x000040, 7, 4, SsmPollClass.Fast, "mA", 0, b => b * 32.0),
        U8("Intake OCV Current Left", 0x000041, 7, 3, SsmPollClass.Fast, "mA", 0, b => b * 32.0),
        U8("Air/Fuel Sensor #1 Current", 0x000042, 7, 2, SsmPollClass.Fast, "mA", 3, b => (b - 128.0) * 0.125),
        U8("Air/Fuel Sensor #2 Current", 0x000043, 7, 1, SsmPollClass.Fast, "mA", 3, b => (b - 128.0) * 0.125),

        // BF flag byte 8
        U8("Air/Fuel Sensor #1 Resistance", 0x000044, 8, 8, SsmPollClass.Snapshot, "Ω", 0, b => b),
        U8("Air/Fuel Sensor #2 Resistance", 0x000045, 8, 7, SsmPollClass.Snapshot, "Ω", 0, b => b),
        U8("Air/Fuel Sensor #1", 0x000046, 8, 6, SsmPollClass.Fast, "λ", 3, b => b / 128.0),
        U8("Air/Fuel Sensor #2", 0x000047, 8, 5, SsmPollClass.Fast, "λ", 3, b => b / 128.0),
        U8("Air/Fuel Correction #3", 0x0000D0, 8, 4, SsmPollClass.Slow, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Air/Fuel Learning #3", 0x0000D1, 8, 3, SsmPollClass.Slow, "%", 2, b => (b - 128.0) * 100.0 / 128.0),
        U8("Rear O2 Heater Voltage", 0x0000D2, 8, 2, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),
        U8("Air/Fuel Adjustment Voltage", 0x0000D3, 8, 1, SsmPollClass.Snapshot, "V", 3, b => b * 0.02),

        // BF flag byte 9 / 10
        U8("Gear Position", 0x00004A, 9, 6, SsmPollClass.Slow, "gear", 0, b => b + 1.0),
        U8("Air/Fuel Sensor #1 Heater Current", 0x000053, 10, 5, SsmPollClass.Snapshot, "A", 2, b => b / 10.0),
        U8("Air/Fuel Sensor #2 Heater Current", 0x000054, 10, 4, SsmPollClass.Snapshot, "A", 2, b => b / 10.0),

        // BF flag bytes 12-20: status/switch/output bitfields. The SSM value bit
        // matches the standard flag bit in the published base definition.
        Bool("Read Memory Signal", 0x000061, 12, 5, 5),
        Bool("Test Mode Signal", 0x000061, 12, 6, 6),
        Bool("AT/MT Identification Signal", 0x000061, 12, 7, 7),

        Bool("A/C Switch Signal", 0x000062, 13, 2, 2),
        Bool("Power Steering Switch Signal", 0x000062, 13, 3, 3),
        Bool("Ignition Switch Signal", 0x000062, 13, 4, 4),
        Bool("Intercooler Auto-Wash Switch Signal", 0x000062, 13, 5, 5),
        Bool("Idle Switch Signal", 0x000062, 13, 7, 7),
        Bool("Neutral Position Switch Signal", 0x000062, 13, 8, 8),

        Bool("Electrical Load Signal", 0x000063, 14, 1, 1),
        Bool("Knock Signal #2", 0x000063, 14, 2, 2),
        Bool("Knock Signal #1", 0x000063, 14, 3, 3),
        Bool("Front O2 #2 Rich Signal", 0x000063, 14, 4, 4),
        Bool("Rear O2 Rich Signal", 0x000063, 14, 5, 5),
        Bool("Front O2 Rich Signal", 0x000063, 14, 6, 6),
        Bool("Starter Switch Signal", 0x000063, 14, 7, 7),
        Bool("Handle Switch", 0x000063, 14, 8, 8),

        Bool("A/C Mid Pressure Switch", 0x000064, 15, 1, 1),
        Bool("A/C Lock Signal", 0x000064, 15, 2, 2),
        Bool("Wiper Switch", 0x000064, 15, 3, 3),
        Bool("Interior Light Switch", 0x000064, 15, 4, 4),
        Bool("Blower Fan Switch", 0x000064, 15, 5, 5),
        Bool("Rear Defogger Switch Signal", 0x000064, 15, 6, 6),
        Bool("Camshaft Position Sensor Signal", 0x000064, 15, 7, 7),
        Bool("Crankshaft Position Sensor Signal", 0x000064, 15, 8, 8),

        Bool("Blow-By Leak Connector", 0x000065, 16, 1, 1),
        Bool("CPC Solenoid Valve", 0x000065, 16, 2, 2),
        Bool("Intercooler Auto-Wash Relay", 0x000065, 16, 3, 3),
        Bool("Fuel Pump Relay", 0x000065, 16, 4, 4),
        Bool("Radiator Fan Relay #2", 0x000065, 16, 5, 5),
        Bool("Radiator Fan Relay #1", 0x000065, 16, 6, 6),
        Bool("Radiator Fan Relay #3", 0x000065, 16, 7, 7),
        Bool("A/C Compressor Signal", 0x000065, 16, 8, 8),

        Bool("Assist Air Solenoid Valve", 0x000066, 17, 1, 1),
        Bool("Power Steering Solenoid Valve", 0x000066, 17, 2, 2),
        Bool("Vent Solenoid Valve", 0x000066, 17, 3, 3),
        Bool("Pressure Sources Change", 0x000066, 17, 4, 4),
        Bool("Variable Intake Air Solenoid", 0x000066, 17, 5, 5),
        Bool("TGV Drive Signal", 0x000066, 17, 6, 6),
        Bool("TGV Output Signal", 0x000066, 17, 7, 7),
        Bool("Fuel Pressure Control Solenoid Valve", 0x000066, 17, 8, 8),

        Bool("Muffler Control", 0x000067, 18, 1, 1),
        Bool("Intake Air Solenoid", 0x000067, 18, 2, 2),
        Bool("Exhaust Gas Negative Pressure", 0x000067, 18, 3, 3),
        Bool("Exhaust Gas Positive Pressure", 0x000067, 18, 4, 4),
        Bool("TCS Relief Valve Solenoid", 0x000067, 18, 5, 5),
        Bool("Relief Valve Solenoid #2", 0x000067, 18, 6, 6),
        Bool("Relief Valve Solenoid #1", 0x000067, 18, 7, 7),
        Bool("Tank Sensor Control Valve", 0x000067, 18, 8, 8),

        Bool("Request Torque Down VDC", 0x000068, 19, 1, 1),
        Bool("Ban Of Torque Down", 0x000068, 19, 2, 2),
        Bool("Fuel Cut Signal From AT", 0x000068, 19, 3, 3),
        Bool("Retard Signal From AT", 0x000068, 19, 4, 4),

        Bool("AET Signal", 0x000069, 20, 1, 1),
        Bool("AT Coop. Rich Spike Signal", 0x000069, 20, 2, 2),
        Bool("AT Coop. Lean Burn Signal", 0x000069, 20, 3, 3),
        Bool("AT Coop. Lock Up Signal", 0x000069, 20, 4, 4),
        Bool("EAM Signal", 0x000069, 20, 5, 5),
        Bool("Torque Permission Signal", 0x000069, 20, 6, 6),
        Bool("Torque Control Signal #2", 0x000069, 20, 7, 7),
        Bool("Torque Control Signal #1", 0x000069, 20, 8, 8),

        // Later standard SSM base parameters. There are no base definitions in
        // RomRaider for every intervening BF flag byte, so gaps are intentional.
        U8("Throttle Motor Voltage", 0x0000FB, 31, 5, SsmPollClass.Snapshot, "V", 3, b => b * 0.08),
        U8("Throttle Motor Duty", 0x0000FA, 31, 6, SsmPollClass.Snapshot, "%", 2, b => (b - 128.0) * 100.0 / 128.0),

        U8("Exhaust Gas Temperature", 0x000106, 33, 2, SsmPollClass.Slow, "°C", 0, b => (b + 40.0) * 5.0),
        U8("Fuel Pressure (High)", 0x000105, 33, 3, SsmPollClass.Snapshot, "MPa", 3, b => b * 0.04),
        U8("Brake Booster Pressure", 0x000104, 33, 4, SsmPollClass.Snapshot, "psi", 3, b => b * 37.0 / 255.0),
        U8("Main Accelerator Sensor", 0x000103, 33, 5, SsmPollClass.Fast, "V", 3, b => b * 0.02),
        U8("Sub Accelerator Sensor", 0x000102, 33, 6, SsmPollClass.Fast, "V", 3, b => b * 0.02),
        U8("Main Throttle Sensor", 0x000101, 33, 7, SsmPollClass.Fast, "V", 3, b => b * 0.02),
        U8("Sub Throttle Sensor", 0x000100, 33, 8, SsmPollClass.Fast, "V", 3, b => b * 0.02),

        U8("Memorized Cruise Speed", 0x00010A, 34, 6, SsmPollClass.Slow, "km/h", 0, b => b),
        U8("SCV Step", 0x000109, 34, 7, SsmPollClass.Snapshot, "steps", 0, b => b),
        U8("Cold Start Injector", 0x000108, 34, 8, SsmPollClass.Snapshot, "ms", 3, b => b * 256.0 / 1000.0),

        U8("Exhaust OCV Current Left", 0x00011D, 36, 3, SsmPollClass.Fast, "mA", 0, b => b * 32.0),
        U8("Exhaust OCV Current Right", 0x00011C, 36, 4, SsmPollClass.Fast, "mA", 0, b => b * 32.0),
        U8("Exhaust OCV Duty Left", 0x00011B, 36, 5, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U8("Exhaust OCV Duty Right", 0x00011A, 36, 6, SsmPollClass.Fast, "%", 2, b => b * 100.0 / 255.0),
        U8("Exhaust VVT Advance Angle Left", 0x000119, 36, 7, SsmPollClass.Fast, "°", 1, b => b - 50.0),
        U8("Exhaust VVT Advance Angle Right", 0x000118, 36, 8, SsmPollClass.Fast, "°", 1, b => b - 50.0),

        Bool("ETC Motor Relay", 0x000120, 37, 7, 7),
        Bool("Accelerator Switch", 0x000121, 38, 2, 2),
        Bool("Brake Switch", 0x000121, 38, 4, 4),
        Bool("Resume/Accelerate Switch", 0x000121, 38, 5, 5),
        Bool("Set/Coast Switch", 0x000121, 38, 6, 6),
        Bool("Stop Light Switch", 0x000121, 38, 7, 7),
        Bool("Clutch Switch", 0x000121, 38, 8, 8),

        U8("Roughness Monitor Cylinder #4", 0x0000D9, 48, 5, SsmPollClass.Snapshot, "count", 0, b => b),
        U8("Roughness Monitor Cylinder #3", 0x0000D8, 48, 6, SsmPollClass.Snapshot, "count", 0, b => b),
        U8("Roughness Monitor Cylinder #2", 0x0000CF, 48, 7, SsmPollClass.Snapshot, "count", 0, b => b),
        U8("Roughness Monitor Cylinder #1", 0x0000CE, 48, 8, SsmPollClass.Snapshot, "count", 0, b => b)
    };

    public static IReadOnlyList<SsmParameterDefinition> Supported(byte[] initData)
        => All.Where(p => p.Supported(initData)).ToList();

    public static string SelfTest()
    {
        var duplicateNames = All.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicateNames.Length > 0)
            throw new InvalidOperationException("Duplicate parameter name(s): " + string.Join(", ", duplicateNames));
        if (All.Any(p => p.FlagByte < 1 || p.FlagBit < 1 || p.FlagBit > 8 || p.Width < 1 || p.Width > 4))
            throw new InvalidOperationException("Parameter catalog contains an invalid BF flag or storage width.");

        // The captured EZ30 capability pattern used during development reports RPM/MAP
        // supported and MAF unsupported. Keep this as a regression check for flag mapping.
        var sample = new byte[57];
        sample[9] = 0xFF;
        sample[10] = 0xEA;
        sample[11] = 0xAF;
        sample[12] = 0xA2;
        var rpm = All.First(p => p.Name == "Engine Speed");
        var map = All.First(p => p.Name == "Manifold Absolute Pressure");
        var maf = All.First(p => p.Name == "Mass Air Flow");
        if (!rpm.Supported(sample) || !map.Supported(sample) || maf.Supported(sample))
            throw new InvalidOperationException("Parameter catalog BF mapping regression self-test failed.");

        return $"PASS: {All.Count} definitions, unique names, BF mapping through byte {MaxCatalogFlagByte}";
    }

    public static int MaxCatalogFlagByte => All.Max(p => p.FlagByte);

    public static IReadOnlyList<int> DistinctAddresses(IEnumerable<SsmParameterDefinition> parameters)
        => parameters.SelectMany(p => p.Addresses).Distinct().OrderBy(a => a).ToList();
}
