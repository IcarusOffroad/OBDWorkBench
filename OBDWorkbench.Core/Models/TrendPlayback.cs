namespace OBDWorkbench.Models;

// Pure time-window state: pausing affects only display, never acquisition.
public sealed class TrendPlayback
{
    public bool IsLive { get; private set; } = true;
    public bool Connected { get; private set; }
    private DateTime _edgeUtc = DateTime.UtcNow;
    private DateTime _heldUtc = DateTime.UtcNow;
    public DateTime EndUtc(DateTime now) => IsLive && Connected ? now : _heldUtc;
    public void SetConnected(bool connected, DateTime now)
    {
        if (Connected == connected) return;
        if (!connected)
        {
            _edgeUtc = now;
            if (IsLive) _heldUtc = now;
        }
        else if (IsLive) _heldUtc = now;
        Connected = connected;
    }
    public void Pause(DateTime now) { _heldUtc = EndUtc(now); IsLive = false; }
    public void Live(DateTime now)
    {
        IsLive = true;
        _heldUtc = Connected ? now : _edgeUtc;
    }
    public void Back(DateTime now, int seconds, DateTime? earliest)
    {
        var end = EndUtc(now).AddSeconds(-seconds / 2.0);
        if (earliest.HasValue)
        {
            var firstFullWindow = earliest.Value.AddSeconds(seconds);
            var edge = Connected ? now : _edgeUtc;
            if (firstFullWindow > edge) firstFullWindow = edge;
            if (end < firstFullWindow) end = firstFullWindow;
        }
        _heldUtc = end; IsLive = false;
    }
    public void Forward(DateTime now, int seconds)
    {
        var end = EndUtc(now).AddSeconds(seconds / 2.0);
        var edge = Connected ? now : _edgeUtc;
        if (end >= edge) Live(now);
        else { _heldUtc = end; IsLive = false; }
    }
}
