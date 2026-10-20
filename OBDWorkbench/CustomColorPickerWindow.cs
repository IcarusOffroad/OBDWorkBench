using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

/// <summary>
/// Dependency-free custom signal color picker. The large field selects saturation/value,
/// the hue strip selects hue, and the RGB boxes allow exact numeric entry.
/// </summary>
public sealed class CustomColorPickerWindow : Window
{
    private readonly Grid _svField = new() { Width = 330, Height = 210, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Rectangle _hueBase = new();
    private readonly Ellipse _svMarker = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Slider _hueSlider = new() { Minimum = 0, Maximum = 360, TickFrequency = 1, IsSnapToTickEnabled = false, Width = 330 };
    private readonly TextBox _rBox = NumericBox();
    private readonly TextBox _gBox = NumericBox();
    private readonly TextBox _bBox = NumericBox();
    private readonly TextBlock _hexText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _preview = new() { Width = 64, Height = 32, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };

    private double _h;
    private double _s;
    private double _v;
    private bool _updating;
    private bool _draggingSv;

    public string SelectedColorHex { get; private set; }

    public CustomColorPickerWindow(string currentHex)
    {
        Title = "Custom color";
        Width = 410;
        Height = 430;
        MinWidth = 410;
        MinHeight = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SelectedColorHex = TrendPalette.NormalizeHex(currentHex);

        Color initial = ParseColor(SelectedColorHex);
        RgbToHsv(initial, out _h, out _s, out _v);

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "Choose a custom signal color",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        BuildSvField();
        Grid.SetRow(_svField, 1);
        root.Children.Add(_svField);

        var hueLabel = new TextBlock { Text = "Hue", Margin = new Thickness(0, 8, 0, 2) };
        Grid.SetRow(hueLabel, 2);
        root.Children.Add(hueLabel);

        var huePanel = new Grid { Width = 330, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
        huePanel.Children.Add(new Rectangle
        {
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Colors.Red, 0.00), new(Colors.Yellow, 0.167), new(Colors.Lime, 0.333),
                new(Colors.Cyan, 0.500), new(Colors.Blue, 0.667), new(Colors.Magenta, 0.833), new(Colors.Red, 1.00)
            }, new Point(0, 0.5), new Point(1, 0.5))
        });
        _hueSlider.Background = Brushes.Transparent;
        _hueSlider.VerticalAlignment = VerticalAlignment.Center;
        _hueSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _h = _hueSlider.Value;
            UpdateFromHsv(updateSlider: false);
        };
        huePanel.Children.Add(_hueSlider);
        Grid.SetRow(huePanel, 3);
        root.Children.Add(huePanel);

        var rgbPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        rgbPanel.Children.Add(LabeledRgb("R", _rBox));
        rgbPanel.Children.Add(LabeledRgb("G", _gBox));
        rgbPanel.Children.Add(LabeledRgb("B", _bBox));
        _preview.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        _preview.Margin = new Thickness(10, 0, 8, 0);
        rgbPanel.Children.Add(_preview);
        rgbPanel.Children.Add(_hexText);
        Grid.SetRow(rgbPanel, 4);
        root.Children.Add(rgbPanel);

        _rBox.TextChanged += RgbBox_TextChanged;
        _gBox.TextChanged += RgbBox_TextChanged;
        _bBox.TextChanged += RgbBox_TextChanged;
        foreach (TextBox box in new[] { _rBox, _gBox, _bBox })
        {
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
            box.PreviewTextInput += Numeric_PreviewTextInput;
            DataObject.AddPastingHandler(box, Numeric_Pasting);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 6, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(22, 5, 22, 5) };
        ok.Click += (_, _) => { SelectedColorHex = CurrentHex(); DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 6);
        root.Children.Add(buttons);

        Content = root;
        ThemeService.Apply(this, CurrentThemeIsDark());
        UpdateFromHsv(updateSlider: true);
    }

    private void BuildSvField()
    {
        _hueBase.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
        _svField.Children.Add(_hueBase);
        _svField.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection { new(Colors.White, 0), new(Color.FromArgb(0, 255, 255, 255), 1) },
                new Point(0, 0.5), new Point(1, 0.5))
        });
        _svField.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection { new(Color.FromArgb(0, 0, 0, 0), 0), new(Colors.Black, 1) },
                new Point(0.5, 0), new Point(0.5, 1))
        });
        _svField.Children.Add(_svMarker);
        _svField.MouseLeftButtonDown += (_, e) => { _draggingSv = true; _svField.CaptureMouse(); SetSvFromPoint(e.GetPosition(_svField)); };
        _svField.MouseMove += (_, e) => { if (_draggingSv && e.LeftButton == MouseButtonState.Pressed) SetSvFromPoint(e.GetPosition(_svField)); };
        _svField.MouseLeftButtonUp += (_, e) => { if (!_draggingSv) return; SetSvFromPoint(e.GetPosition(_svField)); _draggingSv = false; _svField.ReleaseMouseCapture(); };
    }

    private void SetSvFromPoint(Point p)
    {
        double w = Math.Max(1, _svField.ActualWidth > 0 ? _svField.ActualWidth : _svField.Width);
        double h = Math.Max(1, _svField.ActualHeight > 0 ? _svField.ActualHeight : _svField.Height);
        _s = Math.Clamp(p.X / w, 0, 1);
        _v = Math.Clamp(1 - p.Y / h, 0, 1);
        UpdateFromHsv(updateSlider: false);
    }

    private void UpdateFromHsv(bool updateSlider)
    {
        Color color = HsvToRgb(_h, _s, _v);
        _updating = true;
        try
        {
            _hueBase.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
            if (updateSlider) _hueSlider.Value = _h;
            _rBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
            _gBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
            _bBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
            _preview.Background = new SolidColorBrush(color);
            _hexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            SelectedColorHex = _hexText.Text;
            PositionMarker();
        }
        finally { _updating = false; }
    }

    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (!TryRgb(out Color color)) return;
        RgbToHsv(color, out _h, out _s, out _v);
        _updating = true;
        try
        {
            _hueSlider.Value = _h;
            _hueBase.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
            _preview.Background = new SolidColorBrush(color);
            _hexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            SelectedColorHex = _hexText.Text;
            PositionMarker();
        }
        finally { _updating = false; }
    }

    private bool TryRgb(out Color color)
    {
        color = default;
        if (!byte.TryParse(_rBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(_gBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(_bBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
            return false;
        color = Color.FromRgb(r, g, b);
        return true;
    }

    private void PositionMarker()
    {
        double w = _svField.ActualWidth > 0 ? _svField.ActualWidth : _svField.Width;
        double h = _svField.ActualHeight > 0 ? _svField.ActualHeight : _svField.Height;
        double left = Math.Clamp(_s * w - _svMarker.Width / 2, -_svMarker.Width / 2, w - _svMarker.Width / 2);
        double top = Math.Clamp((1 - _v) * h - _svMarker.Height / 2, -_svMarker.Height / 2, h - _svMarker.Height / 2);
        _svMarker.Margin = new Thickness(left, top, 0, 0);
        Panel.SetZIndex(_svMarker, 20);
    }

    private string CurrentHex()
    {
        if (TryRgb(out Color rgb)) return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
        Color color = HsvToRgb(_h, _s, _v);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static TextBox NumericBox() => new() { Width = 48, MaxLength = 3, Padding = new Thickness(4, 2, 4, 2) };

    private static StackPanel LabeledRgb(string label, TextBox box)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        p.Children.Add(box);
        return p;
    }

    private static void Numeric_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !int.TryParse(e.Text, out _);

    private static void Numeric_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text) ||
            e.SourceDataObject.GetData(DataFormats.Text) is not string text ||
            !int.TryParse(text, out int value) || value is < 0 or > 255)
            e.CancelCommand();
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString(TrendPalette.NormalizeHex(hex));
            return converted is Color c ? c : Colors.DeepSkyBlue;
        }
        catch { return Colors.DeepSkyBlue; }
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max;
        v = max;
    }

    private static bool CurrentThemeIsDark()
    {
        if (Application.Current.Resources["AppBackgroundBrush"] is not SolidColorBrush brush) return false;
        Color c = brush.Color;
        return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128;
    }
}
