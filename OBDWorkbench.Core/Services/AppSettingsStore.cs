using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OBDWorkbench.Services;

public sealed class AdapterProfile
{
    public string Identity { get; set; } = "";
    public string Transport { get; set; } = "Serial";
    public string ConnectionId { get; set; } = "";
    public string Port { get; set; } = "";
    public int Baud { get; set; } = 38400;
    public string Host { get; set; } = "192.168.0.10";
    public int TcpPort { get; set; } = 35000;
    public int ProtocolIndex { get; set; }
    public string ConfirmedProtocolNumber { get; set; } = "";
    public int TimingProfile { get; set; }
    public bool UseResponseCount { get; set; } = true;
    public int InterRequestDelayMs { get; set; }
    public bool AutoRecover { get; set; } = true;
    public int RecoveryThreshold { get; set; } = 3;
}


public sealed class SignalOffsetSetting
{
    public double Gain { get; set; } = 1.0;
    public double Offset { get; set; }
    public string Unit { get; set; } = "";
    // -1 means automatic formatting. 0..6 forces a fixed number of decimals.
    public int DecimalPlaces { get; set; } = -1;
}

public sealed class CalculatedSignalSetting
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Calculated Value";
    public string SourceKey { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceUnit { get; set; } = "";
    public string Units { get; set; } = "";
    public string Formula { get; set; } = "A";
    public double B { get; set; }
    public double C { get; set; }
    public double D { get; set; }
    public int DecimalPlaces { get; set; } = 2;
}

public sealed class TrendSeriesPreset
{
    public string Request { get; set; } = "";
    public string SignalName { get; set; } = "";
    public string Source { get; set; } = "";
    public string Unit { get; set; } = "";
    public int ColorIndex { get; set; }
    public string ColorHex { get; set; } = "";
    public bool AutoScale { get; set; } = true;
    public double? ManualMin { get; set; }
    public double? ManualMax { get; set; }
    public double DisplayGain { get; set; } = 1.0;
    public double DisplayOffset { get; set; }
    public string OffsetUnit { get; set; } = "";
    public int DecimalPlaces { get; set; } = -1;
    public bool ShowDataPoints { get; set; }
}

public sealed class TrendPreset
{
    public string Name { get; set; } = "";
    public string VehicleSignature { get; set; } = "";
    public int WindowSeconds { get; set; } = 60;
    public List<TrendSeriesPreset> Series { get; set; } = new();
}


public sealed class DashboardGaugePreset
{
    public string Request { get; set; } = "";
    public string SignalName { get; set; } = "";
    public string Source { get; set; } = "";
    public string Unit { get; set; } = "";
    public int ColorIndex { get; set; }
    public string ColorHex { get; set; } = "";
    public bool AutoScale { get; set; } = true;
    public double? ManualMin { get; set; }
    public double? ManualMax { get; set; }
    public double? ObservedMin { get; set; }
    public double? ObservedMax { get; set; }
    public double DisplayGain { get; set; } = 1.0;
    public double DisplayOffset { get; set; }
    public string OffsetUnit { get; set; } = "";
    public int DecimalPlaces { get; set; } = -1;
    public double? LowRangeMin { get; set; }
    public double? LowRangeMax { get; set; }
    public string LowRangeColorHex { get; set; } = "#2196F3";
    public double? HighRangeMin { get; set; }
    public double? HighRangeMax { get; set; }
    public string HighRangeColorHex { get; set; } = "#F44336";
}

public sealed class DashboardPreset
{
    public string Name { get; set; } = "";
    public string VehicleSignature { get; set; } = "";
    public List<DashboardGaugePreset> Gauges { get; set; } = new();
}

public sealed class AppSettingsData
{
    // Existing installations retain their direct COM source until explicitly changed.
    public string GpsSource { get; set; } = "COM";
    public bool GpsEnabled { get; set; }
    public string GpsPort { get; set; } = "";
    public int GpsBaud { get; set; } = 9600;
    public int GpsDataBits { get; set; } = 8;
    public string GpsParity { get; set; } = "None";
    public string GpsStopBits { get; set; } = "One";

    public string LastUsedPort { get; set; } = "";
    public string LastSuccessfulPort { get; set; } = "";
    public string LastSsmPort { get; set; } = "";
    public string LastTd5Port { get; set; } = "";
    public int LastTransportIndex { get; set; }
    public string LastWifiHost { get; set; } = "192.168.0.10";
    public int LastWifiPort { get; set; } = 35000;
    public bool RememberAdapterSettings { get; set; } = true;
    public string UnitSystem { get; set; } = "Imperial";
    public Dictionary<string, string> UnitOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PollPriorityOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SignalColors { get; set; } = new();
    public Dictionary<string, SignalOffsetSetting> SignalOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CalculatedSignalSetting> CalculatedSignals { get; set; } = new();
    public HashSet<string> PinnedSignals { get; set; } = new();
    public bool OverlayPlots { get; set; } = true;
    public int PresentationVersion { get; set; }
    public bool UseIcarusTheme { get; set; } = true;
    public bool UseLcarsSkin { get; set; }
    public bool FollowSystemTheme { get; set; } = true;
    public bool ConfirmDisruptiveCommands { get; set; } = true;
    public bool AutoCollapseConnectionPanel { get; set; } = true;
    public int TrendWindowSeconds { get; set; } = 60;
    public int DashboardGridColumns { get; set; } = 4;
    public Dictionary<string, VehicleProfile> VehicleProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TrendPreset> TrendPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DashboardPreset> DashboardPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AdapterProfile> AdapterProfiles { get; set; } = new Dictionary<string, AdapterProfile>(StringComparer.OrdinalIgnoreCase);
}

public sealed class VehicleProfile
{
    public string Signature { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Vin { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string PreferredProtocolNumber { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool CalibrationAccepted { get; set; }
    public int TimingProfile { get; set; }
    public int InterRequestDelayMs { get; set; }
    public bool UseResponseCount { get; set; } = true;
    public string CalibrationSignature { get; set; } = "";
    public string ManufacturerFamily { get; set; } = "Generic/unknown";
    // Adapter-independent identity evidence used from v0.3.0.4 onward.
    public string ProtocolNumber { get; set; } = "";
    public string SupportedPidEvidence { get; set; } = "";
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettingsStore(string settingsPath)
    {
        SettingsPath = Path.GetFullPath(settingsPath);
    }
    public string SettingsPath { get; }

    public AppSettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettingsData();

            string json = File.ReadAllText(SettingsPath);
            AppSettingsData? loaded = JsonSerializer.Deserialize<AppSettingsData>(json, JsonOptions);
            if (loaded is null)
                return new AppSettingsData();

            loaded.AdapterProfiles = loaded.AdapterProfiles is null
                ? new Dictionary<string, AdapterProfile>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, AdapterProfile>(loaded.AdapterProfiles, StringComparer.OrdinalIgnoreCase);
            loaded.UnitOverrides = loaded.UnitOverrides is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded.UnitOverrides, StringComparer.OrdinalIgnoreCase);
            loaded.SignalOffsets = loaded.SignalOffsets is null
                ? new Dictionary<string, SignalOffsetSetting>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, SignalOffsetSetting>(loaded.SignalOffsets, StringComparer.OrdinalIgnoreCase);
            foreach (string key in loaded.SignalOffsets.Keys.ToList())
            {
                SignalOffsetSetting? adjustment = loaded.SignalOffsets[key];
                if (string.IsNullOrWhiteSpace(key) || adjustment is null || !double.IsFinite(adjustment.Offset) || !double.IsFinite(adjustment.Gain))
                {
                    loaded.SignalOffsets.Remove(key);
                    continue;
                }
                if (Math.Abs(adjustment.Gain) < 1e-12) adjustment.Gain = 1.0;
                adjustment.Unit = adjustment.Unit?.Trim() ?? "";
                adjustment.DecimalPlaces = Math.Clamp(adjustment.DecimalPlaces, -1, 6);
            }
            loaded.CalculatedSignals ??= new List<CalculatedSignalSetting>();
            loaded.CalculatedSignals = loaded.CalculatedSignals
                .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.SourceKey) && !string.IsNullOrWhiteSpace(x.Name))
                .ToList();
            foreach (CalculatedSignalSetting calc in loaded.CalculatedSignals)
            {
                calc.Id = string.IsNullOrWhiteSpace(calc.Id) ? Guid.NewGuid().ToString("N") : calc.Id.Trim();
                calc.Name = calc.Name.Trim();
                calc.SourceKey = calc.SourceKey.Trim();
                calc.SourceName = calc.SourceName?.Trim() ?? "";
                calc.SourceUnit = calc.SourceUnit?.Trim() ?? "";
                calc.Units = calc.Units?.Trim() ?? "";
                calc.Formula = string.IsNullOrWhiteSpace(calc.Formula) ? "A" : calc.Formula.Trim();
                calc.DecimalPlaces = Math.Clamp(calc.DecimalPlaces, 0, 6);
                if (!double.IsFinite(calc.B)) calc.B = 0;
                if (!double.IsFinite(calc.C)) calc.C = 0;
                if (!double.IsFinite(calc.D)) calc.D = 0;
            }
            loaded.PollPriorityOverrides = loaded.PollPriorityOverrides is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(loaded.PollPriorityOverrides, StringComparer.OrdinalIgnoreCase);
            foreach (string key in loaded.PollPriorityOverrides.Keys.ToList())
            {
                int value = loaded.PollPriorityOverrides[key];
                if (value < 1 || value > 5 || string.IsNullOrWhiteSpace(key))
                    loaded.PollPriorityOverrides.Remove(key);
            }
            loaded.VehicleProfiles = loaded.VehicleProfiles is null
                ? new Dictionary<string, VehicleProfile>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, VehicleProfile>(loaded.VehicleProfiles, StringComparer.OrdinalIgnoreCase);
            loaded.TrendPresets = loaded.TrendPresets is null
                ? new Dictionary<string, TrendPreset>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, TrendPreset>(loaded.TrendPresets, StringComparer.OrdinalIgnoreCase);
            foreach (string key in loaded.TrendPresets.Keys.ToList())
            {
                TrendPreset preset = loaded.TrendPresets[key];
                if (preset is null || string.IsNullOrWhiteSpace(key))
                {
                    loaded.TrendPresets.Remove(key);
                    continue;
                }
                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? key : preset.Name.Trim();
                preset.WindowSeconds = Math.Clamp(preset.WindowSeconds, 5, 3600);
                preset.Series ??= new List<TrendSeriesPreset>();
                preset.Series = preset.Series
                    .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Request) && !string.IsNullOrWhiteSpace(x.SignalName))
                    .ToList();
                foreach (TrendSeriesPreset series in preset.Series)
                {
                    if (!double.IsFinite(series.DisplayGain) || Math.Abs(series.DisplayGain) < 1e-12) series.DisplayGain = 1.0;
                    if (!double.IsFinite(series.DisplayOffset)) series.DisplayOffset = 0;
                    series.OffsetUnit = series.OffsetUnit?.Trim() ?? "";
                    series.DecimalPlaces = Math.Clamp(series.DecimalPlaces, -1, 6);
                    if (series.ManualMin.HasValue && !double.IsFinite(series.ManualMin.Value)) series.ManualMin = null;
                    if (series.ManualMax.HasValue && !double.IsFinite(series.ManualMax.Value)) series.ManualMax = null;
                    if (!series.AutoScale && (!series.ManualMin.HasValue || !series.ManualMax.HasValue || series.ManualMax.Value <= series.ManualMin.Value))
                        series.AutoScale = true;
                }
            }
            loaded.DashboardPresets = loaded.DashboardPresets is null
                ? new Dictionary<string, DashboardPreset>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DashboardPreset>(loaded.DashboardPresets, StringComparer.OrdinalIgnoreCase);
            foreach (string key in loaded.DashboardPresets.Keys.ToList())
            {
                DashboardPreset preset = loaded.DashboardPresets[key];
                if (preset is null || string.IsNullOrWhiteSpace(key))
                {
                    loaded.DashboardPresets.Remove(key);
                    continue;
                }
                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? key : preset.Name.Trim();
                preset.Gauges ??= new List<DashboardGaugePreset>();
                preset.Gauges = preset.Gauges
                    .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Request) && !string.IsNullOrWhiteSpace(x.SignalName))
                    .ToList();
                foreach (DashboardGaugePreset gauge in preset.Gauges)
                {
                    if (!double.IsFinite(gauge.DisplayGain) || Math.Abs(gauge.DisplayGain) < 1e-12) gauge.DisplayGain = 1.0;
                    if (!double.IsFinite(gauge.DisplayOffset)) gauge.DisplayOffset = 0;
                    gauge.OffsetUnit = gauge.OffsetUnit?.Trim() ?? "";
                    gauge.DecimalPlaces = Math.Clamp(gauge.DecimalPlaces, -1, 6);
                    if (gauge.ObservedMin.HasValue && !double.IsFinite(gauge.ObservedMin.Value)) gauge.ObservedMin = null;
                    if (gauge.ObservedMax.HasValue && !double.IsFinite(gauge.ObservedMax.Value)) gauge.ObservedMax = null;
                    if (gauge.ManualMin.HasValue && !double.IsFinite(gauge.ManualMin.Value)) gauge.ManualMin = null;
                    if (gauge.ManualMax.HasValue && !double.IsFinite(gauge.ManualMax.Value)) gauge.ManualMax = null;
                    if (!gauge.AutoScale && (!gauge.ManualMin.HasValue || !gauge.ManualMax.HasValue || gauge.ManualMax.Value <= gauge.ManualMin.Value))
                        gauge.AutoScale = true;
                }
            }
            if (loaded.UnitSystem is not ("Imperial" or "Metric")) loaded.UnitSystem = "Imperial";

            if (string.IsNullOrWhiteSpace(loaded.LastWifiHost))
                loaded.LastWifiHost = "192.168.0.10";
            if (loaded.LastWifiPort <= 0 || loaded.LastWifiPort > 65535)
                loaded.LastWifiPort = 35000;
            loaded.LastTransportIndex = Math.Clamp(loaded.LastTransportIndex, 0, 1);
            loaded.TrendWindowSeconds = Math.Clamp(loaded.TrendWindowSeconds <= 0 ? 60 : loaded.TrendWindowSeconds, 5, 3600);
            loaded.DashboardGridColumns = Math.Clamp(loaded.DashboardGridColumns <= 0 ? 4 : loaded.DashboardGridColumns, 2, 5);

            // Backward compatibility with v0.2.3 profiles, which only had Port/Baud.
            foreach (AdapterProfile profile in loaded.AdapterProfiles.Values)
            {
                if (string.IsNullOrWhiteSpace(profile.Transport))
                    profile.Transport = "Serial";
                if (string.IsNullOrWhiteSpace(profile.ConnectionId))
                {
                    profile.ConnectionId = profile.Transport.Equals("Tcp", StringComparison.OrdinalIgnoreCase)
                        ? $"{profile.Host}:{profile.TcpPort}"
                        : profile.Port;
                }
                if (profile.TcpPort <= 0 || profile.TcpPort > 65535)
                    profile.TcpPort = 35000;
                if (string.IsNullOrWhiteSpace(profile.Host))
                    profile.Host = "192.168.0.10";
                string protocol = (profile.ConfirmedProtocolNumber ?? "").Trim().ToUpperInvariant();
                profile.ConfirmedProtocolNumber = protocol.Length == 1 && "123456789ABC".Contains(protocol) ? protocol : "";
            }
            foreach (VehicleProfile profile in loaded.VehicleProfiles.Values)
            {
                string protocol = (profile.PreferredProtocolNumber ?? "").Trim().ToUpperInvariant();
                profile.PreferredProtocolNumber = protocol.Length == 1 && "123456789ABC".Contains(protocol) ? protocol : "";
            }

            return loaded;
        }
        catch
        {
            return new AppSettingsData();
        }
    }

    public void Save(AppSettingsData settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static string BuildProfileKey(string identity, string connectionId) =>
        $"{identity.Trim()}|{connectionId.Trim()}";

    public static AdapterProfile? FindBestProfile(AppSettingsData settings, string identity, string connectionId, string? transport = null)
    {
        string exactKey = BuildProfileKey(identity, connectionId);
        if (settings.AdapterProfiles.TryGetValue(exactKey, out AdapterProfile? exact))
            return exact;

        foreach (AdapterProfile profile in settings.AdapterProfiles.Values)
        {
            if (profile.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase) &&
                GetConnectionId(profile).Equals(connectionId, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        foreach (AdapterProfile profile in settings.AdapterProfiles.Values)
        {
            bool transportMatches = string.IsNullOrWhiteSpace(transport) || profile.Transport.Equals(transport, StringComparison.OrdinalIgnoreCase);
            if (transportMatches && profile.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        foreach (AdapterProfile profile in settings.AdapterProfiles.Values)
        {
            if (GetConnectionId(profile).Equals(connectionId, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return null;
    }

    public static string GetConnectionId(AdapterProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConnectionId))
            return profile.ConnectionId;
        if (profile.Transport.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
            return $"{profile.Host}:{profile.TcpPort}";
        return profile.Port;
    }
}
