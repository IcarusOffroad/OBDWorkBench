using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public sealed class CalculatedValueWindow : Window
{
    private readonly PidLiveValue _source;
    private readonly string _sourceKey;
    private readonly TextBox _name = new();
    private readonly TextBox _units = new();
    private readonly TextBox _formula = new();
    private readonly TextBox _b = new();
    private readonly TextBox _c = new();
    private readonly TextBox _d = new();
    private readonly ComboBox _decimals = new();
    private readonly ComboBox _preset = new();
    private readonly TextBlock _aText = new();
    private readonly TextBlock _preview = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush InvalidBrush = new SolidColorBrush(Color.FromRgb(229, 57, 53));

    public CalculatedSignalSetting? Result { get; private set; }

    public CalculatedValueWindow(PidLiveValue source, string sourceKey)
    {
        _source = source;
        _sourceKey = sourceKey;
        Title = $"Create calculated value - {source.Definition.Name}";
        Width = 650;
        Height = 560;
        MinWidth = 560;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "Create a new signal from this value. A is always the live source. Formulas may use A, B, C, D, numbers, parentheses, and + - * /. The calculated signal is logged to CSV and can be added to gauges and trends like any other numeric value.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(intro);

        var nameGrid = PairRow("Name", _name, "Units", _units);
        _name.Text = source.Definition.Name + " Calculated";
        _units.Text = string.IsNullOrWhiteSpace(source.TrendUnit) ? source.Units : source.TrendUnit;
        Grid.SetRow(nameGrid, 1); root.Children.Add(nameGrid);

        _preset.Items.Add("Custom formula");
        _preset.Items.Add("Temperature °F → °C");
        _preset.Items.Add("Temperature °C → °F");
        _preset.Items.Add("Speed mph → km/h");
        _preset.Items.Add("Speed km/h → mph");
        _preset.Items.Add("Speed mph → m/s");
        _preset.Items.Add("Speed km/h → m/s");
        _preset.Items.Add("RPM → Hz");
        _preset.Items.Add("RPM → RPH");
        _preset.SelectedIndex = 0;
        _preset.SelectionChanged += (_, _) => ApplyPreset();
        var presetRow = Labeled("Conversion preset", _preset);
        Grid.SetRow(presetRow, 2); root.Children.Add(presetRow);

        _formula.Text = "A";
        _formula.Padding = new Thickness(6, 4, 6, 4);
        _formula.FontFamily = new FontFamily("Consolas");
        _formula.TextChanged += (_, _) => RefreshPreview();
        var formulaRow = Labeled("Formula", _formula);
        Grid.SetRow(formulaRow, 3); root.Children.Add(formulaRow);

        var vars = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        vars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _aText.TextWrapping = TextWrapping.Wrap;
        vars.Children.Add(VarPanel("A - Source", _aText));
        _b.Text = "0"; _c.Text = "0"; _d.Text = "0";
        _b.TextChanged += (_, _) => RefreshPreview(); _c.TextChanged += (_, _) => RefreshPreview(); _d.TextChanged += (_, _) => RefreshPreview();
        var bp = VarPanel("B", _b); Grid.SetColumn(bp, 1); vars.Children.Add(bp);
        var cp = VarPanel("C", _c); Grid.SetColumn(cp, 2); vars.Children.Add(cp);
        var dp = VarPanel("D", _d); Grid.SetColumn(dp, 3); vars.Children.Add(dp);
        Grid.SetRow(vars, 4); root.Children.Add(vars);

        for (int i = 0; i <= 6; i++) _decimals.Items.Add(i.ToString(CultureInfo.InvariantCulture));
        _decimals.SelectedIndex = 2;
        _decimals.SelectionChanged += (_, _) => RefreshPreview();
        var decRow = Labeled("Decimal places", _decimals);
        Grid.SetRow(decRow, 5); root.Children.Add(decRow);

        _preview.Margin = new Thickness(0, 16, 0, 0);
        _preview.FontSize = 15;
        _preview.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(_preview, 6); root.Children.Add(_preview);

        var note = new TextBlock
        {
            Text = "Tip: for boost from absolute pressure, use A - B and enter the engine-off atmospheric pressure as B. For a fixed zero at the current source reading, the signal calibration dialog also has Set zero.",
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(note, 7); root.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
        var create = new Button { Content = "Create value", IsDefault = true, Padding = new Thickness(16, 5, 16, 5) };
        create.Click += (_, _) => Create();
        buttons.Children.Add(cancel); buttons.Children.Add(create);
        Grid.SetRow(buttons, 9); root.Children.Add(buttons);

        Content = root;
        _timer.Tick += (_, _) => RefreshPreview();
        Loaded += (_, _) => { _timer.Start(); RefreshPreview(); };
        Closed += (_, _) => _timer.Stop();
        ThemeService.Apply(this, CurrentThemeIsDark());
    }

    private static Grid PairRow(string label1, FrameworkElement field1, string label2, FrameworkElement field2)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        var l1 = new TextBlock { Text = label1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        grid.Children.Add(l1); Grid.SetColumn(field1, 1); field1.Margin = new Thickness(0, 0, 12, 0); grid.Children.Add(field1);
        var l2 = new TextBlock { Text = label2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; Grid.SetColumn(l2, 2); grid.Children.Add(l2);
        Grid.SetColumn(field2, 3); grid.Children.Add(field2);
        return grid;
    }

    private static StackPanel Labeled(string label, FrameworkElement field)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        panel.Children.Add(new TextBlock { Text = label, Width = 125, VerticalAlignment = VerticalAlignment.Center });
        field.MinWidth = 180; field.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(field);
        return panel;
    }

    private static Border VarPanel(string title, FrameworkElement field)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(field);
        return new Border { Padding = new Thickness(7), Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = stack };
    }

    private void ApplyPreset()
    {
        switch (_preset.SelectedIndex)
        {
            case 1: _formula.Text = "(A - B) * (C / D)"; _b.Text = "32"; _c.Text = "5"; _d.Text = "9"; _units.Text = "°C"; break;
            case 2: _formula.Text = "A * (D / C) + B"; _b.Text = "32"; _c.Text = "5"; _d.Text = "9"; _units.Text = "°F"; break;
            case 3: _formula.Text = "A * B"; _b.Text = "1.609344"; _c.Text = "0"; _d.Text = "0"; _units.Text = "km/h"; break;
            case 4: _formula.Text = "A / B"; _b.Text = "1.609344"; _c.Text = "0"; _d.Text = "0"; _units.Text = "mph"; break;
            case 5: _formula.Text = "A * B"; _b.Text = "0.44704"; _c.Text = "0"; _d.Text = "0"; _units.Text = "m/s"; break;
            case 6: _formula.Text = "A / B"; _b.Text = "3.6"; _c.Text = "0"; _d.Text = "0"; _units.Text = "m/s"; break;
            case 7: _formula.Text = "A / B"; _b.Text = "60"; _c.Text = "0"; _d.Text = "0"; _units.Text = "Hz"; break;
            case 8: _formula.Text = "A * B"; _b.Text = "60"; _c.Text = "0"; _d.Text = "0"; _units.Text = "RPH"; break;
        }
        RefreshPreview();
    }

    private bool TryConstant(TextBox box, out double value)
    {
        bool ok = double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        box.BorderBrush = ok ? (Brush)Application.Current.Resources["BorderBrush"] : InvalidBrush;
        box.BorderThickness = ok ? new Thickness(1) : new Thickness(2);
        return ok;
    }

    private void RefreshPreview()
    {
        double? a = _source.LatestNumericValue;
        _aText.Text = a.HasValue ? $"{_source.Definition.Name}\n{a.Value:0.###} {_source.TrendUnit}" : $"{_source.Definition.Name}\n--";
        if (!a.HasValue) { _preview.Text = "Waiting for a numeric source sample."; return; }
        if (!TryConstant(_b, out double b) || !TryConstant(_c, out double c) || !TryConstant(_d, out double d)) { _preview.Text = "B, C, and D must be finite numbers."; return; }
        if (!CalculatedExpression.TryEvaluate(_formula.Text, a.Value, b, c, d, out double result, out string error))
        {
            _formula.BorderBrush = InvalidBrush; _formula.BorderThickness = new Thickness(2); _preview.Text = "Formula: " + error; return;
        }
        _formula.ClearValue(Control.BorderBrushProperty); _formula.ClearValue(Control.BorderThicknessProperty);
        int places = _decimals.SelectedIndex < 0 ? 2 : _decimals.SelectedIndex;
        _preview.Text = $"Preview: {result.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {_units.Text.Trim()}";
    }

    private void Create()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) { MessageBox.Show(this, "Enter a name for the calculated value.", "Calculated value", MessageBoxButton.OK, MessageBoxImage.Information); _name.Focus(); return; }
        if (!TryConstant(_b, out double b) || !TryConstant(_c, out double c) || !TryConstant(_d, out double d)) return;
        double a = _source.LatestNumericValue ?? 0;
        if (!CalculatedExpression.TryEvaluate(_formula.Text, a, b, c, d, out _, out string error)) { MessageBox.Show(this, error, "Formula", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        Result = new CalculatedSignalSetting
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            SourceKey = _sourceKey,
            SourceName = _source.Definition.Name,
            SourceUnit = string.IsNullOrWhiteSpace(_source.TrendUnit) ? _source.Units : _source.TrendUnit,
            Units = _units.Text.Trim(),
            Formula = _formula.Text.Trim(),
            B = b, C = c, D = d,
            DecimalPlaces = Math.Clamp(_decimals.SelectedIndex < 0 ? 2 : _decimals.SelectedIndex, 0, 6)
        };
        DialogResult = true;
    }

    private static bool CurrentThemeIsDark()
    {
        if (Application.Current.Resources["AppBackgroundBrush"] is not SolidColorBrush brush) return false;
        Color c = brush.Color;
        return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128;
    }
}
