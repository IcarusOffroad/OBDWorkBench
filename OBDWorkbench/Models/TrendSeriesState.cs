using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OBDWorkbench.Models;

public sealed record TrendSample(DateTime TimestampUtc, double Value);

public interface IConfigurableSignal
{
    string Key { get; }
    string SignalName { get; }
    string Source { get; }
    string DisplayName { get; }
    string Unit { get; }
    string ColorHex { get; set; }
    Brush AccentBrush { get; }
    bool AutoScale { get; set; }
    double? ManualMin { get; set; }
    double? ManualMax { get; set; }
    double DisplayGain { get; set; }
    double DisplayOffset { get; set; }
    int DecimalPlaces { get; set; }
}

public sealed class TrendSeriesState : INotifyPropertyChanged, IConfigurableSignal
{
    private string _unit = "";
    private double? _latestValue;
    private DateTime? _latestUtc;
    private string _colorHex = "";
    private bool _autoScale = true;
    private double? _manualMin;
    private double? _manualMax;
    private double _displayGain = 1.0;
    private double _displayOffset;
    private int _decimalPlaces = -1;
    private bool _showDataPoints;

    public required string Key { get; init; }
    public required string Request { get; init; }
    public required string SignalName { get; init; }
    public required string Source { get; init; }
    public int ColorIndex { get; init; }
    public Brush Stroke => TrendPalette.BrushForHex(ColorHex, ColorIndex);
    public Brush AccentBrush => Stroke;
    public List<TrendSample> Samples { get; } = new();

    public string ColorHex
    {
        get => string.IsNullOrWhiteSpace(_colorHex) ? TrendPalette.HexFor(ColorIndex) : _colorHex;
        set
        {
            string normalized = TrendPalette.NormalizeHex(value, ColorIndex);
            if (string.Equals(ColorHex, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _colorHex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Stroke));
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    public bool AutoScale
    {
        get => _autoScale;
        set { if (_autoScale != value) { _autoScale = value; OnPropertyChanged(); } }
    }

    public double? ManualMin
    {
        get => _manualMin;
        set { if (_manualMin != value) { _manualMin = value; OnPropertyChanged(); } }
    }

    public double? ManualMax
    {
        get => _manualMax;
        set { if (_manualMax != value) { _manualMax = value; OnPropertyChanged(); } }
    }


    public double DisplayGain
    {
        get => _displayGain;
        set
        {
            double normalized = double.IsFinite(value) ? value : 1.0;
            if (Math.Abs(normalized) < 1e-12) normalized = 1.0;
            if (Math.Abs(_displayGain - normalized) < 1e-12) return;
            // Samples are already in displayed engineering units. Gain changes require
            // a rebuild from live/raw data, so MainWindow handles the history transform.
            _displayGain = normalized;
            OnPropertyChanged();
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
                for (int i = 0; i < Samples.Count; i++)
                    Samples[i] = Samples[i] with { Value = Samples[i].Value + delta };
                if (LatestValue.HasValue) LatestValue = LatestValue.Value + delta;
            }
            OnPropertyChanged();
        }
    }

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set { int v = Math.Clamp(value, -1, 6); if (_decimalPlaces != v) { _decimalPlaces = v; OnPropertyChanged(); OnPropertyChanged(nameof(LatestText)); } }
    }

    public bool ShowDataPoints
    {
        get => _showDataPoints;
        set { if (_showDataPoints != value) { _showDataPoints = value; OnPropertyChanged(); } }
    }

    public void TransformCalibration(double oldGain, double oldOffset, double newGain, double newOffset)
    {
        oldGain = Math.Abs(oldGain) < 1e-12 ? 1.0 : oldGain;
        for (int i = 0; i < Samples.Count; i++)
        {
            double baseValue = (Samples[i].Value - oldOffset) / oldGain;
            Samples[i] = Samples[i] with { Value = baseValue * newGain + newOffset };
        }
        if (LatestValue.HasValue)
        {
            double baseValue = (LatestValue.Value - oldOffset) / oldGain;
            LatestValue = baseValue * newGain + newOffset;
        }
        _displayGain = newGain;
        _displayOffset = newOffset;
        OnPropertyChanged(nameof(DisplayGain));
        OnPropertyChanged(nameof(DisplayOffset));
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (_unit == value) return;
            _unit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LegendLabel));
            OnPropertyChanged(nameof(LatestText));
        }
    }

    public double? LatestValue
    {
        get => _latestValue;
        private set
        {
            if (_latestValue == value) return;
            _latestValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LatestText));
        }
    }

    public DateTime? LatestUtc
    {
        get => _latestUtc;
        private set
        {
            if (_latestUtc == value) return;
            _latestUtc = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName => $"{SignalName} [{Source}]";
    public string LegendLabel => SignalName;
    public string LatestText => LatestValue.HasValue
        ? FormatWithDecimals(LatestValue.Value)
        : "--";

    private string FormatWithDecimals(double value)
    {
        if (DecimalPlaces >= 0) return value.ToString("F" + DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public void AddSample(DateTime timestampUtc, double value)
    {
        Samples.Add(new TrendSample(timestampUtc, value));
        LatestValue = value;
        LatestUtc = timestampUtc;

        DateTime oldest = timestampUtc.AddHours(-1);
        int remove = 0;
        while (remove < Samples.Count && Samples[remove].TimestampUtc < oldest)
            remove++;
        if (remove > 0)
            Samples.RemoveRange(0, remove);
        if (Samples.Count > 50000)
            Samples.RemoveRange(0, Samples.Count - 50000);
    }

    public void ClearHistory()
    {
        Samples.Clear();
        LatestValue = null;
        LatestUtc = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public static class TrendPalette
{
    private static readonly Color[] Colors =
    {
        Color.FromRgb(0xFF, 0x88, 0x00),
        Color.FromRgb(0x00, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0x40),
        Color.FromRgb(0xFF, 0x40, 0xA0),
        Color.FromRgb(0x20, 0x80, 0xFF),
        Color.FromRgb(0xA0, 0x60, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xBF, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0xB0),
        Color.FromRgb(0xFF, 0x20, 0x20),
        Color.FromRgb(0x40, 0xC0, 0xFF),
        Color.FromRgb(0xFF, 0xD0, 0x00),
        Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0xFF, 0x60, 0x40),
        Color.FromRgb(0xD9, 0x9A, 0x6C)
    };

    private static readonly Brush[] Brushes = Colors.Select(c =>
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return (Brush)brush;
    }).ToArray();

    public static int Count => Brushes.Length;
    public static Brush BrushFor(int index) => Brushes[Math.Abs(index) % Brushes.Length];
    public static string HexFor(int index)
    {
        Color c = Colors[Math.Abs(index) % Colors.Length];
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static string NormalizeHex(string? value, int fallbackIndex = 0)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                Color c = (Color)ColorConverter.ConvertFromString(value)!;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch { }
        }
        return HexFor(fallbackIndex);
    }

    public static Brush BrushForHex(string? value, int fallbackIndex = 0)
    {
        string hex = NormalizeHex(value, fallbackIndex);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
