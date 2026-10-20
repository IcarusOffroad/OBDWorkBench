namespace OBDWorkbench.Models;

public sealed class PidDefinition
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = "";
    public string Request { get; set; } = "";
    public string ResponseMode { get; set; } = "41";
    public string PID { get; set; } = "";
    public string Formula { get; set; } = "A";
    public string Units { get; set; } = "";
    public double Min { get; set; }
    public double Max { get; set; }
    public int PollMs { get; set; } = 1000;
    public bool DiscoveryOnly { get; set; }
    // Derived-only rows share an ECU request with a scheduled parent row. They are decoded
    // from the same response and never consume their own diagnostic-bus slot.
    public bool DerivedOnly { get; set; }
    public string DisplayKind { get; set; } = "";
    public double EffectivePollMs { get; set; }
    public int Priority { get; set; } = 1;
    public int DefaultPriority { get; set; } = 1;
    public int ExpectedResponses { get; set; } = 0;
    public string Responders { get; set; } = "Not discovered";
    public string Category { get; set; } = "General";
    public string Notes { get; set; } = "";

    public double RequestedHz => PollMs > 0 ? 1000.0 / PollMs : 0;
}
