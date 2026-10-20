using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using OBDWorkbench.Models;
using OBDWorkbench.Services;
namespace OBDWorkbench;

public enum StartupConnectionFamily
{
    ObdII,
    Td5,
    Ssm,
    Gems
}

public partial class MainWindow
{
    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is not null && sender is System.Windows.Controls.RadioButton button && int.TryParse(button.Tag?.ToString(), out int index)) MainTabs.SelectedIndex = index;
    }

    private void Navigation_TouchDown(object sender, TouchEventArgs e)
    {
        if (MainTabs is null || sender is not System.Windows.Controls.RadioButton button || !int.TryParse(button.Tag?.ToString(), out int index)) return;
        button.IsChecked = true;
        MainTabs.SelectedIndex = index;
        e.Handled = true;
    }
    private readonly TrendPlayback _trendPlayback = new();
    private bool _autoCsvEnabled = true;
    private bool _startupDataReady;
    private StartupConnectionFamily _startupConnectionFamily = StartupConnectionFamily.ObdII;
    public event Action? StartupDataReady;
    public event Action<string>? StartupStatusChanged;
    public bool HasPollingData => _startupDataReady;

    private void RefreshTrendPlayback()
    {
        if (TrendChart is null || PlaybackStatus is null) return;
        if (_isReplayMode) { RefreshReplayTrendViewport(); return; }
        var end = _trendPlayback.EndUtc(DateTime.UtcNow);
        TrendChart.ViewEndUtc = end;
        PlaybackStatus.Text = $"{(_trendPlayback.IsLive && _trendPlayback.Connected ? "Live" : "Frozen")}  {end.AddSeconds(-_trendWindowSeconds).ToLocalTime():HH:mm:ss} – {end.ToLocalTime():HH:mm:ss}";
        TrendChart.InvalidateVisual();
    }
    private void TrendBack_Click(object sender, RoutedEventArgs e)
    {
        if (_isReplayMode && _replaySession is not null) { SeekReplay(Math.Max(0, (_replayCurrentUtc - _replaySession.StartUtc).TotalSeconds - _trendWindowSeconds / 2.0)); return; }
        DateTime? earliest = TrendSeries.SelectMany(x => x.Samples).Select(x => (DateTime?)x.TimestampUtc).Min();
        if (!earliest.HasValue) return;
        _trendPlayback.Back(DateTime.UtcNow, _trendWindowSeconds, earliest); RefreshTrendPlayback();
    }
    private void TrendForward_Click(object sender, RoutedEventArgs e) { if (_isReplayMode && _replaySession is not null) { SeekReplay(Math.Min(ReplaySlider.Maximum, (_replayCurrentUtc - _replaySession.StartUtc).TotalSeconds + _trendWindowSeconds / 2.0)); return; } _trendPlayback.Forward(DateTime.UtcNow, _trendWindowSeconds); RefreshTrendPlayback(); }
    private void TrendPause_Click(object sender, RoutedEventArgs e) { if (_isReplayMode) { PauseReplay(); return; } _trendPlayback.Pause(DateTime.UtcNow); RefreshTrendPlayback(); }
    private void TrendPlay_Click(object sender, RoutedEventArgs e) { if (_isReplayMode) { PlayReplay(); return; } _trendPlayback.Live(DateTime.UtcNow); RefreshTrendPlayback(); }

    private void OnFirstPollingData()
    {
        if (_autoCsvEnabled && !_logger.IsLogging)
        {
            try { Logging_Click(this, new RoutedEventArgs()); }
            catch (Exception ex) { _autoCsvEnabled = false; SetError("CSV recording could not start: " + ex.Message); }
        }
        if (!_startupDataReady)
        {
            _startupDataReady = true;
            SetConnectionPanelCollapsed(true);
            StartupDataReady?.Invoke();
        }
    }
    public void SelectStartupConnectionFamily(StartupConnectionFamily family)
    {
        _startupConnectionFamily = family;
        if (family == StartupConnectionFamily.Ssm)
        {
            ConnectionTypeCombo.SelectedIndex = 0;
            UpdateTransportControls();
            RefreshPorts();
            string preferred = !string.IsNullOrWhiteSpace(_settings.LastSsmPort)
                ? _settings.LastSsmPort
                : (!string.IsNullOrWhiteSpace(_settings.LastSuccessfulPort) ? _settings.LastSuccessfulPort : _settings.LastUsedPort);
            if (!string.IsNullOrWhiteSpace(preferred) && PortCombo.Items.Contains(preferred))
                PortCombo.SelectedItem = preferred;
            StartupStatusChanged?.Invoke("SSM mode selected. Native 4800-baud K-Line will be used; ELM probing is disabled for this connection.");
        }
        else if (family == StartupConnectionFamily.Td5)
        {
            ConnectionTypeCombo.SelectedIndex = 0;
            UpdateTransportControls();
            RefreshPorts();
            string preferred = !string.IsNullOrWhiteSpace(_settings.LastTd5Port)
                ? _settings.LastTd5Port
                : (!string.IsNullOrWhiteSpace(_settings.LastSuccessfulPort) ? _settings.LastSuccessfulPort : _settings.LastUsedPort);
            if (!string.IsNullOrWhiteSpace(preferred) && PortCombo.Items.Contains(preferred))
                PortCombo.SelectedItem = preferred;
            StartupStatusChanged?.Invoke("TD5 mode selected. Native raw K-Line will be used; ELM probing is disabled for this connection.");
        }
        else
        {
            ConnectionTypeCombo.SelectedIndex = Math.Clamp(_settings.LastTransportIndex, 0, 1);
            UpdateTransportControls();
            RefreshPorts();
            ApplySavedProfileForCurrentSelection();
            StartupStatusChanged?.Invoke(family == StartupConnectionFamily.Gems
                ? "GEMS mode selected. ELM327/STN dealer-session probing will be used; write functions remain conservative."
                : "OBDII mode selected. ELM327/STN over USB, Bluetooth serial, or Wi-Fi/TCP will be used.");
        }
    }

    public async Task TryStartupConnectAsync()
    {
        if (_startupConnectionFamily == StartupConnectionFamily.Ssm)
        {
            ConnectionTypeCombo.SelectedIndex = 0;
            UpdateTransportControls();
            string[] ports = OBDWorkbench.Platform.Windows.WindowsSerialTransport.GetPortNames();
            string preferred = !string.IsNullOrWhiteSpace(_settings.LastSsmPort)
                ? _settings.LastSsmPort
                : (!string.IsNullOrWhiteSpace(_settings.LastSuccessfulPort) ? _settings.LastSuccessfulPort : _settings.LastUsedPort);
            string? candidate = ports.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                ? preferred
                : (PortCombo.SelectedItem as string ?? ports.FirstOrDefault());
            if (candidate is null)
            {
                StartupStatusChanged?.Invoke("No COM ports are available for Subaru SSM K-Line. Connect the KKL adapter, Refresh, or Skip.");
                return;
            }
            PortCombo.SelectedItem = candidate;
            StartupStatusChanged?.Invoke($"SSM mode • trying native K-Line on {candidate} at 4800 baud…");
            await ConnectAsync();
            if (!_startupDataReady)
                StartupStatusChanged?.Invoke(_ssm.IsOpen
                    ? "SSM ECU connected; waiting for live data. You can Skip to Live Data."
                    : "SSM connection unavailable. Select the KKL COM port and retry, or Skip to Live Data.");
            return;
        }

        if (_startupConnectionFamily == StartupConnectionFamily.Td5)
        {
            ConnectionTypeCombo.SelectedIndex = 0;
            UpdateTransportControls();
            string[] ports = OBDWorkbench.Platform.Windows.WindowsSerialTransport.GetPortNames();
            string preferred = !string.IsNullOrWhiteSpace(_settings.LastTd5Port)
                ? _settings.LastTd5Port
                : (!string.IsNullOrWhiteSpace(_settings.LastSuccessfulPort) ? _settings.LastSuccessfulPort : _settings.LastUsedPort);
            string? candidate = ports.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                ? preferred
                : (PortCombo.SelectedItem as string ?? ports.FirstOrDefault());
            if (candidate is null)
            {
                StartupStatusChanged?.Invoke("No COM ports are available for TD5 K-Line. Connect the KKL adapter, Refresh, or Skip.");
                return;
            }
            PortCombo.SelectedItem = candidate;
            StartupStatusChanged?.Invoke($"TD5 mode • trying native K-Line on {candidate}…");
            await ConnectAsync();
            if (!_startupDataReady)
                StartupStatusChanged?.Invoke(_td5.IsOpen
                    ? "TD5 ECU connected; waiting for live data. You can Skip to Live Data."
                    : "TD5 connection unavailable. Select the KKL COM port and retry, or Skip to Live Data.");
            return;
        }

        if (!IsWifiSelected)
        {
            string preferred = string.IsNullOrWhiteSpace(_settings.LastUsedPort) ? _settings.LastSuccessfulPort : _settings.LastUsedPort;
            var ports = OBDWorkbench.Platform.Windows.WindowsSerialTransport.GetPortNames();
            string? candidate = ports.Contains(preferred, StringComparer.OrdinalIgnoreCase) ? preferred :
                SerialAdapterDiscovery.Candidates(ports, preferred).FirstOrDefault(x => ports.Contains(x, StringComparer.OrdinalIgnoreCase));
            if (candidate is not null) PortCombo.SelectedItem = candidate;
            else { StartupStatusChanged?.Invoke("No saved or recognizable OBDII adapter found. Select a port and Connect, or Skip."); return; }
        }
        if (!IsWifiSelected && PortCombo.SelectedItem is null)
        { StartupStatusChanged?.Invoke("No COM ports available. Connect an OBDII adapter, Refresh, or Skip."); return; }
        await ConnectAsync();
        if (!_startupDataReady) StartupStatusChanged?.Invoke(_elm.IsOpen
            ? (_startupConnectionFamily == StartupConnectionFamily.Gems
                ? "GEMS adapter/ECU session connected; waiting for dealer data. You can Skip to Live Data."
                : "OBDII adapter connected; waiting for vehicle data. You can Skip to Live Data.")
            : (_startupConnectionFamily == StartupConnectionFamily.Gems
                ? "GEMS connection unavailable. Confirm ELM/STN compatibility and vehicle wiring, then retry or Skip."
                : "OBDII connection unavailable. Adjust settings and retry, or Skip to Live Data."));
    }
    public void ShowStartupConnectionPanel() => SetConnectionPanelCollapsed(false);
    public void FinishSplash()
    {
        StartupOwner = null;
        MainTabs.SelectedIndex = 0;
        SetConnectionPanelCollapsed(_startupDataReady);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBDWorkbench", "Logs");
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { SetError("Could not open logs folder: " + ex.Message); }
    }
    private void RefreshModulePresentation()
    {
        bool jeep = _currentVehicleProfile?.ManufacturerFamily == VehicleKnowledge.Jeep.Family ||
            LiveValues.Any(x => x.Source.Equals("D1", StringComparison.OrdinalIgnoreCase));
        foreach (var live in LiveValues)
        {
            string name = DetectedModules.FirstOrDefault(x => x.Source.Equals(live.Source, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
            bool engine = (jeep && live.Source.Equals("D1", StringComparison.OrdinalIgnoreCase)) || name.Contains("Engine", StringComparison.OrdinalIgnoreCase) || name.Contains("PCM", StringComparison.OrdinalIgnoreCase);
            bool trans = (jeep && live.Source.Equals("18", StringComparison.OrdinalIgnoreCase)) || name.Contains("Transmission", StringComparison.OrdinalIgnoreCase) || name.Contains("TCM", StringComparison.OrdinalIgnoreCase);
            live.SourceDisplay = engine ? "Engine" : trans ? "Trans" : live.Source;
            live.ModuleOrder = engine ? 0 : trans ? 1 : 2;
        }
        ApplyLiveDataView();
    }
}
