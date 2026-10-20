using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

/// <summary>Per-signal engineering calibration editor. Displayed = decoded * gain + offset.</summary>
public sealed class SignalOffsetWindow : Window
{
    private readonly PidLiveValue _signal;
    private readonly TextBox _gainBox;
    private readonly TextBox _offsetBox;
    private readonly ComboBox _decimalBox;
    private readonly TextBlock _previewText;
    private readonly string _unit;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush InvalidBrush = new SolidColorBrush(Color.FromRgb(229, 57, 53));

    public double SelectedGain { get; private set; }
    public double SelectedOffset { get; private set; }
    public int SelectedDecimalPlaces { get; private set; }

    public SignalOffsetWindow(PidLiveValue signal, double currentGain, double currentOffset, int decimalPlaces)
    {
        _signal = signal;
        Title = $"Signal calibration - {signal.Definition.Name}";
        Width = 520;
        Height = 390;
        MinWidth = 470;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _unit = string.IsNullOrWhiteSpace(signal.TrendUnit) ? signal.Units : signal.TrendUnit;
        SelectedGain = double.IsFinite(currentGain) && Math.Abs(currentGain) > 1e-12 ? currentGain : 1.0;
        SelectedOffset = double.IsFinite(currentOffset) ? currentOffset : 0;
        SelectedDecimalPlaces = Math.Clamp(decimalPlaces, -1, 6);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = signal.Definition.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = $"Source {signal.Source}" + (string.IsNullOrWhiteSpace(_unit) ? "" : $"  •  {_unit}"),
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 2, 0, 0)
        });
        root.Children.Add(header);

        var explanation = new TextBlock
        {
            Text = "Displayed value = decoded value × gain + offset. Gain is a 1× multiplier. Example: if indicated speed is 100 and actual speed is 106, use gain 1.06. Offset is entered in the displayed engineering units.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(explanation, 1); root.Children.Add(explanation);

        var calibration = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        calibration.Children.Add(new TextBlock { Text = "Gain", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _gainBox = new TextBox { Width = 100, Text = SelectedGain.ToString("0.######", CultureInfo.InvariantCulture), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 14, 0) };
        calibration.Children.Add(_gainBox);
        calibration.Children.Add(new TextBlock { Text = "Offset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _offsetBox = new TextBox { Width = 110, Text = SelectedOffset.ToString("0.######", CultureInfo.InvariantCulture), Padding = new Thickness(6, 3, 6, 3) };
        calibration.Children.Add(_offsetBox);
        if (!string.IsNullOrWhiteSpace(_unit)) calibration.Children.Add(new TextBlock { Text = _unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        var zero = new Button { Content = "Set zero", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(14, 0, 0, 0), ToolTip = "Set the offset so the current decoded reading becomes zero." };
        zero.Click += (_, _) => SetCurrentAsZero();
        calibration.Children.Add(zero);
        var reset = new Button { Content = "Reset 1× / 0", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        reset.Click += (_, _) => { _gainBox.Text = "1"; _offsetBox.Text = "0"; };
        calibration.Children.Add(reset);
        Grid.SetRow(calibration, 2); root.Children.Add(calibration);

        var decimalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        decimalRow.Children.Add(new TextBlock { Text = "Decimal places", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _decimalBox = new ComboBox { Width = 120 };
        _decimalBox.Items.Add("Auto");
        for (int i = 0; i <= 6; i++) _decimalBox.Items.Add(i.ToString(CultureInfo.InvariantCulture));
        _decimalBox.SelectedIndex = SelectedDecimalPlaces < 0 ? 0 : SelectedDecimalPlaces + 1;
        _decimalBox.SelectionChanged += (_, _) => RefreshPreview();
        decimalRow.Children.Add(_decimalBox);
        Grid.SetRow(decimalRow, 3); root.Children.Add(decimalRow);

        _previewText = new TextBlock { Margin = new Thickness(0, 16, 0, 0), FontSize = 14, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(_previewText, 4); root.Children.Add(_previewText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
        var apply = new Button { Content = "Set offset / gain", IsDefault = true, Padding = new Thickness(16, 5, 16, 5) };
        apply.Click += (_, _) => Apply();
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        Grid.SetRow(buttons, 5); root.Children.Add(buttons);

        _gainBox.GotKeyboardFocus += (_, _) => _gainBox.SelectAll();
        _offsetBox.GotKeyboardFocus += (_, _) => _offsetBox.SelectAll();
        _gainBox.TextChanged += (_, _) => RefreshPreview();
        _offsetBox.TextChanged += (_, _) => RefreshPreview();

        Content = root;
        _timer.Tick += (_, _) => RefreshPreview();
        Loaded += (_, _) => { _timer.Start(); RefreshPreview(); };
        Closed += (_, _) => _timer.Stop();
        ThemeService.Apply(this, CurrentThemeIsDark());
    }

    private void SetCurrentAsZero()
    {
        if (!TryReadGain(out double gain)) { RefreshPreview(); return; }
        double? current = _signal.UnadjustedNumericValue;
        if (!current.HasValue)
        {
            MessageBox.Show(this, "A current decoded numeric reading is not available yet.", "Set zero", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        double offset = -(current.Value * gain);
        _offsetBox.Text = offset.ToString("0.######", CultureInfo.InvariantCulture);
        _offsetBox.Focus(); _offsetBox.SelectAll();
    }

    private void RefreshPreview()
    {
        bool gainOk = TryReadGain(out double gain);
        bool offsetOk = TryReadOffset(out double offset);
        SetValidation(_gainBox, !gainOk); SetValidation(_offsetBox, !offsetOk);
        if (!gainOk || !offsetOk)
        {
            _previewText.Text = "Enter a finite non-zero gain and a finite numeric offset.";
            return;
        }
        double? baseValue = _signal.UnadjustedNumericValue;
        if (baseValue.HasValue)
        {
            double adjusted = baseValue.Value * gain + offset;
            _previewText.Text = $"Current decoded value: {Format(baseValue.Value, -1)}{UnitSuffix()}   →   Displayed: {Format(adjusted, SelectedDecimals())}{UnitSuffix()}";
        }
        else _previewText.Text = "Calibration will be applied to the next numeric sample.";
    }

    private void Apply()
    {
        if (!TryReadGain(out double gain) || !TryReadOffset(out double offset)) { RefreshPreview(); return; }
        SelectedGain = gain;
        SelectedOffset = offset;
        SelectedDecimalPlaces = SelectedDecimals();
        DialogResult = true;
    }

    private int SelectedDecimals() => _decimalBox.SelectedIndex <= 0 ? -1 : Math.Clamp(_decimalBox.SelectedIndex - 1, 0, 6);
    private bool TryReadGain(out double value) => double.TryParse(_gainBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && Math.Abs(value) > 1e-12;
    private bool TryReadOffset(out double value) { string text = _offsetBox.Text.Trim(); if (text.Length == 0) { value = 0; return true; } return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value); }
    private static void SetValidation(TextBox box, bool invalid) { if (invalid) { box.BorderBrush = InvalidBrush; box.BorderThickness = new Thickness(2); } else { box.ClearValue(Control.BorderBrushProperty); box.ClearValue(Control.BorderThicknessProperty); } }
    private string UnitSuffix() => string.IsNullOrWhiteSpace(_unit) ? "" : $" {_unit}";
    private static string Format(double value, int decimals) => decimals >= 0 ? value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool CurrentThemeIsDark() { if (Application.Current.Resources["AppBackgroundBrush"] is not SolidColorBrush brush) return false; Color c = brush.Color; return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128; }
}
