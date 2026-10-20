namespace OBDWorkbench.Models;

public static class SignalScaleDefaults
{
    // Signals that have a physically meaningful zero baseline. Gauges use this for both automatic and manual ranges; trends use it for autoscale.
    public static bool StartsAtZero(string request, string name)
    {
        string pid = request.Replace(" ", "").ToUpperInvariant();
        return pid == "010C" || pid == "010D" || pid == "0111" ||
            name.Contains("RPM", System.StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Engine Speed", System.StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Vehicle Speed", System.StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Throttle", System.StringComparison.OrdinalIgnoreCase);
    }
}
