using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public sealed record EcuMessage(string Source, byte[] Payload, bool IsoTp = false);

public static class MultiEcuParser
{
    // Parse transport framing BEFORE service data. Never search inside data for a mode byte.
    // ATH1 is required for provenance. Headerless data is explicitly Unattributed.
    public static List<EcuMessage> ParseMessages(string response)
    {
        var messages = new List<EcuMessage>();
        var pending = new Dictionary<string, (int Length, int Sequence, List<byte> Data)>();
        foreach (string line in response.Replace('>', ' ').Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            string source = "Unattributed";
            bool can = tokens[0].Length == 3 && tokens[0].All(Uri.IsHexDigit);
            bool can29 = tokens[0].Length == 8 && tokens[0].StartsWith("18", StringComparison.OrdinalIgnoreCase) && tokens[0].All(Uri.IsHexDigit);
            if (can || can29) source = tokens[0].ToUpperInvariant();
            var bytes = new List<byte>();
            foreach (string token in tokens.Skip(can || can29 ? 1 : 0))
            {
                if (token.Length < 2 || token.Length % 2 != 0 || !token.All(Uri.IsHexDigit)) continue;
                for (int i = 0; i < token.Length; i += 2)
                    bytes.Add(byte.Parse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            if (bytes.Count == 0) continue;
            if (can || can29)
            {
                // ELM CAF1 output still includes PCI with ATH1; optional DLC is one hex digit.
                int type = bytes[0] >> 4;
                if (type == 0)
                {
                    int length = bytes[0] & 15;
                    if (length > 0 && bytes.Count >= length + 1)
                        messages.Add(new(source, bytes.Skip(1).Take(length).ToArray(), true));
                }
                else if (type == 1 && bytes.Count >= 3)
                    pending[source] = (((bytes[0] & 15) << 8) | bytes[1], 1, bytes.Skip(2).ToList());
                else if (type == 2 && pending.TryGetValue(source, out var frame))
                {
                    if ((bytes[0] & 15) != frame.Sequence) { pending.Remove(source); continue; }
                    frame.Data.AddRange(bytes.Skip(1));
                    if (frame.Data.Count >= frame.Length)
                    {
                        messages.Add(new(source, frame.Data.Take(frame.Length).ToArray(), true));
                        pending.Remove(source);
                    }
                    else pending[source] = (frame.Length, (frame.Sequence + 1) & 15, frame.Data);
                }
                continue;
            }
            if (bytes.Count >= 5 && ((bytes[0] == 0x48 && bytes[1] == 0x6B) || bytes[0] == 0x68 || (bytes[0] & 0xC0) == 0x80))
            {
                source = bytes[2].ToString("X2");
                // ISO 9141 / J1850 / KWP headers are three bytes; final byte is
                // transport checksum/CRC, even when it happens to be printable ASCII.
                bytes = bytes.Skip(3).Take(bytes.Count - 4).ToList();
                // KWP extended length byte (format length == 0).
                if (bytes.Count > 0 && (tokens[0].Equals("80", StringComparison.OrdinalIgnoreCase))) bytes.RemoveAt(0);
            }
            // Most OBD replies include a response mode plus PID, but some services
            // (notably Mode 04 clear-DTC acknowledgement = 0x44) are one byte long.
            if (bytes.Count >= 1 && bytes[0] is >= 0x40 and <= 0x7F)
                messages.Add(new(source, bytes.ToArray()));
        }
        // Some inexpensive Bluetooth ELM327 clones emit one hex byte per CR/LF.
        // The normal line parser above intentionally preserves message boundaries,
        // but that formatting leaves every line too short to recognize a legacy
        // ISO 9141/J1850 frame.  If nothing was decoded, retry by treating whitespace
        // as insignificant and reconstructing framed legacy messages from the byte
        // stream.  This fallback is deliberately limited to known three-byte legacy
        // headers so command echoes and arbitrary text cannot become fake responses.
        if (!messages.Any(m => !m.Source.Equals("Unattributed", StringComparison.OrdinalIgnoreCase)))
        {
            var rebuilt = ParseWhitespaceFragmentedLegacy(response).ToList();
            if (rebuilt.Count > 0)
            {
                messages.RemoveAll(m => m.Source.Equals("Unattributed", StringComparison.OrdinalIgnoreCase));
                messages.AddRange(rebuilt);
            }
        }

        return messages;
    }

    private static IEnumerable<EcuMessage> ParseWhitespaceFragmentedLegacy(string response)
    {
        // A few inexpensive ELM clones concatenate their BUS INIT status and the
        // first response byte (for example, "BUS INIT: OK48 6B E1 ..."). Split
        // that boundary before tokenizing so the valid 0x48 header byte is not lost.
        response = Regex.Replace(response, @"(?i)\bOK(?=[0-9A-F]{2}(?:\s|$))", "OK ");

        var stream = new List<byte>();
        foreach (string token in response.Replace('>', ' ').Replace('\r', ' ').Replace('\n', ' ')
                     .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length != 2 || !token.All(Uri.IsHexDigit))
                continue;
            stream.Add(byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        for (int start = 0; start + 4 < stream.Count;)
        {
            bool iso9141 = stream[start] == 0x48 && stream[start + 1] == 0x6B;
            bool j1850 = stream[start] == 0x68;
            if (!iso9141 && !j1850)
            {
                start++;
                continue;
            }

            int next = start + 4;
            while (next + 2 < stream.Count)
            {
                if ((stream[next] == 0x48 && stream[next + 1] == 0x6B) || stream[next] == 0x68)
                    break;
                next++;
            }
            int end = next + 2 < stream.Count ? next : stream.Count;
            int count = end - start;
            if (count >= 5)
            {
                string source = stream[start + 2].ToString("X2");
                // Three header bytes and one trailing checksum/CRC byte. A valid
                // service acknowledgement may contain only its response mode.
                byte[] payload = stream.Skip(start + 3).Take(count - 4).ToArray();
                if (payload.Length > 0 && payload[0] is >= 0x40 and <= 0x7F)
                    yield return new EcuMessage(source, payload);
            }
            start = Math.Max(start + 1, end);
        }
    }

    public static Dictionary<string, List<byte>> SplitBySource(string response, params byte[] responseModes) =>
        ParseMessages(response).Where(m => responseModes.Contains(m.Payload[0]))
            .GroupBy(m => m.Source).ToDictionary(g => g.Key, g => g.SelectMany(m => m.Payload).ToList(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, HashSet<byte>> DecodeSupportedPids(string response, byte block = 0)
    {
        var result = new Dictionary<string, HashSet<byte>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in ParseMessages(response).Where(m => m.Payload.Length >= 6 && m.Payload[0] == 0x41 && m.Payload[1] == block))
        {
            if (!result.TryGetValue(m.Source, out var pids)) result[m.Source] = pids = new();
            for (int bit = 0; bit < 32; bit++)
                if ((m.Payload[2 + bit / 8] & (1 << (7 - bit % 8))) != 0 && block + bit + 1 <= 255)
                    pids.Add((byte)(block + bit + 1));
        }
        return result;
    }

    public static string ModuleName(string source) => source.ToUpperInvariant() switch
    {
        "D1" => "Jeep PCM", "18" => "Jeep TCM", "10" or "7E8" => "Engine/PCM",
        "11" or "7E9" => "Transmission/TCM", _ => "ECU " + source
    };

    public static List<DiagnosticTroubleCode> DecodeMode03(string response)
    {
        var result = new List<DiagnosticTroubleCode>();
        foreach (var m in ParseMessages(response).Where(m => m.Payload[0] == 0x43))
            for (int i = m.IsoTp ? 2 : 1; i + 1 < m.Payload.Length; i += 2)
            {
                byte a = m.Payload[i], b = m.Payload[i + 1];
                if (a == 0 && b == 0) continue;
                result.Add(new() { Source = m.Source, Module = ModuleName(m.Source), Code = $"{"PCBU"[(a >> 6) & 3]}{(a >> 4) & 3:X1}{a & 15:X1}{b:X2}" });
            }
        return result.DistinctBy(d => (d.Source, d.Code)).ToList();
    }

    private static Dictionary<string, byte[]> Mode09Data(string response, byte pid)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in ParseMessages(response).Where(m => m.Payload.Length >= 3 && m.Payload[0] == 0x49 && m.Payload[1] == pid).GroupBy(m => m.Source))
        {
            if (group.Any(m => m.IsoTp))
            {
                var m = group.First(m => m.IsoTp);
                int width = pid == 2 ? 17 : pid == 4 ? 16 : 4;
                int expected = m.Payload[2] * width;
                if (expected > 0 && m.Payload.Length >= expected + 3)
                    result[group.Key] = m.Payload.Skip(3).Take(expected).ToArray();
            }
            else
            {
                var frames = group.OrderBy(m => m.Payload[2]).DistinctBy(m => m.Payload[2]).ToList();
                // Legacy Mode 09 carries a sequence byte and exactly four data bytes.
                if (frames.Select((m, i) => m.Payload[2] == i + 1 && m.Payload.Length == 7).All(x => x))
                {
                    byte[] data = frames.SelectMany(m => m.Payload.Skip(3).Take(4)).ToArray();
                    int width = pid == 2 ? 20 : pid == 4 ? 16 : 4;
                    if (data.Length % width == 0) result[group.Key] = data;
                }
            }
        }
        return result;
    }

    public static Dictionary<string, string> DecodeMode09TextBySource(string response, byte pid)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Mode09Data(response, pid))
        {
            var texts = new List<string>();
            int width = pid == 4 ? 16 : pair.Value.Length;
            for (int offset = 0; offset < pair.Value.Length; offset += width)
            {
                byte[] bytes = pair.Value.Skip(offset).Take(width).ToArray();
                if (pid == 2) bytes = bytes.SkipWhile(b => b == 0).Take(17).ToArray();
                // CALID is NUL terminated; ignore all padding after its first NUL,
                // including the captured TCM's stray 49 in the padding area.
                bytes = bytes.TakeWhile(b => b != 0).ToArray();
                if (bytes.Length > 0 && bytes.All(b => b >= 0x20 && b <= 0x7E))
                    texts.Add(Encoding.ASCII.GetString(bytes).Trim());
            }
            result[pair.Key] = string.Join(", ", texts);
        }
        return result;
    }

    public static string DecodeMode09Text(string response, byte pid) =>
        DecodeMode09TextBySource(response, pid).OrderBy(p => p.Key).Select(p => p.Value).FirstOrDefault() ?? "";

    public static Dictionary<string, string> DecodeCvnBySource(string response) =>
        Mode09Data(response, 6).ToDictionary(p => p.Key, p => string.Join(", ", p.Value.Chunk(4).Select(b => Convert.ToHexString(b))), StringComparer.OrdinalIgnoreCase);

    public static string DecodeCvn(string response) => string.Join("; ", DecodeCvnBySource(response).Select(p => $"{p.Key}: {p.Value}"));
}
