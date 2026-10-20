using System;
using System.Collections.Generic;

namespace OBDWorkbench.Services;

public sealed record VehicleStrategy(string Family, string[] VinPrefixes, int FastTimingProfile, int ComprehensiveTimingProfile, bool AllowResponseCount, IReadOnlyDictionary<string, string> ModuleNames, string Notes);

public static class VehicleKnowledge
{
    public static readonly VehicleStrategy Subaru = new("Subaru", new[] { "JF1", "JF2", "JF3", "JF4", "4S3", "4S4" }, 1, 0, true,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["10"] = "Subaru Engine ECU", ["11"] = "Subaru Transmission ECU", ["7E8"] = "Subaru Engine ECU", ["7E9"] = "Subaru Transmission ECU" },
        "ISO 9141 and CAN Subaru profiles remain separate from Chrysler addressing and are optimized only after neutral discovery.");

    public static readonly VehicleStrategy Jeep = new("Jeep/Chrysler", new[] { "1J4", "1J8", "1C4", "1C6" }, 1, 0, false,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["D1"] = "Jeep PCM", ["18"] = "Jeep TCM" },
        "Recognizes ISO/K-line D1 PCM and 18 TCM response sources. Response-count optimization stays off until a source-specific test proves it safe.");

    public static VehicleStrategy? Identify(string vin, IEnumerable<string> sources)
    {
        if (!string.IsNullOrWhiteSpace(vin))
        {
            foreach (string prefix in Subaru.VinPrefixes) if (vin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Subaru;
            foreach (string prefix in Jeep.VinPrefixes) if (vin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Jeep;
        }
        foreach (string source in sources) if (source.Equals("D1", StringComparison.OrdinalIgnoreCase) || source.Equals("18", StringComparison.OrdinalIgnoreCase)) return Jeep;
        return null;
    }
}
