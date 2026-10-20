using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OBDWorkbench.Models;

public sealed partial class PidLiveValue : INotifyPropertyChanged
{
    private readonly Queue<double> _recentIntervalsSeconds = new();
    private string _displayValue = "--";
    private string _raw = "";
    private string _sampleStatus = "";
    public string SampleStatus { get => _sampleStatus; set { _sampleStatus = value; OnPropertyChanged(); } }
    private DateTime? _updated;
    private double? _lastRttMs;
    private double? _lastFirstByteMs;
    private double? _lastTailMs;
    private double? _actualHz;
    private string _units = "";
    private bool _showDetailedMetrics;
    private DateTime? _lastRateSample;
    private double? _latestNumericValue;
    private double? _unadjustedNumericValue;
    private double _displayGain = 1.0;
    private double _displayOffset;
    private int _decimalPlaces = -1;
    private bool _canTrend;
    private string _trendUnit = "";

    public required PidDefinition Definition { get; init; }
    public string Source { get; init; } = "Unattributed";
    public string Name => $"{Definition.Name} [{Source}]";
    public string Units { get => _units.Length == 0 ? Definition.Units : _units; set { if (_units != value) { _units = value; OnPropertyChanged(); } } }
    public string Category => Definition.Category;
    public bool ShowDetailedMetrics { get => _showDetailedMetrics; set { if (_showDetailedMetrics != value) { _showDetailedMetrics = value; OnPropertyChanged(); } } }
    public bool CanTrend { get => _canTrend; set { if (_canTrend != value) { _canTrend = value; OnPropertyChanged(); } } }
    public double? LatestNumericValue { get => _latestNumericValue; set { if (_latestNumericValue != value) { _latestNumericValue = value; OnPropertyChanged(); } } }
    public double? UnadjustedNumericValue { get => _unadjustedNumericValue; set { if (_unadjustedNumericValue != value) { _unadjustedNumericValue = value; OnPropertyChanged(); } } }
    public double DisplayGain { get => _displayGain; set { double v = double.IsFinite(value) && Math.Abs(value) > 1e-12 ? value : 1.0; if (Math.Abs(_displayGain - v) > 1e-12) { _displayGain = v; OnPropertyChanged(); OnPropertyChanged(nameof(OffsetText)); } } }
    public double DisplayOffset { get => _displayOffset; set { double v = double.IsFinite(value) ? value : 0; if (Math.Abs(_displayOffset - v) > 1e-12) { _displayOffset = v; OnPropertyChanged(); OnPropertyChanged(nameof(OffsetText)); } } }
    public int DecimalPlaces { get => _decimalPlaces; set { int v = Math.Clamp(value, -1, 6); if (_decimalPlaces != v) { _decimalPlaces = v; OnPropertyChanged(); } } }
    public string OffsetText => $"Gain {DisplayGain:0.######}  Offset {DisplayOffset:+0.###;-0.###;0}";
    public bool IsCalculated { get; set; }
    public string CalculatedId { get; set; } = "";
    public string TrendUnit { get => _trendUnit; set { if (_trendUnit != value) { _trendUnit = value; OnPropertyChanged(); } } }

    public string DisplayValue
    {
        get => _displayValue;
        set { if (_displayValue != value) { _displayValue = value; OnPropertyChanged(); } }
    }

    public string Raw
    {
        get => _raw;
        set { if (_raw != value) { _raw = value; OnPropertyChanged(); } }
    }

    public DateTime? Updated
    {
        get => _updated;
        private set
        {
            if (_updated != value)
            {
                _updated = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpdatedText));
            }
        }
    }

    public double? LastRttMs
    {
        get => _lastRttMs;
        private set
        {
            if (_lastRttMs != value)
            {
                _lastRttMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PerformanceText));
            }
        }
    }

    public double? LastFirstByteMs
    {
        get => _lastFirstByteMs;
        private set
        {
            if (_lastFirstByteMs != value)
            {
                _lastFirstByteMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PerformanceText));
            }
        }
    }

    public double? LastTailMs
    {
        get => _lastTailMs;
        private set
        {
            if (_lastTailMs != value)
            {
                _lastTailMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PerformanceText));
            }
        }
    }

    public double? ActualHz
    {
        get => _actualHz;
        private set
        {
            if (_actualHz != value)
            {
                _actualHz = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PerformanceText));
            }
        }
    }

    public string UpdatedText => Updated?.ToString("HH:mm:ss.fff") ?? "Never";

    public string PerformanceText
    {
        get
        {
            string actual = ActualHz.HasValue ? ActualHz.Value.ToString("0.00") : "--";
            string rtt = LastRttMs.HasValue ? LastRttMs.Value.ToString("0") : "--";
            string first = LastFirstByteMs.HasValue ? LastFirstByteMs.Value.ToString("0") : "--";
            string tail = LastTailMs.HasValue ? LastTailMs.Value.ToString("0") : "--";
            return Definition.DiscoveryOnly ? "Discovery snapshot" :
                $"Requested {Definition.RequestedHz:0.##} Hz   Scheduled {(Definition.EffectivePollMs > 0 ? 1000 / Definition.EffectivePollMs : Definition.RequestedHz):0.##} Hz   Actual {actual} Hz\nRTT {rtt} ms   First {first} ms   Tail {tail} ms";
        }
    }

    public void RecordSample(DateTime now, double rttMs, double firstByteMs, double tailMs)
    {
        if (_lastRateSample.HasValue)
        {
            double seconds = (now - _lastRateSample.Value).TotalSeconds;
            if (seconds > 0.001 && seconds < 120)
            {
                _recentIntervalsSeconds.Enqueue(seconds);
                while (_recentIntervalsSeconds.Count > 8)
                    _recentIntervalsSeconds.Dequeue();

                double averageSeconds = _recentIntervalsSeconds.Average();
                ActualHz = averageSeconds > 0 ? 1.0 / averageSeconds : null;
            }
        }

        _lastRateSample = now;
        LastRttMs = rttMs;
        LastFirstByteMs = firstByteMs;
        LastTailMs = tailMs;
        Updated = now;
    }

    public void ResetPerformance()
    {
        _recentIntervalsSeconds.Clear();
        _lastRateSample = null;
        ActualHz = null;
        LastRttMs = null;
        LastFirstByteMs = null;
        LastTailMs = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
