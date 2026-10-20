using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OBDWorkbench.Models;
using OBDWorkbench.Services;
using OBDWorkbench.Platform.Windows;

namespace OBDWorkbench;

public partial class MainWindow
{
    private WindowsGpsConnection _gps = new();
    private GpsServiceFeed? _gpsService;
    private bool UseGpsService => _settings.GpsSource == "Service";
    private NmeaGpsParser _gpsParser = new();
    private readonly DispatcherTimer _gpsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Queue<string> _gpsDebug = new();
    private DateTime _gpsLastPublished;
    private string _gpsLastStatus = "";
    private static readonly PidDefinition[] GpsDefinitions =
    {
        GpsDefinition("SPEED", "GPS Speed", "km/h", 0, 240),
        GpsDefinition("LATITUDE", "GPS Latitude", "degrees", -90, 90),
        GpsDefinition("LONGITUDE", "GPS Longitude", "degrees", -180, 180),
        GpsDefinition("ALTITUDE", "GPS Altitude", "m", -500, 6000),
        GpsDefinition("COURSE", "GPS Course", "degrees", 0, 360),
        GpsDefinition("SATELLITES", "GPS Satellites", "count", 0, 50),
        GpsDefinition("HDOP", "GPS HDOP", "", 0, 20),
        GpsDefinition("FIX", "GPS Fix Valid", "0/1", 0, 1)
    };
    private static PidDefinition GpsDefinition(string key, string name, string unit, double min, double max) => new()
    { Request = "GPS-" + key, Name = name, Units = unit, Min = min, Max = max, Category = "GPS", Responders = "GPS", Enabled = true, DerivedOnly = true };

    private void InitializeGps()
    {
        _gpsTimer.Tick += (_, _) => GpsTick();
        _gpsTimer.Start();
        ApplyGpsConfiguration();
    }
    private void ApplyGpsConfiguration()
    {
        _gps.Dispose();
        _gps = new();
        _gpsService?.Dispose();
        _gpsService = null;
        _gpsDebug.Clear();
        _gpsParser = new();
        while (_gps.TryRead(out _)) { }
        _gpsLastPublished = DateTime.MinValue;
        if (_settings.GpsEnabled && !_isReplayMode)
        {
            if (UseGpsService) _gpsService = new GpsServiceFeed();
            else _gps.Start(_settings);
        }
        _logger.LogEvent("GPS_SOURCE", ShortVehicleId(), source: "GPS", message: UseGpsService ? "Icarus GPS Service" : "COM " + _settings.GpsPort);
        GpsTick();
    }
    private void GpsTick()
    {
        if (_closing || _isReplayMode) return;
        DateTime now = DateTime.UtcNow;
        if (_settings.GpsEnabled && !UseGpsService)
        {
            for (int n = 0; n < 500 && _gps.TryRead(out string line); n++)
            {
                _gpsDebug.Enqueue($"{now:HH:mm:ss.fff} {line}");
                while (_gpsDebug.Count > 250) _gpsDebug.Dequeue();
                _gpsParser.Accept(line, now);
            }
        }
        bool enabled = _settings.GpsEnabled;
        var serviceState = _gpsService?.State;
        var snapshot = serviceState?.Snapshot;
        bool serviceOnline = enabled && serviceState is not null && GpsServiceValues.IsAvailable(serviceState, now);
        bool online = enabled && (UseGpsService ? serviceOnline : _gps.IsOpen);
        bool fix = online && (UseGpsService ? snapshot is not null && GpsServiceValues.HasFix(snapshot) : _gpsParser.HasFix(now));
        string status = !enabled ? "GPS disabled" : UseGpsService
            ? !serviceOnline ? serviceState?.Status ?? "GPS service unavailable"
                : fix ? "GPS service · valid signal" : "GPS service · " + (snapshot!.Config?.Enabled != true ? "receiver disabled" : !snapshot.PortOpen ? "receiver disconnected / released" : "waiting for valid fix")
            : !_gps.IsOpen ? _gps.Status : fix ? "GPS COM · valid signal" : "GPS COM · waiting for valid fix";
        if (UseGpsService && enabled && !serviceOnline && snapshot is not null) status = "GPS service · stale heartbeat";
        GpsStatusText.Text = status;
        GpsStatusLamp.Fill = !enabled ? Brushes.Gray : fix ? Brushes.LimeGreen : online ? Brushes.Goldenrod : Brushes.IndianRed;
        if (status != _gpsLastStatus)
        {
            _logger.LogEvent("GPS_STATUS", ShortVehicleId(), source: "GPS", message: status);
            _gpsLastStatus = status;
        }
        var values = !enabled ? new Dictionary<string, double>() : UseGpsService
            ? serviceState is null ? new Dictionary<string, double>() : GpsServiceValues.Map(serviceState, now)
            : _gps.IsOpen ? _gpsParser.Snapshot(now) : new Dictionary<string, double>();
        foreach (var d in GpsDefinitions)
        {
            var item = LiveValues.FirstOrDefault(x => x.Source == "GPS" && x.Definition.Request == d.Request);
            if (item is null && enabled)
            {
                item = new PidLiveValue { Definition = d, Source = "GPS", CanTrend = true, TrendUnit = UnitConverter.TargetUnit(d.Units, _settings) };
                LiveValues.Add(item);
            }
            if (item is not null && !values.ContainsKey(d.Request[4..]))
            {
                item.LatestNumericValue = null; item.UnadjustedNumericValue = null;
                item.DisplayValue = "—"; item.SampleStatus = status + " / no fresh value";
            }
        }
        if (!enabled || (now - _gpsLastPublished).TotalSeconds < 1) return;
        _gpsLastPublished = now;
        foreach (var d in GpsDefinitions)
        {
            if (!values.TryGetValue(d.Request[4..], out double value)) continue;
            string raw = UseGpsService && snapshot is not null ? GpsServiceValues.Evidence(snapshot) : _gpsParser.RawFor(d.Request[4..]);
            UpdateLiveValue(d, value, raw, 0, 0, 0, "GPS");
            var item = LiveValues.First(x => ReferenceEquals(x.Definition, d) && x.Source == "GPS");
            item.SampleStatus = status;
            if (d.Request is "GPS-LATITUDE" or "GPS-LONGITUDE") item.DisplayValue = (item.LatestNumericValue ?? value).ToString("F6", CultureInfo.InvariantCulture);
            _logger.Log(d, value, item.LatestNumericValue ?? value, item.Units, ShortVehicleId(), raw, 0, 0, 0, "GPS", sampleStatus: status);
        }
    }
    private string GpsDebugText()
    {
        if (!_settings.GpsEnabled) return $"Source: {(UseGpsService ? "Icarus GPS Service" : "Direct COM")}\nGPS is disabled in Workbench.\n\nSelect 'Enable GPS in Workbench' above to connect and display diagnostics.\nChoosing the GPS source alone does not enable the connection.\n\nThe separate GPS service may still be running and finding satellites.\nA location fix is not required to receive service status or recent NMEA lines.";
        if (!UseGpsService) return $"Source: Direct COM {_settings.GpsPort}\n{GpsStatusText.Text}\nReceiver UTC: {_gpsParser.UtcTime}\nRejected malformed/checksum sentences: {_gpsParser.RejectedSentences}\n\n" + string.Join("\r\n", _gpsDebug);
        var s = _gpsService?.State.Snapshot;
        return $"Source: Icarus GPS Service · Icarus.GPS.Data.v1\n{GpsStatusText.Text}\n" + (s is null ? "Waiting for service data." :
            $"Receiver: {s.Config?.Port} / {s.Config?.Baud} baud\nPort open: {s.PortOpen} · data fresh: {s.DataFresh} · valid fix: {s.FixValid}\nSatellites used: {s.SatellitesUsed?.ToString() ?? "—"} · HDOP: {s.Hdop?.ToString(CultureInfo.InvariantCulture) ?? "—"} · fix quality: {s.FixQuality?.ToString() ?? "—"}\nSatellites in view by talker: {string.Join(", ", (s.SatellitesInView ?? new()).Select(x => x.Key + ": " + x.Value))}\nAcquisition time: {s.AcquisitionSeconds:0} seconds\nPublished UTC: {s.PublishedUtc:O}\nReceiver UTC: {s.GpsUtc:O}\nState: {s.State} · {s.Message}\nValid sentences: {s.ValidSentences} · rejected: {s.RejectedSentences}\n\nRecent NMEA supplied by the service:\n" + string.Join("\r\n", s.RecentNmea ?? Array.Empty<string>()));
    }
    private void GpsConfiguration_Click(object sender, RoutedEventArgs e) => OpenGpsConfiguration(this);
    private void OpenGpsConfiguration(Window owner)
    {
        if (_isReplayMode) return;
        var window = new GpsConfigurationWindow(_settings, () =>
        {
            SaveSettingsAndGlobalConfiguration();
            ApplyGpsConfiguration();
        }, GpsDebugText) { Owner = owner };
        window.ShowDialog();
    }
}
