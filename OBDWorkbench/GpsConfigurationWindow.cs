using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO.Ports;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public sealed class GpsConfigurationWindow : Window
{
    public GpsConfigurationWindow(AppSettingsData settings, Action changed, Func<string> debug)
    {
        Title = "GPS connection and NMEA debug"; Width = 820; Height = 620; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeService.Apply(this, settings);
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var enabled = new CheckBox { Content = "Enable GPS in Workbench (connect automatically when Workbench starts)", IsChecked = settings.GpsEnabled, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(enabled);
        var row = new WrapPanel(); top.Children.Add(row);
        ComboBox Choice(string label, object[] choices, object selection, bool editable = false)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
            group.Children.Add(new TextBlock { Text = label });
            var box = new ComboBox { ItemsSource = choices, SelectedItem = selection, Text = selection.ToString(), IsEditable = editable, MinWidth = 95 };
            group.Children.Add(box); row.Children.Add(group); return box;
        }
        var source = Choice("GPS source", new object[] { "Direct COM port", "Icarus GPS Service" }, settings.GpsSource == "Service" ? "Icarus GPS Service" : "Direct COM port");
        var port = Choice("COM port", SerialPort.GetPortNames().OrderBy(x => x).Cast<object>().ToArray(), settings.GpsPort, true);
        var baud = Choice("Baud", new object[] { 4800, 9600, 19200, 38400, 57600, 115200 }, settings.GpsBaud);
        var bits = Choice("Data bits", new object[] { 7, 8 }, settings.GpsDataBits);
        var parity = Choice("Parity", new object[] { "None", "Even", "Odd" }, settings.GpsParity);
        var stop = Choice("Stop bits", new object[] { "One", "Two" }, settings.GpsStopBits);
        var refresh = new Button { Content = "Refresh ports", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 15, 0, 8) };
        row.Children.Add(refresh);
        refresh.Click += (_, _) => { string old = port.Text; port.ItemsSource = SerialPort.GetPortNames().OrderBy(x => x).ToArray(); port.Text = old; };
        var note = new TextBlock { Text = "Changes save automatically. Use a different COM port from the diagnostic adapter. Default: 9600, 8-N-1, no flow control. Reconnect retries every 3 seconds.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) }; top.Children.Add(note);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.OrangeRed }; top.Children.Add(error);
        var raw = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(raw);
        void UpdateSourceControls()
        {
            bool service = source.SelectedIndex == 1;
            foreach (var control in new Control[] { port, baud, bits, parity, stop, refresh }) control.IsEnabled = !service;
            note.Text = service
                ? "Select Enable GPS above to connect. Changes save automatically. Workbench subscribes to the installed Icarus GPS Service; no virtual COM port is needed. Set the physical receiver port in the GPS tray monitor. Disabling GPS here only disconnects Workbench; it leaves the service running. See GPSService/README_WORKBENCH.md in the source package for installation."
                : "Changes save automatically. Use a different COM port from the diagnostic adapter. Default: 9600, 8-N-1. If Icarus GPS Service owns this port, release it through its tray monitor before using Direct COM. Workbench never stops or reconfigures the service automatically.";
        }
        UpdateSourceControls();
        bool Save()
        {
            UpdateSourceControls();
            string p = port.Text.Trim().ToUpperInvariant();
            if (enabled.IsChecked == true && source.SelectedIndex != 1 && (!p.StartsWith("COM") || !int.TryParse(p[3..], out int number) || number < 1))
            { error.Text = "Choose a COM port before enabling GPS."; return false; }
            error.Text = "";
            int b = baud.SelectedItem is int speed ? speed : 9600;
            int db = bits.SelectedItem is int count ? count : 8;
            string pa = parity.SelectedItem?.ToString() ?? "None", st = stop.SelectedItem?.ToString() ?? "One";
            bool on = enabled.IsChecked == true;
            string selectedSource = source.SelectedIndex == 1 ? "Service" : "COM";
            if (settings.GpsSource == selectedSource && settings.GpsEnabled == on && settings.GpsPort == p && settings.GpsBaud == b && settings.GpsDataBits == db && settings.GpsParity == pa && settings.GpsStopBits == st) return true;
            settings.GpsSource = selectedSource; settings.GpsEnabled = on; settings.GpsPort = p; settings.GpsBaud = b; settings.GpsDataBits = db; settings.GpsParity = pa; settings.GpsStopBits = st;
            changed(); return true;
        }
        source.SelectionChanged += (_, _) => Save();
        enabled.Click += (_, _) => Save();
        port.LostKeyboardFocus += (_, _) => Save();
        foreach (var box in new[] { baud, bits, parity, stop }) box.SelectionChanged += (_, _) => Save();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { string text = debug(); if (raw.Text != text) { raw.Text = text; raw.ScrollToEnd(); } };
        Loaded += (_, _) => timer.Start();
        Closing += (_, e) => { if (!Save()) e.Cancel = true; };
        Closed += (_, _) => timer.Stop();
    }
}
