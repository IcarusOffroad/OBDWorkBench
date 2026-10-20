namespace OBDWorkbench.Services;

// Session-scoped evidence. Never reuse a response count from a different vehicle.
public sealed class ResponderCatalog
{
    public bool UseSpacedCountSyntax { get; set; }
    public Dictionary<string, HashSet<byte>> Supported { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _uncertain = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _completeStreak = new(StringComparer.OrdinalIgnoreCase);
    public void Clear() { Supported.Clear(); _observed.Clear(); _uncertain.Clear(); _completeStreak.Clear(); UseSpacedCountSyntax = false; }
    public void AddBitmap(string response, byte block = 0)
    {
        foreach (var pair in MultiEcuParser.DecodeSupportedPids(response, block))
        {
            if (!Supported.TryGetValue(pair.Key, out var pids)) Supported[pair.Key] = pids = new();
            pids.UnionWith(pair.Value);
        }
    }
    public string[] Sources(string request)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Length == 4 && request.StartsWith("01") && byte.TryParse(request.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out byte pid))
            sources.UnionWith(Supported.Where(p => p.Value.Contains(pid)).Select(p => p.Key));
        if (_observed.TryGetValue(request, out var observed)) sources.UnionWith(observed);
        return sources.OrderBy(s => s).ToArray();
    }
    public void Observe(string request, IEnumerable<string> sources)
    {
        if (!_observed.TryGetValue(request, out var observed)) _observed[request] = observed = new();
        observed.UnionWith(sources);
        if (observed.Contains("Unattributed")) _uncertain.Add(request);
    }
    public void MarkUncertain(string request)
    {
        _uncertain.Add(request);
        _completeStreak[request] = 0;
    }
    // Only unrestricted, fully decoded replies can restore an uncertain count.
    // A truncated/count-limited reply is never proof that every ECU was collected.
    public bool Revalidate(string request, IEnumerable<string> actualSources, bool decoded, bool unrestricted)
    {
        if (!_uncertain.Contains(request)) return false;
        var expected = Sources(request);
        var actual = actualSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!decoded || expected.Length == 0 || actual.Contains("Unattributed") || !actual.SetEquals(expected))
        { _completeStreak[request] = 0; return false; }
        if (!unrestricted) return false;
        _completeStreak.TryGetValue(request, out int streak);
        _completeStreak[request] = ++streak;
        if (streak < 3) return false;
        _uncertain.Remove(request);
        _completeStreak.Remove(request);
        return true;
    }
    public int ResponseCount(string request)
    {
        var sources = Sources(request);
        return _uncertain.Contains(request) || sources.Contains("Unattributed") || sources.Length > 9 ? 0 : sources.Length;
    }
    public string Command(string request, bool optimize)
    {
        int count = ResponseCount(request);
        if (!optimize || count <= 0) return request;
        if (!UseSpacedCountSyntax) return request + count;
        string bytes = string.Join(" ", Enumerable.Range(0, request.Length / 2).Select(i => request.Substring(i * 2, 2)));
        return $"{bytes} {count}";
    }
}

public static class TimingPolicy
{
    public static double TieMargin(double ms) => Math.Max(5.0, ms * 0.05);
    public static bool MateriallyFaster(double candidate, double baseline) => baseline - candidate > TieMargin(baseline);
}
