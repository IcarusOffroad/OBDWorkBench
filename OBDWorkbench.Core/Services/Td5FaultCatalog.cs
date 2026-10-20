namespace OBDWorkbench.Services;

public sealed record Td5FaultDefinition(string Code, string Status, string Description, string Source, string Evidence = "", string Confidence = "Published");

public static class Td5FaultCatalog
{
    private static readonly Dictionary<string, Td5FaultDefinition> Map = Build();

    public static Td5FaultDefinition Lookup(string code) => Map.TryGetValue(code, out var d)
        ? d
        : new Td5FaultDefinition(code, "Other", "Undocumented TD5 bitmap position " + code, "TD5 bitmap / unmapped", "", "Unknown");

    public static IReadOnlyCollection<Td5FaultDefinition> All => Map.Values;

    private static Dictionary<string, Td5FaultDefinition> Build()
    {
        var d = new Dictionary<string, Td5FaultDefinition>(StringComparer.OrdinalIgnoreCase);
        void Add(string code, string status, string description, string evidence = "", string confidence = "Published") =>
            d[code] = new Td5FaultDefinition(code, status, description, "Published TD5 bitmap map", evidence, confidence);

        // Sensor / plausibility families. Online tables disagree on a few low-numbered
        // descriptions, so experimentally demonstrated associations are retained explicitly.
        string[] g1 = { "EGR inlet-throttle diagnostic", "Wastegate diagnostic", "EGR vacuum diagnostic", "Temperature-gauge diagnostic", "Driver demand problem 1", "Driver demand problem 2", "Air flow circuit", "Manifold pressure circuit" };
        string[] g2 = { "Inlet air temperature circuit", "Fuel temperature circuit", "Coolant temperature circuit", "Battery voltage", "Reference voltage", "Ambient temperature", "Driver demand supply problem", "Ambient pressure circuit" };
        for (int bit=1; bit<=8; bit++) { Add($"1-{bit}", "Logged", g1[bit-1]); Add($"3-{bit}", "Logged", g1[bit-1]); Add($"5-{bit}", "Current", g1[bit-1]); }
        for (int bit=1; bit<=8; bit++) { Add($"2-{bit}", "Logged", g2[bit-1]); Add($"4-{bit}", "Logged", g2[bit-1]); if (bit != 6) Add($"6-{bit}", "Current", g2[bit-1]); }

        string[] overtempA = { "Cruise lamp output over-temperature", "Fuel-used output over-temperature", "Radiator fan output over-temperature", "Active-mount output over-temperature", "Wastegate output short circuit", "EGR throttle output short circuit", "EGR vacuum output short circuit", "Temperature-gauge output short circuit" };
        string[] openA = { "Fuel-used output open load", "Cruise lamp output open load", "Radiator fan output open load", "Active-mount output open load", "Wastegate output open load", "EGR throttle output open load", "EGR vacuum output open load", "Temperature-gauge output open load" };
        string[] openB = { "A/C fan drive open load", "Fuel pump drive open load", "Tachometer open load", "Gearbox/ABS drive open load", "A/C clutch open load", "MIL lamp drive open load", "Glow-lamp drive open load", "Glow-relay drive open load" };
        for (int bit=1; bit<=8; bit++)
        {
            Add($"7-{bit}", "Logged", overtempA[bit-1]); Add($"11-{bit}", "Current", overtempA[bit-1]); Add($"13-{bit}", "Current", overtempA[bit-1]);
            Add($"9-{bit}", "Logged", openA[bit-1]);
            Add($"10-{bit}", "Logged", openB[bit-1]); Add($"12-{bit}", "Current", openB[bit-1]); Add($"14-{bit}", "Current", openB[bit-1]);
        }

        Add("15-2", "Logged", "High speed crank", "Verified after a known high-RPM cutout; cleared and remained cleared.", "Experimentally verified");
        Add("16-2", "Logged", "High speed crank");
        Add("17-2", "Current", "High speed crank");

        Add("19-2", "Logged", "CAN RX/TX error"); Add("19-3", "Logged", "CAN TX/RX error"); Add("19-6", "Logged", "Noisy crank signal detected"); Add("19-8", "Logged", "CAN reset failure");
        Add("20-1", "Logged", "Turbocharger underboost"); Add("20-2", "Logged", "Turbocharger overboost"); Add("20-4", "Logged", "EGR valve stuck open"); Add("20-5", "Logged", "EGR valve stuck closed");
        Add("21-4", "Logged", "Driver demand 1 out of range"); Add("21-5", "Logged", "Driver demand 2 out of range"); Add("21-6", "Logged", "Problem detected with driver demand"); Add("21-7", "Logged", "Driver demand inconsistency"); Add("21-8", "Logged", "Injector-trim data corruption");
        Add("22-1", "Logged", "Road speed missing"); Add("22-3", "Logged", "Vehicle acceleration outside cruise-control bounds"); Add("22-7", "Logged", "Cruise resume switch stuck"); Add("22-8", "Logged", "Cruise set switch stuck");
        Add("23-1", "Current", "Excessive CAN bus-off"); Add("23-2", "Current", "CAN RX/TX error"); Add("23-3", "Current", "CAN TX/RX error"); Add("23-4", "Current", "Remote CAN mode problem"); Add("23-5", "Current", "Underboost occurred this trip"); Add("23-6", "Current", "Noisy crank signal detected");
        Add("24-1", "Current", "Turbocharger underboost"); Add("24-2", "Current", "Turbocharger overboost"); Add("24-3", "Current", "Overboost occurred this trip"); Add("24-4", "Current", "EGR valve stuck open"); Add("24-5", "Current", "EGR valve stuck closed"); Add("24-7", "Current", "Automatic gearbox problem");
        Add("25-4", "Logged", "Driver demand 1 out of range"); Add("25-5", "Logged", "Driver demand 2 out of range"); Add("25-6", "Other", "Driver demand problem"); Add("25-7", "Other", "Driver demand inconsistency"); Add("25-8", "Other", "Injector-trim data corruption");
        Add("26-1", "Current", "Road speed missing"); Add("26-2", "Current", "Cruise control system problem"); Add("26-3", "Current", "Vehicle acceleration outside cruise-control bounds"); Add("26-7", "Current", "Cruise resume switch stuck"); Add("26-8", "Current", "Cruise set switch stuck");

        for (int injector=1; injector<=6; injector++)
        {
            Add($"27-{injector}", "Logged", $"Injector {injector} peak charge long");
            Add($"28-{injector}", "Logged", $"Injector {injector} peak charge short");
            Add($"29-{injector}", "Current", $"Injector {injector} peak charge long");
            Add($"30-{injector}", "Current", $"Injector {injector} peak charge short");
            Add($"31-{injector}", "Logged", $"Injector {injector} open circuit");
            Add($"32-{injector}", "Logged", $"Injector {injector} short circuit");
            Add($"33-{injector}", "Current", $"Injector {injector} open circuit");
            Add($"34-{injector}", "Current", $"Injector {injector} short circuit");
            Add($"35-{injector}", "Logged", $"Injector {injector} partial short");
        }
        Add("27-7", "Logged", "Topside switch post-injection fault"); Add("28-7", "Logged", "Topside switch pre-injection fault"); Add("29-7", "Current", "Topside switch post-injection fault"); Add("30-7", "Current", "Topside switch pre-injection fault");

        // Experiment-derived associations where the exact published semantic is absent or conflicted.
        d["1-7"] = d["1-7"] with { Evidence = "Appeared when MAF was disconnected while running.", Confidence = "Experimentally verified" };
        d["3-7"] = d["3-7"] with { Evidence = "Stored after MAF fault/reconnect and cleared by fault clear.", Confidence = "Experimentally verified" };
        d["5-7"] = d["5-7"] with { Evidence = "Present while MAF was disconnected; disappeared after reconnect.", Confidence = "Experimentally verified" };
        d["4-1"] = d["4-1"] with { Evidence = "Generated when combined MAP/IAT connector was disconnected.", Confidence = "Experimentally verified" };
        d["6-1"] = d["6-1"] with { Evidence = "Present while combined MAP/IAT connector was disconnected; disappeared on reconnect.", Confidence = "Experimentally verified" };
        foreach (string code in new[] { "10-1","10-3","10-4","10-5","14-1","14-3","14-4","14-5" })
            d[code] = d[code] with { Evidence = "Observed on a Defender built without the corresponding A/C, tachometer and/or ABS equipment; current faults can exist without MIL illumination.", Confidence = "Experimentally corroborated" };
        foreach (var x in new[] { ("16-3","MAP/IAT-associated latched flag"),("16-7","MAF-associated latched flag"),("16-8","MAP/IAT-associated latched flag"),("18-3","MAP/IAT-associated live flag"),("18-7","MAF-associated live flag"),("18-8","MAP/IAT-associated live flag"),("24-8","Power-cycle-associated / exact meaning unknown"),("26-4","Persistent/live status; changed during high-RPM testing"),("26-6","Persistent/live status; exact meaning unknown") })
            d[x.Item1] = new Td5FaultDefinition(x.Item1, "Other", x.Item2, "Experimental observation", x.Item2, "Association verified; exact semantic unknown");
        foreach (string code in new[] { "1-5","3-5","5-5" })
            d[code] = d[code] with { Evidence = "Also appeared during combined MAP/IAT disconnect testing; may be a secondary plausibility/default-strategy effect rather than a direct MAP label.", Confidence = "Published label; experimental secondary association" };

        return d;
    }
}
