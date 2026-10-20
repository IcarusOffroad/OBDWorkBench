using System.IO;

namespace OBDWorkbench.Services;

public sealed class DtcDescriptionDatabase
{
    private readonly Dictionary<string, string> _generic = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _manufacturer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _vehicle = new(StringComparer.OrdinalIgnoreCase);

    public int GenericCount => _generic.Count;
    public string DataDirectory { get; }

    public DtcDescriptionDatabase(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        LoadSimple(Path.Combine(dataDirectory, "DTC_generic.csv"), 2, fields =>
        {
            if (fields.Length >= 2) _generic[fields[0]] = fields[1];
        });
        LoadSimple(Path.Combine(dataDirectory, "DTC_manufacturer.csv"), 3, fields =>
        {
            if (fields.Length >= 3) _manufacturer[$"{fields[0]}|{fields[1]}"] = fields[2];
        });
        LoadSimple(Path.Combine(dataDirectory, "DTC_vehicle.csv"), 3, fields =>
        {
            if (fields.Length >= 3) _vehicle[$"{fields[0]}|{fields[1]}"] = fields[2];
        });
    }

    public (string Description, string Source) Lookup(string code, string manufacturer, string vehicleSignature)
    {
        code = code.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(vehicleSignature) && _vehicle.TryGetValue($"{vehicleSignature}|{code}", out string? vehicle))
            return (vehicle, "Vehicle-specific");
        if (!string.IsNullOrWhiteSpace(manufacturer) && _manufacturer.TryGetValue($"{manufacturer}|{code}", out string? make))
            return (make, manufacturer);
        if (_generic.TryGetValue(code, out string? generic))
            return (generic, "Generic OBD-II");

        string family = code.Length == 5 ? code.Substring(0, 2) : "";
        bool probablyGeneric = family is "P0" or "P2" or "B0" or "C0" or "U0";
        return probablyGeneric
            ? ("Generic code not yet described in the local DTC database.", "Generic OBD-II")
            : ("Manufacturer/vehicle-specific description not loaded.", "Manufacturer-specific");
    }

    private static void LoadSimple(string path, int minimumFields, Action<string[]> add)
    {
        if (!File.Exists(path)) return;
        foreach (string raw in File.ReadLines(path).Skip(1))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] fields = ParseCsvLine(line);
            if (fields.Length >= minimumFields) add(fields.Select(f => f.Trim()).ToArray());
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
