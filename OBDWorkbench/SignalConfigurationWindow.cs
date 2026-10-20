using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public sealed class SignalConfigurationWindow : Window
{
    private sealed class EditorRow
    {
        public required IConfigurableSignal Signal { get; init; }
        public required Button ColorButton { get; init; }
        public required CheckBox AutoScaleCheck { get; init; }
        public required TextBox MinBox { get; init; }
        public required TextBox MaxBox { get; init; }
        public required TextBox GainBox { get; init; }
        public required TextBox OffsetBox { get; init; }
        public required ComboBox DecimalBox { get; init; }
        public CheckBox? ShowPointsCheck { get; init; }
        public TextBox? LowMinBox { get; init; }
        public TextBox? LowMaxBox { get; init; }
        public Button? LowColorButton { get; init; }
        public TextBox? HighMinBox { get; init; }
        public TextBox? HighMaxBox { get; init; }
        public Button? HighColorButton { get; init; }
    }

    private readonly Func<IEnumerable<IConfigurableSignal>> _signalProvider;
    private readonly Action<IConfigurableSignal> _removeSignal;
    private readonly Action _changed;
    private readonly Func<bool>? _applyOptions;
    private readonly StackPanel _rowsPanel = new();
    private readonly List<EditorRow> _rows = new();
    private static readonly Brush InvalidBrush = new SolidColorBrush(Color.FromRgb(229, 57, 53));

    public SignalConfigurationWindow(string title, Func<IEnumerable<IConfigurableSignal>> signalProvider,
        Action<IConfigurableSignal> removeSignal, Action changed, FrameworkElement? options = null,
        Func<bool>? applyOptions = null)
    {
        Title = title;
        Width = options is null ? 1100 : 1220;
        Height = 590;
        MinWidth = 760;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _signalProvider = signalProvider;
        _removeSignal = removeSignal;
        _changed = changed;
        _applyOptions = applyOptions;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var note = new TextBlock
        {
            Text = "Displayed value = decoded value × Gain + Offset. Autoscale is on by default. Typing Min or Max automatically disables Autoscale. Decimal places controls the displayed numeric precision. Gauge rows also support low/high colored sweep bands; trend rows can show a dot at every recorded sample.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        };
        var top = new StackPanel(); top.Children.Add(note);
        if (options is not null) top.Children.Add(options);
        root.Children.Add(top);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = _rowsPanel; Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 6, 0) };
        var apply = new Button { Content = "Apply", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 6, 0) };
        var close = new Button { Content = "Apply && Close", Padding = new Thickness(18, 5, 18, 5) };
        apply.Click += (_, _) => { if (TryApplyAll()) _changed(); };
        close.Click += (_, _) => { if (TryApplyAll()) { _changed(); Close(); } };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); buttons.Children.Add(close);
        Grid.SetRow(buttons, 2); root.Children.Add(buttons);

        Content = root;
        BuildRows();
        ThemeService.Apply(this, CurrentThemeIsDark());
    }

    public void RefreshSignals() => BuildRows();

    private static void SetCurrentAsZero(IConfigurableSignal signal, TextBox gainBox, TextBox offsetBox)
    {
        if (!double.TryParse(gainBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double newGain) || !double.IsFinite(newGain) || Math.Abs(newGain) < 1e-12)
            return;

        double? displayed = signal switch
        {
            DashboardGaugeState gauge => gauge.CurrentValue,
            TrendSeriesState trend when trend.Samples.Count > 0 => trend.Samples[^1].Value,
            _ => null
        };
        if (!displayed.HasValue) return;

        double oldGain = double.IsFinite(signal.DisplayGain) && Math.Abs(signal.DisplayGain) > 1e-12 ? signal.DisplayGain : 1.0;
        double decoded = (displayed.Value - signal.DisplayOffset) / oldGain;
        offsetBox.Text = (-(decoded * newGain)).ToString("0.######", CultureInfo.InvariantCulture);
        offsetBox.Focus();
        offsetBox.SelectAll();
    }

    private void BuildRows()
    {
        _rows.Clear(); _rowsPanel.Children.Clear();
        foreach (IConfigurableSignal signal in _signalProvider().ToList())
        {
            var border = new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 7),
                Background = (Brush)Application.Current.Resources["PanelBackgroundBrush"]
            };
            var stack = new StackPanel();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 185 });
            for (int i = 0; i < 9; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labelStack.Children.Add(new TextBlock { Text = signal.SignalName, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            labelStack.Children.Add(new TextBlock { Text = $"Source {signal.Source}" + (string.IsNullOrWhiteSpace(signal.Unit) ? "" : $"  •  {signal.Unit}"), FontSize = 10.5, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            grid.Children.Add(labelStack);

            var colorButton = ColorButton(signal.ColorHex, "Signal color"); Grid.SetColumn(colorButton, 1); grid.Children.Add(colorButton);
            var auto = new CheckBox { Content = "Autoscale", IsChecked = signal.AutoScale, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 9, 0) }; Grid.SetColumn(auto, 2); grid.Children.Add(auto);
            var minBox = NumberBox(signal.ManualMin, 68); var maxBox = NumberBox(signal.ManualMax, 68);
            var minPanel = LabeledBox("Min", minBox); var maxPanel = LabeledBox("Max", maxBox); Grid.SetColumn(minPanel, 3); Grid.SetColumn(maxPanel, 4); grid.Children.Add(minPanel); grid.Children.Add(maxPanel);
            var gainBox = NumberBox(signal.DisplayGain, 72, "0.######"); var gainPanel = LabeledBox("Gain", gainBox); Grid.SetColumn(gainPanel, 5); grid.Children.Add(gainPanel);
            var offsetBox = NumberBox(signal.DisplayOffset, 78, "0.######"); var offsetPanel = LabeledBox("Offset", offsetBox); Grid.SetColumn(offsetPanel, 6); grid.Children.Add(offsetPanel);
            var setZero = new Button { Content = "Set zero", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Calculate the offset that makes the current reading zero." };
            Grid.SetColumn(setZero, 7); grid.Children.Add(setZero);
            var decimals = new ComboBox { Width = 74, Margin = new Thickness(0, 0, 8, 0) }; decimals.Items.Add("Auto"); for (int i = 0; i <= 6; i++) decimals.Items.Add(i.ToString(CultureInfo.InvariantCulture)); decimals.SelectedIndex = signal.DecimalPlaces < 0 ? 0 : signal.DecimalPlaces + 1;
            var decPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; decPanel.Children.Add(new TextBlock { Text = "Decimals", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); decPanel.Children.Add(decimals); Grid.SetColumn(decPanel, 8); grid.Children.Add(decPanel);
            var remove = new Button { Content = "Remove", Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(remove, 9); grid.Children.Add(remove);
            setZero.Click += (_, _) => SetCurrentAsZero(signal, gainBox, offsetBox);

            minBox.TextChanged += (_, _) => ManualRangeEdited(auto, minBox, maxBox); maxBox.TextChanged += (_, _) => ManualRangeEdited(auto, minBox, maxBox);
            auto.Checked += (_, _) => ClearValidation(minBox, maxBox); auto.Unchecked += (_, _) => UpdateValidation(minBox, maxBox, true);
            gainBox.TextChanged += (_, _) => UpdateGainValidation(gainBox); offsetBox.TextChanged += (_, _) => UpdateFiniteValidation(offsetBox, allowBlank: true);

            stack.Children.Add(grid);

            CheckBox? showPoints = null;
            TextBox? lowMin = null, lowMax = null, highMin = null, highMax = null;
            Button? lowColor = null, highColor = null;
            if (signal is TrendSeriesState trend)
            {
                showPoints = new CheckBox { Content = "Show recorded data points as dots", IsChecked = trend.ShowDataPoints, Margin = new Thickness(2, 8, 0, 0) };
                stack.Children.Add(showPoints);
            }
            if (signal is DashboardGaugeState gauge)
            {
                var bands = new WrapPanel { Margin = new Thickness(2, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                bands.Children.Add(new TextBlock { Text = "Gauge sweep bands:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                lowMin = NumberBox(gauge.LowRangeMin, 62); lowMax = NumberBox(gauge.LowRangeMax, 62); lowColor = ColorButton(gauge.LowRangeColorHex, "Low-range color");
                bands.Children.Add(new TextBlock { Text = "Low", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); bands.Children.Add(lowMin); bands.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) }); bands.Children.Add(lowMax); bands.Children.Add(lowColor);
                highMin = NumberBox(gauge.HighRangeMin, 62); highMax = NumberBox(gauge.HighRangeMax, 62); highColor = ColorButton(gauge.HighRangeColorHex, "High-range color");
                bands.Children.Add(new TextBlock { Text = "High", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 4, 0) }); bands.Children.Add(highMin); bands.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) }); bands.Children.Add(highMax); bands.Children.Add(highColor);
                bands.Children.Add(new TextBlock { Text = "Leave a band's From/To blank to disable it.", Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
                lowMin.TextChanged += (_, _) => UpdateBandValidation(lowMin, lowMax); lowMax.TextChanged += (_, _) => UpdateBandValidation(lowMin, lowMax); highMin.TextChanged += (_, _) => UpdateBandValidation(highMin, highMax); highMax.TextChanged += (_, _) => UpdateBandValidation(highMin, highMax);
                stack.Children.Add(bands);
            }

            var row = new EditorRow { Signal = signal, ColorButton = colorButton, AutoScaleCheck = auto, MinBox = minBox, MaxBox = maxBox, GainBox = gainBox, OffsetBox = offsetBox, DecimalBox = decimals, ShowPointsCheck = showPoints, LowMinBox = lowMin, LowMaxBox = lowMax, LowColorButton = lowColor, HighMinBox = highMin, HighMaxBox = highMax, HighColorButton = highColor };
            remove.Click += (_, _) => { if (!TryApply()) return; _removeSignal(signal); _changed(); BuildRows(); };
            border.Child = stack; _rowsPanel.Children.Add(border); _rows.Add(row);
        }
        if (_rows.Count == 0) _rowsPanel.Children.Add(new TextBlock { Text = "No signals are currently configured.", Margin = new Thickness(4, 12, 4, 12) });
    }

    private static TextBox NumberBox(double? value, double width, string format = "0.###")
    {
        var box = new TextBox { Width = width, Text = value?.ToString(format, CultureInfo.InvariantCulture) ?? "", Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 8, 0) };
        box.GotKeyboardFocus += (_, _) => box.SelectAll(); return box;
    }
    private static TextBox NumberBox(double value, double width, string format = "0.###") => NumberBox((double?)value, width, format);
    private Button ColorButton(string hex, string tooltip)
    {
        var button = new Button { Width = 32, Height = 28, Margin = new Thickness(6, 0, 10, 0), Background = TrendPalette.BrushForHex(hex), Tag = hex, ToolTip = tooltip };
        button.Click += ColorButton_Click; return button;
    }
    private static StackPanel LabeledBox(string label, TextBox box) { var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; p.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); p.Children.Add(box); return p; }

    private void ManualRangeEdited(CheckBox auto, TextBox minBox, TextBox maxBox) { if (auto.IsChecked == true && (!string.IsNullOrWhiteSpace(minBox.Text) || !string.IsNullOrWhiteSpace(maxBox.Text))) auto.IsChecked = false; UpdateValidation(minBox, maxBox, auto.IsChecked != true); }
    private static void UpdateValidation(TextBox minBox, TextBox maxBox, bool active)
    {
        if (!active) { ClearValidation(minBox, maxBox); return; }
        string a = minBox.Text.Trim(), b = maxBox.Text.Trim(); bool ae = a.Length == 0, be = b.Length == 0;
        double av = 0, bv = 0;
        bool aok = !ae && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out av) && double.IsFinite(av);
        bool bok = !be && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out bv) && double.IsFinite(bv);
        bool badA = (!ae && !aok) || (!be && ae), badB = (!be && !bok) || (!ae && be); if (aok && bok && bv <= av) badA = badB = true;
        SetValidation(minBox, badA); SetValidation(maxBox, badB);
    }
    private static void UpdateGainValidation(TextBox box) { bool ok = double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && Math.Abs(v) > 1e-12; SetValidation(box, !ok); }
    private static void UpdateFiniteValidation(TextBox box, bool allowBlank) { string t = box.Text.Trim(); bool ok = allowBlank && t.Length == 0 || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v); SetValidation(box, !ok); }
    private static void UpdateBandValidation(TextBox a, TextBox b) { string at = a.Text.Trim(), bt = b.Text.Trim(); if (at.Length == 0 && bt.Length == 0) { ClearValidation(a, b); return; } double av = 0, bv = 0; bool aok = double.TryParse(at, NumberStyles.Float, CultureInfo.InvariantCulture, out av) && double.IsFinite(av); bool bok = double.TryParse(bt, NumberStyles.Float, CultureInfo.InvariantCulture, out bv) && double.IsFinite(bv); SetValidation(a, !aok || !bok || (aok && bok && bv <= av)); SetValidation(b, !aok || !bok || (aok && bok && bv <= av)); }
    private static void SetValidation(TextBox box, bool invalid) { if (invalid) { box.BorderBrush = InvalidBrush; box.BorderThickness = new Thickness(2); } else { box.ClearValue(Control.BorderBrushProperty); box.ClearValue(Control.BorderThicknessProperty); } }
    private static void ClearValidation(TextBox a, TextBox b) { SetValidation(a, false); SetValidation(b, false); }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return; string current = button.Tag as string ?? "#2196F3";
        var picker = new CrayonColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() == true) { button.Tag = picker.SelectedColorHex; button.Background = TrendPalette.BrushForHex(picker.SelectedColorHex); }
    }

    private bool TryApplyAll() { if (!TryApply()) return false; return _applyOptions?.Invoke() ?? true; }
    private bool TryApply()
    {
        foreach (EditorRow row in _rows)
        {
            bool auto = row.AutoScaleCheck.IsChecked == true; double? min = null, max = null;
            if (!auto)
            {
                UpdateValidation(row.MinBox, row.MaxBox, true);
                if (!double.TryParse(row.MinBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) || !double.TryParse(row.MaxBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double b) || !double.IsFinite(a) || !double.IsFinite(b) || b <= a)
                { MessageBox.Show(this, $"Enter a valid Min and Max for {row.Signal.SignalName}; Max must be greater than Min.", "Signal scale", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
                min = a; max = b;
            }
            if (!double.TryParse(row.GainBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double gain) || !double.IsFinite(gain) || Math.Abs(gain) < 1e-12)
            { SetValidation(row.GainBox, true); MessageBox.Show(this, $"Enter a finite non-zero Gain for {row.Signal.SignalName}.", "Signal gain", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
            string ot = row.OffsetBox.Text.Trim(); double offset = 0; if (ot.Length > 0 && (!double.TryParse(ot, NumberStyles.Float, CultureInfo.InvariantCulture, out offset) || !double.IsFinite(offset)))
            { SetValidation(row.OffsetBox, true); MessageBox.Show(this, $"Enter a valid numeric Offset for {row.Signal.SignalName}.", "Signal offset", MessageBoxButton.OK, MessageBoxImage.Information); return false; }

            row.Signal.ColorHex = row.ColorButton.Tag as string ?? row.Signal.ColorHex; row.Signal.ManualMin = min; row.Signal.ManualMax = max; row.Signal.AutoScale = auto; row.Signal.DisplayGain = gain; row.Signal.DisplayOffset = offset; row.Signal.DecimalPlaces = row.DecimalBox.SelectedIndex <= 0 ? -1 : Math.Clamp(row.DecimalBox.SelectedIndex - 1, 0, 6);
            if (row.Signal is TrendSeriesState trend && row.ShowPointsCheck is not null) trend.ShowDataPoints = row.ShowPointsCheck.IsChecked == true;
            if (row.Signal is DashboardGaugeState gauge)
            {
                if (!TryBand(row.LowMinBox!, row.LowMaxBox!, out double? lowA, out double? lowB) || !TryBand(row.HighMinBox!, row.HighMaxBox!, out double? highA, out double? highB))
                { MessageBox.Show(this, $"Gauge range bands for {row.Signal.SignalName} must have both From/To values and To must be greater than From.", "Gauge range", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
                gauge.LowRangeMin = lowA; gauge.LowRangeMax = lowB; gauge.LowRangeColorHex = row.LowColorButton?.Tag as string ?? gauge.LowRangeColorHex;
                gauge.HighRangeMin = highA; gauge.HighRangeMax = highB; gauge.HighRangeColorHex = row.HighColorButton?.Tag as string ?? gauge.HighRangeColorHex;
            }
        }
        return true;
    }
    private static bool TryBand(TextBox a, TextBox b, out double? min, out double? max)
    {
        min = max = null; string at = a.Text.Trim(), bt = b.Text.Trim(); if (at.Length == 0 && bt.Length == 0) { ClearValidation(a, b); return true; }
        double av = 0, bv = 0;
        bool ok = double.TryParse(at, NumberStyles.Float, CultureInfo.InvariantCulture, out av) && double.TryParse(bt, NumberStyles.Float, CultureInfo.InvariantCulture, out bv) && double.IsFinite(av) && double.IsFinite(bv) && bv > av;
        SetValidation(a, !ok); SetValidation(b, !ok); if (!ok) return false; min = av; max = bv; return true;
    }

    private static bool CurrentThemeIsDark() { if (Application.Current.Resources["AppBackgroundBrush"] is not SolidColorBrush brush) return false; Color c = brush.Color; return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128; }
}
