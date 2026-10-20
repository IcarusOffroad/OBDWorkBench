using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public sealed class CrayonColorPickerWindow : Window
{
    private static readonly (string Name, string Hex)[] Crayons =
    {
        ("Red", "#FF2020"), ("Green", "#00FF40"), ("Blue", "#2080FF"), ("Yellow", "#FFFF00"),
        ("Cyan", "#00FFFF"), ("Magenta", "#FF00FF"), ("Orange", "#FF8800"), ("White", "#FFFFFF"),
        ("Lime", "#BFFF00"), ("Pink", "#FF40A0"), ("Violet", "#A060FF"), ("Aqua", "#00FFB0"),
        ("Sky", "#40C0FF"), ("Gold", "#FFD000"), ("Coral", "#FF6040"), ("Copper", "#D99A6C")
    };

    public string SelectedColorHex { get; private set; }

    public CrayonColorPickerWindow(string currentHex)
    {
        Title = "Choose color";
        SelectedColorHex = TrendPalette.NormalizeHex(currentHex);
        Width = 440;
        Height = 375;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Choose a high-visibility signal color", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
        var wrap = new WrapPanel();
        foreach ((string name, string hex) in Crayons)
        {
            var button = new Button
            {
                Width = 88,
                Height = 48,
                Margin = new Thickness(3),
                Background = TrendPalette.BrushForHex(hex),
                Content = new System.Windows.Shapes.Rectangle { Width = 66, Height = 30, Fill = TrendPalette.BrushForHex(hex) },
                BorderBrush = hex.Equals(SelectedColorHex, System.StringComparison.OrdinalIgnoreCase) ? Brushes.White : (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = hex.Equals(SelectedColorHex, System.StringComparison.OrdinalIgnoreCase) ? new Thickness(3) : new Thickness(1),
                ToolTip = name,
                Tag = hex
            };
            button.Click += (_, _) =>
            {
                SelectedColorHex = (string)button.Tag;
                DialogResult = true;
            };
            wrap.Children.Add(button);
        }
        root.Children.Add(wrap);

        var custom = new Button
        {
            Content = "Custom…",
            Padding = new Thickness(18, 5, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Choose an exact custom RGB color"
        };
        custom.Click += (_, _) =>
        {
            var picker = new CustomColorPickerWindow(SelectedColorHex) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                SelectedColorHex = picker.SelectedColorHex;
                DialogResult = true;
            }
        };
        root.Children.Add(custom);
        Content = root;
        ThemeService.Apply(this, CurrentThemeIsDark());
    }

    private static bool CurrentThemeIsDark()
    {
        if (Application.Current.Resources["AppBackgroundBrush"] is not SolidColorBrush brush) return false;
        Color c = brush.Color;
        return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128;
    }
}
