using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OBDWorkbench.Models;

public sealed class DashboardGaugeState : INotifyPropertyChanged, IConfigurableSignal
{
    private string _unit = "";
    private double? _currentValue;
    private double? _observedMin;
    private double? _observedMax;
    private double _rangeMin;
    private double _rangeMax = 1;
    private string _colorHex = "";
    private bool _autoScale = true;
    private double? _manualMin;
    private double? _manualMax;
    private double _displayGain = 1.0;
    private double _displayOffset;
    private int _decimalPlaces = -1;
    private double? _lowRangeMin;
    private double? _lowRangeMax;
    private string _lowRangeColorHex = "#2196F3";
    private double? _highRangeMin;
    private double? _highRangeMax;
    private string _highRangeColorHex = "#F44336";

    public required string Key { get; init; }
    public required string Request { get; init; }
    public required string SignalName { get; init; }
    public required string Source { get; init; }
    public int ColorIndex { get; init; }
    public Brush Accent => TrendPalette.BrushForHex(ColorHex, ColorIndex);
    public Brush AccentBrush => Accent;

    public string ColorHex
    {
        get => string.IsNullOrWhiteSpace(_colorHex) ? TrendPalette.HexFor(ColorIndex) : _colorHex;
        set
        {
            string normalized = TrendPalette.NormalizeHex(value, ColorIndex);
            if (string.Equals(ColorHex, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _colorHex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Accent));
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    public bool AutoScale
    {
        get => _autoScale;
        set
        {
            if (_autoScale == value) return;
            _autoScale = value;
            OnPropertyChanged();
            RecalculateRange();
        }
    }

    public double? ManualMin
    {
        get => _manualMin;
        set { if (_manualMin != value) { _manualMin = value; OnPropertyChanged(); RecalculateRange(); } }
    }

    public double? ManualMax
    {
        get => _manualMax;
        set { if (_manualMax != value) { _manualMax = value; OnPropertyChanged(); RecalculateRange(); } }
    }


    public double DisplayGain
    {
        get => _displayGain;
        set
        {
            double normalized = double.IsFinite(value) ? value : 1.0;
            if (Math.Abs(normalized) < 1e-12) normalized = 1.0;
            if (Math.Abs(_displayGain - normalized) < 1e-12) return;
            double oldGain = Math.Abs(_displayGain) < 1e-12 ? 1.0 : _displayGain;
            if (CurrentValue.HasValue) CurrentValue = ((CurrentValue.Value - _displayOffset) / oldGain) * normalized + _displayOffset;
            if (ObservedMin.HasValue && ObservedMax.HasValue)
            {
                double a = ((ObservedMin.Value - _displayOffset) / oldGain) * normalized + _displayOffset;
                double b = ((ObservedMax.Value - _displayOffset) / oldGain) * normalized + _displayOffset;
                ObservedMin = Math.Min(a, b); ObservedMax = Math.Max(a, b);
            }
            _displayGain = normalized;
            OnPropertyChanged();
            RecalculateRange();
        }
    }

    public double DisplayOffset
    {
        get => _displayOffset;
        set
        {
            double normalized = double.IsFinite(value) ? value : 0;
            if (Math.Abs(_displayOffset - normalized) < 1e-12) return;
            double delta = normalized - _displayOffset;
            _displayOffset = normalized;
            if (Math.Abs(delta) > 1e-12)
            {
                if (CurrentValue.HasValue) CurrentValue = CurrentValue.Value + delta;
                if (ObservedMin.HasValue) ObservedMin = ObservedMin.Value + delta;
                if (ObservedMax.HasValue) ObservedMax = ObservedMax.Value + delta;
                RecalculateRange();
            }
            OnPropertyChanged();
        }
    }

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set { int v = Math.Clamp(value, -1, 6); if (_decimalPlaces != v) { _decimalPlaces = v; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentText)); } }
    }

    public double? LowRangeMin { get => _lowRangeMin; set { if (_lowRangeMin != value) { _lowRangeMin = value; OnPropertyChanged(); } } }
    public double? LowRangeMax { get => _lowRangeMax; set { if (_lowRangeMax != value) { _lowRangeMax = value; OnPropertyChanged(); } } }
    public string LowRangeColorHex { get => _lowRangeColorHex; set { string v = TrendPalette.NormalizeHex(value); if (_lowRangeColorHex != v) { _lowRangeColorHex = v; OnPropertyChanged(); OnPropertyChanged(nameof(LowRangeBrush)); } } }
    public Brush LowRangeBrush => TrendPalette.BrushForHex(LowRangeColorHex);
    public double? HighRangeMin { get => _highRangeMin; set { if (_highRangeMin != value) { _highRangeMin = value; OnPropertyChanged(); } } }
    public double? HighRangeMax { get => _highRangeMax; set { if (_highRangeMax != value) { _highRangeMax = value; OnPropertyChanged(); } } }
    public string HighRangeColorHex { get => _highRangeColorHex; set { string v = TrendPalette.NormalizeHex(value); if (_highRangeColorHex != v) { _highRangeColorHex = v; OnPropertyChanged(); OnPropertyChanged(nameof(HighRangeBrush)); } } }
    public Brush HighRangeBrush => TrendPalette.BrushForHex(HighRangeColorHex);

    public string Unit
    {
        get => _unit;
        private set
        {
            if (_unit == value) return;
            _unit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentText));
        }
    }

    public double? CurrentValue
    {
        get => _currentValue;
        private set
        {
            if (_currentValue == value) return;
            _currentValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentText));
        }
    }

    public double? ObservedMin
    {
        get => _observedMin;
        private set { if (_observedMin != value) { _observedMin = value; OnPropertyChanged(); } }
    }

    public double? ObservedMax
    {
        get => _observedMax;
        private set { if (_observedMax != value) { _observedMax = value; OnPropertyChanged(); } }
    }

    public double RangeMin
    {
        get => _rangeMin;
        private set { if (Math.Abs(_rangeMin - value) > 1e-12) { _rangeMin = value; OnPropertyChanged(); } }
    }

    public double RangeMax
    {
        get => _rangeMax;
        private set { if (Math.Abs(_rangeMax - value) > 1e-12) { _rangeMax = value; OnPropertyChanged(); } }
    }

    public string DisplayName => $"{SignalName} [{Source}]";
    public string CurrentText => CurrentValue.HasValue
        ? FormatCurrent(CurrentValue.Value)
        : "--";

    private string FormatCurrent(double value)
    {
        if (DecimalPlaces >= 0) return value.ToString("F" + DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public void AddSample(double value, string unit)
    {
        if (!double.IsFinite(value)) return;
        if (!string.Equals(Unit, unit, StringComparison.OrdinalIgnoreCase))
            ResetForUnit(unit);

        CurrentValue = value;
        ObservedMin = !ObservedMin.HasValue ? value : Math.Min(ObservedMin.Value, value);
        ObservedMax = !ObservedMax.HasValue ? value : Math.Max(ObservedMax.Value, value);
        RecalculateRange();
    }

    public void SeedLearnedRange(double? observedMin, double? observedMax, string unit)
    {
        Unit = unit;
        if (observedMin.HasValue && observedMax.HasValue &&
            double.IsFinite(observedMin.Value) && double.IsFinite(observedMax.Value))
        {
            ObservedMin = Math.Min(observedMin.Value, observedMax.Value);
            ObservedMax = Math.Max(observedMin.Value, observedMax.Value);
            RecalculateRange();
        }
    }

    public void ResetRange()
    {
        double? current = CurrentValue;
        ObservedMin = current;
        ObservedMax = current;
        RecalculateRange();
    }

    public void ClearSamples()
    {
        CurrentValue = null;
        ObservedMin = null;
        ObservedMax = null;
        RecalculateRange();
    }

    public void ResetForUnit(string unit)
    {
        Unit = unit;
        CurrentValue = null;
        ObservedMin = null;
        ObservedMax = null;
        ManualMin = null;
        ManualMax = null;
        DisplayGain = 1.0;
        DisplayOffset = 0;
        DecimalPlaces = -1;
        AutoScale = true;
        RangeMin = 0;
        RangeMax = 1;
    }

    private void RecalculateRange()
    {
        if (!AutoScale && ManualMin.HasValue && ManualMax.HasValue &&
            double.IsFinite(ManualMin.Value) && double.IsFinite(ManualMax.Value) && ManualMax.Value > ManualMin.Value)
        {
            RangeMin = SignalScaleDefaults.StartsAtZero(Request, SignalName) ? 0 : ManualMin.Value;
            RangeMax = ManualMax.Value;
            return;
        }

        if (!ObservedMin.HasValue || !ObservedMax.HasValue)
        {
            RangeMin = 0;
            RangeMax = 1;
            return;
        }

        (double low, double high) = MakeNiceRange(ObservedMin.Value, ObservedMax.Value);
        RangeMin = SignalScaleDefaults.StartsAtZero(Request, SignalName) ? 0 : low;
        RangeMax = high;
    }

    internal static (double Min, double Max) MakeNiceRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return (0, 1);
        if (max < min) (min, max) = (max, min);

        double range = max - min;
        if (range < 1e-9)
        {
            double pad = Math.Max(Math.Abs(max) * 0.05, 1.0);
            double step0 = NiceStep((pad * 2) / 10.0);
            double low0 = Math.Floor((min - pad) / step0) * step0;
            double high0 = Math.Ceiling((max + pad) / step0) * step0;
            if (high0 <= low0) high0 = low0 + step0;
            return (low0, high0);
        }

        double step = NiceStep(range / 20.0);
        double low = Math.Floor(min / step) * step;
        double high = Math.Ceiling((max + range * 0.03) / step) * step;
        if (high <= low) high = low + step;
        return (low, high);
    }

    private static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0) return 1;
        double exponent = Math.Floor(Math.Log10(raw));
        double power = Math.Pow(10, exponent);
        double fraction = raw / power;
        double nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
        return nice * power;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
