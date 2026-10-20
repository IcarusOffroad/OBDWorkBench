using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

// Work-conserving scheduler. Priority 5 is the foreground/live lane and fills otherwise-idle
// bus time. When background work is also due the contested pattern is F F F F N, reserving
// about 80% of saturated-link opportunities for live data. Priorities 1-4 are weighted when
// background capacity is planned, so changing a preference changes real bus allocation.
public sealed class PollScheduler
{
    private readonly Dictionary<string, DateTime> _last = new(StringComparer.OrdinalIgnoreCase);
    private double _averageRttMs = 300;
    private DateTime _lastPlanUtc = DateTime.MinValue;
    private int _lastDelay = -1;
    private int _laneCycleIndex;

    public void Reset()
    {
        _last.Clear();
        _lastPlanUtc = DateTime.MinValue;
        _lastDelay = -1;
        _laneCycleIndex = 0;
    }

    public void Complete(string request, DateTime when, double rttMs)
    {
        _last[request] = when;
        if (rttMs > 0 && rttMs < 1500)
            _averageRttMs = 0.9 * _averageRttMs + 0.1 * rttMs;
    }

    public PidDefinition? Next(IEnumerable<PidDefinition> definitions, DateTime now, int quietMs, out int waitMs)
    {
        var active = definitions.Where(d => d.Enabled && !d.DiscoveryOnly && !d.DerivedOnly).ToList();
        if (active.Count == 0)
        {
            waitMs = 100;
            return null;
        }

        if (_lastDelay != quietMs || (now - _lastPlanUtc).TotalSeconds >= 5)
            Replan(active, now, quietMs);

        bool IsDue(PidDefinition d) => !_last.TryGetValue(d.Request, out DateTime last) ||
            last.AddMilliseconds(d.EffectivePollMs) <= now;
        DateTime DueAt(PidDefinition d) => _last.TryGetValue(d.Request, out DateTime last)
            ? last.AddMilliseconds(d.EffectivePollMs) : DateTime.MinValue;
        DateTime LastAt(PidDefinition d) => _last.TryGetValue(d.Request, out DateTime last)
            ? last : DateTime.MinValue;

        var due = active.Where(IsDue).ToList();
        var fast = active.Where(d => d.Priority >= 5).ToList();

        if (due.Count > 0)
        {
            var fastDue = due.Where(d => d.Priority >= 5).ToList();
            var normalDue = due.Where(d => d.Priority < 5).ToList();

            // Contested slots: F F F F N. If the preferred lane has nothing due, immediately
            // use the other lane rather than leaving the diagnostic bus idle.
            bool preferFast = _laneCycleIndex < 4;
            _laneCycleIndex = (_laneCycleIndex + 1) % 5;
            List<PidDefinition> pool = preferFast && fastDue.Count > 0 ? fastDue
                : normalDue.Count > 0 ? normalDue : fastDue;

            PidDefinition selected = pool
                .OrderBy(LastAt)
                .ThenBy(DueAt)
                .ThenByDescending(d => d.Priority)
                .ThenBy(d => d.Request)
                .First();
            waitMs = 0;
            return selected;
        }

        // Priority 5 explicitly means Live/foreground. Use those PIDs to consume bus slots
        // that would otherwise be empty, even if their normal requested interval is satisfied.
        if (fast.Count > 0)
        {
            PidDefinition selected = fast
                .OrderBy(LastAt)
                .ThenBy(d => d.Request)
                .First();
            waitMs = 0;
            return selected;
        }

        // If the user has no priority-5 PIDs, preserve requested cadence rather than turning
        // another priority into an implicit foreground lane.
        var next = active.OrderBy(DueAt).ThenByDescending(d => d.Priority).First();
        double delay = (DueAt(next) - now).TotalMilliseconds;
        waitMs = (int)Math.Clamp(Math.Ceiling(delay), 1, 100);
        return null;
    }

    private static double BackgroundWeight(int priority) => Math.Clamp(priority, 1, 4) switch
    {
        4 => 8.0,
        3 => 4.0,
        2 => 2.0,
        _ => 1.0
    };

    private void Replan(List<PidDefinition> active, DateTime now, int quietMs)
    {
        double slotMs = Math.Max(40, _averageRttMs + quietMs + 10);
        var fast = active.Where(d => d.Priority >= 5).ToList();
        var normal = active.Where(d => d.Priority < 5).ToList();

        if (fast.Count > 0 && normal.Count > 0)
        {
            const double fastFraction = 0.80;
            double fastInterval = slotMs * fast.Count / fastFraction;
            foreach (var d in fast)
                d.EffectivePollMs = Math.Ceiling(Math.Max(d.PollMs, fastInterval));

            // Allocate the remaining 20% according to both requested cadence and priority.
            // A priority-4 PID gets 8x the saturated-link background weight of priority 1.
            double normalCapacityPerMs = (1.0 - fastFraction) / slotMs;
            double scoreTotal = normal.Sum(d => BackgroundWeight(d.Priority) / Math.Max(100.0, d.PollMs));
            foreach (var d in normal)
            {
                double score = BackgroundWeight(d.Priority) / Math.Max(100.0, d.PollMs);
                double allocatedFrequencyPerMs = normalCapacityPerMs * score / Math.Max(0.0000001, scoreTotal);
                double allocatedInterval = 1.0 / Math.Max(0.0000001, allocatedFrequencyPerMs);
                // Keep even low-priority live-data rows from becoming effectively abandoned.
                // They may run slower than this when the link is physically saturated, but
                // they become due at least once per minute and re-enter fair background service.
                allocatedInterval = Math.Min(60000, allocatedInterval);
                d.EffectivePollMs = Math.Ceiling(Math.Max(d.PollMs, allocatedInterval));
            }
        }
        else
        {
            // Without a separate foreground lane, use weighted capacity planning across all
            // active PIDs but never promise a faster cadence than the database requested.
            double capacityPerMs = 0.90 / slotMs;
            double scoreTotal = active.Sum(d => BackgroundWeight(Math.Min(d.Priority, 4)) / Math.Max(100.0, d.PollMs));
            foreach (var d in active)
            {
                double score = BackgroundWeight(Math.Min(d.Priority, 4)) / Math.Max(100.0, d.PollMs);
                double allocatedFrequencyPerMs = capacityPerMs * score / Math.Max(0.0000001, scoreTotal);
                double allocatedInterval = 1.0 / Math.Max(0.0000001, allocatedFrequencyPerMs);
                d.EffectivePollMs = Math.Ceiling(Math.Max(d.PollMs, allocatedInterval));
            }
        }

        _lastDelay = quietMs;
        _lastPlanUtc = now;
    }
}
