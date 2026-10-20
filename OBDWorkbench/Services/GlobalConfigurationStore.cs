using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public sealed class GlobalConfigurationData
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public string VehicleSignature { get; set; } = "";
    public string LiveDataSortColumn { get; set; } = "";
    public int LiveDataSortState { get; set; }
    public string LiveDataFilter { get; set; } = "";
    public bool CardView { get; set; }
    public AppSettingsData Settings { get; set; } = new();
    public List<TrendSeriesPreset> ActiveTrendSeries { get; set; } = new();
    public List<DashboardGaugePreset> ActiveDashboardGauges { get; set; } = new();
}

public sealed class GlobalConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigurationPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OBDWorkbench",
        "OBDWorkbenchConfig.json");

    public void Save(
        AppSettingsData settings,
        IEnumerable<TrendSeriesState> trendSeries,
        IEnumerable<DashboardGaugeState> dashboardGauges,
        string vehicleSignature,
        string liveDataSortColumn,
        int liveDataSortState,
        string liveDataFilter,
        bool cardView)
    {
        string? directory = Path.GetDirectoryName(ConfigurationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var data = new GlobalConfigurationData
        {
            SavedUtc = DateTime.UtcNow,
            VehicleSignature = vehicleSignature ?? "",
            LiveDataSortColumn = liveDataSortColumn ?? "",
            LiveDataSortState = liveDataSortState,
            LiveDataFilter = liveDataFilter ?? "",
            CardView = cardView,
            Settings = settings,
            ActiveTrendSeries = trendSeries.Select(x => new TrendSeriesPreset
            {
                Request = x.Request,
                SignalName = x.SignalName,
                Source = x.Source,
                Unit = x.Unit,
                ColorIndex = x.ColorIndex,
                ColorHex = x.ColorHex,
                AutoScale = x.AutoScale,
                ManualMin = x.ManualMin,
                ManualMax = x.ManualMax,
                DisplayGain = x.DisplayGain,
                DisplayOffset = x.DisplayOffset,
                OffsetUnit = x.Unit,
                DecimalPlaces = x.DecimalPlaces,
                ShowDataPoints = x.ShowDataPoints
            }).ToList(),
            ActiveDashboardGauges = dashboardGauges.Select(x => new DashboardGaugePreset
            {
                Request = x.Request,
                SignalName = x.SignalName,
                Source = x.Source,
                Unit = x.Unit,
                ColorIndex = x.ColorIndex,
                ColorHex = x.ColorHex,
                AutoScale = x.AutoScale,
                ManualMin = x.ManualMin,
                ManualMax = x.ManualMax,
                ObservedMin = x.ObservedMin,
                ObservedMax = x.ObservedMax,
                DisplayGain = x.DisplayGain,
                DisplayOffset = x.DisplayOffset,
                OffsetUnit = x.Unit,
                DecimalPlaces = x.DecimalPlaces,
                LowRangeMin = x.LowRangeMin,
                LowRangeMax = x.LowRangeMax,
                LowRangeColorHex = x.LowRangeColorHex,
                HighRangeMin = x.HighRangeMin,
                HighRangeMax = x.HighRangeMax,
                HighRangeColorHex = x.HighRangeColorHex
            }).ToList()
        };

        string tempPath = ConfigurationPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(tempPath, ConfigurationPath, true);
    }
}
