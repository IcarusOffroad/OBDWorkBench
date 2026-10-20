using System;
using System.Collections.Generic;

namespace OBDWorkbench.Services;

public static class UnitConverter
{
    public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Temperature"] = new[] { "°F", "°C" },
        ["Pressure"] = new[] { "psi", "kPa", "bar" },
        ["Speed"] = new[] { "mph", "km/h" },
        ["Distance"] = new[] { "mi", "km" },
        ["Flow"] = new[] { "lb/min", "g/s" }
    };

    public static string MeasurementFor(string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "°f" or "°c" => "Temperature",
        "psi" or "kpa" or "bar" => "Pressure",
        "mph" or "km/h" or "kph" => "Speed",
        "mi" or "km" => "Distance",
        "lb/min" or "g/s" => "Flow",
        _ => ""
    };

    public static string TargetUnit(string source, AppSettingsData settings)
    {
        string kind = MeasurementFor(source);
        if (kind.Length == 0) return source;
        if (settings.UnitOverrides.TryGetValue(kind, out string? chosen) && !string.IsNullOrWhiteSpace(chosen)) return chosen;
        bool metric = settings.UnitSystem.Equals("Metric", StringComparison.OrdinalIgnoreCase);
        return kind switch { "Temperature" => metric ? "°C" : "°F", "Pressure" => metric ? "kPa" : "psi", "Speed" => metric ? "km/h" : "mph", "Distance" => metric ? "km" : "mi", "Flow" => metric ? "g/s" : "lb/min", _ => source };
    }

    public static double Convert(double value, string source, string target)
    {
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return value;
        string s = source.ToLowerInvariant(), t = target.ToLowerInvariant();
        if (s == "°f" && t == "°c") return (value - 32) * 5 / 9;
        if (s == "°c" && t == "°f") return value * 9 / 5 + 32;
        if (s == "psi" && t == "kpa") return value * 6.894757;
        if (s == "psi" && t == "bar") return value * 0.06894757;
        if (s == "kpa" && t == "psi") return value / 6.894757;
        if (s == "kpa" && t == "bar") return value / 100;
        if (s == "bar" && t == "kpa") return value * 100;
        if (s == "bar" && t == "psi") return value / 0.06894757;
        if (s == "mph" && (t == "km/h" || t == "kph")) return value * 1.609344;
        if ((s == "km/h" || s == "kph") && t == "mph") return value / 1.609344;
        if (s == "mi" && t == "km") return value * 1.609344;
        if (s == "km" && t == "mi") return value / 1.609344;
        if (s == "g/s" && t == "lb/min") return value * 0.13227736;
        if (s == "lb/min" && t == "g/s") return value / 0.13227736;
        return value;
    }
}
