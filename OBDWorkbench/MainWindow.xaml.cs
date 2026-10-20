using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class MainWindow : Window
{
    private readonly Elm327Connection _elm = new(OBDWorkbench.Platform.Windows.WindowsSerialTransport.Open);
    private readonly CsvDataLogger _logger = new();
    private readonly ISessionDiagnosticLogger _sessionLog;
    private readonly AppSettingsStore _settingsStore = new(WindowsStoragePaths.Settings());
    private AppSettingsData _settings = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private bool _elmNeedsResync;
    private readonly HashSet<string> _mode03Responders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastCompletedUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recentQueryCompletionsUtc = new();
    private readonly Queue<bool> _recentPollOutcomes = new();
    private CancellationTokenSource? _pollCts;
    private List<PidDefinition> _definitions = new();
    private readonly ResponderCatalog _responders = new();
    private readonly PollScheduler _scheduler = new();
    private DtcDescriptionDatabase? _dtcDatabase;
    private string _confirmedProtocol = "";
    private bool _closing;
    private readonly Dictionary<string, int> _countedPolls = new();

    private long _totalQueries;
    private long _queryErrors;
    private double _totalRttMs;
    private double _totalFirstByteMs;
    private double _totalTailMs;
    private double _lastRttMs;
    private DateTime _pollSessionStartUtc;
    private string _lastLoggedError = "";
    private int _activeTimingProfile;
    private int _consecutiveFailures;
    private int _recoveryCount;
    private bool _isRecovering;
    private DateTime _lastRecoveryAttemptUtc = DateTime.MinValue;
    private string _currentAdapterIdentity = "";
    private string _currentPort = "";
    private int _currentBaud = 38400;
    private string _currentTransport = "Serial";
    private string _currentConnectionId = "";
    private string _currentWifiHost = "192.168.0.10";
    private int _currentWifiPort = 35000;
    private int _consecutiveRecoveryFailures;
    private bool _linkLost;
    private bool _connectionPanelCollapsed;
    private string _currentVehicleSignature = "";
    private VehicleProfile? _currentVehicleProfile;
    private string _ecuFingerprint = "";
    private string _ecuProfileName = "Unidentified";
    private bool _ecuProfileKnown;
    private CancellationTokenSource? _monitorCts;
    private readonly Dictionary<string, DateTime> _dtcFirstRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitorStatusSnapshot> _monitorStatusBySource = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDtcPollUtc = DateTime.MinValue;
    private TaskCompletionSource<bool>? _firstPollCompletion;
    private readonly DispatcherTimer _trendRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _trendWindowSeconds = 60;
    private int _nextTrendColorIndex;
    private string _trendVehicleSignature = "";
    private int _nextDashboardColorIndex;
    private string _dashboardVehicleSignature = "";
    private DashboardGaugeState? _dashboardDragGauge;
    private Point _dashboardDragStart;

    public static readonly DependencyProperty DashboardGridColumnsProperty = DependencyProperty.Register(
        nameof(DashboardGridColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(4));

    public int DashboardGridColumns
    {
        get => (int)GetValue(DashboardGridColumnsProperty);
        set => SetValue(DashboardGridColumnsProperty, Math.Clamp(value, 2, 5));
    }

    public ObservableCollection<PidLiveValue> LiveValues { get; } = new();
    public ObservableCollection<DetectedModule> DetectedModules { get; } = new();
    public ObservableCollection<DiagnosticTroubleCode> DetectedDtcs { get; } = new();
    public ObservableCollection<TrendSeriesState> TrendSeries { get; } = new();
    public ObservableCollection<string> TrendPresetNames { get; } = new();
    public ObservableCollection<DashboardGaugeState> DashboardGauges { get; } = new();
    public ObservableCollection<string> DashboardPresetNames { get; } = new();
    private DiscoveryResult? _lastDiscovery;

    private string PidDatabasePath => Path.Combine(AppContext.BaseDirectory, "Data", "PID_database.csv");
    private string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

    private readonly record struct CommandResult(string Response, double RttMs, double FirstByteMs, double TailMs);
    private readonly record struct BenchmarkResult(int Successes, int Errors, double AvgRttMs, double AvgFirstByteMs, double AvgTailMs);
    private readonly record struct DelayTestResult(int DelayMs, BenchmarkResult Result);
    private readonly record struct MonitorStatusSnapshot(int? DtcCount, bool? MilOn);

    public MainWindow() : this(false) { }

    public MainWindow(bool replayMode, ISessionDiagnosticLogger? diagnosticLogger = null)
    {
        _sessionLog = diagnosticLogger ?? new SessionDiagnosticLogger();
        _isReplayMode = replayMode;
        _settingsStore = new AppSettingsStore(WindowsStoragePaths.Settings(replayMode ? "replay-settings.json" : "settings.json"));
        InitializeComponent();
        // Resolve and apply the theme before the window is ever shown. Applying it in
        // Loaded allowed Windows to paint one light title-bar frame at startup.
        _settings = _settingsStore.Load();
        if (replayMode && !File.Exists(_settingsStore.SettingsPath))
        {
            _settings = new AppSettingsStore(WindowsStoragePaths.Settings()).Load();
            _settingsStore.Save(_settings);
        }
        DashboardGridColumns = Math.Clamp(_settings.DashboardGridColumns <= 0 ? 4 : _settings.DashboardGridColumns, 2, 5);
        if (DashboardGridCombo is not null) DashboardGridCombo.SelectedIndex = DashboardGridColumns - 2;
        ThemeService.Apply(this, _settings);
        ApplyLcarsLayout();
        DataContext = this;
        InitializeSignalPresentation();
        _trendRefreshTimer.Tick += (_, _) =>
        {
            RefreshTrendPlayback();
        };
        Loaded += MainWindow_Loaded;
        ContentRendered += (_, _) =>
        {
            if (_settings.UseLcarsSkin) SetConnectionPanelCollapsed(true);
        };
        Closing += MainWindow_Closing;
        WindowPlacement.Attach(this, 1440, 850, maximize: true, reference: () => StartupMonitorWindow);
    }

    public Window? StartupMonitorWindow { get; set; }

    private bool _workspaceInitialized;
    public void InitializeWorkspace() => MainWindow_Loaded(this, new RoutedEventArgs());
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_workspaceInitialized) return;
        _workspaceInitialized = true;

        if (_isReplayMode)
        {
            _trendWindowSeconds = Math.Clamp(_settings.TrendWindowSeconds, 5, 3600);
            TrendWindowSecondsBox.Text = _trendWindowSeconds.ToString(CultureInfo.InvariantCulture);
            TrendChart.WindowSeconds = _trendWindowSeconds;
            TrendChart.Series = TrendSeries;
            RefreshTrendPresetNames();
            RefreshDashboardPresetNames();
            InitializeReplayMode();
            return;
        }
        RememberAdapterSettingsCheck.IsChecked = _settings.RememberAdapterSettings;
        ConnectionTypeCombo.SelectedIndex = Math.Clamp(_settings.LastTransportIndex, 0, 1);
        WifiHostBox.Text = string.IsNullOrWhiteSpace(_settings.LastWifiHost) ? "192.168.0.10" : _settings.LastWifiHost;
        WifiPortBox.Text = (_settings.LastWifiPort > 0 ? _settings.LastWifiPort : 35000).ToString(CultureInfo.InvariantCulture);
        UpdateTransportControls();

        try
        {
            string sessionPath = _sessionLog.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBDWorkbench", "Logs"));
            SessionLogPathText.Text = sessionPath;
            _sessionLog.WriteLine($"PID database: {PidDatabasePath}");
            _sessionLog.WriteLine($"Settings: {_settingsStore.SettingsPath}");
            _sessionLog.WriteLine($"Global configuration: {_globalConfigurationStore.ConfigurationPath}");
        }
        catch (Exception ex)
        {
            SessionLogPathText.Text = "unavailable: " + ex.Message;
        }

        RefreshPorts();
        ApplySavedProfileForCurrentSelection();
        LoadPidDatabase();
        _trendWindowSeconds = Math.Clamp(_settings.TrendWindowSeconds <= 0 ? 60 : _settings.TrendWindowSeconds, 5, 3600);
        TrendWindowSecondsBox.Text = _trendWindowSeconds.ToString(CultureInfo.InvariantCulture);
        TrendChart.WindowSeconds = _trendWindowSeconds;
        TrendChart.Series = TrendSeries;
        RefreshTrendPresetNames();
        RefreshDashboardPresetNames();
        _trendRefreshTimer.Start();
        _dtcDatabase = new DtcDescriptionDatabase(DataDirectory);
        _sessionLog.WriteLine($"DTC database: {_dtcDatabase.GenericCount} generic descriptions loaded from {DataDirectory}");
        ResetBusStats();
        UpdateLinkHealthUi();
        UpdateCompactStatus();
        SaveGlobalConfiguration();
        InitializeGps();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _gpsTimer.Stop();
        _gps.Dispose();
        _gpsService?.Dispose();
        PauseReplay();
        CancelReplayPrefetch();
        _trendRefreshTimer.Stop();
        StopPolling("APPLICATION_CLOSE");
        if (!_isReplayMode) SaveCurrentAdapterProfile();
        SaveGlobalConfiguration();
        _logger.LogEvent("RECORDING_STOP", ShortVehicleId(), message: "Application closed.");
        _logger.Dispose();
        _monitorCts?.Cancel();
        _elm.Dispose();
        _td5.Dispose();
        _ssm.Dispose();
        _sessionLog.Dispose();
        // An in-flight command still owns the gate; its finally block releases it.
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();


    private bool IsWifiSelected => ConnectionTypeCombo.SelectedIndex == 1;

    private void ConnectionType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateTransportControls();
        ApplySavedProfileForCurrentSelection();
    }

    private void UpdateTransportControls()
    {
        if (SerialControlsPanel is null || WifiControlsPanel is null)
            return;

        bool wifi = IsWifiSelected;
        SerialControlsPanel.Visibility = wifi ? Visibility.Collapsed : Visibility.Visible;
        WifiControlsPanel.Visibility = wifi ? Visibility.Visible : Visibility.Collapsed;
        TransportText.Text = wifi ? "Wi-Fi/TCP" : "Serial";
    }

    private async void FindWifiPort_Click(object sender, RoutedEventArgs e)
    {
        string host = WifiHostBox.Text.Trim();
        if (host.Length == 0)
        {
            MessageBox.Show(StartupOwner ?? this, "Enter the Wi-Fi adapter IP address or host name first.", "OBD Workbench", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int preferredPort = 35000;
        _ = int.TryParse(WifiPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out preferredPort);
        if (preferredPort <= 0 || preferredPort > 65535)
            preferredPort = 35000;

        int[] candidates = new[] { preferredPort, 35000, 23, 2000, 35001, 5000, 8080, 8899, 1234 }
            .Where(p => p > 0 && p <= 65535)
            .Distinct()
            .ToArray();

        FindWifiPortButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        SetError("");
        AppendRaw($"\r\n=== WIFI ELM PORT SEARCH {host} ===\r\n");

        try
        {
            foreach (int port in candidates)
            {
                StatusText.Text = $"Testing {host}:{port}...";
                AppendRaw($"PROBE {host}:{port}\r\n");
                try
                {
                    ElmCommandResponse response = await Elm327Connection.ProbeTcpElmAsync(host, port, 900, 2000);
                    string identity = ExtractElmIdentity(response.Response);
                    bool looksLikeElm = response.Response.Contains("ELM", StringComparison.OrdinalIgnoreCase) && response.Response.Contains('>');
                    AppendRaw($"  response first={response.FirstByteMs:0}ms total={response.TotalMs:0}ms identity={identity}\r\n");
                    if (looksLikeElm)
                    {
                        WifiPortBox.Text = port.ToString(CultureInfo.InvariantCulture);
                        _settings.LastWifiHost = host;
                        _settings.LastWifiPort = port;
                        _settings.LastTransportIndex = 1;
                        SaveSettingsAndGlobalConfiguration();
                        SetConnectionUi($"ELM found at {host}:{port}", false);
                        MessageBox.Show(StartupOwner ?? this, $"ELM327-compatible adapter found at {host}:{port}.\n\n{identity}", "Wi-Fi ELM found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppendRaw($"  no ELM response: {ex.Message}\r\n");
                }
            }

            SetConnectionUi("Disconnected", false);
            MessageBox.Show(StartupOwner ?? this,
                $"No ELM327 prompt was found on the common TCP ports tested for {host}.\n\nYou can still enter a port manually if the adapter documentation specifies one.",
                "Wi-Fi port search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            FindWifiPortButton.IsEnabled = true;
            ConnectButton.IsEnabled = true;
            UpdateTransportControls();
        }
    }

    private void RefreshPorts()
    {
        try
        {
            string? previous = PortCombo.SelectedItem as string;
            string preferred = !string.IsNullOrWhiteSpace(previous) ? previous : (!string.IsNullOrWhiteSpace(_settings.LastUsedPort) ? _settings.LastUsedPort : _settings.LastSuccessfulPort);
            PortCombo.ItemsSource = OBDWorkbench.Platform.Windows.WindowsSerialTransport.GetPortNames();

            if (!string.IsNullOrWhiteSpace(preferred) && PortCombo.Items.Contains(preferred))
                PortCombo.SelectedItem = preferred;
            else if (PortCombo.Items.Count > 0)
                PortCombo.SelectedIndex = 0;

            if (PortCombo.Items.Count == 0)
                StatusText.Text = "No COM ports found";
        }
        catch (Exception ex)
        {
            PortCombo.ItemsSource = Array.Empty<string>();
            SetError("Unable to enumerate COM ports: " + ex.Message);
        }
    }

    private void LoadPidDatabase()
    {
        try
        {
            _definitions = PidDatabase.Load(PidDatabasePath);
            ApplyPollingPriorityOverrides();
            foreach (var definition in _definitions)
            {
                var sources = _responders.Sources(definition.Request);
                definition.ExpectedResponses = _responders.ResponseCount(definition.Request);
                definition.Responders = sources.Length == 0 ? "Not discovered" : string.Join(", ", sources);
                if (definition.Request.StartsWith("01") && sources.Length == 0) definition.Enabled = false;
            }
            PidGrid.ItemsSource = _definitions;
            PidPathText.Text = PidDatabasePath;

            LiveValues.Clear();
            foreach (var definition in _definitions.Where(d => d.Enabled))
                foreach (string source in _responders.Sources(definition.Request))
                    LiveValues.Add(new PidLiveValue { Definition = definition, Source = source, ShowDetailedMetrics = DetailedPollMetricsCheck.IsChecked == true, CanTrend = CanTrendDefinition(definition), TrendUnit = UnitConverter.TargetUnit(definition.Units, _settings) });

            _lastCompletedUtc.Clear();
            foreach (var definition in _definitions.Where(d => d.Enabled))
                _lastCompletedUtc[definition.Request] = DateTime.MinValue;

            SetError("");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(StartupOwner ?? this, ex.Message, "PID database error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyPollingPriorityOverrides()
    {
        foreach (PidDefinition definition in _definitions)
        {
            definition.Priority = _settings.PollPriorityOverrides.TryGetValue(definition.Request, out int priority)
                ? Math.Clamp(priority, 1, 5)
                : Math.Clamp(definition.DefaultPriority, 1, 5);
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private bool _connecting;
    private CancellationTokenSource? _connectCancellation;
    public Window? StartupOwner { get; set; }
    public async Task StopConnectionSearchAsync()
    {
        _connectCancellation?.Cancel();
        while (_connecting) await Task.Delay(50);
        StartupStatusChanged?.Invoke("Search stopped. Adjust settings and choose Connect + Initialize, or Skip.");
    }
    private async Task ConnectAsync()
    {
        if (_isReplayMode) return;
        if (_connecting) return;
        if (ConnectionIsOpen)
        {
            Disconnect();
            return;
        }

        int baud = 38400;
        if (BaudCombo.SelectedItem is ComboBoxItem baudItem)
            _ = int.TryParse(baudItem.Content?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out baud);
        if (baud <= 0)
            baud = 38400;

        string portName = "";
        string wifiHost = WifiHostBox.Text.Trim();
        int wifiPort = 0;

        if (!IsWifiSelected)
        {
            if (PortCombo.SelectedItem is not string selectedPort)
            {
                MessageBox.Show(StartupOwner ?? this, "Select a COM port first.", "OBD Workbench", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            portName = selectedPort;
            _settings.LastUsedPort = portName;
            SaveSettingsAndGlobalConfiguration();
        }
        else
        {
            if (wifiHost.Length == 0 || !int.TryParse(WifiPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out wifiPort) || wifiPort <= 0 || wifiPort > 65535)
            {
                MessageBox.Show(StartupOwner ?? this, "Enter a valid Wi-Fi adapter host/IP address and TCP port.", "OBD Workbench", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        _connecting = true;
        _connectCancellation = new CancellationTokenSource();
        ConnectButton.IsEnabled = false;
        SetConnectionInputEnabled(false);
        SetError("");
        try
        {
            _responders.Clear();
            _confirmedProtocol = "";
            _countedPolls.Clear();
            _dtcFirstRead.Clear();
            _monitorStatusBySource.Clear();
            _lastDtcPollUtc = DateTime.MinValue;
            DetectedDtcs.Clear();
            LoadPidDatabase();
            _currentAdapterIdentity = "";
            _ecuFingerprint = "";
            _ecuProfileName = "Unidentified";
            _ecuProfileKnown = false;
            AdapterText.Text = "--";
            _linkLost = false;
            _consecutiveRecoveryFailures = 0;

            // The splash-screen connection family is authoritative. Native SSM/TD5 modes never send
            // ELM AT commands, and OBDII/GEMS modes never probe raw K-Line families implicitly.
            if (_startupConnectionFamily == StartupConnectionFamily.Ssm)
            {
                if (IsWifiSelected)
                    throw new InvalidOperationException("Subaru SSM requires a serial KKL-compatible adapter, not Wi-Fi/TCP.");
                _elm.Close();
                _td5.Close();
                _td5Mode = false;
                _gemsMode = false;
                _currentPort = portName;
                _currentConnectionId = portName;
                _currentTransport = "Raw K-Line";
                _currentBaud = 4800;
                await ConnectSsmAsync(portName);
                return;
            }
            if (_startupConnectionFamily == StartupConnectionFamily.Td5)
            {
                if (IsWifiSelected)
                    throw new InvalidOperationException("TD5 native K-Line requires a serial KKL-compatible adapter, not Wi-Fi/TCP.");
                _elm.Close();
                _currentPort = portName;
                _currentConnectionId = portName;
                _currentTransport = "Raw K-Line";
                _currentBaud = 10400;
                StartupStatusChanged?.Invoke($"Trying {portName} as native Land Rover TD5 K-Line at 10400 baud…");
                if (await TryConnectTd5Async(portName, "TD5 mode selected from the startup launcher."))
                    return;
                throw new IOException($"No usable Land Rover TD5 native K-Line session was established on {portName}.");
            }

            string atz;
            if (IsWifiSelected)
            {
                _currentTransport = "Tcp";
                _currentWifiHost = wifiHost;
                _currentWifiPort = wifiPort;
                _currentPort = "";
                _currentBaud = 0;
                _currentConnectionId = $"{wifiHost}:{wifiPort}";
                TransportText.Text = "Wi-Fi/TCP";
                SetConnectionUi("Opening Wi-Fi/TCP...", false);
                await _elm.OpenTcpAsync(wifiHost, wifiPort, 3500);
                AppendRaw($"TRANSPORT WiFi/TCP\r\nOPEN {wifiHost}:{wifiPort}\r\n");
                atz = await SendCommandAsync("ATZ", 3500, true);
            }
            else
            {
                _currentTransport = "Serial";
                _currentBaud = baud;
                TransportText.Text = "Serial";
                SetConnectionUi("Opening serial adapter...", false);
                try
                {
                    // OBDII mode probes the explicitly selected COM port as ELM/STN first.
                    // If it fails, adjacent serial ELM ports may be tried, but native TD5
                    // traffic is reserved for the explicit TD5 startup tile.
                    _elm.Close();
                    _currentPort = portName;
                    _currentConnectionId = portName;
                    _connectCancellation?.Token.ThrowIfCancellationRequested();
                    StartupStatusChanged?.Invoke($"Trying {portName} as ELM/STN at {baud} baud (3.5-second response timeout)…");
                    await Task.Run(() => _elm.OpenSerial(portName, baud));
                    AppendRaw($"TRANSPORT Serial\r\nOPEN {portName} @ {baud} 8N1, no flow control\r\n");
                    atz = await SendCommandAsync("ATZ", 3500, true);
                    if (!atz.Contains("ELM", StringComparison.OrdinalIgnoreCase) &&
                        !atz.Contains("STN", StringComparison.OrdinalIgnoreCase) &&
                        !atz.Contains("OBD", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("No recognizable ELM/STN adapter identification received.");
                }
                catch (Exception selectedPortError) when (selectedPortError is TimeoutException || selectedPortError is InvalidOperationException || selectedPortError is IOException || selectedPortError is UnauthorizedAccessException || selectedPortError is ArgumentException)
                {
                    _elm.Close();
                    string selectedError = FriendlySerialPortError(portName, selectedPortError);
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] SELECTED SERIAL ELM PROBE FAILED port={portName} {selectedError}");

                    // OBDII mode is intentionally ELM/STN-only. Preserve adjacent-port ELM
                    // fallback for users whose USB/Bluetooth adapter changed COM number, but do
                    // not send native TD5 traffic unless the TD5 tile was selected at startup.
                    (atz, portName) = await OpenSerialAndResetWithAdjacentFallbackAsync(portName, baud, skipPreferred: true);
                }
                _currentPort = portName;
                _currentConnectionId = portName;
                if (PortCombo.Items.Contains(portName)) PortCombo.SelectedItem = portName;
            }

            _ssm.Close();
            _ssmMode = false;
            _td5Mode = false;
            _gemsMode = false;
            _currentAdapterIdentity = ExtractElmIdentity(atz);
            AdapterText.Text = string.IsNullOrWhiteSpace(_currentAdapterIdentity) ? "Unknown" : _currentAdapterIdentity;
            if (!atz.Contains("ELM", StringComparison.OrdinalIgnoreCase) && !atz.Contains("STN", StringComparison.OrdinalIgnoreCase))
                AppendRaw("WARNING: ATZ did not contain a recognizable ELM/STN identification string.\r\n");

            if (_startupConnectionFamily == StartupConnectionFamily.Gems)
            {
                _settings.LastTransportIndex = IsWifiSelected ? 1 : 0;
                if (IsWifiSelected)
                {
                    _settings.LastWifiHost = wifiHost;
                    _settings.LastWifiPort = wifiPort;
                }
                else
                {
                    _settings.LastSuccessfulPort = _currentPort;
                }
                SaveSettingsAndGlobalConfiguration();
                await InitializeGemsAsync(_connectCancellation?.Token ?? default);
                return;
            }

            AdapterProfile? savedProfile = null;
            if (RememberAdapterSettingsCheck.IsChecked == true && !string.IsNullOrWhiteSpace(_currentAdapterIdentity))
            {
                savedProfile = AppSettingsStore.FindBestProfile(_settings, _currentAdapterIdentity, _currentConnectionId, _currentTransport);
                if (savedProfile is not null)
                {
                    ApplyProfileToUi(savedProfile, includeConnection: false, includeBaud: false);
                    AppendRaw($"Loaded saved adapter profile for {_currentAdapterIdentity} on {_currentConnectionId}.\r\n");
                }
            }

            await RequireOkAsync("ATE0");
            await RequireOkAsync("ATL0");
            await RequireOkAsync("ATH1");

            // Try the last protocol actually confirmed on this adapter/connection before
            // paying for a fresh ATSP0 search. Any failure falls back to the user's
            // selected protocol policy, so moving the adapter to another vehicle is safe.
            string pid00 = await InitializeProtocolAndReadPid00Async(savedProfile?.ConfirmedProtocolNumber);

            string protocol = await SendCommandAsync("ATDP", 2500, true);
            ProtocolText.Text = CleanElmText(protocol, "ATDP");
            await RememberConfirmedProtocolAsync();

            string vinResponse = "";
            try { vinResponse = await SendCommandAsync("0902", 5000, true); } catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] VEHICLE VIN probe unavailable: {ex.Message}"); }
            await CompleteNeutralDiscoveryAsync(pid00, ProtocolText.Text, vinResponse, comprehensive: false);

            ProtocolCombo.IsEnabled = false;
            SetConnectionUi("ECU connected • optimizing live polling...", true);
            await AutoOptimizeForLivePollingAsync();
            try
            {
                await RefreshDtcsAsync(true);
                // RefreshDtcsAsync already records the read time; make the startup
                // scheduling intent explicit so the first automatic Mode 03 refresh
                // cannot occur until a full interval after initialization.
                _lastDtcPollUtc = DateTime.UtcNow;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC REFRESH TIMER armed interval=60s after initial read.");
            }
            catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC INITIAL READ unavailable: {ex.Message}"); }

            SetConnectionUi("ECU connected", true);
            PollingButton.IsEnabled = true;
            TimingCombo.IsEnabled = true;
            ApplyTimingButton.IsEnabled = true;
            BenchmarkButton.IsEnabled = true;
            StabilityTestButton.IsEnabled = true;
            ConnectButton.Content = "Disconnect";

            _settings.LastTransportIndex = IsWifiSelected ? 1 : 0;
            if (IsWifiSelected)
            {
                _settings.LastWifiHost = wifiHost;
                _settings.LastWifiPort = wifiPort;
            }
            else
            {
                _settings.LastSuccessfulPort = _currentPort;
            }

            SaveCurrentAdapterProfile();
            SaveVehicleCalibration();
            UpdateLinkHealthUi();

            // Initialization is complete. Start live polling automatically; only minimize
            // the connection banner after polling has actually started (preferably after
            // the first successful data response).
            _connectCancellation?.Token.ThrowIfCancellationRequested();
            StartPolling();
            if (_pollCts is not null && _firstPollCompletion is not null)
                await Task.WhenAny(_firstPollCompletion.Task, Task.Delay(1500));
            if (_settings.AutoCollapseConnectionPanel && _pollCts is not null)
                SetConnectionPanelCollapsed(true);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            AppendRaw("ERROR: " + ex.Message + "\r\n");
            _elm.Close();
            _ssm.Close();
            _ssmMode = false;
            _td5.Close();
            _td5Mode = false;
            _gemsMode = false;
            SetConnectionUi("Disconnected", false);
            ProtocolText.Text = "--";
            TimingStatusText.Text = "ELM timing: default";
            ProtocolCombo.IsEnabled = true;
            AdapterText.Text = string.IsNullOrWhiteSpace(_currentAdapterIdentity) ? "--" : _currentAdapterIdentity;
            StabilityTestButton.IsEnabled = false;
            UpdateLinkHealthUi();
        }
        finally
        {
            _connecting = false;
            _connectCancellation?.Dispose();
            _connectCancellation = null;
            ConnectButton.IsEnabled = true;
            if (!ConnectionIsOpen)
                SetConnectionInputEnabled(true);
        }
    }

    private async Task RequireOkAsync(string command)
    {
        string response = await SendCommandAsync(command, 2500, true);
        if (!response.Contains("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{command} did not return OK.");
    }

    private async Task<(string Atz, string Port)> OpenSerialAndResetWithAdjacentFallbackAsync(string preferredPort, int baud, bool skipPreferred = false)
    {
        string[] available = OBDWorkbench.Platform.Windows.WindowsSerialTransport.GetPortNames();
        var candidates = SerialAdapterDiscovery.Candidates(available, preferredPort)
            .Where(x => available.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Where(x => !skipPreferred || !x.Equals(preferredPort, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(skipPreferred ? "No additional serial adapter ports were available after the selected port failed OBDII ELM/STN probing." : "No available saved or recognizable adapter ports. Refresh ports or select an adapter manually.");

        Exception? last = null;
        foreach (string candidate in candidates)
        {
            try
            {
                _elm.Close();
                _currentPort = candidate;
                _currentConnectionId = candidate;
                _connectCancellation?.Token.ThrowIfCancellationRequested();
                StartupStatusChanged?.Invoke($"Trying {candidate} — {SerialAdapterDiscovery.Description(candidate)} at {baud} baud (3.5-second response timeout)…");
                await Task.Run(() => _elm.OpenSerial(candidate, baud));
                AppendRaw($"TRANSPORT Serial\r\nOPEN {candidate} @ {baud} 8N1, no flow control\r\n");
                string atz = await SendCommandAsync("ATZ", 3500, true);
                if (!atz.Contains("ELM", StringComparison.OrdinalIgnoreCase) && !atz.Contains("STN", StringComparison.OrdinalIgnoreCase) && !atz.Contains("OBD", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("No recognizable OBD adapter identification received.");
                if (!candidate.Equals(preferredPort, StringComparison.OrdinalIgnoreCase))
                {
                    AppendRaw($"SERIAL FALLBACK: {preferredPort} did not answer ATZ; using {candidate}.\r\n");
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] SERIAL FALLBACK preferred={preferredPort} selected={candidate}");
                }
                return (atz, candidate);
            }
            catch (Exception ex) when (ex is TimeoutException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                bool unavailable = ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException;
                string message = FriendlySerialPortError(candidate, ex);
                last = new InvalidOperationException(message, ex);
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SERIAL PROBE FAILED port={candidate} {message}");
                AppendRaw($"SERIAL PROBE FAILED {candidate}: {message}\r\n");
                if (unavailable)
                    RefreshPorts();
            }
        }

        _elm.Close();
        throw last ?? new InvalidOperationException($"Unable to initialize serial adapter on {preferredPort}.");
    }

    private static string FriendlySerialPortError(string portName, Exception ex) => ex switch
    {
        TimeoutException => ex.Message,
        UnauthorizedAccessException => $"{portName} is unavailable or already in use by another application.",
        IOException => $"{portName} is no longer available. The adapter may have been unplugged or its COM number may have changed.",
        ArgumentException => $"{portName} is not a valid or currently available serial port.",
        _ => ex.Message
    };

    private static bool TryComNumber(string portName, out int number)
    {
        number = 0;
        return portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(portName[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private async Task<string> InitializeProtocolAndReadPid00Async(string? savedProtocolNumber)
    {
        static bool IsSupported(string reply) => MultiEcuParser.DecodeSupportedPids(reply).Any(p => p.Key != "Unattributed");

        async Task<string> TryPid00TwiceAsync()
        {
            string last = "";
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                last = await SendCommandAsync("0100", 7000, true);
                if (IsSupported(last)) return last;
                if (attempt == 1)
                {
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] PID00 RETRY first attempt did not produce a valid attributed response.");
                    await Task.Delay(250);
                }
            }
            return last;
        }

        string preferred = (savedProtocolNumber ?? "").Trim().ToUpperInvariant();
        if (preferred.Length == 1 && "123456789ABC".Contains(preferred))
        {
            try
            {
                AppendRaw($"Trying saved confirmed protocol ATSP{preferred} before automatic search.\r\n");
                await RequireOkAsync("ATSP" + preferred);
                string savedReply = await TryPid00TwiceAsync();
                if (IsSupported(savedReply))
                {
                    _confirmedProtocol = preferred;
                    return savedReply;
                }
                AppendRaw($"Saved protocol {preferred} did not produce a valid PID 00 response; falling back.\r\n");
            }
            catch (Exception ex)
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SAVED PROTOCOL FAILED protocol={preferred} {ex.Message}");
                AppendRaw($"Saved protocol {preferred} failed: {ex.Message}\r\n");
            }
        }

        await RequireOkAsync(GetProtocolCommand());
        string pid00 = await TryPid00TwiceAsync();
        if (!IsSupported(pid00))
            throw new InvalidOperationException("ELM327 responded, but a valid Mode 01 PID 00 ECU response was not recognized. See Raw Terminal.");
        return pid00;
    }

    private void Disconnect()
    {
        _monitorCts?.Cancel();
        StopPolling("USER_DISCONNECT");
        _elmNeedsResync = false;
        _mode03Responders.Clear();
        SaveCurrentAdapterProfile();
        string closing = _ssmMode ? "SSM K-Line" : _td5Mode ? _td5.TransportDescription : _elm.TransportDescription;
        if (_ssmMode) _ssm.Close(); else if (_td5Mode) _td5.Close(); else _elm.Close();
        _ssmMode = false;
        _td5Mode = false;
        _gemsMode = false;
        RawCommandBox.IsEnabled = true;
        RawSendButton.IsEnabled = true;
        if (EraseDtcsButton is not null) EraseDtcsButton.IsEnabled = true;
        if (ReplayRefreshDtcButton is not null) ReplayRefreshDtcButton.IsEnabled = true;
        if (ReplayDiscoveryButton is not null) ReplayDiscoveryButton.Content = "Run neutral discovery";
        PollingButton.IsEnabled = false;
        TimingCombo.IsEnabled = false;
        ApplyTimingButton.IsEnabled = false;
        BenchmarkButton.IsEnabled = false;
        StabilityTestButton.IsEnabled = false;
        ProtocolCombo.IsEnabled = true;
        ConnectButton.Content = "Connect + Initialize";
        ProtocolText.Text = "--";
        TimingStatusText.Text = "ELM timing: default";
        SetConnectionUi("Disconnected", false);
        AppendRaw($"CLOSE {closing}\r\n");
        SetConnectionInputEnabled(true);
        UpdateLinkHealthUi();
        SetConnectionPanelCollapsed(false);
        _currentVehicleSignature = ""; _currentVehicleProfile = null; UpdateCompactStatus();
    }

    private void SetConnectionInputEnabled(bool enabled)
    {
        ConnectionTypeCombo.IsEnabled = enabled;
        SerialControlsPanel.IsEnabled = enabled;
        WifiControlsPanel.IsEnabled = enabled;
        RefreshPortsButton.IsEnabled = enabled;
        FindWifiPortButton.IsEnabled = enabled && IsWifiSelected;
    }

    private async Task<string> SendCommandAsync(string command, int timeoutMs, bool alwaysShowTraffic = false, CancellationToken token = default, int postQuietMs = 0, IEnumerable<string>? expectedSources = null, byte? expectedService = null, int responderWaitMaxMs = 0)
    {
        if (_connecting && StartupOwner is not null) StartupStatusChanged?.Invoke($"{_currentConnectionId}: {command} — initializing adapter / vehicle…");
        CommandResult result = await SendCommandTimedAsync(command, timeoutMs, alwaysShowTraffic, token, postQuietMs, expectedSources, expectedService, responderWaitMaxMs);
        return result.Response;
    }

    private async Task<CommandResult> SendCommandTimedAsync(string command, int timeoutMs, bool alwaysShowTraffic = false, CancellationToken token = default, int postQuietMs = 0, IEnumerable<string>? expectedSources = null, byte? expectedService = null, int responderWaitMaxMs = 0)
    {
        if (_connecting) _connectCancellation?.Token.ThrowIfCancellationRequested();
        await _commandGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (_elmNeedsResync && _elm.IsOpen)
            {
                string sync = await Task.Run(() => _elm.InterruptObdAndWaitForPrompt(1200), token);
                string compactSync = sync.Replace("\r", " ").Replace("\n", " ").Trim();
                bool promptRecovered = sync.Contains('>');
                _sessionLog.WriteLine($"[{DateTime.Now:O}] ELM_RESYNC prompt={(promptRecovered ? "yes" : "no")} raw={(string.IsNullOrWhiteSpace(compactSync) ? "(no prompt text)" : compactSync)}");
                _logger.LogEvent(promptRecovered ? "ELM_RESYNC" : "ELM_RESYNC_FAILED", ShortVehicleId(), message: string.IsNullOrWhiteSpace(compactSync) ? "No prompt returned while resynchronizing a timed-out ELM operation." : compactSync);
                if (!promptRecovered)
                    throw new TimeoutException("ELM did not return to the command prompt after interrupting the timed-out OBD operation. Reconnect the adapter before sending more vehicle traffic.");
                _elmNeedsResync = false;
            }
            bool show = alwaysShowTraffic || ShowPollingTrafficCheck.IsChecked == true;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TX source={(alwaysShowTraffic ? "interactive/system" : "poller")} transport={_currentTransport} vehicle={ShortVehicleId()} command={command}");
            if (show) AppendRaw($">> {command}\r\n");

            ElmCommandResponse response;
            try
            {
                response = await Task.Run(() => _elm.SendCommandDetailed(command, timeoutMs));
            }
            catch (TimeoutException)
            {
                // The host timeout does not prove the ELM has returned to its command
                // prompt. Mark the next transaction for an explicit interrupt/prompt sync.
                _elmNeedsResync = true;
                throw;
            }
            token.ThrowIfCancellationRequested();
            _sessionLog.WriteLine($"[{DateTime.Now:O}] RX source={(alwaysShowTraffic ? "interactive/system" : "poller")} vehicle={ShortVehicleId()} command={command} bytes={response.Response.Length} rtt_ms={response.TotalMs:0.0}");

            if (show)
            {
                AppendRaw(response.Response.TrimEnd() + "\r\n");
                AppendRaw($"[timing: first {response.FirstByteMs:0} ms, tail {response.TailMs:0} ms, total {response.TotalMs:0} ms]\r\n\r\n");
            }

            AppendLcarsTraffic("<< " + response.Response);
            string combinedResponse = response.Response;
            var expected = (expectedSources ?? Array.Empty<string>())
                .Where(source => !string.IsNullOrWhiteSpace(source) && !source.Equals("Unattributed", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenExpected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void ObserveExpected(string raw)
            {
                if (!expectedService.HasValue || expected.Count == 0) return;
                foreach (var frame in MultiEcuParser.ParseMessages(raw))
                    if (frame.Payload.Length > 0 && frame.Payload[0] == expectedService.Value && expected.Contains(frame.Source))
                        seenExpected.Add(frame.Source);
            }

            void LogDrained(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                var lateFrames = MultiEcuParser.ParseMessages(raw).ToList();
                if (lateFrames.Count > 0)
                {
                    foreach (var frame in lateFrames)
                    {
                        string service = frame.Payload.Length > 0 ? frame.Payload[0].ToString("X2") : "--";
                        bool matched = expectedService.HasValue && frame.Payload.Length > 0 && frame.Payload[0] == expectedService.Value && expected.Contains(frame.Source);
                        string status = matched ? "DELAYED_RESPONSE" : "LATE_FRAME";
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] {status} after={command} source={frame.Source} service={service} bytes={frame.Payload.Length} expected={(matched ? "yes" : "no")}");
                        _logger.LogEvent(status, ShortVehicleId(), command, frame.Source, raw, matched
                            ? $"Delayed expected response service={service} collected into the originating command."
                            : $"Late response service={service} drained before next command.");
                    }
                }
                else
                {
                    string compact = raw.Replace("\r", " ").Replace("\n", " ").Trim();
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] LATE_INPUT after={command} raw={compact}");
                    _logger.LogEvent("LATE_INPUT", ShortVehicleId(), command, raw: raw, message: "Unattributed late input drained before next command.");
                }
            }

            ObserveExpected(combinedResponse);
            bool waitForResponders = expectedService.HasValue && expected.Count > 0 && responderWaitMaxMs > 0;
            int quietMs = Math.Max(10, postQuietMs);
            int maxWaitMs = waitForResponders ? Math.Max(quietMs, responderWaitMaxMs) : Math.Max(250, quietMs * 4);

            if (postQuietMs > 0 || waitForResponders)
            {
                if (waitForResponders && !expected.IsSubsetOf(seenExpected))
                {
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] RESPONDER_WAIT command={command} service={expectedService!.Value:X2} expected={string.Join(",", expected)} seen={(seenExpected.Count == 0 ? "none" : string.Join(",", seenExpected))} maxMs={maxWaitMs}");
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < maxWaitMs && !expected.IsSubsetOf(seenExpected))
                    {
                        token.ThrowIfCancellationRequested();
                        int remaining = Math.Max(1, maxWaitMs - (int)wait.ElapsedMilliseconds);
                        int sliceMax = Math.Min(150, remaining);
                        string late = await Task.Run(() => _elm.DrainInputUntilQuiet(Math.Min(75, sliceMax), sliceMax), token);
                        token.ThrowIfCancellationRequested();
                        if (!string.IsNullOrWhiteSpace(late))
                        {
                            combinedResponse += "\r\n" + late;
                            LogDrained(late);
                            ObserveExpected(late);
                        }
                    }

                    string[] missing = expected.Except(seenExpected, StringComparer.OrdinalIgnoreCase).OrderBy(source => source).ToArray();
                    if (missing.Length == 0)
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] RESPONDER_WAIT_COMPLETE command={command} service={expectedService!.Value:X2} responders={string.Join(",", seenExpected.OrderBy(source => source))} elapsedMs={wait.ElapsedMilliseconds}");
                    else
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] RESPONDER_WAIT_TIMEOUT command={command} service={expectedService!.Value:X2} seen={(seenExpected.Count == 0 ? "none" : string.Join(",", seenExpected.OrderBy(source => source)))} missing={string.Join(",", missing)} elapsedMs={wait.ElapsedMilliseconds}");
                }

                // Once all expected modules have had a chance to answer, require a short
                // genuinely idle period. Any extra traffic resets the quiet timer inside
                // DrainInputUntilQuiet, so a slow final ECU cannot spill into the next PID.
                if (postQuietMs > 0)
                {
                    string trailing = await Task.Run(() => _elm.DrainInputUntilQuiet(quietMs, Math.Max(quietMs, 600)), token);
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(trailing))
                    {
                        combinedResponse += "\r\n" + trailing;
                        LogDrained(trailing);
                        ObserveExpected(trailing);
                    }
                }
            }

            return new CommandResult(combinedResponse, response.TotalMs, response.FirstByteMs, response.TailMs);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void Polling_Click(object sender, RoutedEventArgs e)
    {
        if (_pollCts is null)
            StartPolling();
        else
            StopPolling("USER_STOP");
    }

    private void StartPolling()
    {
        if (!ConnectionIsOpen || _pollCts is not null || _closing)
            return;

        _trendPlayback.SetConnected(true, DateTime.UtcNow);
        _pollCts = new CancellationTokenSource();
        _firstPollCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scheduler.Reset();
        PollingButton.Content = "Stop Polling";
        UseResponseCountCheck.IsEnabled = false;

        _lastCompletedUtc.Clear();
        foreach (var definition in _definitions.Where(d => d.Enabled))
            _lastCompletedUtc[definition.Request] = DateTime.MinValue;

        foreach (var item in LiveValues)
            item.ResetPerformance();

        ResetBusStats();
        _pollSessionStartUtc = DateTime.UtcNow;
        string livePriorityPids = string.Join(",", _definitions.Where(d => d.Enabled && !d.DiscoveryOnly && !d.DerivedOnly && d.Priority >= 5).Select(d => d.Request).Distinct(StringComparer.OrdinalIgnoreCase));
        _sessionLog.WriteLine($"[{DateTime.Now:O}] POLLING START response-count={(UseResponseCountCheck.IsChecked == true ? "on" : "off")} timing={GetTimingProfileLabel(_activeTimingProfile)} delay={GetInterRequestDelayMs()}ms autoRecover={(AutoRecoverCheck.IsChecked == true ? "on" : "off")} threshold={GetRecoveryThreshold()} livePriority={livePriorityPids}");
        _logger.LogEvent("POLLING_START", ShortVehicleId());
        _ = RunPollingSessionAsync(_pollCts);
    }

    private void StopPolling(string reason = "USER_OR_OPERATION_STOP")
    {
        _trendPlayback.SetConnected(false, DateTime.UtcNow);
        RefreshTrendPlayback();
        if (_pollCts is not null)
        {
            _sessionLog.WriteLine($"[{DateTime.Now:O}] POLLING STOP queries={_totalQueries} errors={_queryErrors} avgRTT={(_totalQueries > 0 ? _totalRttMs / _totalQueries : 0):0.0}ms rollingError={GetRollingErrorRate() * 100:0.0}% recoveries={_recoveryCount}");
            _logger.LogEvent("POLLING_STOP", ShortVehicleId(), message: reason);
            _sessionLog.WriteLine($"POLLING STOP reason={reason}");
            _pollCts.Cancel();
            // The owning session disposes its CTS once any in-flight command returns.
            _pollCts = null;
        }
        if (PollingButton is not null)
            PollingButton.Content = "Start Polling";
        if (UseResponseCountCheck is not null)
            UseResponseCountCheck.IsEnabled = ConnectionIsOpen && !_td5Mode && !_ssmMode;
    }

    private async Task RunPollingSessionAsync(CancellationTokenSource owner)
    {
        string reason = "COMPLETED";
        try
        {
            await PollLoopAsync(owner.Token);
            reason = owner.IsCancellationRequested ? "CANCELLED" : !ConnectionIsOpen ? "TRANSPORT_CLOSED" : _linkLost ? "RECOVERY_FAILED" : "COMPLETED";
        }
        catch (OperationCanceledException) { reason = "CANCELLED"; }
        catch (Exception ex)
        {
            reason = "UNEXPECTED_ERROR";
            if (!_closing) { _sessionLog.WriteLine($"POLLING FAILED {ex.Message}"); SetError(ex.Message); }
        }
        finally
        {
            // An old cancelled loop must never reset the controls of a newer session.
            if (ReferenceEquals(_pollCts, owner))
            {
                _trendPlayback.SetConnected(false, DateTime.UtcNow);
                RefreshTrendPlayback();
                _pollCts = null;
                if (!_closing)
                {
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] POLLING STOP reason={reason} queries={_totalQueries} errors={_queryErrors}");
                    _logger.LogEvent("POLLING_STOP", ShortVehicleId(), message: reason);
                    PollingButton.Content = "Start Polling";
                    PollingButton.IsEnabled = ConnectionIsOpen;
                    UseResponseCountCheck.IsEnabled = ConnectionIsOpen && !_td5Mode && !_ssmMode;
                    if (!ConnectionIsOpen)
                    {
                        _logger.LogEvent("TRANSPORT_CLOSED", ShortVehicleId(), message: "Polling ended because the adapter port/socket closed. Cause unknown.");
                        SetConnectionUi("Disconnected", false);
                        ConnectButton.Content = "Connect + Initialize";
                        ConnectButton.IsEnabled = true;
                        SetConnectionInputEnabled(true);
                        ProtocolCombo.IsEnabled = true;
                        SetTimingControlsEnabled(false);
                    }
                    foreach (var item in LiveValues.Where(v => !v.Definition.DiscoveryOnly))
                    { item.DisplayValue = "STALE"; item.ResetPerformance(); }
                    UpdateLinkHealthUi();
                }
            }
            owner.Dispose();
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        if (_ssmMode)
        {
            await RunSsmPollLoopAsync(token);
            return;
        }
        if (_td5Mode)
        {
            await RunTd5PollLoopAsync(token);
            return;
        }
        while (!token.IsCancellationRequested && _elm.IsOpen)
        {
            PidDefinition? activePid = null;
            try
            {
                if (_isRecovering) { await Task.Delay(50, token); continue; }
                DateTime now = DateTime.UtcNow;
                PidDefinition? due = SelectNextPid(now, out int waitMs);

                if (due is null)
                {
                    await Task.Delay(waitMs, token);
                    continue;
                }

                activePid = due;
                string pollCommand = BuildPidCommand(due);
                
        CommandResult result = await SendCommandTimedAsync(pollCommand, 3000, false, token);
                DateTime completedUtc = DateTime.UtcNow;
                _lastCompletedUtc[due.Request] = completedUtc;
                _scheduler.Complete(due.Request, completedUtc, result.RttMs);
                RecordQueryMetrics(result.RttMs, result.FirstByteMs, result.TailMs, completedUtc);

                byte mode = byte.Parse(due.ResponseMode, NumberStyles.HexNumber);
                byte pid = byte.Parse(due.PID, NumberStyles.HexNumber);
                var frames = MultiEcuParser.ParseMessages(result.Response)
                    .Where(m => m.Payload.Length >= 2 && m.Payload[0] == mode && m.Payload[1] == pid)
                    .GroupBy(m => m.Source).Select(g => g.First()).ToList();
                var expected = _responders.Sources(due.Request);
                var missingExpected = expected.Except(frames.Select(m => m.Source), StringComparer.OrdinalIgnoreCase).ToList();
                _responders.Observe(due.Request, frames.Select(m => m.Source));
                due.ExpectedResponses = _responders.ResponseCount(due.Request);
                due.Responders = string.Join(", ", _responders.Sources(due.Request));
                bool success = frames.Count > 0;
                bool monitorStatusChanged = false;
                var monitorChangeDescriptions = new List<string>();
                var requestDefinitions = _definitions
                    .Where(d => d.Enabled && d.Request.Equals(due.Request, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (PidDefinition target in requestDefinitions)
                {
                    target.ExpectedResponses = due.ExpectedResponses;
                    target.Responders = due.Responders;
                }
                foreach (var frame in frames)
                {
                    string payload = string.Join(" ", frame.Payload.Select(b => b.ToString("X2")));
                    foreach (PidDefinition target in requestDefinitions)
                    {
                        if (ObdParser.TryDecode(target, payload, out double value, out string error))
                        {
                            OnFirstPollingData();
                            UpdateLiveValue(target, value, result.Response, result.RttMs, result.FirstByteMs, result.TailMs, frame.Source);
                            string? decodedText = StandardPidDisplay.Describe(target, value);
                            string logUnit = decodedText is null ? UnitConverter.TargetUnit(target.Units, _settings) : "";
                            double logValue = decodedText is null ? UnitConverter.Convert(value, target.Units, logUnit) : value;
                            if (decodedText is null && CanTrendDefinition(target))
                            {
                                SignalOffsetSetting cal = GetSignalAdjustment(TrendKey(target, frame.Source), logUnit);
                                logValue = logValue * cal.Gain + cal.Offset;
                            }
                            _logger.Log(target, value, logValue, logUnit, ShortVehicleId(), result.Response,
                                result.RttMs, result.FirstByteMs, result.TailMs, frame.Source, decodedText ?? "");

                            // PID 01 already carries MIL state and confirmed-DTC count. Use it as
                            // an inexpensive change detector so a newly set/cleared code can trigger
                            // Mode 03 immediately instead of waiting for the periodic 60-second read.
                            if (target.Request.Equals("0101", StringComparison.OrdinalIgnoreCase) && !target.DerivedOnly)
                            {
                                uint rawMonitor = (uint)Math.Round(Math.Clamp(value, 0d, (double)uint.MaxValue));
                                byte monitorA = (byte)(rawMonitor >> 24);
                                int dtcCount = monitorA & 0x7F;
                                bool milOn = (monitorA & 0x80) != 0;
                                _monitorStatusBySource.TryGetValue(frame.Source, out MonitorStatusSnapshot previous);

                                bool countChanged = previous.DtcCount.HasValue && previous.DtcCount.Value != dtcCount;
                                bool milChanged = previous.MilOn.HasValue && previous.MilOn.Value != milOn;
                                _monitorStatusBySource[frame.Source] = new MonitorStatusSnapshot(dtcCount, milOn);

                                if (countChanged || milChanged)
                                {
                                    monitorStatusChanged = true;
                                    string oldCount = previous.DtcCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
                                    string oldMil = previous.MilOn.HasValue ? (previous.MilOn.Value ? "ON" : "off") : "unknown";
                                    string change = $"source={frame.Source} count={oldCount}->{dtcCount} MIL={oldMil}->{(milOn ? "ON" : "off")}";
                                    monitorChangeDescriptions.Add(change);
                                    _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC STATUS CHANGE {change}; scheduling immediate Mode 03.");
                                    _logger.LogEvent("DTC_STATUS_CHANGE", ShortVehicleId(), target.Request, frame.Source, result.Response, change, result.RttMs, result.FirstByteMs, result.TailMs);
                                }
                            }
                        }
                        else
                        {
                            // A scheduled definition failing to decode means the transaction failed. A
                            // derived companion is supplemental and must not destabilize otherwise-good polling.
                            if (!target.DerivedOnly)
                            {
                                success = false;
                                RecordPidError(target, frame.Source, "DECODE_ERROR", error, result);
                            }
                            else
                            {
                                _sessionLog.WriteLine($"[{DateTime.Now:O}] DERIVED PID ERROR source={frame.Source} request={target.Request} name={target.Name} {error}");
                                _logger.LogEvent("DERIVED_DECODE_ERROR", ShortVehicleId(), target.Request, frame.Source, result.Response, $"{target.Name}: {error}", result.RttMs, result.FirstByteMs, result.TailMs);
                            }
                        }
                    }
                }
                foreach (string missing in missingExpected)
                {
                    success = false;
                    string status = result.Response.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) ? "NO DATA" : "MISSING_RESPONDER";
                    RecordPidError(due, missing, status, "Expected ECU did not supply this PID.", result);
                }
                if (frames.Count == 0 && expected.Length == 0)
                    RecordPidError(due, "Unattributed", result.Response.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) ? "NO DATA" : "INVALID_RESPONSE", "No matching PID response.", result);
                if (!success)
                {
                    _responders.MarkUncertain(due.Request);
                    _queryErrors++;
                    UpdateBusStatsUi();
                }

                if (_responders.Revalidate(due.Request, frames.Select(m => m.Source), success, pollCommand == due.Request))
                {
                    due.ExpectedResponses = _responders.ResponseCount(due.Request);
                    _sessionLog.WriteLine($"RESPONSE COUNT RESTORED request={due.Request} count={due.ExpectedResponses}");
                    _logger.LogEvent("RESPONSE_COUNT_REVALIDATED", ShortVehicleId(), due.Request,
                        message: $"Three complete unrestricted replies; count={due.ExpectedResponses}.");
                }
                RecordPollOutcome(success);
                if (success) _firstPollCompletion?.TrySetResult(true);

                // PID 01 is our cheap change detector. A changed DTC count/MIL state gets
                // an immediate Mode 03 read; otherwise retain the low-bandwidth 60-second
                // periodic refresh. RefreshDtcsAsync updates _lastDtcPollUtc in either case.
                if (success && monitorStatusChanged)
                {
                    try
                    {
                        _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC IMMEDIATE READ trigger={string.Join("; ", monitorChangeDescriptions)}");
                        await RefreshDtcsAsync(false, token);
                    }
                    catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC IMMEDIATE READ unavailable: {ex.Message}"); }
                }
                else if (success && DateTime.UtcNow - _lastDtcPollUtc >= TimeSpan.FromSeconds(60))
                {
                    try { await RefreshDtcsAsync(false, token); }
                    catch (Exception ex) { _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC PERIODIC READ unavailable: {ex.Message}"); }
                }

                if (!success && ShouldAttemptRecovery())
                {
                    await TryRecoverLinkAsync(token, requiredSources: RecoveryRequiredSources(missingExpected, due));
                    if (_linkLost)
                        break;
                }

                int quietDelay = GetInterRequestDelayMs();
                if (quietDelay > 0 && !token.IsCancellationRequested)
                    await Task.Delay(quietDelay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                bool fatalTcpTransport = IsFatalTcpTransportFailure(ex);
                _queryErrors++;
                if (activePid is not null) _scheduler.Complete(activePid.Request, DateTime.UtcNow, 0);
                _sessionLog.WriteLine($"[{DateTime.Now:O}] POLL EXCEPTION {ex.Message}");
                _logger.LogEvent("POLL_EXCEPTION", ShortVehicleId(), activePid?.Request ?? "", message: ex.Message);
                if (activePid is not null)
                {
                    _responders.MarkUncertain(activePid.Request);
                    foreach (var item in LiveValues.Where(v => ReferenceEquals(v.Definition, activePid)))
                    { item.DisplayValue = "ERROR"; item.ResetPerformance(); }
                }
                RecordPollOutcome(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    SetError(ex.Message);
                    UpdateBusStatsUi();
                });

                if (fatalTcpTransport)
                {
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] TCP_TRANSPORT_LOST Dead TCP socket detected; polling suspended before any further PID writes.");
                    _logger.LogEvent("TCP_TRANSPORT_LOST", ShortVehicleId(), activePid?.Request ?? "", message: ex.Message);
                    _elm.Close();
                    if (AutoRecoverCheck.IsChecked == true)
                    {
                        bool recovered = await TryRecoverLinkAsync(token, forceTransportReopen: true, requiredSources: RecoveryRequiredSources(null, activePid));
                        if (recovered)
                            continue;
                    }
                    _elm.Close();
                    _linkLost = true;
                    break;
                }

                if (ShouldAttemptRecovery())
                {
                    await TryRecoverLinkAsync(token, requiredSources: RecoveryRequiredSources(null, activePid));
                    if (_linkLost)
                        break;
                }

                try { await Task.Delay(Math.Max(250, GetInterRequestDelayMs()), token); } catch { break; }
            }
        }

        if (_linkLost && !token.IsCancellationRequested)
            SetError("Link lost after repeated recovery failures. Reconnect the adapter to resume polling.");
    }

    private bool IsFatalTcpTransportFailure(Exception ex)
    {
        if (!_currentTransport.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
            return false;

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException || current is IOException)
                return true;
            string message = current.Message;
            if (message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("transport connection", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection reset", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void RecordPidError(PidDefinition definition, string source, string status, string message, CommandResult result)
    {
        _logger.LogEvent(status, ShortVehicleId(), definition.Request, source, result.Response, message, result.RttMs, result.FirstByteMs, result.TailMs);
        _sessionLog.WriteLine($"[{DateTime.Now:O}] PID ERROR source={source} request={definition.Request} status={status} {message}");
        var item = LiveValues.FirstOrDefault(v => ReferenceEquals(v.Definition, definition) && v.Source == source);
        if (item is not null) { item.DisplayValue = status; item.Raw = result.Response; item.ResetPerformance(); }
    }

    private string BuildPidCommand(PidDefinition definition)
    {
        _countedPolls.TryGetValue(definition.Request, out int polls);
        _countedPolls[definition.Request] = polls + 1;
        // Discovery already established the responder set. Use optimized framing immediately,
        // then make every twentieth request unrestricted as a periodic verification.
        return _responders.Command(definition.Request, UseResponseCountCheck.IsChecked == true && polls % 20 != 19);
    }

    private int GetInterRequestDelayMs()
    {
        if (DelayCombo.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int tagged))
                return tagged;
            string text = item.Content?.ToString() ?? "0";
            string digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int parsed))
                return parsed;
        }
        return 0;
    }

    private int GetPostInteractiveQuietMs() => Math.Max(250, GetInterRequestDelayMs());

    private string[] KnownModuleSources() => _responders.Supported.Keys
        .Where(source => !string.IsNullOrWhiteSpace(source) && !source.Equals("Unattributed", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(source => source)
        .ToArray();

    private const int DtcResponderWaitMaxMs = 1500;

    private int GetRecoveryThreshold()
    {
        if (RecoveryThresholdCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int threshold))
            return Math.Max(1, threshold);
        return 3;
    }

    private void RecordPollOutcome(bool success)
    {
        _recentPollOutcomes.Enqueue(success);
        while (_recentPollOutcomes.Count > 20)
            _recentPollOutcomes.Dequeue();

        if (success)
            _consecutiveFailures = 0;
        else
            _consecutiveFailures++;

        UpdateBusStatsUi();
        UpdateLinkHealthUi();
    }

    private double GetRollingErrorRate()
    {
        if (_recentPollOutcomes.Count == 0)
            return 0;
        int failures = _recentPollOutcomes.Count(x => !x);
        return (double)failures / _recentPollOutcomes.Count;
    }

    private bool ShouldAttemptRecovery()
    {
        if (AutoRecoverCheck.IsChecked != true || _isRecovering || !_elm.IsOpen || _linkLost)
            return false;
        if (_consecutiveFailures < GetRecoveryThreshold())
            return false;
        return (DateTime.UtcNow - _lastRecoveryAttemptUtc).TotalSeconds >= 5;
    }

    private string PreferredRecoverySource()
    {
        string[] rpmSources = _responders.Sources("010C");
        if (rpmSources.Length == 1) return rpmSources[0];

        string? namedEngine = _responders.Supported.Keys
            .FirstOrDefault(source =>
            {
                string name = MultiEcuParser.ModuleName(source);
                return name.Contains("PCM", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Engine", StringComparison.OrdinalIgnoreCase);
            });
        if (!string.IsNullOrWhiteSpace(namedEngine)) return namedEngine;

        return _responders.Supported
            .OrderByDescending(pair => pair.Value.Count)
            .Select(pair => pair.Key)
            .FirstOrDefault() ?? string.Empty;
    }

    private string[] RecoveryRequiredSources(IEnumerable<string>? missingSources, PidDefinition? activePid)
    {
        var missing = (missingSources ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Equals("Unattributed", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string primary = PreferredRecoverySource();
        if (!string.IsNullOrWhiteSpace(primary) && missing.Contains(primary, StringComparer.OrdinalIgnoreCase))
            return new[] { primary };
        if (missing.Length > 0)
            return missing;

        if (activePid is not null)
        {
            string[] expected = _responders.Sources(activePid.Request);
            if (!string.IsNullOrWhiteSpace(primary) && expected.Contains(primary, StringComparer.OrdinalIgnoreCase))
                return new[] { primary };
            if (expected.Length == 1) return expected;
        }

        return string.IsNullOrWhiteSpace(primary) ? Array.Empty<string>() : new[] { primary };
    }

    private static string[] Pid00Sources(string reply) => MultiEcuParser.DecodeSupportedPids(reply)
        .Keys.Where(k => !k.Equals("Unattributed", StringComparison.OrdinalIgnoreCase))
        .OrderBy(k => k).ToArray();

    private static bool RecoveryResponseHasRequiredSources(string reply, IReadOnlyCollection<string> requiredSources, out string[] actualSources)
    {
        string[] sources = Pid00Sources(reply);
        actualSources = sources;
        if (sources.Length == 0) return false;
        if (requiredSources.Count == 0) return true;
        return requiredSources.All(required => sources.Contains(required, StringComparer.OrdinalIgnoreCase));
    }

    private async Task RememberConfirmedProtocolAsync(CancellationToken token = default)
    {
        try
        {
            string number = CleanElmText(await SendCommandAsync("ATDPN", 2500, true, token), "ATDPN").Trim().ToUpperInvariant();
            if (number.Length == 2 && number[0] == 'A') number = number[1..];
            if (number.Length == 1 && "123456789ABC".Contains(number)) _confirmedProtocol = number;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _sessionLog.WriteLine($"Protocol number unavailable: {ex.Message}"); }
    }

    private async Task<bool> TryRecoverLinkAsync(CancellationToken token, bool forceTransportReopen = false, IEnumerable<string>? requiredSources = null)
    {
        if (_isRecovering || _linkLost || token.IsCancellationRequested) return false;
        if (!forceTransportReopen && !_elm.IsOpen) return false;
        _isRecovering = true;
        _lastRecoveryAttemptUtc = DateTime.UtcNow;
        UpdateLinkHealthUi();

        string[] required = (requiredSources ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Equals("Unattributed", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (required.Length == 0)
            required = RecoveryRequiredSources(null, null);
        string requiredText = required.Length == 0 ? "any-attributed" : string.Join(",", required);

        void Event(string status, string message)
        {
            _sessionLog.WriteLine($"[{DateTime.Now:O}] {status} {message}");
            _logger.LogEvent(status, ShortVehicleId(), message: message);
        }
        bool Valid(string reply, out string[] actual) => RecoveryResponseHasRequiredSources(reply, required, out actual);
        async Task Ok(string command)
        {
            string reply = await SendCommandAsync(command, 2500, true, token);
            if (!reply.Contains("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{command} did not return OK.");
        }
        async Task<string> Probe(string command)
        {
            try
            {
                string reply = await SendCommandAsync(command, 2500, true, token);
                string clean = CleanElmText(reply, command).Replace("\r", " ").Replace("\n", " ").Trim();
                return string.IsNullOrWhiteSpace(clean) ? "(empty)" : clean;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return "unavailable:" + ex.GetType().Name; }
        }
        void Succeeded(string pid00, string stage, string[] actualSources)
        {
            token.ThrowIfCancellationRequested();
            _consecutiveFailures = 0;
            _consecutiveRecoveryFailures = 0;
            _linkLost = false;
            _responders.AddBitmap(pid00);
            _countedPolls.Clear();
            _recoveryCount++;
            Event("RECOVERY_SUCCESS", $"stage={stage} count={_recoveryCount} responders={string.Join(",", actualSources)} required={requiredText}");
            SetError("");
            UpdateBusStatsUi();
        }

        Event("RECOVERY_START", $"consecutiveFailures={_consecutiveFailures} knownProtocol={_confirmedProtocol} forceReopen={forceTransportReopen} required={requiredText}");
        try
        {
            if (!forceTransportReopen)
            {
                // Capture the adapter/bus state before ATZ or protocol changes destroy useful evidence.
                await Task.Delay(500, token);
                string voltage = await Probe("ATRV");
                string protocolBefore = await Probe("ATDP");
                string protocolNumberBefore = await Probe("ATDPN");
                string retry = "";
                string[] retrySources = Array.Empty<string>();
                try
                {
                    Event("RECOVERY_RETRY", "Unrestricted 0100 with responder validation before adapter reset.");
                    retry = await SendCommandAsync("0100", 2500, true, token, GetPostInteractiveQuietMs());
                    Valid(retry, out retrySources);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Event("RECOVERY_RETRY_FAILED", ex.Message);
                    if (!_elm.IsOpen) throw;
                }

                Event("RECOVERY_SNAPSHOT", $"voltage={voltage} protocol={protocolBefore} protocolNumber={protocolNumberBefore} responders={(retrySources.Length == 0 ? "none" : string.Join(",", retrySources))} required={requiredText}");
                if (Valid(retry, out retrySources))
                {
                    Succeeded(retry, "soft-retry", retrySources);
                    return true;
                }
                if (retrySources.Length > 0)
                    Event("RECOVERY_PARTIAL", $"stage=soft-retry responders={string.Join(",", retrySources)} missing={string.Join(",", required.Except(retrySources, StringComparer.OrdinalIgnoreCase))}");
            }
            else
            {
                Event("RECOVERY_SNAPSHOT", $"skipped=dead-transport required={requiredText}");
            }

            token.ThrowIfCancellationRequested();
            if (forceTransportReopen)
                Event("RECOVERY_TRANSPORT_REOPEN", "TCP transport failed; discarding the dead socket and opening a new connection.");
            else
                Event("RECOVERY_RESET", "Required responder did not return; resetting adapter.");

            if (forceTransportReopen || _consecutiveRecoveryFailures > 0)
            {
                _elm.Close();
                await ReopenCurrentTransportAsync(token);
                token.ThrowIfCancellationRequested();
            }
            string atz = await SendCommandAsync("ATZ", 3500, true, token);
            if (!atz.Contains("ELM", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Recovery ATZ did not return an ELM identification string.");
            await Ok("ATE0");
            await Ok("ATL0");
            await Ok("ATH1");

            string pid00 = "";
            string[] pid00Sources = Array.Empty<string>();
            if (_confirmedProtocol.Length > 0)
            {
                try
                {
                    Event("RECOVERY_KNOWN_PROTOCOL", $"Trying protocol {_confirmedProtocol}.");
                    await Ok("ATSP" + _confirmedProtocol);
                    pid00 = await SendCommandAsync("0100", 7000, true, token, GetPostInteractiveQuietMs());
                    Valid(pid00, out pid00Sources);
                    if (pid00Sources.Length > 0 && !Valid(pid00, out _))
                        Event("RECOVERY_PARTIAL", $"stage=known-protocol responders={string.Join(",", pid00Sources)} required={requiredText}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Event("RECOVERY_KNOWN_PROTOCOL_FAILED", ex.Message);
                    if (!_elm.IsOpen) throw;
                }
            }
            if (!Valid(pid00, out pid00Sources))
            {
                if (_confirmedProtocol.Length > 0)
                {
                    Event("RECOVERY_FIXED_PROTOCOL_STOP", $"Protocol {_confirmedProtocol} was previously confirmed; automatic multi-protocol fallback suppressed to avoid driving unrelated DLC buses during recovery.");
                    throw new InvalidOperationException($"Confirmed protocol {_confirmedProtocol} did not recover. Automatic protocol search was suppressed; reconnect or select a protocol manually.");
                }

                string fallback = GetProtocolCommand();
                Event("RECOVERY_PROTOCOL_FALLBACK", $"No confirmed protocol is available; using selected protocol policy {fallback}.");
                await Ok(fallback);
                pid00 = await SendCommandAsync("0100", 7000, true, token, GetPostInteractiveQuietMs());
            }
            if (!Valid(pid00, out pid00Sources))
            {
                string responding = pid00Sources.Length == 0 ? "none" : string.Join(",", pid00Sources);
                throw new InvalidOperationException($"Required responder(s) {requiredText} absent after recovery; PID 00 responders={responding}.");
            }
            await ApplyTimingProfileAsync(_activeTimingProfile, true, token);
            string protocol = await SendCommandAsync("ATDP", 2500, true, token);
            ProtocolText.Text = CleanElmText(protocol, "ATDP");
            await RememberConfirmedProtocolAsync(token);
            Succeeded(pid00, forceTransportReopen ? "transport-reopen" : "adapter-reset", pid00Sources);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closing) Event("RECOVERY_CANCELLED", "Polling was stopped or disconnected.");
            return false;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                if (!_closing) Event("RECOVERY_CANCELLED", "Polling was stopped or disconnected.");
                return false;
            }
            _consecutiveRecoveryFailures++;
            Event("RECOVERY_FAILED", $"attempt={_consecutiveRecoveryFailures} required={requiredText} {ex.Message}");
            if (!_elm.IsOpen || _consecutiveRecoveryFailures >= 2)
            {
                _linkLost = true;
                Event("LINK_LOST", !_elm.IsOpen ? "Adapter transport closed; cause unknown." :
                    $"Required responder(s) {requiredText} remained offline after repeated recovery attempts.");
            }
            SetError("Recovery failed: " + ex.Message);
            return false;
        }
        finally
        {
            _isRecovering = false;
            if (!_closing) UpdateLinkHealthUi();
        }
    }

    private async Task ReopenCurrentTransportAsync(CancellationToken token = default)
    {
        if (_currentTransport.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await _elm.OpenTcpAsync(_currentWifiHost, _currentWifiPort, 3500);
                    AppendRaw($"REOPEN WiFi/TCP {_currentWifiHost}:{_currentWifiPort} attempt={attempt}\r\n");
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] TCP REOPEN SUCCESS attempt={attempt} endpoint={_currentWifiHost}:{_currentWifiPort}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] TCP REOPEN FAILED attempt={attempt} endpoint={_currentWifiHost}:{_currentWifiPort} {ex.Message}");
                    _elm.Close();
                    if (attempt < 2) await Task.Delay(750, token);
                }
            }
            throw new InvalidOperationException("Unable to reopen the Wi-Fi/TCP adapter connection.", last);
        }
        else
        {
            _elm.OpenSerial(_currentPort, _currentBaud);
            AppendRaw($"REOPEN Serial {_currentPort} @ {_currentBaud}\r\n");
        }
    }

    private PidDefinition? SelectNextPid(DateTime now, out int waitMs) =>
        _scheduler.Next(_definitions, now, GetInterRequestDelayMs(), out waitMs);

    private void RecordQueryMetrics(double rttMs, double firstByteMs, double tailMs, DateTime completedUtc)
    {
        _totalQueries++;
        _totalRttMs += rttMs;
        _totalFirstByteMs += firstByteMs;
        _totalTailMs += tailMs;
        _lastRttMs = rttMs;
        _recentQueryCompletionsUtc.Enqueue(completedUtc);

        if (_totalQueries % 25 == 0)
        {
            double avgRtt = _totalRttMs / _totalQueries;
            double avgFirst = _totalFirstByteMs / _totalQueries;
            double avgTail = _totalTailMs / _totalQueries;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] POLL STATS queries={_totalQueries} errors={_queryErrors} rollingError={GetRollingErrorRate() * 100:0.0}% consecutive={_consecutiveFailures} recoveries={_recoveryCount} avgRTT={avgRtt:0.0}ms avgFirst={avgFirst:0.0}ms avgTail={avgTail:0.0}ms delay={GetInterRequestDelayMs()}ms");
        }

        DateTime cutoff = completedUtc.AddSeconds(-10);
        while (_recentQueryCompletionsUtc.Count > 0 && _recentQueryCompletionsUtc.Peek() < cutoff)
            _recentQueryCompletionsUtc.Dequeue();

        UpdateBusStatsUi();
    }

    private void ResetBusStats()
    {
        _totalQueries = 0;
        _queryErrors = 0;
        _totalRttMs = 0;
        _totalFirstByteMs = 0;
        _totalTailMs = 0;
        _lastRttMs = 0;
        _recentQueryCompletionsUtc.Clear();
        _recentPollOutcomes.Clear();
        _consecutiveFailures = 0;
        _recoveryCount = 0;
        _consecutiveRecoveryFailures = 0;
        _linkLost = false;
        _isRecovering = false;
        _pollSessionStartUtc = DateTime.UtcNow;
        UpdateBusStatsUi();
        UpdateLinkHealthUi();
    }

    private void UpdateBusStatsUi()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateBusStatsUi);
            return;
        }

        double elapsedSeconds = Math.Max(1.0, (DateTime.UtcNow - _pollSessionStartUtc).TotalSeconds);
        double windowSeconds = Math.Min(10.0, elapsedSeconds);
        double queryRate = _recentQueryCompletionsUtc.Count / windowSeconds;
        double avgRtt = _totalQueries > 0 ? _totalRttMs / _totalQueries : 0;
        double avgFirst = _totalQueries > 0 ? _totalFirstByteMs / _totalQueries : 0;
        double avgTail = _totalQueries > 0 ? _totalTailMs / _totalQueries : 0;

        QueryRateText.Text = _totalQueries > 0 ? $"{queryRate:0.00} /s" : "-- /s";
        AvgRttText.Text = _totalQueries > 0 ? $"{avgRtt:0} ms" : "-- ms";
        AvgFirstByteText.Text = _totalQueries > 0 ? $"{avgFirst:0} ms" : "-- ms";
        AvgTailText.Text = _totalQueries > 0 ? $"{avgTail:0} ms" : "-- ms";
        LastRttText.Text = _totalQueries > 0 ? $"{_lastRttMs:0} ms" : "-- ms";
        QueryCountText.Text = _totalQueries.ToString(CultureInfo.InvariantCulture);
        QueryErrorText.Text = _queryErrors.ToString(CultureInfo.InvariantCulture);
        RollingErrorText.Text = _recentPollOutcomes.Count > 0 ? $"{GetRollingErrorRate() * 100:0.0}%" : "-- %";
        RecoveryCountText.Text = _recoveryCount.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateLinkHealthUi()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateLinkHealthUi);
            return;
        }

        if (_linkLost)
        {
            LinkHealthText.Text = "LINK LOST";
            LinkHealthText.Foreground = Brushes.Firebrick;
            UpdateCompactStatus();
            return;
        }

        if (_isRecovering)
        {
            LinkHealthText.Text = "Recovering";
            LinkHealthText.Foreground = Brushes.DarkOrange;
            UpdateCompactStatus();
            return;
        }

        if (!_elm.IsOpen)
        {
            LinkHealthText.Text = "--";
            LinkHealthText.Foreground = Brushes.Gray;
            UpdateCompactStatus();
            return;
        }

        if (_recentPollOutcomes.Count < 5)
        {
            LinkHealthText.Text = "Learning";
            LinkHealthText.Foreground = Brushes.SteelBlue;
            UpdateCompactStatus();
            return;
        }

        double rate = GetRollingErrorRate();
        if (rate == 0)
        {
            LinkHealthText.Text = "Healthy";
            LinkHealthText.Foreground = Brushes.ForestGreen;
        }
        else if (rate < 0.10)
        {
            LinkHealthText.Text = $"Good ({rate * 100:0.#}% err)";
            LinkHealthText.Foreground = Brushes.OliveDrab;
        }
        else if (rate < 0.30)
        {
            LinkHealthText.Text = $"UNSTABLE ({rate * 100:0.#}% err)";
            LinkHealthText.Foreground = Brushes.DarkOrange;
        }
        else
        {
            LinkHealthText.Text = $"CRITICAL ({rate * 100:0.#}% err)";
            LinkHealthText.Foreground = Brushes.Firebrick;
        }
        UpdateCompactStatus();
    }

    private string GetProtocolCommand() => ProtocolCombo.SelectedIndex == 1 ? "ATSP3" : "ATSP0";

    private static string ExtractElmIdentity(string response)
    {
        string cleaned = CleanElmText(response, "ATZ");
        string[] pieces = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int elmIndex = Array.FindIndex(pieces, p => p.StartsWith("ELM", StringComparison.OrdinalIgnoreCase));
        if (elmIndex >= 0)
            return string.Join(" ", pieces.Skip(elmIndex).Take(2));
        return cleaned.Length > 48 ? cleaned[..48] : cleaned;
    }

    private void ApplySavedProfileForCurrentSelection()
    {
        if (RememberAdapterSettingsCheck.IsChecked != true)
            return;

        if (IsWifiSelected)
        {
            string host = WifiHostBox.Text.Trim();
            if (!int.TryParse(WifiPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port <= 0 || port > 65535)
                return;
            string connectionId = $"{host}:{port}";
            AdapterProfile? profile = _settings.AdapterProfiles.Values
                .FirstOrDefault(p => AppSettingsStore.GetConnectionId(p).Equals(connectionId, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
                ApplyProfileToUi(profile, includeConnection: false, includeBaud: false);
            return;
        }

        if (PortCombo.SelectedItem is not string serialPort)
            return;

        AdapterProfile? serialProfile = _settings.AdapterProfiles.Values
            .FirstOrDefault(p => AppSettingsStore.GetConnectionId(p).Equals(serialPort, StringComparison.OrdinalIgnoreCase));
        if (serialProfile is not null)
            ApplyProfileToUi(serialProfile, includeConnection: false, includeBaud: true);
    }

    private void ApplyProfileToUi(AdapterProfile profile, bool includeConnection, bool includeBaud)
    {
        bool profileIsTcp = profile.Transport.Equals("Tcp", StringComparison.OrdinalIgnoreCase);
        if (includeConnection)
        {
            ConnectionTypeCombo.SelectedIndex = profileIsTcp ? 1 : 0;
            if (profileIsTcp)
            {
                WifiHostBox.Text = string.IsNullOrWhiteSpace(profile.Host) ? "192.168.0.10" : profile.Host;
                WifiPortBox.Text = (profile.TcpPort > 0 ? profile.TcpPort : 35000).ToString(CultureInfo.InvariantCulture);
            }
            else if (PortCombo.Items.Contains(profile.Port))
            {
                PortCombo.SelectedItem = profile.Port;
            }
            UpdateTransportControls();
        }

        if (includeBaud && !profileIsTcp)
            SelectComboItemByText(BaudCombo, profile.Baud.ToString(CultureInfo.InvariantCulture));

        ProtocolCombo.SelectedIndex = Math.Clamp(profile.ProtocolIndex, 0, 1);
        TimingCombo.SelectedIndex = Math.Clamp(profile.TimingProfile, 0, 2);
        UseResponseCountCheck.IsChecked = profile.UseResponseCount;
        SelectDelay(profile.InterRequestDelayMs);
        AutoRecoverCheck.IsChecked = profile.AutoRecover;
        SelectComboItemByText(RecoveryThresholdCombo, profile.RecoveryThreshold.ToString(CultureInfo.InvariantCulture));
    }

    private void SaveCurrentAdapterProfile()
    {
        if (_ssmMode)
        {
            _settings.LastSsmPort = _currentPort;
            SaveSettingsAndGlobalConfiguration();
            return;
        }
        try
        {
            _settings.RememberAdapterSettings = RememberAdapterSettingsCheck?.IsChecked == true;
            _settings.LastTransportIndex = IsWifiSelected ? 1 : 0;

            if (IsWifiSelected)
            {
                _settings.LastWifiHost = WifiHostBox.Text.Trim();
                if (int.TryParse(WifiPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedTcpPort) && savedTcpPort > 0 && savedTcpPort <= 65535)
                    _settings.LastWifiPort = savedTcpPort;
            }
            else if (PortCombo.SelectedItem is string selectedPort)
            {
                _settings.LastSuccessfulPort = selectedPort;
            }

            if (_settings.RememberAdapterSettings && !string.IsNullOrWhiteSpace(_currentAdapterIdentity) && !string.IsNullOrWhiteSpace(_currentConnectionId))
            {
                var profile = new AdapterProfile
                {
                    Identity = _currentAdapterIdentity,
                    Transport = _currentTransport,
                    ConnectionId = _currentConnectionId,
                    Port = _currentPort,
                    Baud = _currentBaud,
                    Host = _currentWifiHost,
                    TcpPort = _currentWifiPort,
                    ProtocolIndex = Math.Clamp(ProtocolCombo.SelectedIndex, 0, 1),
                    ConfirmedProtocolNumber = _confirmedProtocol,
                    TimingProfile = Math.Clamp(_activeTimingProfile, 0, 2),
                    UseResponseCount = UseResponseCountCheck.IsChecked == true,
                    InterRequestDelayMs = GetInterRequestDelayMs(),
                    AutoRecover = AutoRecoverCheck.IsChecked == true,
                    RecoveryThreshold = GetRecoveryThreshold()
                };

                _settings.AdapterProfiles[AppSettingsStore.BuildProfileKey(_currentAdapterIdentity, _currentConnectionId)] = profile;
            }

            SaveSettingsAndGlobalConfiguration();
        }
        catch (Exception ex)
        {
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SETTINGS SAVE ERROR {ex.Message}");
        }
    }

    private static void SelectComboItemByText(ComboBox combo, string text)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private void SelectDelay(int delayMs)
    {
        for (int i = 0; i < DelayCombo.Items.Count; i++)
        {
            if (DelayCombo.Items[i] is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int value) && value == delayMs)
            {
                DelayCombo.SelectedIndex = i;
                return;
            }
        }
        DelayCombo.SelectedIndex = 0;
    }

    private void UpdateLiveValue(PidDefinition definition, double value, string raw, double rttMs, double firstByteMs, double tailMs, string source)
    {
        var item = LiveValues.FirstOrDefault(v => ReferenceEquals(v.Definition, definition) && v.Source == source);
        if (item is null)
        {
            item = new PidLiveValue { Definition = definition, Source = source, ShowDetailedMetrics = DetailedPollMetricsCheck.IsChecked == true, CanTrend = CanTrendDefinition(definition), TrendUnit = UnitConverter.TargetUnit(definition.Units, _settings) };
            LiveValues.Add(item);
        }
        string targetUnit = UnitConverter.TargetUnit(definition.Units, _settings);
        double converted = UnitConverter.Convert(value, definition.Units, targetUnit);
        string? decodedText = StandardPidDisplay.Describe(definition, value);
        item.CanTrend = CanTrendDefinition(definition);
        item.TrendUnit = targetUnit;
        SignalOffsetSetting calibration = decodedText is null && item.CanTrend
            ? GetSignalAdjustment(TrendKey(definition, source), targetUnit)
            : new SignalOffsetSetting { Gain = 1.0, Offset = 0, DecimalPlaces = -1, Unit = targetUnit };
        double adjusted = converted * calibration.Gain + calibration.Offset;
        item.DisplayGain = calibration.Gain;
        item.DisplayOffset = calibration.Offset;
        item.DecimalPlaces = calibration.DecimalPlaces;
        item.UnadjustedNumericValue = item.CanTrend ? converted : null;
        item.Units = decodedText is null ? targetUnit : "";
        item.DisplayValue = decodedText ?? FormatValue(adjusted, targetUnit, item.DecimalPlaces);
        item.Raw = raw;
        item.LatestNumericValue = item.CanTrend ? adjusted : null;
        item.RecordSample(DateTime.Now, rttMs, firstByteMs, tailMs);
        if (item.CanTrend)
        {
            RecordTrendSample(item, adjusted, targetUnit);
            RecordDashboardSample(item, adjusted, targetUnit);
            UpdateCalculatedSignalsFromSource(item);
        }
        if (source != "GPS") SetError("");
    }

    private static string FormatValue(double value, string units) => FormatValue(value, units, -1);

    private static string FormatValue(double value, string units, int decimalPlaces)
    {
        if (decimalPlaces >= 0)
            return value.ToString("F" + Math.Clamp(decimalPlaces, 0, 6).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (string.Equals(units, "rpm", StringComparison.OrdinalIgnoreCase))
            return value.ToString("0", CultureInfo.InvariantCulture);
        if (units is "%" or "°F" or "°C" or "mph" or "km/h" or "psi" or "kPa" or "bar")
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool CanTrendDefinition(PidDefinition definition)
    {
        if (definition.DisplayKind.Equals("RawText", StringComparison.OrdinalIgnoreCase) ||
            definition.DisplayKind.Equals("FuelStatus", StringComparison.OrdinalIgnoreCase) ||
            definition.DisplayKind.Equals("ObdCompliance", StringComparison.OrdinalIgnoreCase) ||
            definition.DisplayKind.Equals("O2Locations", StringComparison.OrdinalIgnoreCase))
            return false;
        if (definition.Request.Equals("0101", StringComparison.OrdinalIgnoreCase) && !definition.DerivedOnly)
            return false; // Monitor Status is a packed/decoded status word; DTC Count is trendable separately.
        return !definition.Units.Equals("status", StringComparison.OrdinalIgnoreCase) &&
               !definition.Units.Equals("enum", StringComparison.OrdinalIgnoreCase) &&
               !definition.Units.Equals("bitmask", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrendKey(PidDefinition definition, string source) =>
        $"{definition.Request.Trim().ToUpperInvariant()}|{definition.Name.Trim()}|{source.Trim().ToUpperInvariant()}";

    private void AddToTrend_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not PidLiveValue item)
            return;

        if (!CanTrendDefinition(item.Definition))
        {
            TrendStatusText.Text = $"{item.Name} is a decoded/status value and cannot be plotted as a numeric trend.";
            return;
        }

        AddTrendSeries(item, null, true);
    }

    private void AddToTrendAndGauge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not PidLiveValue item)
            return;

        if (!CanTrendDefinition(item.Definition))
        {
            TrendStatusText.Text = $"{item.Name} is a decoded/status value and cannot be plotted as a numeric trend.";
            DashboardStatusText.Text = $"{item.Name} is a decoded/status value and cannot be displayed as a numeric gauge.";
            return;
        }

        AddTrendSeries(item, null, true);
        AddDashboardGauge(item, null, null, null, null, true);
        SaveGlobalConfiguration();
    }

    private TrendSeriesState? AddTrendSeries(PidLiveValue item, int? forcedColorIndex, bool includeLatestSample)
    {
        string key = TrendKey(item.Definition, item.Source);
        TrendSeriesState? existing = TrendSeries.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            TrendStatusText.Text = $"{existing.DisplayName} is already on the trend.";
            return existing;
        }

        int colorIndex = forcedColorIndex ?? _nextTrendColorIndex;
        if (!forcedColorIndex.HasValue)
            _nextTrendColorIndex++;
        else
            _nextTrendColorIndex = Math.Max(_nextTrendColorIndex, colorIndex + 1);

        var series = new TrendSeriesState
        {
            Key = key,
            Request = item.Definition.Request,
            SignalName = item.Definition.Name,
            Source = item.Source,
            ColorIndex = colorIndex,
            Unit = string.IsNullOrWhiteSpace(item.TrendUnit)
                ? UnitConverter.TargetUnit(item.Definition.Units, _settings)
                : item.TrendUnit,
            DisplayGain = item.DisplayGain,
            DisplayOffset = item.DisplayOffset,
            DecimalPlaces = item.DecimalPlaces
        };
        TrendSeries.Add(series);
        if (_isReplayMode) BackfillReplayTrendSeries(series, item);
        else if (includeLatestSample && item.LatestNumericValue.HasValue)
            series.AddSample(DateTime.UtcNow, item.LatestNumericValue.Value);

        if (!string.IsNullOrWhiteSpace(_currentVehicleSignature))
            _trendVehicleSignature = _currentVehicleSignature;
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
        TrendStatusText.Text = $"Added {series.DisplayName}. {TrendSeries.Count} signal(s) with independent autoscaling Y ranges.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND ADD key={series.Key} color={series.ColorIndex} window={_trendWindowSeconds}s");
        SaveGlobalConfiguration();
        return series;
    }

    private void RecordTrendSample(PidLiveValue item, double value, string unit)
    {
        string key = TrendKey(item.Definition, item.Source);
        TrendSeriesState? series = TrendSeries.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (series is null) return;

        if (!string.Equals(series.Unit, unit, StringComparison.OrdinalIgnoreCase))
        {
            series.ClearHistory();
            series.Unit = unit;
            SignalOffsetSetting cal = GetSignalAdjustment(key, unit);
            series.DisplayGain = cal.Gain;
            series.DisplayOffset = cal.Offset;
            series.DecimalPlaces = cal.DecimalPlaces;
            TrendStatusText.Text = $"Trend history for {series.DisplayName} was cleared because its display unit changed to {unit}.";
        }
        series.AddSample(DateTime.UtcNow, value);
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }

    private void TrendChart_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        OpenTrendConfiguration();
    }

    private void ConfigureTrends_Click(object sender, RoutedEventArgs e) => OpenTrendConfiguration();

    private void TrendResetZoom_Click(object sender, RoutedEventArgs e)
    {
        TrendChart.ResetZoom();
        TrendStatusText.Text = "Trend zoom reset to the normal rolling-window view.";
    }

    private void OpenTrendConfiguration()
    {
        var optionsParent = (System.Windows.Controls.Panel)TrendOptionsBar.Parent;
        optionsParent.Children.Remove(TrendOptionsBar);
        TrendOptionsBar.Visibility = Visibility.Visible;
        var dialog = new SignalConfigurationWindow(
            "Trend configuration",
            () => TrendSeries.Cast<IConfigurableSignal>(),
            signal =>
            {
                if (signal is TrendSeriesState series)
                {
                    TrendSeries.Remove(series);
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND REMOVE key={series.Key} source=config");
                }
            },
            () =>
            {
                TrendChart.InvalidateMeasure();
                TrendChart.InvalidateVisual();
                TrendStatusText.Text = "Trend configuration updated.";
            }, TrendOptionsBar, TryApplyTrendWindow)
        { Owner = this, DataContext = this };
        System.Collections.Specialized.NotifyCollectionChangedEventHandler updateRows = (_, _) => dialog.RefreshSignals();
        TrendSeries.CollectionChanged += updateRows;
        try { dialog.ShowDialog(); }
        finally
        {
            TrendSeries.CollectionChanged -= updateRows;
            if (TrendOptionsBar.Parent is System.Windows.Controls.Panel popupParent) popupParent.Children.Remove(TrendOptionsBar);
            TrendOptionsBar.Visibility = Visibility.Collapsed;
            optionsParent.Children.Add(TrendOptionsBar);
        }
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }

    private bool TryApplyTrendWindow()
    {
        if (!int.TryParse(TrendWindowSecondsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds < 5 || seconds > 3600)
        {
            MessageBox.Show(StartupOwner ?? this, "Enter a rolling trend window from 5 to 3600 seconds.", "Trend window", MessageBoxButton.OK, MessageBoxImage.Information);
            TrendWindowSecondsBox.Focus();
            TrendWindowSecondsBox.SelectAll();
            return false;
        }

        _trendWindowSeconds = seconds;
        _settings.TrendWindowSeconds = seconds;
        if (_isReplayMode) BackfillAllReplayTrends();
        SaveSettingsAndGlobalConfiguration();
        TrendChart.WindowSeconds = seconds;
        RefreshTrendPlayback();
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
        TrendStatusText.Text = $"Rolling trend window set to {seconds} seconds.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND WINDOW seconds={seconds}");
        return true;
    }

    // Retained as a compatibility handler for older XAML/user customizations. The current
    // configuration dialog applies the time span from its bottom Apply buttons.
    private void ApplyTrendWindow_Click(object sender, RoutedEventArgs e) => TryApplyTrendWindow();

    private void ClearTrendHistory_Click(object sender, RoutedEventArgs e)
    {
        foreach (TrendSeriesState series in TrendSeries)
            series.ClearHistory();
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
        TrendStatusText.Text = "Trend history cleared; selected signals remain active.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND HISTORY CLEAR series={TrendSeries.Count}");
    }

    private void ClearTrendSignals_Click(object sender, RoutedEventArgs e)
    {
        ClearTrendSignalsInternal();
        TrendStatusText.Text = "All trend signals cleared. Add values from Data to start a new trend.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND SIGNALS CLEAR");
        SaveGlobalConfiguration();
    }

    private void ClearTrendSignalsInternal()
    {
        TrendSeries.Clear();
        _nextTrendColorIndex = 0;
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }

    private void RemoveTrendSeries_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        TrendSeriesState? series = TrendSeries.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (series is null) return;
        TrendSeries.Remove(series);
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
        TrendStatusText.Text = $"Removed {series.DisplayName} from the trend.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND REMOVE key={series.Key}");
        SaveGlobalConfiguration();
    }

    private void SaveTrendPreset_Click(object sender, RoutedEventArgs e)
    {
        string name = TrendPresetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(StartupOwner ?? this, "Enter a name in the Save as box first.", "Save trend", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (TrendSeries.Count == 0)
        {
            MessageBox.Show(StartupOwner ?? this, "Add at least one Data value to the trend before saving it.", "Save trend", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.TrendPresets.ContainsKey(name))
        {
            MessageBoxResult overwrite = MessageBox.Show(StartupOwner ?? this, $"A saved trend named '{name}' already exists. Replace it?", "Save trend", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
        }

        var preset = new TrendPreset
        {
            Name = name,
            VehicleSignature = !string.IsNullOrWhiteSpace(_currentVehicleSignature) ? _currentVehicleSignature : _trendVehicleSignature,
            WindowSeconds = _trendWindowSeconds,
            Series = TrendSeries.Select(x => new TrendSeriesPreset
            {
                Request = x.Request,
                SignalName = x.SignalName,
                Source = x.Source,
                Unit = x.Unit,
                ColorIndex = x.ColorIndex,
                ColorHex = x.ColorHex,
                AutoScale = x.AutoScale,
                ManualMin = x.ManualMin,
                ManualMax = x.ManualMax,
                DisplayGain = x.DisplayGain,
                DisplayOffset = x.DisplayOffset,
                OffsetUnit = x.Unit,
                DecimalPlaces = x.DecimalPlaces,
                ShowDataPoints = x.ShowDataPoints
            }).ToList()
        };
        _settings.TrendPresets[name] = preset;
        SaveSettingsAndGlobalConfiguration();
        RefreshTrendPresetNames(name);
        TrendStatusText.Text = $"Saved trend '{name}' with {preset.Series.Count} signal(s) and a {preset.WindowSeconds}-second window.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND PRESET SAVE name={name} vehicle={ShortVehicleId()} series={preset.Series.Count} window={preset.WindowSeconds}s");
    }

    private void LoadTrendPreset_Click(object sender, RoutedEventArgs e)
    {
        string? name = TrendPresetCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name) || !_settings.TrendPresets.TryGetValue(name, out TrendPreset? preset))
        {
            MessageBox.Show(StartupOwner ?? this, "Select a saved trend to load.", "Load trend", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearTrendSignalsInternal();
        _trendWindowSeconds = Math.Clamp(preset.WindowSeconds, 5, 3600);
        _settings.TrendWindowSeconds = _trendWindowSeconds;
        TrendWindowSecondsBox.Text = _trendWindowSeconds.ToString(CultureInfo.InvariantCulture);
        TrendChart.WindowSeconds = _trendWindowSeconds;

        int loaded = 0;
        var missing = new List<string>();
        foreach (TrendSeriesPreset saved in preset.Series)
        {
            var candidates = LiveValues.Where(v =>
                v.Definition.Request.Equals(saved.Request, StringComparison.OrdinalIgnoreCase) &&
                v.Definition.Name.Equals(saved.SignalName, StringComparison.OrdinalIgnoreCase) &&
                CanTrendDefinition(v.Definition)).ToList();
            PidLiveValue? live = candidates.FirstOrDefault(v => v.Source.Equals(saved.Source, StringComparison.OrdinalIgnoreCase));
            if (live is null && candidates.Count == 1)
                live = candidates[0];
            if (live is null)
            {
                missing.Add($"{saved.SignalName} [{saved.Source}]");
                continue;
            }

            TrendSeriesState? restored = AddTrendSeries(live, saved.ColorIndex, true);
            if (restored is not null)
            {
                restored.ColorHex = saved.ColorHex;
                bool sameUnit = string.IsNullOrWhiteSpace(saved.Unit) || string.Equals(saved.Unit, restored.Unit, StringComparison.OrdinalIgnoreCase);
                if (sameUnit)
                {
                    restored.DisplayGain = string.IsNullOrWhiteSpace(saved.OffsetUnit) || string.Equals(saved.OffsetUnit, restored.Unit, StringComparison.OrdinalIgnoreCase) ? saved.DisplayGain : 1.0;
                    restored.DisplayOffset = string.IsNullOrWhiteSpace(saved.OffsetUnit) || string.Equals(saved.OffsetUnit, restored.Unit, StringComparison.OrdinalIgnoreCase) ? saved.DisplayOffset : 0;
                    restored.DecimalPlaces = saved.DecimalPlaces;
                    restored.ShowDataPoints = saved.ShowDataPoints;
                    restored.ManualMin = saved.ManualMin;
                    restored.ManualMax = saved.ManualMax;
                    restored.AutoScale = saved.AutoScale;
                }
                else
                {
                    restored.DisplayGain = 1.0;
                    restored.DisplayOffset = 0;
                    restored.DecimalPlaces = saved.DecimalPlaces;
                    restored.ShowDataPoints = saved.ShowDataPoints;
                    restored.ManualMin = null;
                    restored.ManualMax = null;
                    restored.AutoScale = true;
                }
                loaded++;
            }
        }

        SaveSettingsAndGlobalConfiguration();
        _trendVehicleSignature = !string.IsNullOrWhiteSpace(_currentVehicleSignature) ? _currentVehicleSignature : preset.VehicleSignature;
        TrendPresetNameBox.Text = name;
        string vehicleNote = !string.IsNullOrWhiteSpace(preset.VehicleSignature) &&
                             !string.IsNullOrWhiteSpace(_currentVehicleSignature) &&
                             !preset.VehicleSignature.Equals(_currentVehicleSignature, StringComparison.OrdinalIgnoreCase)
            ? " Preset was saved on a different vehicle; only matching signals were loaded."
            : "";
        string missingNote = missing.Count > 0 ? $" Missing now: {string.Join(", ", missing)}." : "";
        TrendStatusText.Text = $"Loaded trend '{name}': {loaded}/{preset.Series.Count} signal(s), {_trendWindowSeconds}-second window.{vehicleNote}{missingNote}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND PRESET LOAD name={name} vehicle={ShortVehicleId()} loaded={loaded}/{preset.Series.Count} window={_trendWindowSeconds}s missing={string.Join(';', missing)}");
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }

    private void DeleteTrendPreset_Click(object sender, RoutedEventArgs e)
    {
        string? name = TrendPresetCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name) || !_settings.TrendPresets.ContainsKey(name))
        {
            MessageBox.Show(StartupOwner ?? this, "Select a saved trend to delete.", "Delete trend", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBoxResult result = MessageBox.Show(StartupOwner ?? this, $"Delete saved trend '{name}'?", "Delete trend", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        _settings.TrendPresets.Remove(name);
        SaveSettingsAndGlobalConfiguration();
        RefreshTrendPresetNames();
        TrendPresetNameBox.Text = "";
        TrendStatusText.Text = $"Deleted saved trend '{name}'.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND PRESET DELETE name={name}");
    }

    private void RefreshTrendPresetNames(string? selectName = null)
    {
        string? previous = selectName ?? TrendPresetCombo?.SelectedItem as string;
        TrendPresetNames.Clear();
        foreach (string name in _settings.TrendPresets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            TrendPresetNames.Add(name);
        if (TrendPresetCombo is null) return;
        if (!string.IsNullOrWhiteSpace(previous) && TrendPresetNames.Contains(previous))
            TrendPresetCombo.SelectedItem = previous;
        else if (TrendPresetNames.Count > 0)
            TrendPresetCombo.SelectedIndex = 0;
    }

    private void HandleTrendVehicleDetected(string stableSignature)
    {
        if (TrendSeries.Count > 0 && !string.IsNullOrWhiteSpace(_trendVehicleSignature) &&
            !_trendVehicleSignature.Equals(stableSignature, StringComparison.OrdinalIgnoreCase))
        {
            string previous = _trendVehicleSignature;
            ClearTrendSignalsInternal();
            TrendStatusText.Text = "Trend signals were cleared because a different vehicle was detected. Load a saved trend or add Data values for this vehicle.";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] TREND VEHICLE CHANGE old={previous} new={stableSignature} action=clear-active-series");
        }
        _trendVehicleSignature = stableSignature;
    }

    private string UnitSettingsSignature()
    {
        string overrides = string.Join(';', _settings.UnitOverrides.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value}"));
        return $"{_settings.UnitSystem}|{overrides}";
    }

    private void RefreshTrendUnitsAndClearHistory()
    {
        foreach (TrendSeriesState series in TrendSeries)
        {
            PidLiveValue? live = LiveValues.FirstOrDefault(v => TrendKey(v.Definition, v.Source).Equals(series.Key, StringComparison.OrdinalIgnoreCase));
            if (live is not null)
            {
                string newUnit = UnitConverter.TargetUnit(live.Definition.Units, _settings);
                if (!string.Equals(series.Unit, newUnit, StringComparison.OrdinalIgnoreCase))
                {
                    series.ManualMin = null;
                    series.ManualMax = null;
                    series.AutoScale = true;
                }
                series.Unit = newUnit;
            }
            series.ClearHistory();
        }
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
        if (TrendSeries.Count > 0)
            TrendStatusText.Text = "Trend history was cleared because display units changed; selected signals remain active.";
    }

    private void AddToDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not PidLiveValue item)
            return;

        if (!CanTrendDefinition(item.Definition))
        {
            DashboardStatusText.Text = $"{item.Name} is a decoded/status value and cannot be displayed as a numeric gauge.";
            return;
        }

        AddDashboardGauge(item, null, null, null, null, true);
    }

    private DashboardGaugeState? AddDashboardGauge(PidLiveValue item, int? forcedColorIndex, string? savedUnit, double? savedMin, double? savedMax, bool includeLatestSample)
    {
        string key = TrendKey(item.Definition, item.Source);
        DashboardGaugeState? existing = DashboardGauges.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DashboardStatusText.Text = $"{existing.DisplayName} is already on the dashboard.";
            return existing;
        }

        int colorIndex = forcedColorIndex ?? _nextDashboardColorIndex;
        if (!forcedColorIndex.HasValue)
            _nextDashboardColorIndex++;
        else
            _nextDashboardColorIndex = Math.Max(_nextDashboardColorIndex, colorIndex + 1);

        string unit = string.IsNullOrWhiteSpace(item.TrendUnit)
            ? UnitConverter.TargetUnit(item.Definition.Units, _settings)
            : item.TrendUnit;
        var gauge = new DashboardGaugeState
        {
            Key = key,
            Request = item.Definition.Request,
            SignalName = item.Definition.Name,
            Source = item.Source,
            ColorIndex = colorIndex,
            DisplayGain = item.DisplayGain,
            DisplayOffset = item.DisplayOffset,
            DecimalPlaces = item.DecimalPlaces
        };
        if (string.Equals(savedUnit ?? unit, unit, StringComparison.OrdinalIgnoreCase))
            gauge.SeedLearnedRange(savedMin, savedMax, unit);
        else
            gauge.ResetForUnit(unit);
        DashboardGauges.Add(gauge);

        if (includeLatestSample && item.LatestNumericValue.HasValue)
            gauge.AddSample(item.LatestNumericValue.Value, unit);

        if (!string.IsNullOrWhiteSpace(_currentVehicleSignature))
            _dashboardVehicleSignature = _currentVehicleSignature;
        DashboardStatusText.Text = $"Added {gauge.DisplayName}. {DashboardGauges.Count} gauge(s); dashboard uses {_settings.DashboardGridColumns} column(s).";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD ADD key={gauge.Key} color={gauge.ColorIndex} position={DashboardGauges.Count - 1}");
        SaveGlobalConfiguration();
        return gauge;
    }

    private void RecordDashboardSample(PidLiveValue item, double value, string unit)
    {
        string key = TrendKey(item.Definition, item.Source);
        DashboardGaugeState? gauge = DashboardGauges.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (gauge is null) return;
        bool unitChanged = !string.Equals(gauge.Unit, unit, StringComparison.OrdinalIgnoreCase);
        gauge.AddSample(value, unit);
        if (unitChanged)
            DashboardStatusText.Text = $"Gauge range for {gauge.DisplayName} was reset because its display unit changed to {unit}.";
    }

    private void DashboardGauge_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement element || element.DataContext is not DashboardGaugeState selected) return;
        e.Handled = true;
        var dialog = new SignalConfigurationWindow(
            $"Gauge configuration - {selected.SignalName}",
            () => new IConfigurableSignal[] { selected },
            signal =>
            {
                if (signal is DashboardGaugeState gauge)
                {
                    DashboardGauges.Remove(gauge);
                    _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD REMOVE key={gauge.Key} source=config");
                }
            },
            () => DashboardStatusText.Text = $"Gauge configuration updated for {selected.SignalName}.")
        { Owner = this };
        dialog.ShowDialog();
    }

    private void DashboardGridCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLcarsColumns) return;
        if (DashboardGridCombo?.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Tag?.ToString(), out int columns)) return;
        DashboardGridColumns = Math.Clamp(columns, 2, 5);
        if (_settings is not null && !_settings.UseLcarsSkin)
        {
            _settings.DashboardGridColumns = DashboardGridColumns;
            SaveSettingsAndGlobalConfiguration();
        }
        if (DashboardStatusText is not null) DashboardStatusText.Text = $"Dashboard grid set to {DashboardGridColumns} columns.";
    }

    private void ResetDashboardRanges_Click(object sender, RoutedEventArgs e)
    {
        foreach (DashboardGaugeState gauge in DashboardGauges)
            gauge.ResetRange();
        DashboardStatusText.Text = "Gauge ranges reset and will expand again from the current values.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD RANGE RESET gauges={DashboardGauges.Count}");
        SaveGlobalConfiguration();
    }

    private void ClearDashboardGauges_Click(object sender, RoutedEventArgs e)
    {
        ClearDashboardGaugesInternal();
        DashboardStatusText.Text = "All dashboard gauges cleared. Add values from the Data tab to create a new layout.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD CLEAR");
        SaveGlobalConfiguration();
    }

    private void ClearDashboardGaugesInternal()
    {
        DashboardGauges.Clear();
        _nextDashboardColorIndex = 0;
        _dashboardDragGauge = null;
    }

    private void RemoveDashboardGauge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        DashboardGaugeState? gauge = DashboardGauges.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (gauge is null) return;
        DashboardGauges.Remove(gauge);
        DashboardStatusText.Text = $"Removed {gauge.DisplayName} from the dashboard.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD REMOVE key={gauge.Key}");
        SaveGlobalConfiguration();
    }

    private void SaveDashboardPreset_Click(object sender, RoutedEventArgs e)
    {
        string name = DashboardPresetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(StartupOwner ?? this, "Enter a name in the Save as box first.", "Save dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (DashboardGauges.Count == 0)
        {
            MessageBox.Show(StartupOwner ?? this, "Add at least one Data value to the dashboard before saving it.", "Save dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.DashboardPresets.ContainsKey(name))
        {
            MessageBoxResult overwrite = MessageBox.Show(StartupOwner ?? this, $"A saved dashboard named '{name}' already exists. Replace it?", "Save dashboard", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
        }

        var preset = new DashboardPreset
        {
            Name = name,
            VehicleSignature = !string.IsNullOrWhiteSpace(_currentVehicleSignature) ? _currentVehicleSignature : _dashboardVehicleSignature,
            Gauges = DashboardGauges.Select(x => new DashboardGaugePreset
            {
                Request = x.Request,
                SignalName = x.SignalName,
                Source = x.Source,
                Unit = x.Unit,
                ColorIndex = x.ColorIndex,
                ColorHex = x.ColorHex,
                AutoScale = x.AutoScale,
                ManualMin = x.ManualMin,
                ManualMax = x.ManualMax,
                ObservedMin = x.ObservedMin,
                ObservedMax = x.ObservedMax,
                DisplayGain = x.DisplayGain,
                DisplayOffset = x.DisplayOffset,
                OffsetUnit = x.Unit,
                DecimalPlaces = x.DecimalPlaces,
                LowRangeMin = x.LowRangeMin,
                LowRangeMax = x.LowRangeMax,
                LowRangeColorHex = x.LowRangeColorHex,
                HighRangeMin = x.HighRangeMin,
                HighRangeMax = x.HighRangeMax,
                HighRangeColorHex = x.HighRangeColorHex
            }).ToList()
        };
        _settings.DashboardPresets[name] = preset;
        SaveSettingsAndGlobalConfiguration();
        RefreshDashboardPresetNames(name);
        DashboardStatusText.Text = $"Saved dashboard '{name}' with {preset.Gauges.Count} gauge(s), their order, colors, and learned ranges.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD PRESET SAVE name={name} vehicle={ShortVehicleId()} gauges={preset.Gauges.Count}");
    }

    private void LoadDashboardPreset_Click(object sender, RoutedEventArgs e)
    {
        string? name = DashboardPresetCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name) || !_settings.DashboardPresets.TryGetValue(name, out DashboardPreset? preset))
        {
            MessageBox.Show(StartupOwner ?? this, "Select a saved dashboard to load.", "Load dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearDashboardGaugesInternal();
        int loaded = 0;
        var missing = new List<string>();
        foreach (DashboardGaugePreset saved in preset.Gauges)
        {
            var candidates = LiveValues.Where(v =>
                v.Definition.Request.Equals(saved.Request, StringComparison.OrdinalIgnoreCase) &&
                v.Definition.Name.Equals(saved.SignalName, StringComparison.OrdinalIgnoreCase) &&
                CanTrendDefinition(v.Definition)).ToList();
            PidLiveValue? live = candidates.FirstOrDefault(v => v.Source.Equals(saved.Source, StringComparison.OrdinalIgnoreCase));
            if (live is null && candidates.Count == 1)
                live = candidates[0];
            if (live is null)
            {
                missing.Add($"{saved.SignalName} [{saved.Source}]");
                continue;
            }

            DashboardGaugeState? restored = AddDashboardGauge(live, saved.ColorIndex, saved.Unit, saved.ObservedMin, saved.ObservedMax, true);
            if (restored is not null)
            {
                restored.ColorHex = saved.ColorHex;
                bool sameUnit = string.IsNullOrWhiteSpace(saved.Unit) || string.Equals(saved.Unit, restored.Unit, StringComparison.OrdinalIgnoreCase);
                if (sameUnit)
                {
                    restored.DisplayGain = string.IsNullOrWhiteSpace(saved.OffsetUnit) || string.Equals(saved.OffsetUnit, restored.Unit, StringComparison.OrdinalIgnoreCase) ? saved.DisplayGain : 1.0;
                    restored.DisplayOffset = string.IsNullOrWhiteSpace(saved.OffsetUnit) || string.Equals(saved.OffsetUnit, restored.Unit, StringComparison.OrdinalIgnoreCase) ? saved.DisplayOffset : 0;
                    restored.DecimalPlaces = saved.DecimalPlaces;
                    restored.LowRangeMin = saved.LowRangeMin; restored.LowRangeMax = saved.LowRangeMax; restored.LowRangeColorHex = saved.LowRangeColorHex;
                    restored.HighRangeMin = saved.HighRangeMin; restored.HighRangeMax = saved.HighRangeMax; restored.HighRangeColorHex = saved.HighRangeColorHex;
                    restored.ManualMin = saved.ManualMin;
                    restored.ManualMax = saved.ManualMax;
                    restored.AutoScale = saved.AutoScale;
                }
                else
                {
                    restored.DisplayGain = 1.0;
                    restored.DisplayOffset = 0;
                    restored.DecimalPlaces = saved.DecimalPlaces;
                    restored.LowRangeMin = saved.LowRangeMin; restored.LowRangeMax = saved.LowRangeMax; restored.LowRangeColorHex = saved.LowRangeColorHex;
                    restored.HighRangeMin = saved.HighRangeMin; restored.HighRangeMax = saved.HighRangeMax; restored.HighRangeColorHex = saved.HighRangeColorHex;
                    restored.ManualMin = null;
                    restored.ManualMax = null;
                    restored.AutoScale = true;
                }
                loaded++;
            }
        }

        _dashboardVehicleSignature = !string.IsNullOrWhiteSpace(_currentVehicleSignature) ? _currentVehicleSignature : preset.VehicleSignature;
        DashboardPresetNameBox.Text = name;
        string vehicleNote = !string.IsNullOrWhiteSpace(preset.VehicleSignature) &&
                             !string.IsNullOrWhiteSpace(_currentVehicleSignature) &&
                             !preset.VehicleSignature.Equals(_currentVehicleSignature, StringComparison.OrdinalIgnoreCase)
            ? " Preset was saved on a different vehicle; only matching signals were loaded."
            : "";
        string missingNote = missing.Count > 0 ? $" Missing now: {string.Join(", ", missing)}." : "";
        DashboardStatusText.Text = $"Loaded dashboard '{name}': {loaded}/{preset.Gauges.Count} gauge(s).{vehicleNote}{missingNote}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD PRESET LOAD name={name} vehicle={ShortVehicleId()} loaded={loaded}/{preset.Gauges.Count} missing={string.Join(';', missing)}");
    }

    private void DeleteDashboardPreset_Click(object sender, RoutedEventArgs e)
    {
        string? name = DashboardPresetCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name) || !_settings.DashboardPresets.ContainsKey(name))
        {
            MessageBox.Show(StartupOwner ?? this, "Select a saved dashboard to delete.", "Delete dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBoxResult result = MessageBox.Show(StartupOwner ?? this, $"Delete saved dashboard '{name}'?", "Delete dashboard", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        _settings.DashboardPresets.Remove(name);
        SaveSettingsAndGlobalConfiguration();
        RefreshDashboardPresetNames();
        DashboardPresetNameBox.Text = "";
        DashboardStatusText.Text = $"Deleted saved dashboard '{name}'.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD PRESET DELETE name={name}");
    }

    private void RefreshDashboardPresetNames(string? selectName = null)
    {
        string? previous = selectName ?? DashboardPresetCombo?.SelectedItem as string;
        DashboardPresetNames.Clear();
        foreach (string name in _settings.DashboardPresets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            DashboardPresetNames.Add(name);
        if (DashboardPresetCombo is null) return;
        if (!string.IsNullOrWhiteSpace(previous) && DashboardPresetNames.Contains(previous))
            DashboardPresetCombo.SelectedItem = previous;
        else if (DashboardPresetNames.Count > 0)
            DashboardPresetCombo.SelectedIndex = 0;
    }

    private void HandleDashboardVehicleDetected(string stableSignature)
    {
        if (DashboardGauges.Count > 0 && !string.IsNullOrWhiteSpace(_dashboardVehicleSignature) &&
            !_dashboardVehicleSignature.Equals(stableSignature, StringComparison.OrdinalIgnoreCase))
        {
            string previous = _dashboardVehicleSignature;
            ClearDashboardGaugesInternal();
            DashboardStatusText.Text = "Dashboard gauges were cleared because a different vehicle was detected. Load a saved dashboard or add Data values for this vehicle.";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD VEHICLE CHANGE old={previous} new={stableSignature} action=clear-active-gauges");
        }
        _dashboardVehicleSignature = stableSignature;
    }

    private void RefreshDashboardUnitsAndRanges()
    {
        foreach (DashboardGaugeState gauge in DashboardGauges)
        {
            PidLiveValue? live = LiveValues.FirstOrDefault(v => TrendKey(v.Definition, v.Source).Equals(gauge.Key, StringComparison.OrdinalIgnoreCase));
            if (live is not null)
                gauge.ResetForUnit(UnitConverter.TargetUnit(live.Definition.Units, _settings));
        }
        if (DashboardGauges.Count > 0)
            DashboardStatusText.Text = "Gauge values and learned ranges were reset because display units changed; selected gauges remain active.";
    }

    private void DashboardTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.DataContext is not DashboardGaugeState gauge) return;
        _dashboardDragGauge = gauge;
        _dashboardDragStart = e.GetPosition(DashboardGaugeItems);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void DashboardTile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dashboardDragGauge is null || sender is not FrameworkElement handle) return;
        Point current = e.GetPosition(DashboardGaugeItems);
        if (Math.Abs(current.X - _dashboardDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DashboardGaugeState dragged = _dashboardDragGauge;
        _dashboardDragGauge = null;
        handle.ReleaseMouseCapture();
        DragDrop.DoDragDrop(handle, dragged, DragDropEffects.Move);
    }

    private void DashboardTile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement handle)
            handle.ReleaseMouseCapture();
        _dashboardDragGauge = null;
        e.Handled = true;
    }

    private void DashboardTile_PreviewTouchDown(object sender, TouchEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.DataContext is not DashboardGaugeState gauge) return;
        _dashboardDragGauge = gauge;
        _dashboardDragStart = e.GetTouchPoint(DashboardGaugeItems).Position;
        handle.CaptureTouch(e.TouchDevice);
        e.Handled = true;
    }

    private void DashboardTile_PreviewTouchMove(object sender, TouchEventArgs e)
    {
        if (_dashboardDragGauge is null || sender is not FrameworkElement handle) return;
        Point current = e.GetTouchPoint(DashboardGaugeItems).Position;
        if (Math.Abs(current.X - _dashboardDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DashboardGaugeState dragged = _dashboardDragGauge;
        _dashboardDragGauge = null;
        handle.ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
        DragDrop.DoDragDrop(handle, dragged, DragDropEffects.Move);
    }

    private void DashboardTile_PreviewTouchUp(object sender, TouchEventArgs e)
    {
        if (sender is FrameworkElement handle)
            handle.ReleaseTouchCapture(e.TouchDevice);
        _dashboardDragGauge = null;
        e.Handled = true;
    }

    private void DashboardTile_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not DashboardGaugeState target) return;
        if (e.Data.GetData(typeof(DashboardGaugeState)) is not DashboardGaugeState dragged) return;
        int oldIndex = DashboardGauges.IndexOf(dragged);
        int newIndex = DashboardGauges.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        DashboardGauges.Move(oldIndex, newIndex);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        DashboardStatusText.Text = $"Moved {dragged.DisplayName} to tile {newIndex + 1}.";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DASHBOARD REORDER key={dragged.Key} from={oldIndex} to={newIndex}");
        SaveGlobalConfiguration();
    }

    private async void ApplyTiming_Click(object sender, RoutedEventArgs e)
    {
        if (!_elm.IsOpen)
            return;

        bool restartPolling = _pollCts is not null;
        if (restartPolling)
            StopPolling();

        SetTimingControlsEnabled(false);
        PollingButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        try
        {
            await ApplyTimingProfileAsync(TimingCombo.SelectedIndex, true);
            SaveCurrentAdapterProfile();
            SaveVehicleCalibration();
            SetError("");
        }
        catch (Exception ex)
        {
            SetError("Timing profile failed: " + ex.Message);
            AppendRaw("TIMING ERROR: " + ex.Message + "\r\n");
        }
        finally
        {
            SetTimingControlsEnabled(_elm.IsOpen);
            PollingButton.IsEnabled = _elm.IsOpen;
            ConnectButton.IsEnabled = true;
            if (restartPolling && _elm.IsOpen)
                StartPolling();
        }
    }

    private async Task ApplyTimingProfileAsync(int profileIndex, bool showTraffic, CancellationToken token = default)
    {
        string adaptiveCommand;
        string timeoutCommand;
        string description;

        switch (profileIndex)
        {
            case 1:
                adaptiveCommand = "ATAT2";
                timeoutCommand = "ATST32";
                description = "Fast adaptive (~200 ms max)";
                break;
            case 2:
                adaptiveCommand = "ATAT2";
                timeoutCommand = "ATST14";
                description = "Fast + short timeout (~80 ms)";
                break;
            default:
                adaptiveCommand = "ATAT1";
                timeoutCommand = "ATST32";
                description = "Conservative (~200 ms)";
                break;
        }

        string adaptiveResponse = await SendCommandAsync(adaptiveCommand, 2500, showTraffic, token);
        if (!adaptiveResponse.Contains("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{adaptiveCommand} did not return OK.");

        string timeoutResponse = await SendCommandAsync(timeoutCommand, 2500, showTraffic, token);
        if (!timeoutResponse.Contains("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{timeoutCommand} did not return OK.");

        _activeTimingProfile = profileIndex;
        TimingStatusText.Text = $"ELM timing: {description}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] TIMING PROFILE {description}");
    }

    private async Task AutoOptimizeForLivePollingAsync()
    {
        bool autoCountAllowed = !string.Equals(_currentVehicleProfile?.ManufacturerFamily, VehicleKnowledge.Jeep.Family, StringComparison.OrdinalIgnoreCase);
        bool canCount = autoCountAllowed && _responders.ResponseCount("010C") > 0;
        UseResponseCountCheck.IsChecked = canCount;

        // Some ELM327 clones accept the compact 010C1 syntax but do not actually shorten
        // the response window. Benchmark compact vs canonical spaced "01 0C 1" syntax and
        // remember the one that produces valid replies with the smaller tail/RTT.
        _responders.UseSpacedCountSyntax = false;
        if (canCount)
        {
            BenchmarkResult compact = await RunDelayTestRequestsAsync(_responders.Command("010C", true), 60, 3);
            _responders.UseSpacedCountSyntax = true;
            BenchmarkResult spaced = await RunDelayTestRequestsAsync(_responders.Command("010C", true), 60, 3);

            bool spacedGood = spaced.Successes == 3 && spaced.Errors == 0;
            bool compactGood = compact.Successes == 3 && compact.Errors == 0;
            bool chooseSpaced = spacedGood && (!compactGood ||
                spaced.AvgTailMs + 10 < compact.AvgTailMs || spaced.AvgRttMs + 10 < compact.AvgRttMs);
            _responders.UseSpacedCountSyntax = chooseSpaced;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] AUTO TUNE response-count syntax compact={compact.Successes}/3 rtt={compact.AvgRttMs:0.0} tail={compact.AvgTailMs:0.0}; spaced={spaced.Successes}/3 rtt={spaced.AvgRttMs:0.0} tail={spaced.AvgTailMs:0.0}; selected={(chooseSpaced ? "spaced" : "compact")}");
        }

        string command = _responders.Command("010C", canCount);
        int selectedTiming = 0;

        // First enable aggressive adaptive timing while retaining the long 200 ms timeout.
        // Only after that proves reliable do we try the 80 ms timeout. This avoids the old
        // all-or-nothing jump that rejected ATAT2 simply because ATST14 was too short.
        try
        {
            await ApplyTimingProfileAsync(1, true);
            BenchmarkResult adaptive = await RunDelayTestRequestsAsync(command, 60, 3);
            if (adaptive.Successes != 3 || adaptive.Errors != 0)
                throw new InvalidOperationException($"Adaptive timing returned {adaptive.Successes}/3 good RPM responses.");
            selectedTiming = 1;

            try
            {
                await ApplyTimingProfileAsync(2, true);
                BenchmarkResult shortTimeout = await RunDelayTestRequestsAsync(command, 60, 3);
                if (shortTimeout.Successes == 3 && shortTimeout.Errors == 0)
                    selectedTiming = 2;
                else
                    throw new InvalidOperationException($"Short-timeout validation returned {shortTimeout.Successes}/3 good RPM responses.");
            }
            catch (Exception ex)
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] AUTO TUNE short timeout rejected: {ex.Message}");
                await ApplyTimingProfileAsync(1, true);
                selectedTiming = 1;
            }
        }
        catch (Exception ex)
        {
            _sessionLog.WriteLine($"[{DateTime.Now:O}] AUTO TUNE adaptive timing rejected: {ex.Message}");
            await ApplyTimingProfileAsync(0, true);
            selectedTiming = 0;
        }

        TimingCombo.SelectedIndex = selectedTiming;

        int selectedDelay = 200;
        foreach (int delay in new[] { 60, 75, 100, 200 })
        {
            BenchmarkResult test = await RunDelayTestRequestsAsync(command, delay, 4);
            _sessionLog.WriteLine($"[{DateTime.Now:O}] AUTO TUNE delay={delay}ms successes={test.Successes}/4 errors={test.Errors} avgRTT={test.AvgRttMs:0.0}ms");
            if (test.Successes == 4 && test.Errors == 0)
            {
                selectedDelay = delay;
                break;
            }
        }
        SelectDelay(selectedDelay);
        _sessionLog.WriteLine($"[{DateTime.Now:O}] AUTO TUNE selected timing={GetTimingProfileLabel(selectedTiming)} delay={selectedDelay}ms response-count={(canCount ? "on" : "off")} syntax={(_responders.UseSpacedCountSyntax ? "spaced" : "compact")}");
        AppendRaw($"AUTO LIVE TUNE: {GetTimingProfileLabel(selectedTiming)}, {selectedDelay} ms quiet delay, response count {(canCount ? "ON" : "OFF")} ({(_responders.UseSpacedCountSyntax ? "spaced" : "compact")})\r\n");
    }

    private bool ValidRpmResponse(string response)
    {
        var sources = MultiEcuParser.ParseMessages(response)
            .Where(m => m.Payload.Length >= 4 && m.Payload[0] == 0x41 && m.Payload[1] == 0x0C)
            .Select(m => m.Source).ToArray();
        var expected = _responders.Sources("010C");
        return sources.Length > 0 && !sources.Contains("Unattributed") && expected.All(sources.Contains);
    }

    private async Task<BenchmarkResult> RunBenchmarkRequestsAsync(string command, int warmups = 2, int measured = 8)
    {
        for (int warmup = 0; warmup < warmups; warmup++)
        {
            try { await SendCommandTimedAsync(command, 2500, false); }
            catch { }
        }

        int successes = 0;
        int errors = 0;
        double totalRtt = 0;
        double totalFirst = 0;
        double totalTail = 0;

        for (int sample = 0; sample < measured; sample++)
        {
            try
            {
                
        CommandResult result = await SendCommandTimedAsync(command, 2500, false);
                bool valid = ValidRpmResponse(result.Response);

                if (!valid)
                {
                    errors++;
                    continue;
                }

                successes++;
                totalRtt += result.RttMs;
                totalFirst += result.FirstByteMs;
                totalTail += result.TailMs;
            }
            catch
            {
                errors++;
            }
        }

        return successes > 0
            ? new BenchmarkResult(successes, errors, totalRtt / successes, totalFirst / successes, totalTail / successes)
            : new BenchmarkResult(0, errors, 0, 0, 0);
    }

    private static string FormatBenchmarkLine(string label, string command, BenchmarkResult result, int measured = 8)
    {
        return result.Successes > 0
            ? $"{label} [{command}]: {result.Successes}/{measured} OK, {result.Errors} errors, avg RTT {result.AvgRttMs:0} ms, first {result.AvgFirstByteMs:0} ms, tail {result.AvgTailMs:0} ms"
            : $"{label} [{command}]: 0/{measured} OK, {result.Errors} errors";
    }

    private async void BenchmarkTiming_Click(object sender, RoutedEventArgs e)
    {
        if (!_elm.IsOpen)
            return;

        bool restartPolling = _pollCts is not null;
        if (restartPolling)
            StopPolling();

        SetTimingControlsEnabled(false);
        UseResponseCountCheck.IsEnabled = false;
        PollingButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        TimingStatusText.Text = "ELM timing: safe benchmarking...";
        SetError("");

        const int measured = 16;
        var summaryLines = new List<string>
        {
            "Safe ELM timing + response-count benchmark using PID 010C (Engine RPM)",
            $"Each test: 2 warm-up requests + {measured} measured requests",
            "Automatic selection tests Conservative and Fast only; Aggressive remains manual/experimental.",
            "010C = normal request; optimized count comes from discovered RPM responders",
            "Response-count optimization is only selected when it is error-free and materially faster (or more reliable).",
            ""
        };

        string optimizedCommand = _responders.Command("010C", true);
        var optimizedResults = new List<(int Profile, BenchmarkResult Result)>();
        int selectedProfile = 0;
        bool useResponseCount = false;
        int originalDelay = GetInterRequestDelayMs();
        BenchmarkResult baseline = default;

        try
        {
            AppendRaw("\r\n=== SAFE BASELINE Conservative / normal 010C ===\r\n");
            await ApplyTimingProfileAsync(0, true);
            baseline = await RunBenchmarkRequestsAsync("010C", 2, measured);
            string baselineLine = FormatBenchmarkLine("Conservative (~200 ms)", "010C", baseline, measured);
            summaryLines.Add(baselineLine);
            AppendRaw(baselineLine + "\r\n");
            summaryLines.Add("");

            for (int profile = 0; profile <= 1; profile++)
            {
                string label = GetTimingProfileLabel(profile);
                AppendRaw($"\r\n=== SAFE OPTIMIZED BENCHMARK {label} / {optimizedCommand} ===\r\n");

                try
                {
                    await ApplyTimingProfileAsync(profile, true);
                }
                catch (Exception ex)
                {
                    string applyFailure = $"{label} [{optimizedCommand}]: profile could not be applied ({ex.Message})";
                    summaryLines.Add(applyFailure);
                    AppendRaw(applyFailure + "\r\n");
                    continue;
                }

                BenchmarkResult result = await RunBenchmarkRequestsAsync(optimizedCommand, 2, measured);
                optimizedResults.Add((profile, result));
                string line = FormatBenchmarkLine(label, optimizedCommand, result, measured);
                summaryLines.Add(line);
                AppendRaw(line + "\r\n");
            }

            var errorFreeOptimized = optimizedResults
                .Where(x => x.Result.Successes == measured && x.Result.Errors == 0)
                .OrderBy(x => x.Result.AvgRttMs)
                .ThenBy(x => x.Profile)
                .ToList();

            bool baselineErrorFree = baseline.Successes == measured && baseline.Errors == 0;
            if (errorFreeOptimized.Count > 0)
            {
                double fastest = errorFreeOptimized[0].Result.AvgRttMs;
                var best = errorFreeOptimized.Where(x => x.Result.AvgRttMs - fastest <= TimingPolicy.TieMargin(fastest))
                    .OrderBy(x => x.Profile).First();
                bool materiallyFaster = baseline.Successes == 0 || TimingPolicy.MateriallyFaster(best.Result.AvgRttMs, baseline.AvgRttMs);
                bool moreReliable = !baselineErrorFree;

                if (materiallyFaster || moreReliable)
                {
                    selectedProfile = best.Profile;
                    useResponseCount = optimizedCommand != "010C";
                    summaryLines.Add("");
                    summaryLines.Add($"Selected for polling: {GetTimingProfileLabel(selectedProfile)} with expected-response optimization enabled.");
                }
                else
                {
                    selectedProfile = 0;
                    useResponseCount = false;
                    summaryLines.Add("");
                    summaryLines.Add("Response-count optimization was not materially faster than normal 010C; selected Conservative timing with expected-response count disabled.");
                }
            }
            else
            {
                selectedProfile = 0;
                useResponseCount = false;
                summaryLines.Add("");
                summaryLines.Add("No error-free optimized profile was found; selected Conservative timing with expected-response count disabled.");
            }
        }
        catch (Exception ex)
        {
            summaryLines.Add("Benchmark aborted: " + ex.Message);
            SetError("Benchmark: " + ex.Message);
            selectedProfile = 0;
            useResponseCount = false;
        }
        finally
        {
            try
            {
                await ApplyTimingProfileAsync(selectedProfile, true);
                TimingCombo.SelectedIndex = selectedProfile;
                UseResponseCountCheck.IsChecked = useResponseCount;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] BENCHMARK SELECTION timing={GetTimingProfileLabel(selectedProfile)} response-count={(useResponseCount ? "on" : "off")}");

                // Once the response framing/timing choice is known, find the shortest
                // conservative ISO 9141 quiet delay.  We intentionally do not auto-select
                // values below 60 ms; 0/25/50 ms remain available manually for experiments.
                string delayCommand = _responders.Command("010C", useResponseCount);
                var delayResults = new List<DelayTestResult>();
                summaryLines.Add("");
                summaryLines.Add($"Safe quiet-delay benchmark using {delayCommand} (12 samples each)");
                foreach (int delay in new[] { 200, 100, 75, 60 })
                {
                    AppendRaw($"\r\n=== SAFE DELAY BENCHMARK {delay} ms ===\r\n");
                    BenchmarkResult delayResult = await RunDelayTestRequestsAsync(delayCommand, delay, 12);
                    delayResults.Add(new DelayTestResult(delay, delayResult));
                    string delayLine = delayResult.Successes > 0
                        ? $"{delay} ms: {delayResult.Successes}/12 OK, {delayResult.Errors} errors, avg RTT {delayResult.AvgRttMs:0} ms"
                        : $"{delay} ms: 0/12 OK, {delayResult.Errors} errors";
                    summaryLines.Add(delayLine);
                    AppendRaw(delayLine + "\r\n");
                }
                var safeDelays = delayResults.Where(r => r.Result.Successes == 12 && r.Result.Errors == 0).OrderBy(r => r.DelayMs).ToList();
                int selectedDelay = safeDelays.Count > 0 ? safeDelays[0].DelayMs : originalDelay;
                SelectDelay(selectedDelay);
                summaryLines.Add(safeDelays.Count > 0
                    ? $"Selected safe quiet delay: {selectedDelay} ms."
                    : $"No error-free safe delay was found; restored {originalDelay} ms.");
                _sessionLog.WriteLine($"[{DateTime.Now:O}] BENCHMARK DELAY SELECTION delay={selectedDelay}ms candidates=200,100,75,60");
            }
            catch (Exception ex)
            {
                summaryLines.Add("Could not apply safe benchmark result: " + ex.Message);
                SetError("Timing apply failed: " + ex.Message);
            }

            SetTimingControlsEnabled(_elm.IsOpen);
            UseResponseCountCheck.IsEnabled = _elm.IsOpen;
            PollingButton.IsEnabled = _elm.IsOpen;
            ConnectButton.IsEnabled = true;

            if (restartPolling && _elm.IsOpen)
                StartPolling();
        }

        string summary = string.Join(Environment.NewLine, summaryLines);
        AppendRaw("\r\n" + summary + "\r\n\r\n");
        SaveCurrentAdapterProfile();
        SaveVehicleCalibration();
        MessageBox.Show(StartupOwner ?? this, summary, "ELM safe benchmark", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void StabilityTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_elm.IsOpen)
            return;

        bool restartPolling = _pollCts is not null;
        if (restartPolling)
            StopPolling();

        SetTimingControlsEnabled(false);
        PollingButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        SetError("");

        int originalDelay = GetInterRequestDelayMs();
        string command = _responders.Command("010C", UseResponseCountCheck.IsChecked == true);
        int[] delays = { 60, 75, 100, 200 };
        var results = new List<DelayTestResult>();
        var summary = new List<string>
        {
            $"Inter-request delay stability test using {command}",
            $"Timing: {GetTimingProfileLabel(_activeTimingProfile)}",
            "Each delay: 12 measured RPM requests",
            ""
        };

        try
        {
            foreach (int delay in delays)
            {
                AppendRaw($"\r\n=== DELAY TEST {delay} ms ===\r\n");
                BenchmarkResult result = await RunDelayTestRequestsAsync(command, delay, 12);
                results.Add(new DelayTestResult(delay, result));
                string line = result.Successes > 0
                    ? $"{delay} ms: {result.Successes}/12 OK, {result.Errors} errors, avg RTT {result.AvgRttMs:0} ms, first {result.AvgFirstByteMs:0} ms, tail {result.AvgTailMs:0} ms"
                    : $"{delay} ms: 0/12 OK, {result.Errors} errors";
                summary.Add(line);
                AppendRaw(line + "\r\n");
            }

            List<DelayTestResult> errorFree = results
                .Where(r => r.Result.Successes == 12 && r.Result.Errors == 0)
                .OrderBy(r => r.DelayMs)
                .ToList();

            summary.Add("");
            if (errorFree.Count > 0)
            {
                int selectedDelay = errorFree[0].DelayMs;
                SelectDelay(selectedDelay);
                summary.Add($"Selected quiet delay: {selectedDelay} ms (smallest safe error-free setting).");
            }
            else
            {
                SelectDelay(originalDelay);
                summary.Add($"No error-free delay was found; left quiet delay at {originalDelay} ms.");
            }
        }
        catch (Exception ex)
        {
            SelectDelay(originalDelay);
            summary.Add("Delay test aborted: " + ex.Message);
            SetError("Delay test: " + ex.Message);
        }
        finally
        {
            SetTimingControlsEnabled(_elm.IsOpen);
            PollingButton.IsEnabled = _elm.IsOpen;
            ConnectButton.IsEnabled = true;
            SaveCurrentAdapterProfile();
            if (results.Any(r => r.Result.Successes == 12 && r.Result.Errors == 0)) SaveVehicleCalibration();
            if (restartPolling && _elm.IsOpen)
                StartPolling();
        }

        string report = string.Join(Environment.NewLine, summary);
        AppendRaw("\r\n" + report + "\r\n\r\n");
        MessageBox.Show(StartupOwner ?? this, report, "Inter-request delay test", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task<BenchmarkResult> RunDelayTestRequestsAsync(string command, int delayMs, int measured)
    {
        int successes = 0;
        int errors = 0;
        double totalRtt = 0;
        double totalFirst = 0;
        double totalTail = 0;

        for (int sample = 0; sample < measured; sample++)
        {
            try
            {
                
        CommandResult result = await SendCommandTimedAsync(command, 3000, false);
                bool valid = ValidRpmResponse(result.Response);

                if (valid)
                {
                    successes++;
                    totalRtt += result.RttMs;
                    totalFirst += result.FirstByteMs;
                    totalTail += result.TailMs;
                }
                else
                {
                    errors++;
                }
            }
            catch
            {
                errors++;
            }

            if (delayMs > 0 && sample < measured - 1)
                await Task.Delay(delayMs);
        }

        return successes > 0
            ? new BenchmarkResult(successes, errors, totalRtt / successes, totalFirst / successes, totalTail / successes)
            : new BenchmarkResult(0, errors, 0, 0, 0);
    }

    private static string GetTimingProfileLabel(int profileIndex) => profileIndex switch
    {
        1 => "Fast adaptive (~200 ms max)",
        2 => "Fast + short timeout (~80 ms)",
        _ => "Conservative (~200 ms)"
    };

    private void SetTimingControlsEnabled(bool enabled)
    {
        TimingCombo.IsEnabled = enabled;
        ApplyTimingButton.IsEnabled = enabled;
        BenchmarkButton.IsEnabled = enabled;
        StabilityTestButton.IsEnabled = enabled;
    }

    private async void SendRaw_Click(object sender, RoutedEventArgs e) => await SendRawCommandAsync();

    private async void RawCommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendRawCommandAsync();
        }
    }

    private async Task SendRawCommandAsync()
    {
        if (_ssmMode)
        {
            SetError("SSM uses structured native K-Line requests; the generic raw/ELM command console is disabled in SSM mode.");
            return;
        }
        if (_monitorCts is not null) { _monitorCts.Cancel(); return; }
        string command = RawCommandBox.Text.Trim();
        if (command.Length == 0) return;
        if (_td5Mode)
        {
            SetError("Raw console AT/OBD commands are disabled for the native TD5 K-Line profile. Use the Data, Trend, Dashboard and DTC tabs.");
            return;
        }
        if (!_elm.IsOpen)
        {
            SetError("Connect to an ELM327 first.");
            return;
        }

        RawCommandBox.Clear();
        string normalized = command.Replace(" ", "").ToUpperInvariant();
        if (normalized == "ATMA") { await StartMonitorAsync(); return; }
        string[] disruptive = { "ATZ", "ATD", "ATWS", "ATPC", "ATSP", "ATTP", "ATAL", "ATCAF", "ATH", "ATE" };
        bool isDisruptive = disruptive.Any(x => normalized.StartsWith(x, StringComparison.Ordinal));
        if (isDisruptive && _settings.ConfirmDisruptiveCommands)
        {
            MessageBoxResult choice = MessageBox.Show(StartupOwner ?? this, $"{command} can reset or change adapter state, interrupt polling, or invalidate the active protocol. Continue?", "Potentially disruptive AT command", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) { AppendRaw($"CANCELLED >> {command}\r\n"); return; }
        }
        bool resumePolling = _pollCts is not null;
        if (isDisruptive && resumePolling) StopPolling();
        try
        {
            await SendCommandAsync(command, 7000, true);
            SetError("");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            AppendRaw("ERROR: " + ex.Message + "\r\n");
        }
        finally { if (isDisruptive && resumePolling && _elm.IsOpen) StartPolling(); }
    }

    private async Task StartMonitorAsync()
    {
        bool resumePolling = _pollCts is not null; if (resumePolling) StopPolling();
        _monitorCts = new CancellationTokenSource(); RawSendButton.Content = "Stop Monitor"; RawCommandBox.IsEnabled = false;
        ConnectButton.IsEnabled = false; PollingButton.IsEnabled = false;
        AppendRaw(">> ATMA\r\n[monitor streaming; press Stop Monitor to exit]\r\n");
        _sessionLog.WriteLine($"[{DateTime.Now:O}] MONITOR START source=console vehicle={ShortVehicleId()}");
        await _commandGate.WaitAsync();
        try { await Task.Run(() => _elm.MonitorAll(chunk => Dispatcher.BeginInvoke(() => AppendRaw(chunk)), _monitorCts.Token)); }
        catch (Exception ex) { SetError(ex.Message); }
        finally { _commandGate.Release(); _sessionLog.WriteLine($"[{DateTime.Now:O}] MONITOR STOP vehicle={ShortVehicleId()}"); _monitorCts.Dispose(); _monitorCts = null; RawSendButton.Content = "Send"; RawCommandBox.IsEnabled = true; ConnectButton.IsEnabled = true; PollingButton.IsEnabled = _elm.IsOpen; if (resumePolling && _elm.IsOpen) StartPolling(); }
    }

    private static void RefreshGaugeSkin(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is OBDWorkbench.Controls.DashboardGaugeControl gauge) gauge.InvalidateVisual();
            RefreshGaugeSkin(child);
        }
    }

    private void Preferences_Click(object sender, RoutedEventArgs e)
    {
        string unitSettingsBefore = UnitSettingsSignature();
        var dialog = new PreferencesWindow(_settings, _definitions) { Owner = this };
        dialog.GpsRequested += () => OpenGpsConfiguration(dialog);
        ThemeService.Apply(dialog, _settings);
        if (dialog.ShowDialog() == true)
        {
            SaveSettingsAndGlobalConfiguration();
            ApplyPollingPriorityOverrides();
            _scheduler.Reset();
            PidGrid.Items.Refresh();
            ApplyLiveDataView();
            ThemeService.Apply(this, _settings);
            ApplyLcarsLayout();
            TrendChart.InvalidateMeasure();
            TrendChart.InvalidateVisual();
            RefreshGaugeSkin(DashboardGaugeItems);
            if (!_isReplayMode) RefreshDisplayedUnits();
            if (!_isReplayMode && !string.Equals(unitSettingsBefore, UnitSettingsSignature(), StringComparison.Ordinal))
            {
                RefreshTrendUnitsAndClearHistory();
                RefreshDashboardUnitsAndRanges();
            }
            string priorities = _settings.PollPriorityOverrides.Count == 0 ? "database-defaults"
                : string.Join(';', _settings.PollPriorityOverrides.OrderBy(x => x.Key).Select(x => x.Key + '=' + x.Value));
            _sessionLog.WriteLine($"[{DateTime.Now:O}] PREFERENCES units={_settings.UnitSystem} unitOverrides={string.Join(';', _settings.UnitOverrides.Select(x => x.Key + '=' + x.Value))} pollPriorities={priorities}");
            SaveGlobalConfiguration();
        }
    }

    private void RefreshDisplayedUnits()
    {
        foreach (PidLiveValue item in LiveValues)
        {
            string target = UnitConverter.TargetUnit(item.Definition.Units, _settings);
            item.Units = target;
            if (item.CanTrend)
            {
                item.TrendUnit = target;
                // The cached numeric value was expressed in the old display units. Wait for
                // the next ECU sample rather than plotting a mixed-unit point.
                item.LatestNumericValue = null;
            }
        }
    }

    private void DetailedPollMetrics_Changed(object sender, RoutedEventArgs e)
    {
        bool show = DetailedPollMetricsCheck.IsChecked == true;
        foreach (PidLiveValue item in LiveValues) item.ShowDetailedMetrics = show;
    }

    private void CollapseConnection_Click(object sender, RoutedEventArgs e) => SetConnectionPanelCollapsed(!_connectionPanelCollapsed);
    private void SetConnectionPanelCollapsed(bool collapsed)
    {
        _connectionPanelCollapsed = collapsed; ConnectionPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; ConnectionPrimaryControls.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; ConnectionOptionsRow.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; ConnectionHealthRow.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; ConnectionMetricsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseConnectionButton.Content = collapsed ? "▼" : "▲"; CollapseConnectionButton.ToolTip = collapsed ? "Expand connection controls" : "Collapse connection controls";
    }

    private void UpdateCompactStatus()
    {
        if (VehicleText is null) return;
        string? vehicle = _currentVehicleProfile?.DisplayName;
        VehicleText.Text = !string.IsNullOrWhiteSpace(vehicle)
            ? $"{vehicle} • {(_currentVehicleProfile!.CalibrationAccepted ? "calibrated" : "conservative")}" 
            : (_currentVehicleSignature.Length > 0 ? "Unknown / conservative" : "--");
    }

    private string CanonicalProtocolNumber(string protocol)
    {
        if (!string.IsNullOrWhiteSpace(_confirmedProtocol)) return _confirmedProtocol.Trim().ToUpperInvariant();
        string upper = (protocol ?? "").ToUpperInvariant();
        if (upper.Contains("ISO 9141")) return "3";
        if (upper.Contains("ISO 14230") && upper.Contains("FAST")) return "5";
        if (upper.Contains("ISO 14230")) return "4";
        return upper.Trim();
    }

    private static string VehicleHash(string protocolEvidence, string pidEvidence, string vin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{protocolEvidence}|{pidEvidence}|{vin}")))[..16];

    private void DetectVehicle(string pid00Response, string protocol, string vinResponse)
    {
        string vin = MultiEcuParser.DecodeMode09Text(vinResponse, 0x02);
        string pidEvidence = SupportedPidEvidence(pid00Response);
        string protocolNumber = CanonicalProtocolNumber(protocol);
        string stableSignature = VehicleHash("P" + protocolNumber, pidEvidence, vin);
        HandleTrendVehicleDetected(stableSignature);
        HandleDashboardVehicleDetected(stableSignature);
        _currentVehicleSignature = stableSignature;

        VehicleProfile? profile = null;
        string? oldKey = null;
        if (_settings.VehicleProfiles.TryGetValue(stableSignature, out VehicleProfile? exact))
            profile = exact;

        // New-format profiles can match across Wi-Fi, USB and Bluetooth adapters.
        if (profile is null)
        {
            var match = _settings.VehicleProfiles.FirstOrDefault(p =>
                (vin.Length == 17 && p.Value.Vin.Equals(vin, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Value.ProtocolNumber) &&
                 p.Value.ProtocolNumber.Equals(protocolNumber, StringComparison.OrdinalIgnoreCase) &&
                 p.Value.SupportedPidEvidence.Equals(pidEvidence, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(match.Key)) { oldKey = match.Key; profile = match.Value; }
        }

        // v0.3.0.2 used raw ATDP text in the signature. Recognize the common ISO 9141
        // spellings emitted by the Wi-Fi and Bluetooth clones and migrate transparently.
        if (profile is null && protocolNumber == "3")
        {
            foreach (string legacyProtocol in new[] { protocol, "AUTO, ISO 9141-2", "AUTO,", "ISO 9141-2" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string legacy = VehicleHash(legacyProtocol, pidEvidence, vin);
                if (_settings.VehicleProfiles.TryGetValue(legacy, out VehicleProfile? migrated))
                {
                    oldKey = legacy; profile = migrated; break;
                }
            }
        }

        bool known = profile is not null;
        if (!known)
        {
            profile = new VehicleProfile
            {
                Signature = stableSignature, Vin = vin, Protocol = protocol, ProtocolNumber = protocolNumber,
                SupportedPidEvidence = pidEvidence, PreferredProtocolNumber = _confirmedProtocol,
                FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow,
                DisplayName = vin.Length == 17 ? vin : $"Vehicle {stableSignature[..6]}",
                CalibrationAccepted = false, TimingProfile = 0, InterRequestDelayMs = 200,
                UseResponseCount = _responders.ResponseCount("010C") > 0
            };
            _settings.VehicleProfiles[stableSignature] = profile;
            TimingCombo.SelectedIndex = 0; SelectDelay(200); UseResponseCountCheck.IsChecked = profile.UseResponseCount;
            AppendRaw($"VEHICLE: New/unknown ({profile.DisplayName}). Adapter-independent identity created; live settings will be auto-tuned.\r\n");
            _sessionLog.WriteLine($"[{DateTime.Now:O}] VEHICLE NEW signature={stableSignature} vin={vin} protocol={protocolNumber} identity=stable");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(oldKey) && !oldKey.Equals(stableSignature, StringComparison.OrdinalIgnoreCase))
            {
                _settings.VehicleProfiles.Remove(oldKey);
                _settings.VehicleProfiles[stableSignature] = profile!;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] VEHICLE PROFILE MIGRATED old={oldKey} new={stableSignature}");
            }
            profile!.Signature = stableSignature;
            profile.LastSeenUtc = DateTime.UtcNow;
            profile.Protocol = protocol;
            profile.ProtocolNumber = protocolNumber;
            profile.SupportedPidEvidence = pidEvidence;
            if (_confirmedProtocol.Length > 0) profile.PreferredProtocolNumber = _confirmedProtocol;
            TimingCombo.SelectedIndex = Math.Clamp(profile.TimingProfile, 0, 2);
            SelectDelay(profile.InterRequestDelayMs > 0 ? profile.InterRequestDelayMs : 200);
            UseResponseCountCheck.IsChecked = profile.UseResponseCount || _responders.ResponseCount("010C") > 0;
            AppendRaw($"VEHICLE: Known ({profile.DisplayName}); adapter-independent profile loaded.\r\n");
            _sessionLog.WriteLine($"[{DateTime.Now:O}] VEHICLE KNOWN signature={stableSignature} vin={vin} calibrated={profile.CalibrationAccepted} protocol={protocolNumber}");
        }

        profile!.ProtocolNumber = protocolNumber;
        profile.SupportedPidEvidence = pidEvidence;
        _currentVehicleProfile = profile;
        SaveSettingsAndGlobalConfiguration();
        UpdateCompactStatus();
    }

    private async void RefreshDtcs_Click(object sender, RoutedEventArgs e)
    {
        if (!ConnectionIsOpen) { SetError("Connect to an ECU before reading DTCs."); return; }
        try
        {
            if (_ssmMode) await RefreshSsmDtcsAsync(true);
            else if (_td5Mode) await RefreshTd5DtcsAsync(true);
            else if (_gemsMode) await RefreshGemsDtcsAsync(true);
            else await RefreshDtcsAsync(true);
            SetError("");
        }
        catch (Exception ex) { SetError("DTC read failed: " + ex.Message); }
    }

    private async void EraseDtcs_Click(object sender, RoutedEventArgs e)
    {
        if (_ssmMode)
        {
            await ClearSsmFaultsFromWorkbenchAsync();
            return;
        }
        if (_td5Mode)
        {
            await ClearTd5FaultsFromWorkbenchAsync();
            return;
        }
        if (_gemsMode)
        {
            SetError("GEMS DTC clearing is intentionally disabled in this public-test preview. Use the OBDII tile for standard Mode 04 only after validating the vehicle/adapter combination.");
            return;
        }
        if (!_elm.IsOpen)
        {
            SetError("Connect to an ECU before clearing DTCs.");
            return;
        }
        if (_isRecovering || _linkLost || _consecutiveFailures > 0 || _elmNeedsResync)
        {
            string reason = "DTC clear blocked because the diagnostic link is not currently stable. Wait for normal live responses or reconnect the adapter before sending Mode 04.";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR BLOCKED unstable-link recovering={_isRecovering} linkLost={_linkLost} consecutiveFailures={_consecutiveFailures} needsResync={_elmNeedsResync}");
            SetError(reason);
            DtcStatusText.Text = reason;
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(StartupOwner ?? this,
            "Send OBD-II Mode 04 to clear diagnostic trouble codes?\n\n" +
            "This can also clear freeze-frame data and reset emissions readiness monitors to incomplete. " +
            "Any fault that is still present may set its DTC again immediately.",
            "Erase DTCs", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        bool resumePolling = _pollCts is not null;
        if (resumePolling)
            StopPolling("DTC_CLEAR");

        var beforeDtcs = DetectedDtcs.Select(d => new { d.Source, d.Code }).ToList();
        string[] targetSources = beforeDtcs.Select(d => d.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source)
            .ToArray();

        try
        {
            DtcStatusText.Text = "Sending Mode 04 clear-DTC request...";
            _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR REQUEST mode=04 vehicle={ShortVehicleId()} targets={(targetSources.Length == 0 ? "unknown" : string.Join(",", targetSources))}");
            _logger.LogEvent("DTC_CLEAR_REQUEST", ShortVehicleId(), message: $"OBD-II Mode 04 requested by user; targets={(targetSources.Length == 0 ? "unknown" : string.Join(",", targetSources))}.");

            string[] mode04ExpectedSources = targetSources.Length > 0 ? targetSources : KnownModuleSources();
            string response = await SendCommandAsync("04", 5000, true, default, GetPostInteractiveQuietMs(), mode04ExpectedSources, 0x44, DtcResponderWaitMaxMs);
            string[] ackSources = MultiEcuParser.ParseMessages(response)
                .Where(m => m.Payload.Length >= 1 && m.Payload[0] == 0x44)
                .Select(m => m.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source)
                .ToArray();
            bool anyAck = ackSources.Length > 0;
            string[] unackedTargets = targetSources
                .Except(ackSources, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] ackedTargets = targetSources
                .Intersect(ackSources, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (anyAck)
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR ACK response=44 sources={string.Join(",", ackSources)} targetsAcked={(ackedTargets.Length == 0 ? "none" : string.Join(",", ackedTargets))}");
                _logger.LogEvent("DTC_CLEAR_ACK", ShortVehicleId(), source: string.Join(",", ackSources), message: "Mode 04 acknowledgement observed.");
            }
            else
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR NO_ACK raw={response.Replace("\r", " ").Replace("\n", " ").Trim()}");
                _logger.LogEvent("DTC_CLEAR_NO_ACK", ShortVehicleId(), message: "Mode 04 completed without a recognized 0x44 acknowledgement.");
            }

            if (unackedTargets.Length > 0)
            {
                _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR PARTIAL ackSources={(ackSources.Length == 0 ? "none" : string.Join(",", ackSources))} noAckFrom={string.Join(",", unackedTargets)}");
                _logger.LogEvent("DTC_CLEAR_PARTIAL", ShortVehicleId(), source: string.Join(",", unackedTargets), message: $"No Mode 04 acknowledgement observed from DTC-owning module(s): {string.Join(",", unackedTargets)}.");
            }

            // Reset First Seen only for modules that actually acknowledged the clear.
            // If an unacknowledged module's code disappears on the verification read,
            // its timestamp is removed afterward based on observed absence instead.
            foreach (string source in ackedTargets)
            {
                foreach (string key in _dtcFirstRead.Keys.Where(k => k.StartsWith(source + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    _dtcFirstRead.Remove(key);
            }

            DtcStatusText.Text = anyAck
                ? $"Mode 04 acknowledgement from {string.Join(", ", ackSources)}. Re-reading confirmed DTCs..."
                : "No recognized Mode 04 acknowledgement. Re-reading confirmed DTCs...";

            await Task.Delay(750);
            await RefreshDtcsAsync(true);

            var afterKeys = DetectedDtcs.Select(d => $"{d.Source}|{d.Code}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in beforeDtcs)
            {
                string key = $"{old.Source}|{old.Code}";
                if (!afterKeys.Contains(key))
                    _dtcFirstRead.Remove(key);
            }

            int remaining = DetectedDtcs.Count;
            if (targetSources.Length > 0 && unackedTargets.Length > 0)
            {
                string message = $"Partial clear: acknowledgement from {(ackSources.Length == 0 ? "none" : string.Join(", ", ackSources))}; no acknowledgement from {string.Join(", ", unackedTargets)}. {remaining} confirmed DTC(s) remain after verification.";
                DtcStatusText.Text = message;
                SetError(message);
            }
            else if (anyAck)
            {
                DtcStatusText.Text = $"Mode 04 acknowledged by {string.Join(", ", ackSources)}. {remaining} confirmed DTC(s) remain after verification.";
                SetError("");
            }
            else
            {
                string message = $"Mode 04 returned no recognized acknowledgement. {remaining} confirmed DTC(s) remain after verification.";
                DtcStatusText.Text = message;
                SetError(message);
            }
        }
        catch (Exception ex)
        {
            _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC CLEAR FAILED {ex.Message}");
            _logger.LogEvent("DTC_CLEAR_FAILED", ShortVehicleId(), message: ex.Message);
            SetError("DTC clear failed: " + ex.Message);
            DtcStatusText.Text = "DTC clear request failed. Existing DTC list has been retained where possible.";
        }
        finally
        {
            _lastDtcPollUtc = DateTime.UtcNow;
            if (resumePolling && _elm.IsOpen && _pollCts is null)
                StartPolling();
        }
    }

    private async Task RefreshDtcsAsync(bool showTraffic, CancellationToken token = default)
    {
        string[] expectedSources = _mode03Responders.Count > 0
            ? _mode03Responders.OrderBy(source => source).ToArray()
            : KnownModuleSources();
        string response = await SendCommandAsync("03", 5000, showTraffic, token, GetPostInteractiveQuietMs(), expectedSources, 0x43, DtcResponderWaitMaxMs);
        DateTime readAt = DateTime.Now;
        var decoded = MultiEcuParser.DecodeMode03(response);
        var rows = new List<DiagnosticTroubleCode>();

        // Mode 03 gives us an authoritative confirmed-code count for every module that
        // answered, even when its response is all zeroes. Preserve any known MIL state
        // from PID 01 and use the count as the next change-detection baseline.
        var mode03Sources = MultiEcuParser.ParseMessages(response)
            .Where(m => m.Payload.Length > 0 && m.Payload[0] == 0x43)
            .Select(m => m.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mode03Sources.Length > 0)
        {
            _mode03Responders.Clear();
            foreach (string source in mode03Sources) _mode03Responders.Add(source);
        }
        foreach (string source in mode03Sources)
        {
            int confirmedCount = decoded.Count(d => d.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
            _monitorStatusBySource.TryGetValue(source, out MonitorStatusSnapshot previous);
            _monitorStatusBySource[source] = new MonitorStatusSnapshot(confirmedCount, previous.MilOn);
        }

        foreach (DiagnosticTroubleCode dtc in decoded.OrderBy(d => d.Source).ThenBy(d => d.Code))
        {
            string key = $"{dtc.Source}|{dtc.Code}";
            bool isNew = !_dtcFirstRead.TryGetValue(key, out DateTime firstRead);
            if (isNew)
            {
                firstRead = readAt;
                _dtcFirstRead[key] = firstRead;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] DTC FIRST_SEEN source={dtc.Source} module={dtc.Module} code={dtc.Code}");
                _logger.LogEvent("DTC_FIRST_SEEN", ShortVehicleId(), source: dtc.Source, message: dtc.Code);
            }
            dtc.Status = "Active / confirmed";
            dtc.FirstRead = firstRead;
            dtc.LastRead = readAt;
            if (_dtcDatabase is not null)
            {
                string manufacturer = _currentVehicleProfile?.ManufacturerFamily ?? "";
                var definition = _dtcDatabase.Lookup(dtc.Code, manufacturer, _currentVehicleSignature);
                dtc.Description = definition.Description;
                dtc.DefinitionSource = definition.Source;
            }
            rows.Add(dtc);
        }

        DetectedDtcs.Clear();
        foreach (DiagnosticTroubleCode dtc in rows.OrderByDescending(d => d.FirstRead).ThenBy(d => d.Code)) DetectedDtcs.Add(dtc);
        _lastDtcPollUtc = DateTime.UtcNow;
        if (DtcStatusText is not null)
            DtcStatusText.Text = rows.Count == 0
                ? $"No confirmed Mode 03 DTCs read at {readAt:HH:mm:ss}."
                : $"{rows.Count} confirmed DTC(s). Last refresh {readAt:HH:mm:ss}; timestamp records first observation this connection.";
    }

    private async void Discovery_Click(object sender, RoutedEventArgs e)
    {
        if (_ssmMode)
        {
            DiscoverySummaryText.Text = $"SSM: {_ssm.Supported.Count} advertised parameters; A8 batch limit {_ssm.BatchLimit}; fingerprint {_ecuFingerprint}.";
            return;
        }
        if (!ConnectionIsOpen) { SetError("Connect to an ECU before running discovery."); return; }
        bool resume = _pollCts is not null; if (resume) StopPolling();
        if (_td5Mode)
        {
            try { DiscoverySummaryText.Text = "TD5 native discovery in progress…"; await RunTd5DiscoveryAsync(); SetError(""); }
            catch (Exception ex) { SetError("TD5 discovery failed: " + ex.Message); }
            finally { if (resume && ConnectionIsOpen) StartPolling(); }
            return;
        }
        try
        {
            DiscoverySummaryText.Text = "Neutral discovery in progress…";
            string pids = await SendCommandAsync("0100", 7000, true);
            string protocol = CleanElmText(await SendCommandAsync("ATDP", 2500, true), "ATDP");
            string vin = await SendCommandAsync("0902", 6000, true);
            await CompleteNeutralDiscoveryAsync(pids, protocol, vin, DiscoveryStrategyCombo.SelectedIndex == 1);
            await RefreshDtcsAsync(true);
        }
        catch (Exception ex) { SetError("Discovery failed: " + ex.Message); DiscoverySummaryText.Text = "Discovery incomplete."; }
        finally { if (resume && _elm.IsOpen) StartPolling(); }
    }

    private async Task CompleteNeutralDiscoveryAsync(string pid00, string protocol, string vinResponse, bool comprehensive)
    {
        // Discovery remains generic. Manufacturer knowledge is selected only after responses are collected.
        var result = new DiscoveryResult { Protocol = protocol, Vin = MultiEcuParser.DecodeMode09Text(vinResponse, 0x02) };
        _responders.Clear();
        _countedPolls.Clear();
        _responders.AddBitmap(pid00);
        for (int block = 0x20; block <= 0xE0 && _responders.Supported.Values.Any(p => p.Contains((byte)block)); block += 0x20)
        {
            try { _responders.AddBitmap(await SendCommandAsync($"01{block:X2}", 6000, true), (byte)block); }
            catch (Exception ex) { _sessionLog.WriteLine($"Bitmap {block:X2} unavailable: {ex.Message}"); break; }
        }
        foreach (var pair in _responders.Supported)
            result.Modules.Add(new DetectedModule { Source = pair.Key, Name = MultiEcuParser.ModuleName(pair.Key), Protocol = protocol,
                SupportedPids = string.Join(" ", pair.Value.OrderBy(p => p).Select(p => p.ToString("X2"))) });
        LoadPidDatabase();

        string calibrationResponse = "", cvnResponse = "", dtcResponse = "";
        if (comprehensive)
        {
            try { calibrationResponse = await SendCommandAsync("0904", 6000, true); } catch { }
            try { cvnResponse = await SendCommandAsync("0906", 6000, true); } catch { }
            try { dtcResponse = await SendCommandAsync("03", 6000, true, default, GetPostInteractiveQuietMs(), KnownModuleSources(), 0x43, DtcResponderWaitMaxMs); } catch { }
        }
        var calibrationIds = MultiEcuParser.DecodeMode09TextBySource(calibrationResponse, 0x04);
        var cvns = MultiEcuParser.DecodeCvnBySource(cvnResponse);
        foreach (string source in calibrationIds.Keys.Union(cvns.Keys).Except(result.Modules.Select(m => m.Source)).ToList())
            result.Modules.Add(new DetectedModule { Source = source, Name = MultiEcuParser.ModuleName(source), Protocol = protocol });
        foreach (DetectedModule module in result.Modules)
        {
            module.CalibrationId = calibrationIds.GetValueOrDefault(module.Source, "");
            module.Cvn = cvns.GetValueOrDefault(module.Source, "");
        }
        string calibration = string.Join("; ", calibrationIds.Select(p => $"{p.Key}: {p.Value}"));
        string cvn = string.Join("; ", cvns.Select(p => $"{p.Key}: {p.Value}"));
        result.Dtcs.AddRange(MultiEcuParser.DecodeMode03(dtcResponse));

        VehicleStrategy? knowledge = VehicleKnowledge.Identify(result.Vin, result.Modules.Select(m => m.Source));
        result.ManufacturerFamily = knowledge?.Family ?? "Generic/unknown";
        if (knowledge is not null)
            foreach (DetectedModule module in result.Modules) if (knowledge.ModuleNames.TryGetValue(module.Source, out string? name)) { module.Name = name; module.Notes = knowledge.Notes; }

        // Fingerprint includes every responding source and calibration evidence, making new ECU software detectable on a known vehicle.
        string calibrationEvidence = string.Join("|", result.Modules.OrderBy(m => m.Source).Select(m => $"{m.Source}:{m.SupportedPids}:{m.CalibrationId}:{m.Cvn}"));
        result.CalibrationSignature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"P{CanonicalProtocolNumber(protocol)}|{result.Vin}|{calibrationEvidence}")))[..16];
        _lastDiscovery = result;
        DetectedModules.Clear(); foreach (DetectedModule module in result.Modules) DetectedModules.Add(module);
        DiscoverySummaryText.Text = $"{result.ManufacturerFamily} • {result.Modules.Count} module(s) • {(comprehensive ? "comprehensive" : "fast")} discovery";
        CalibrationText.Text = $"VIN: {(result.Vin.Length > 0 ? result.Vin : "not reported")}   Calibration: {(calibration.Length > 0 ? calibration : "not reported")}   CVN: {(cvn.Length > 0 ? cvn : "not reported")}   Fingerprint: {result.CalibrationSignature}";
        _sessionLog.WriteLine($"[{DateTime.Now:O}] DISCOVERY strategy={(comprehensive ? "comprehensive" : "fast")} family={result.ManufacturerFamily} vin={result.Vin} modules={string.Join(',', result.Modules.Select(m => m.Source))} calibration={calibration} cvn={cvn} fingerprint={result.CalibrationSignature}");

        DetectVehicle(pid00, protocol, vinResponse);
        if (_currentVehicleProfile is not null)
        {
            bool changed = comprehensive && _currentVehicleProfile.CalibrationSignature.Length > 0 && !_currentVehicleProfile.CalibrationSignature.Equals(result.CalibrationSignature, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                _currentVehicleProfile.CalibrationAccepted = false;
                AppendRaw($"CALIBRATION CHANGE: known vehicle reported a new ECU/module fingerprint. Conservative mode restored.\r\n");
                TimingCombo.SelectedIndex = 0; SelectDelay(200); UseResponseCountCheck.IsChecked = false;
                _sessionLog.WriteLine($"[{DateTime.Now:O}] CALIBRATION NEW vehicle={_currentVehicleSignature} old={_currentVehicleProfile.CalibrationSignature} new={result.CalibrationSignature}");
            }
            if (comprehensive) _currentVehicleProfile.CalibrationSignature = result.CalibrationSignature;
            _currentVehicleProfile.ManufacturerFamily = result.ManufacturerFamily;
            SaveSettingsAndGlobalConfiguration(); UpdateCompactStatus(); RefreshModulePresentation();
        }
        if (knowledge == VehicleKnowledge.Jeep && !(_currentVehicleProfile?.CalibrationAccepted ?? false)) UseResponseCountCheck.IsChecked = false;
        await ReadDiscoverySnapshotsAsync();
    }

    private async Task ReadDiscoverySnapshotsAsync()
    {
        foreach (var definition in _definitions.Where(d => d.Enabled && d.DiscoveryOnly))
        {
            try
            {
                var result = await SendCommandTimedAsync(definition.Request, 3000, true);
                var frames = MultiEcuParser.ParseMessages(result.Response)
                    .Where(m => m.Payload.Length >= 2 && m.Payload[0] == 0x41 && m.Payload[1].ToString("X2") == definition.PID).ToList();
                var validSources = new HashSet<string>();
                foreach (var frame in frames)
                {
                    string payload = string.Join(" ", frame.Payload.Select(b => b.ToString("X2")));
                    if (ObdParser.TryDecode(definition, payload, out double value, out _))
                    {
                        validSources.Add(frame.Source);
                        UpdateLiveValue(definition, value, result.Response, result.RttMs, result.FirstByteMs, result.TailMs, frame.Source);
                        _logger.LogEvent("DISCOVERY_SNAPSHOT", ShortVehicleId(), definition.Request, frame.Source, result.Response,
                            StandardPidDisplay.Describe(definition, value) ?? value.ToString(CultureInfo.InvariantCulture));
                    }
                }
                foreach (string source in _responders.Sources(definition.Request).Except(validSources))
                    RecordPidError(definition, source, "SNAPSHOT_UNAVAILABLE", "Retry with rediscovery.", result);
            }
            catch (Exception ex)
            {
                _sessionLog.WriteLine($"Discovery snapshot {definition.Request}: {ex.Message}");
                _logger.LogEvent("SNAPSHOT_UNAVAILABLE", ShortVehicleId(), definition.Request, message: ex.Message);
            }
        }
    }

    private static string SupportedPidEvidence(string text)
    {
        List<byte> bytes = ObdParser.ExtractHexBytes(text, "0100"); var blocks = new List<string>();
        for (int i = 0; i + 5 < bytes.Count; i++) if (bytes[i] == 0x41 && bytes[i + 1] == 0x00) blocks.Add(Convert.ToHexString(bytes.Skip(i + 2).Take(4).ToArray()));
        return blocks.Count > 0 ? string.Join("+", blocks.Distinct().OrderBy(x => x)) : "PID00-UNKNOWN";
    }
    private string ShortVehicleId() => _currentVehicleSignature.Length > 0 ? _currentVehicleSignature : "unidentified";

    private void SaveVehicleCalibration()
    {
        if (_currentVehicleProfile is null) return;
        _currentVehicleProfile.CalibrationAccepted = true; _currentVehicleProfile.TimingProfile = Math.Clamp(_activeTimingProfile, 0, 2); _currentVehicleProfile.InterRequestDelayMs = GetInterRequestDelayMs(); _currentVehicleProfile.UseResponseCount = UseResponseCountCheck.IsChecked == true; _currentVehicleProfile.PreferredProtocolNumber = _confirmedProtocol; _currentVehicleProfile.ProtocolNumber = CanonicalProtocolNumber(_currentVehicleProfile.Protocol); _currentVehicleProfile.LastSeenUtc = DateTime.UtcNow;
        SaveSettingsAndGlobalConfiguration(); UpdateCompactStatus(); RefreshModulePresentation();
        _sessionLog.WriteLine($"[{DateTime.Now:O}] VEHICLE CALIBRATION SAVED signature={_currentVehicleSignature} timing={_currentVehicleProfile.TimingProfile} delay={_currentVehicleProfile.InterRequestDelayMs}ms responseCount={_currentVehicleProfile.UseResponseCount}");
    }

    private void ClearRaw_Click(object sender, RoutedEventArgs e)
    {
        RawLogBox.Clear();
        ResetLcarsTraffic();
    }

    private async void ReloadPidDatabase_Click(object sender, RoutedEventArgs e)
    {
        bool restartPolling = _pollCts is not null;
        if (restartPolling)
            StopPolling();

        if (_ssmMode)
        {
            LoadSsmDefinitions();
        }
        else if (_td5Mode)
        {
            LoadTd5Definitions();
            if (_td5.IsOpen) await ReadTd5StaticSnapshotsAsync();
        }
        else
        {
            LoadPidDatabase();
            if (_elm.IsOpen) await ReadDiscoverySnapshotsAsync();
        }

        if (restartPolling && ConnectionIsOpen)
            StartPolling();
    }

    private void Logging_Click(object sender, RoutedEventArgs e)
    {
        _autoCsvEnabled = !_logger.IsLogging;
        if (_logger.IsLogging)
        {
            _logger.LogEvent("RECORDING_STOP", ShortVehicleId());
            _logger.Stop();
            LoggingButton.Content = "● Record";
            LogPathText.Text = _logger.CurrentPath ?? "No CSV recording yet";
        }
        else
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBDWorkbench", "Logs");
            string path = _logger.Start(folder);
            LoggingButton.Content = "■ Recording";
            LogPathText.Text = path;
            _logger.LogEvent("RECORDING_START", ShortVehicleId());
            foreach (var module in DetectedModules)
                _logger.LogEvent("MODULE", ShortVehicleId(), source: module.Source,
                    message: $"{module.Name}; PIDs={module.SupportedPids}; CALID={module.CalibrationId}; CVN={module.Cvn}");
            foreach (var item in LiveValues.Where(v => v.Definition.DiscoveryOnly && v.Updated.HasValue))
                _logger.LogEvent("DISCOVERY_SNAPSHOT", ShortVehicleId(), item.Definition.Request, item.Source, item.Raw,
                    $"Captured {item.Updated:O}; {item.DisplayValue}");
        }
    }

    private void OpenLogFolder_Click(object sender, MouseButtonEventArgs e)
    {
        string? path = ReferenceEquals(sender, SessionLogPathText) ? _sessionLog.CurrentPath : _logger.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { SetError("Could not open log folder: " + ex.Message); }
    }

    private void AppendRaw(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendRaw(text));
            return;
        }
        RawLogBox.AppendText(text);
        RawLogBox.ScrollToEnd();
        AppendLcarsTraffic(text);
        _sessionLog.Write(text);
    }

    private void SetConnectionUi(string text, bool connected)
    {
        if (!connected) { _trendPlayback.SetConnected(false, DateTime.UtcNow); RefreshTrendPlayback(); }
        StartupStatusChanged?.Invoke(text);
        StatusText.Text = connected ? "Connected" : "Disconnected";
        StatusText.ToolTip = text;
        StatusLamp.Fill = connected ? Brushes.ForestGreen : Brushes.Firebrick;
    }

    private void SetError(string message)
    {
        LastErrorText.Text = message;
        if (!string.IsNullOrWhiteSpace(message) && !string.Equals(message, _lastLoggedError, StringComparison.Ordinal))
        {
            _lastLoggedError = message;
            _sessionLog.WriteLine($"[{DateTime.Now:O}] ERROR {message}");
        }
        else if (string.IsNullOrWhiteSpace(message))
        {
            _lastLoggedError = "";
        }
    }

    private static string CleanElmText(string response, string command)
    {
        var lines = response.Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l != ">" && !l.Equals(command, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Replace(">", "").Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        return lines.Length == 0 ? "Unknown" : string.Join(" ", lines);
    }
}
