using System.Globalization;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public static class StandardPidDisplay
{
    public static string? Describe(PidDefinition definition, double value)
    {
        if (!string.IsNullOrWhiteSpace(definition.DisplayKind))
        {
            return definition.DisplayKind switch
            {
                "DtcCount" => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
                "FuelStatus" => Fuel((byte)Math.Clamp((int)Math.Round(value), 0, 255)),
                "ObdCompliance" => ObdCompliance((byte)Math.Clamp((int)Math.Round(value), 0, 255)),
                "O2Locations" => OxygenLocations((byte)Math.Clamp((int)Math.Round(value), 0, 255)),
                _ => null
            };
        }
        return Describe(definition.Request, value);
    }

    // Kept for regression tests and callers that only have a request string.
    public static string? Describe(string request, double value)
    {
        uint raw = (uint)value;
        byte a = (byte)(raw >> 24), b = (byte)(raw >> 16), c = (byte)(raw >> 8), d = (byte)raw;
        switch (request)
        {
            case "0101":
                var monitors = new List<string>();
                string State(bool available, bool incomplete) => !available ? "N/A" : incomplete ? "incomplete" : "ready";
                string[] common = { "Misfire", "Fuel", "Components" };
                for (int i = 0; i < 3; i++) monitors.Add($"{common[i]}: {State((b & (1 << i)) != 0, (b & (1 << (i + 4))) != 0)}");
                bool diesel = (b & 8) != 0;
                string[] names = diesel
                    ? new[] { "NMHC catalyst", "NOx/SCR", "Reserved", "Boost", "Reserved", "Exhaust sensor", "PM filter", "EGR/VVT" }
                    : new[] { "Catalyst", "Heated catalyst", "EVAP", "Secondary air", "AC refrigerant", "O2 sensor", "O2 heater", "EGR/VVT" };
                for (int i = 0; i < 8; i++) if (names[i] != "Reserved") monitors.Add($"{names[i]}: {State((c & (1 << i)) != 0, (d & (1 << i)) != 0)}");
                return $"MIL {((a & 128) != 0 ? "ON" : "off")}; {a & 127} DTC(s); " + string.Join("; ", monitors);
            case "0103": return $"System 1: {Fuel(c)}; System 2: {Fuel(d)}";
            case "0113": return OxygenLocations(d);
            case "0114": case "0115": case "0118": case "0119":
                return (c / 200.0).ToString("0.000", CultureInfo.InvariantCulture) + " V; trim " +
                    (d == 255 ? "N/A" : ((d - 128) * 100.0 / 128).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            case "011C": return ObdCompliance(d);
            default: return null;
        }
    }

    private static string OxygenLocations(byte mask) =>
        string.Join(", ", Enumerable.Range(0, 8).Where(i => (mask & (1 << i)) != 0).Select(i => $"B{i / 4 + 1}S{i % 4 + 1}")) is { Length: > 0 } sensors ? sensors : "None reported";

    private static string ObdCompliance(byte code) => code switch
    {
        1 => "OBD-II (CARB)", 2 => "OBD (EPA)", 3 => "OBD and OBD-II", 4 => "OBD-I", 5 => "Not OBD compliant",
        6 => "EOBD", 7 => "EOBD and OBD-II", 8 => "EOBD and OBD", 9 => "EOBD / OBD / OBD-II", 10 => "JOBD",
        _ => $"OBD compliance code {code}"
    };

    private static string Fuel(byte status) => status switch
    {
        0 => "Not used", 1 => "Open loop (cold)", 2 => "Closed loop", 4 => "Open loop (load/deceleration)",
        8 => "Open loop (fault)", 16 => "Closed loop (feedback fault)", _ => $"Unknown 0x{status:X2}"
    };
}
