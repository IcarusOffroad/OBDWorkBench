using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public static class PidDatabase
{
    public static List<PidDefinition> Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("PID database not found.", path);

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            return new List<PidDefinition>();

        var headers = ParseCsvLine(lines[0]);
        var map = headers
            .Select((h, i) => new { Name = h.Trim(), Index = i })
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

        string Get(IReadOnlyList<string> fields, string name, string fallback = "") =>
            map.TryGetValue(name, out int index) && index < fields.Count ? fields[index].Trim() : fallback;

        var result = new List<PidDefinition>();
        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var f = ParseCsvLine(line);
            bool enabled = !bool.TryParse(Get(f, "Enabled", "true"), out bool parsedEnabled) || parsedEnabled;
            _ = double.TryParse(Get(f, "Min", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double min);
            _ = double.TryParse(Get(f, "Max", "100"), NumberStyles.Float, CultureInfo.InvariantCulture, out double max);
            _ = int.TryParse(Get(f, "PollMs", "1000"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pollMs);
            _ = int.TryParse(Get(f, "Priority", "1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority);
            _ = int.TryParse(Get(f, "ExpectedResponses", "1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int expectedResponses);
            pollMs = Math.Max(100, pollMs);
            priority = Math.Clamp(priority, 1, 5);
            expectedResponses = Math.Clamp(expectedResponses, 0, 9);

            result.Add(new PidDefinition
            {
                Enabled = enabled,
                DiscoveryOnly = bool.TryParse(Get(f, "DiscoveryOnly", "false"), out bool discoveryOnly) && discoveryOnly,
                DerivedOnly = bool.TryParse(Get(f, "DerivedOnly", "false"), out bool derivedOnly) && derivedOnly,
                DisplayKind = Get(f, "DisplayKind"),
                Name = Get(f, "Name"),
                Request = Get(f, "Request").Replace(" ", "").ToUpperInvariant(),
                ResponseMode = Get(f, "ResponseMode", "41").Replace(" ", "").ToUpperInvariant(),
                PID = Get(f, "PID").Replace(" ", "").ToUpperInvariant(),
                Formula = Get(f, "Formula", "A"),
                Units = Get(f, "Units"),
                Min = min,
                Max = max,
                PollMs = pollMs,
                Priority = priority,
                DefaultPriority = priority,
                ExpectedResponses = expectedResponses,
                Category = Get(f, "Category", "General"),
                Notes = Get(f, "Notes")
            });
        }

        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
