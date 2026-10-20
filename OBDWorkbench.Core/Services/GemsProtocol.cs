using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OBDWorkbench.Services;

public sealed record GemsReadDefinition(string Request, string Name, string Notes);
public sealed record GemsReadResult(string Request, string Name, string RawResponse, string PayloadHex, string DisplayValue, bool Positive);

public static class GemsProtocol
{
    public static IReadOnlyList<GemsReadDefinition> DealerReads { get; } = new[]
    {
        new GemsReadDefinition("222332", "Air adaptive value", "Recovered from RobertF/FlemcoDesign GEMS utility; 0x22 0x2332."),
        new GemsReadDefinition("222333", "Fuel adaptive value", "Recovered from RobertF/FlemcoDesign GEMS utility; Windows utility renders this as percent."),
        new GemsReadDefinition("222334", "Learned closed-throttle value", "Recovered from RobertF/FlemcoDesign GEMS utility; Windows utility renders learned TPS voltage."),
        new GemsReadDefinition("222336", "Dealer value 0x2336", "Present in Android GEMS Utility v1.5; semantic mapping not yet vehicle-validated."),
        new GemsReadDefinition("222337", "Dealer value 0x2337", "Present in Android GEMS Utility v1.5; semantic mapping not yet vehicle-validated."),
        new GemsReadDefinition("2204BF", "Dealer value 0x04BF", "Recovered from RobertF GEMS utility; likely configuration/identification data."),
        new GemsReadDefinition("2204C4", "Dealer value 0x04C4", "Recovered from RobertF GEMS utility; likely configuration/identification data."),
        new GemsReadDefinition("2204E2", "Dealer value 0x04E2", "Present in Android GEMS Utility v1.5; semantic mapping not yet vehicle-validated.")
    };

    public static ushort CalculateSecurityKey(ushort seed) => unchecked((ushort)(seed * 0x4153));

    public static bool TryExtractPayload(string raw, string prefix, out string payload)
    {
        payload = string.Empty;
        string normalizedPrefix = HexOnly(prefix);
        foreach (string line in LogicalHexLines(raw))
        {
            if (line.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                payload = line;
                return true;
            }
        }
        return false;
    }

    public static bool TryExtractSeed(string raw, out ushort seed)
    {
        seed = 0;
        if (!TryExtractPayload(raw, "6701", out string payload) || payload.Length < 8)
            return false;
        return ushort.TryParse(payload.Substring(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed);
    }

    public static string DecodeDisplay(GemsReadDefinition definition, string raw)
    {
        string id = definition.Request.Substring(2);
        if (!TryExtractPayload(raw, "62" + id, out string payload))
            return "No positive 0x62 response";

        string data = payload.Length > 2 + id.Length ? payload.Substring(2 + id.Length) : string.Empty;
        try
        {
            if (definition.Request.Equals("222332", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
                return ushort.Parse(data.Substring(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (definition.Request.Equals("222333", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
            {
                int rawValue = ushort.Parse(data.Substring(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                double pct = rawValue * 100.0 / 32767.0;
                return $"{pct:0.0}% (raw {rawValue})";
            }
            if (definition.Request.Equals("222334", StringComparison.OrdinalIgnoreCase) && data.Length >= 2)
            {
                int rawValue = byte.Parse(data.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return $"{rawValue * 0.05:0.00} V (raw {rawValue})";
            }
        }
        catch
        {
            // Raw value remains available below.
        }
        return data.Length == 0 ? payload : data;
    }

    public static string Fingerprint(IEnumerable<GemsReadResult> reads, string adapterProtocol)
    {
        string evidence = string.Join("|", reads.OrderBy(r => r.Request).Select(r => $"{r.Request}:{r.PayloadHex.Length}:{r.PayloadHex}"));
        string canonical = $"GEMS|{adapterProtocol}|{evidence}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    public static IEnumerable<string> LogicalHexLines(string raw)
    {
        foreach (string rawLine in raw.Replace('>', '\n').Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = HexOnly(rawLine);
            if (line.Length >= 2 && line.Length % 2 == 0)
                yield return line;
        }
    }

    public static string HexOnly(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value)
            if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }
}
