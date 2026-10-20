using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class MainWindow
{
    private bool _replayLoading;
    private readonly DispatcherTimer _replayTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private const double ReplayWindowBehindSeconds = 30;
    private const double ReplayWindowAheadSeconds = 60;
    private const double ReplayPrefetchLeadSeconds = 15;
    private ReplaySession? _replaySession;
    private ReplayWindow? _replayWindow;
    private int _replayWindowNextIndex;
    private Task<ReplayWindow>? _replayPrefetchTask;
    private CancellationTokenSource? _replayPrefetchCts;
    private DateTime _replayCurrentUtc;
    private DateTime _replayLastWallUtc;
    private double _replaySpeed = 1.0;
    private bool _replayPlaying;
    private bool _updatingReplaySlider;
    private bool _isReplayMode;
    private readonly Dictionary<string, DateTime> _replayDtcFirstSeen = new(StringComparer.OrdinalIgnoreCase);

    public void InitializeReplayMode()
    {
        _isReplayMode = true;
        Title = "Icarus Offroad - OBD WorkBench v0.5.0.0 — Replay";
        ReplayPanel.Visibility = Visibility.Visible;
        LiveRecordingPanel.Visibility = Visibility.Collapsed;
        ConnectButton.IsEnabled = false;
        SetConnectionPanelCollapsed(true);
        // Keep the external toggle reachable if the user expands recorded connection info.
        CollapseConnectionButton.Visibility = Visibility.Visible;
        ConnectionPanel.IsEnabled = false; // Replay never edits or opens a live adapter.
        StatusText.Text = "Replay";
        StatusText.ToolTip = "Recorded-data replay mode";
        StatusLamp.Fill = Brushes.SteelBlue;
        TransportText.Text = "Replay";
        ProtocolText.Text = "Recorded CSV";
        AdapterText.Text = "Virtual";
        VehicleText.Text = "No recording";
        LoggingButton.Visibility = Visibility.Collapsed;
        RawCommandBox.IsEnabled = false;
        RawCommandBox.ToolTip = "Replay console is read-only.";
        RawSendButton.IsEnabled = false;
        RawSendButton.ToolTip = "No vehicle is connected in Replay mode.";
        ReplayRefreshDtcButton.IsEnabled = false;
        EraseDtcsButton.IsEnabled = false;
        ReplayDiscoveryButton.IsEnabled = false;
        ReplayReloadPidDatabaseButton.IsEnabled = false;
        DtcStatusText.Text = "Replay DTC state follows the loaded recording timeline.";
        DiscoverySummaryText.Text = "Recorded module/source information is shown below.";
        CalibrationText.Text = "Replay mode does not run live vehicle discovery.";
        _autoCsvEnabled = false;
        _replayTimer.Tick += ReplayTimer_Tick;
        _replaySpeed = 1.0;
        ReplayRecordingText.Text = "Open one or more CSV logs to begin replay.";
        AppendReplayConsole("OBD WorkBench v0.5.0.0 replay ready.\r\n");
        ReplayCacheText.Text = "Disk-backed replay cache idle";
        _ = Dispatcher.InvokeAsync(() => ReplayOpen_Click(this, new RoutedEventArgs()), DispatcherPriority.ApplicationIdle);
    }

    private async void ReplayOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open OBD WorkBench recording",
            Filter = "CSV log files (*.csv)|*.csv|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await LoadReplayFilesAsync(dialog.FileNames);
    }

    private async Task LoadReplayFilesAsync(IEnumerable<string> paths)
    {
        if (_replayLoading || _closing) return;
        string previousRecordingText = ReplayRecordingText.Text;
        bool loaded = false;
        _replayLoading = true;
        ReplayOpenButton.IsEnabled = false;
        ReplayPanel.IsEnabled = false;
        PauseReplay();
        CancelReplayPrefetch();
        ReplayPlayPauseButton.IsEnabled = false;
        ReplaySlider.IsEnabled = false;
        StatusText.Text = "Indexing replay…";
        ReplayRecordingText.Text = "Indexing CSV logs and building/reusing the disk-backed replay cache…";
        ReplayCacheText.Text = "Large recordings are indexed without retaining the full timeline in RAM.";
        SetError("");
        try
        {
            string[] selected = paths.ToArray();
            ReplaySession session = await Task.Run(() => ReplayCsvLoader.Load(selected, WindowsStoragePaths.ReplayCache));
            if (_closing) return;
            if (session.Cache is null || (session.SampleCount + session.EventCount + session.DtcCount) == 0)
            {
                MessageBox.Show(this, "No replayable timestamped samples or events were found in the selected CSV files.", "OBD WorkBench Replay", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _replaySession = session;
            BuildReplaySignals(session);
            ClearTrendSignalsInternal(); ClearDashboardGaugesInternal(); DetectedDtcs.Clear(); _replayDtcFirstSeen.Clear(); RawLogBox.Clear(); ResetLcarsTraffic();
            double duration = Math.Max(0, (session.EndUtc - session.StartUtc).TotalSeconds);
            _updatingReplaySlider = true;
            ReplaySlider.Minimum = 0; ReplaySlider.Maximum = Math.Max(duration, 0.001); ReplaySlider.Value = 0; ReplaySlider.IsEnabled = true;
            ReplaySlider.Ticks = new DoubleCollection(session.Markers.Select(r => Math.Clamp((r.TimestampUtc - session.StartUtc).TotalSeconds, 0, duration)).Distinct().Take(2000));
            ReplaySlider.TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight;
            _updatingReplaySlider = false;
            ReplayPlayPauseButton.IsEnabled = true;
            ReplayStartText.Text = "00:00:00"; ReplayEndText.Text = "/ " + FormatDuration(duration);
            ReplayRecordingText.Text = $"{session.Files.Count} file(s) • {session.SampleCount:N0} samples • {session.EventCount:N0} events • {session.DtcCount:N0} DTC records • {session.Signals.Count:N0} signals";
            ReplayRecordingText.ToolTip = string.Join(Environment.NewLine, session.Files);
            VehicleText.Text = string.IsNullOrWhiteSpace(session.Vehicle) ? "Recorded vehicle" : session.Vehicle;
            _currentVehicleSignature = "REPLAY|" + (string.IsNullOrWhiteSpace(session.Vehicle) ? Path.GetFileName(session.Files[0]) : session.Vehicle);
            _replayLoading = false;
            SeekReplay(0);
            loaded = true;
            StatusText.Text = "Replay loaded"; StatusLamp.Fill = Brushes.SteelBlue; UpdateReplayCacheStatus(); SetError("");
        }
        catch (Exception ex)
        {
            if (_closing) return;
            SetError(ex.Message); ReplayCacheText.Text = "Replay cache load failed.";
            MessageBox.Show(this, ex.Message, "Unable to open recording", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _replayLoading = false;
            if (!_closing) StatusText.Text = _replaySession is null ? "Replay • no recording" : "Replay paused";
            if (!loaded) { ReplayRecordingText.Text = previousRecordingText; UpdateReplayCacheStatus(); }
            ReplayOpenButton.IsEnabled = true;
            ReplayPanel.IsEnabled = true;
            ReplayPlayPauseButton.IsEnabled = _replaySession is not null;
            ReplaySlider.IsEnabled = _replaySession is not null;
        }
    }

    private void BuildReplaySignals(ReplaySession session)
    {
        LiveValues.Clear(); DetectedModules.Clear(); var defs = new List<PidDefinition>();
        foreach (ReplaySignalDescriptor signal in session.Signals.OrderBy(x => x.Source, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            string request = string.IsNullOrWhiteSpace(signal.Request) ? "REPLAY:" + signal.Name.ToUpperInvariant() : signal.Request;
            string definitionUnits = string.IsNullOrWhiteSpace(signal.Units) ? signal.NativeUnits : signal.Units;
            var definition = new PidDefinition { Enabled = true, Name = signal.Name, Request = request, ResponseMode = "REPLAY", PID = request, Formula = "recorded", Units = definitionUnits, Min = signal.Min, Max = signal.Max, PollMs = 1000, Category = "Replay", Notes = $"Recorded CSV signal • {signal.SampleCount:N0} samples" };
            defs.Add(definition);
            LiveValues.Add(new PidLiveValue { Definition = definition, Source = string.IsNullOrWhiteSpace(signal.Source) ? "Replay" : signal.Source, ShowDetailedMetrics = false, CanTrend = CanTrendDefinition(definition), TrendUnit = string.IsNullOrWhiteSpace(signal.Units) ? definitionUnits : signal.Units });
        }
        _definitions = defs; PidGrid.ItemsSource = _definitions; PidPathText.Text = string.Join("; ", session.Files.Select(Path.GetFileName));
        foreach (string source in LiveValues.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase))
            DetectedModules.Add(new DetectedModule { Source = source, Name = source, Protocol = "Recorded CSV", SupportedPids = LiveValues.Count(x => x.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture) + " signals", Notes = "Replay source" });
        RefreshModulePresentation();
    }

    private void ReplayPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_replayPlaying) PauseReplay(); else PlayReplay();
    }

    private void PlayReplay()
    {
        if (_replayLoading || _replaySession?.Cache is null) return;
        if (_replaySession.EndUtc <= _replaySession.StartUtc) { SeekReplay(0); PauseReplay(); return; }
        if ((_replayCurrentUtc - _replaySession.StartUtc).TotalSeconds >= (ReplaySlider.Maximum - 0.001)) SeekReplay(0);
        EnsureReplayWindow(_replayCurrentUtc);
        _replayPlaying = true;
        _replayLastWallUtc = DateTime.UtcNow;
        ReplayPlayPauseButton.Content = "Ⅱ";
        StatusText.Text = $"Replay • {_replaySpeed:0.##}×";
        _replayTimer.Start();
    }

    private void PauseReplay()
    {
        _replayPlaying = false;
        ReplayPlayPauseButton.Content = "▶";
        if (_replaySession is not null) StatusText.Text = "Replay paused";
        _replayTimer.Stop();
    }

    private void ReplayTimer_Tick(object? sender, EventArgs e)
    {
        if (!_replayPlaying || _replaySession is null) return;
        DateTime now = DateTime.UtcNow;
        double delta = Math.Max(0, (now - _replayLastWallUtc).TotalSeconds) * _replaySpeed;
        _replayLastWallUtc = now;
        double current = (_replayCurrentUtc - _replaySession.StartUtc).TotalSeconds;
        double target = Math.Min(ReplaySlider.Maximum, current + delta);
        AdvanceReplayTo(target);
        if (target >= ReplaySlider.Maximum - 0.0005) PauseReplay();
    }

    private void ReplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingReplaySlider || _replaySession is null) return;
        SeekReplay(e.NewValue);
    }

    private void ReplaySpeed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReplaySpeedCombo?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) && speed > 0)
        {
            _replaySpeed = speed;
            if (_replayPlaying) StatusText.Text = $"Replay • {_replaySpeed:0.##}×";
            RefreshReplayTrendViewport();
        }
    }

    private void SeekReplay(double seconds)
    {
        if (_replayLoading || _replaySession?.Cache is null) return;
        bool resume = _replayPlaying; if (resume) PauseReplay(); CancelReplayPrefetch();
        seconds = Math.Clamp(seconds, 0, Math.Max(0, (_replaySession.EndUtc - _replaySession.StartUtc).TotalSeconds));
        DateTime target = _replaySession.StartUtc.AddSeconds(seconds);
        ResetReplayState(); PrimeReplayDtcFirstSeen(target);
        ReplayCheckpoint checkpoint = _replaySession.Cache.LoadCheckpoint(target);
        ApplyReplayCheckpoint(checkpoint);
        foreach (ReplayRecord record in _replaySession.Cache.ReadRange(checkpoint.TimestampUtc, target))
            ApplyReplayRecord(record, false, false);
        _replayCurrentUtc = target;
        RebuildReplayConsole(target); BackfillAllReplayTrends(); LoadReplayWindow(target); SetReplayPositionUi(seconds); UpdateReplayCacheStatus();
        if (resume) PlayReplay();
    }

    private void AdvanceReplayTo(double seconds)
    {
        if (_replaySession?.Cache is null) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, (_replaySession.EndUtc - _replaySession.StartUtc).TotalSeconds));
        DateTime target = _replaySession.StartUtc.AddSeconds(seconds);
        if (target < _replayCurrentUtc) { SeekReplay(seconds); return; }
        while (_replayCurrentUtc < target)
        {
            EnsureReplayWindow(_replayCurrentUtc); if (_replayWindow is null) break;
            DateTime segmentEnd = target <= _replayWindow.EndUtc ? target : _replayWindow.EndUtc;
            ApplyWindowRecordsThrough(segmentEnd); _replayCurrentUtc = segmentEnd;
            if (_replayCurrentUtc >= target) break;
            SwapOrLoadNextReplayWindow(_replayCurrentUtc);
        }
        SetReplayPositionUi(seconds); ScheduleReplayPrefetchIfNeeded();
    }

    private void ApplyWindowRecordsThrough(DateTime targetUtc)
    {
        if (_replayWindow is null) return;
        while (_replayWindowNextIndex < _replayWindow.Records.Count && _replayWindow.Records[_replayWindowNextIndex].TimestampUtc <= targetUtc)
        {
            ReplayRecord record = _replayWindow.Records[_replayWindowNextIndex++];
            if (record.TimestampUtc > _replayCurrentUtc) ApplyReplayRecord(record, true, true);
        }
    }

    private void ApplyReplayRecord(ReplayRecord record, bool captureTrend = true, bool appendConsole = true)
    {
        if (record.Kind == ReplayRecordKind.Sample && record.Value.HasValue)
        {
            PidLiveValue? item = LiveValues.FirstOrDefault(x => ReplaySignalKey(x.Definition.Request, x.Definition.Name, x.Source)
                .Equals(ReplaySignalKey(record), StringComparison.OrdinalIgnoreCase));
            if (item is null) return;
            double value = record.Value.Value;
            item.SampleStatus = record.Status + " • Raw: " + record.Raw;
            string displayUnit = string.IsNullOrWhiteSpace(record.DisplayText) ? record.Units : "";
            item.Units = displayUnit;
            item.TrendUnit = string.IsNullOrWhiteSpace(record.Units) ? item.Definition.Units : record.Units;
            item.DisplayValue = string.IsNullOrWhiteSpace(record.DisplayText) ? FormatValue(value, displayUnit) : record.DisplayText;
            item.Raw = record.Raw;
            AppendLcarsTraffic("<< " + record.Raw);
            item.DisplayOffset = 0;
            item.UnadjustedNumericValue = item.CanTrend ? value : null;
            item.LatestNumericValue = item.CanTrend ? value : null;
            item.RecordSample(record.TimestampUtc.ToLocalTime(), record.RttMs, record.FirstByteMs, record.TailMs);
            if (item.CanTrend)
            {
                if (captureTrend) RecordReplayTrendSample(item, record.TimestampUtc, value, item.TrendUnit);
                RecordDashboardSample(item, value, item.TrendUnit);
            }
            return;
        }

        if (record.Kind == ReplayRecordKind.Dtc)
        {
            ApplyReplayDtc(record);
            if (appendConsole) AppendReplayConsole($"[{record.TimestampUtc.ToLocalTime():HH:mm:ss.fff}] DTC {record.DtcChange} {record.DtcCode} {record.Status} {record.DtcDescription}\r\n");
            return;
        }

        if (appendConsole) AppendReplayConsole($"[{record.TimestampUtc.ToLocalTime():HH:mm:ss.fff}] {record.Status} {record.Message} {record.Raw}\r\n");
    }

    private void ApplyReplayDtc(ReplayRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.DtcCode)) return;
        DiagnosticTroubleCode? existing = DetectedDtcs.FirstOrDefault(x => x.Code.Equals(record.DtcCode, StringComparison.OrdinalIgnoreCase) && x.Source.Equals(record.Source, StringComparison.OrdinalIgnoreCase));
        if (record.DtcChange.Equals("CLEARED", StringComparison.OrdinalIgnoreCase))
        {
            if (existing is not null) DetectedDtcs.Remove(existing);
            DtcStatusText.Text = $"Replay position: {DetectedDtcs.Count} recorded DTC(s) active at this point in the capture.";
            return;
        }

        DateTime local = record.TimestampUtc.ToLocalTime();
        string key = record.Source + "|" + record.DtcCode;
        if (!_replayDtcFirstSeen.TryGetValue(key, out DateTime firstSeen))
        {
            firstSeen = local;
            _replayDtcFirstSeen[key] = firstSeen;
        }
        if (existing is null)
        {
            DetectedDtcs.Add(new DiagnosticTroubleCode
            {
                Source = record.Source,
                Module = "Recorded ECU",
                Code = record.DtcCode,
                Status = string.IsNullOrWhiteSpace(record.Status) ? "Recorded" : record.Status,
                Description = record.DtcDescription,
                DefinitionSource = "Recorded CSV",
                FirstRead = firstSeen,
                LastRead = local
            });
        }
        else
        {
            string status = string.IsNullOrWhiteSpace(record.Status) ? existing.Status : record.Status;
            string description = string.IsNullOrWhiteSpace(record.DtcDescription) ? existing.Description : record.DtcDescription;
            DetectedDtcs.Remove(existing);
            DetectedDtcs.Add(new DiagnosticTroubleCode
            {
                Source = record.Source, Module = existing.Module, Code = record.DtcCode, Status = status,
                Description = description, DefinitionSource = existing.DefinitionSource, FirstRead = firstSeen, LastRead = local
            });
        }
        DtcStatusText.Text = $"Replay position: {DetectedDtcs.Count} recorded DTC(s) active at this point in the capture.";
    }

    private void ResetReplayState()
    {
        foreach (PidLiveValue item in LiveValues) item.ResetReplayState();
        foreach (TrendSeriesState series in TrendSeries) series.ClearHistory();
        foreach (DashboardGaugeState gauge in DashboardGauges) gauge.ClearSamples();
        DetectedDtcs.Clear();
        _replayDtcFirstSeen.Clear();
        DtcStatusText.Text = "Replay position: no recorded DTCs active.";
        RawLogBox.Clear(); ResetLcarsTraffic();
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }

    private void SetReplayPositionUi(double seconds)
    {
        _updatingReplaySlider = true;
        ReplaySlider.Value = seconds;
        _updatingReplaySlider = false;
        ReplayCurrentText.Text = FormatDuration(seconds);
        RefreshReplayTrendViewport();
    }

    private void RefreshReplayTrendViewport()
    {
        if (!_isReplayMode || _replaySession is null) return;
        TrendChart.ViewEndUtc = _replayCurrentUtc;
        if (PlaybackStatus is not null)
        {
            double position = (_replayCurrentUtc - _replaySession.StartUtc).TotalSeconds;
            PlaybackStatus.Text = $"Replay {FormatDuration(position)} / {FormatDuration(( _replaySession.EndUtc - _replaySession.StartUtc).TotalSeconds)} • {_replaySpeed:0.##}×";
        }
        TrendChart.InvalidateVisual();
    }

    private void RecordReplayTrendSample(PidLiveValue item, DateTime timestampUtc, double value, string unit)
    {
        string key = TrendKey(item.Definition, item.Source);
        TrendSeriesState? series = TrendSeries.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (series is null) return;
        if (!string.Equals(series.Unit, unit, StringComparison.OrdinalIgnoreCase))
        {
            series.ClearHistory();
            series.Unit = unit;
        }
        series.AddSample(timestampUtc, value);
    }

    private void BackfillAllReplayTrends()
    {
        if (_replaySession?.Cache is null || TrendSeries.Count == 0) return;
        foreach (TrendSeriesState series in TrendSeries) series.ClearHistory();
        DateTime begin = _replayCurrentUtc.AddSeconds(-Math.Max(_trendWindowSeconds, 5));
        var map = TrendSeries.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        foreach (ReplayRecord record in _replaySession.Cache.ReadRange(begin, _replayCurrentUtc))
        {
            if (record.Kind != ReplayRecordKind.Sample || !record.Value.HasValue) continue;
            if (!map.TryGetValue(ReplaySignalKey(record), out TrendSeriesState? series)) continue;
            if (!string.IsNullOrWhiteSpace(record.Units)) series.Unit = record.Units;
            series.AddSample(record.TimestampUtc, record.Value.Value);
        }
        TrendChart.ViewEndUtc = _replayCurrentUtc; TrendChart.InvalidateVisual();
    }

    private void BackfillReplayTrendSeries(TrendSeriesState series, PidLiveValue item)
    {
        if (!_isReplayMode || _replaySession?.Cache is null) return;
        series.ClearHistory(); DateTime begin = _replayCurrentUtc.AddSeconds(-Math.Max(_trendWindowSeconds, 5));
        string target = ReplaySignalKey(item.Definition.Request, item.Definition.Name, item.Source);
        foreach (ReplayRecord record in _replaySession.Cache.ReadRange(begin, _replayCurrentUtc))
        {
            if (record.Kind != ReplayRecordKind.Sample || !record.Value.HasValue || !ReplaySignalKey(record).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
            series.Unit = record.Units; series.AddSample(record.TimestampUtc, record.Value.Value);
        }
        TrendChart.ViewEndUtc = _replayCurrentUtc; TrendChart.InvalidateVisual();
    }

    private void ReplayStep(int direction)
    {
        if (_replayLoading || _replaySession?.Cache is null) return; PauseReplay();
        DateTime? target = direction > 0 ? _replaySession.Cache.FindNextTimestamp(_replayCurrentUtc) : _replaySession.Cache.FindPreviousTimestamp(_replayCurrentUtc);
        if (!target.HasValue) { if (direction < 0) SeekReplay(0); return; }
        SeekReplay((target.Value - _replaySession.StartUtc).TotalSeconds);
    }

    private void ReplayStepBack_Click(object sender, RoutedEventArgs e) => ReplayStep(-1);
    private void ReplayStepForward_Click(object sender, RoutedEventArgs e) => ReplayStep(1);

    private void PrimeReplayDtcFirstSeen(DateTime targetUtc)
    {
        _replayDtcFirstSeen.Clear(); if (_replaySession is null) return;
        foreach (ReplayRecord record in _replaySession.Markers.Where(x => x.Kind == ReplayRecordKind.Dtc && x.TimestampUtc <= targetUtc))
        {
            if (string.IsNullOrWhiteSpace(record.DtcCode)) continue; string key = record.Source + "|" + record.DtcCode;
            if (!_replayDtcFirstSeen.ContainsKey(key)) _replayDtcFirstSeen[key] = record.TimestampUtc.ToLocalTime();
        }
    }

    private void ApplyReplayCheckpoint(ReplayCheckpoint checkpoint)
    {
        foreach (ReplaySignalCheckpoint state in checkpoint.Signals)
        {
            ApplyReplayRecord(state.LastSample, false, false);
            PidLiveValue? item = LiveValues.FirstOrDefault(x => ReplaySignalKey(x.Definition.Request, x.Definition.Name, x.Source).Equals(state.Key, StringComparison.OrdinalIgnoreCase));
            if (item is null || !item.CanTrend) continue;
            DashboardGaugeState? gauge = DashboardGauges.FirstOrDefault(x => x.Key.Equals(TrendKey(item.Definition, item.Source), StringComparison.OrdinalIgnoreCase));
            if (gauge is not null) gauge.SeedLearnedRange(state.ObservedMin, state.ObservedMax, string.IsNullOrWhiteSpace(state.LastSample.Units) ? item.TrendUnit : state.LastSample.Units);
        }
        foreach (ReplayRecord dtc in checkpoint.ActiveDtcs) ApplyReplayDtc(dtc);
    }

    private void RebuildReplayConsole(DateTime targetUtc)
    {
        RawLogBox.Clear(); ResetLcarsTraffic(); if (_replaySession is null) return;
        List<ReplayRecord> visible = _replaySession.Markers.Where(x => x.TimestampUtc <= targetUtc).TakeLast(500).ToList();
        foreach (ReplayRecord record in visible)
        {
            if (record.Kind == ReplayRecordKind.Dtc) AppendReplayConsole($"[{record.TimestampUtc.ToLocalTime():HH:mm:ss.fff}] DTC {record.DtcChange} {record.DtcCode} {record.Status} {record.DtcDescription}\r\n");
            else AppendReplayConsole($"[{record.TimestampUtc.ToLocalTime():HH:mm:ss.fff}] {record.Status} {record.Message} {record.Raw}\r\n");
        }
    }

    private void LoadReplayWindow(DateTime centerUtc)
    {
        if (_replaySession?.Cache is null) return;
        DateTime start = centerUtc.AddSeconds(-ReplayWindowBehindSeconds), end = centerUtc.AddSeconds(ReplayWindowAheadSeconds);
        if (start < _replaySession.StartUtc) start = _replaySession.StartUtc; if (end > _replaySession.EndUtc) end = _replaySession.EndUtc;
        _replayWindow = new ReplayWindow { StartUtc = start, EndUtc = end, Records = _replaySession.Cache.ReadRange(start, end) };
        _replayWindowNextIndex = FirstRecordAfter(_replayWindow.Records, _replayCurrentUtc); UpdateReplayCacheStatus(); ScheduleReplayPrefetchIfNeeded();
    }

    private void EnsureReplayWindow(DateTime currentUtc)
    {
        if (_replayWindow is null || currentUtc < _replayWindow.StartUtc || currentUtc >= _replayWindow.EndUtc) LoadReplayWindow(currentUtc);
    }

    private void ScheduleReplayPrefetchIfNeeded()
    {
        if (_replaySession?.Cache is null || _replayWindow is null || (_replayWindow.EndUtc - _replayCurrentUtc).TotalSeconds > ReplayPrefetchLeadSeconds || _replayWindow.EndUtc >= _replaySession.EndUtc) return;
        if (_replayPrefetchTask is not null && !_replayPrefetchTask.IsCompleted) return;
        CancelReplayPrefetch(); var cts = new CancellationTokenSource(); _replayPrefetchCts = cts;
        DateTime nextStart = _replayWindow.EndUtc.AddSeconds(-5), nextEnd = nextStart.AddSeconds(ReplayWindowBehindSeconds + ReplayWindowAheadSeconds); if (nextEnd > _replaySession.EndUtc) nextEnd = _replaySession.EndUtc;
        ReplayCacheStore cache = _replaySession.Cache;
        CancellationToken token = cts.Token;
        _replayPrefetchTask = Task.Run(() => { token.ThrowIfCancellationRequested(); var records = cache.ReadRange(nextStart, nextEnd); token.ThrowIfCancellationRequested(); return new ReplayWindow { StartUtc = nextStart, EndUtc = nextEnd, Records = records }; }, token);
    }

    private void SwapOrLoadNextReplayWindow(DateTime currentUtc)
    {
        ReplayWindow? prefetched = null;
        if (_replayPrefetchTask is { IsCompletedSuccessfully: true }) { try { prefetched = _replayPrefetchTask.Result; } catch { } }
        CancelReplayPrefetch();
        if (prefetched is not null && currentUtc >= prefetched.StartUtc && currentUtc <= prefetched.EndUtc)
        { _replayWindow = prefetched; _replayWindowNextIndex = FirstRecordAfter(prefetched.Records, currentUtc); UpdateReplayCacheStatus(); ScheduleReplayPrefetchIfNeeded(); }
        else LoadReplayWindow(currentUtc);
    }

    private void CancelReplayPrefetch()
    {
        try { _replayPrefetchCts?.Cancel(); } catch { } _replayPrefetchCts?.Dispose(); _replayPrefetchCts = null; _replayPrefetchTask = null;
    }

    private static int FirstRecordAfter(List<ReplayRecord> records, DateTime timestampUtc)
    {
        int lo = 0, hi = records.Count; while (lo < hi) { int mid = lo + ((hi - lo) / 2); if (records[mid].TimestampUtc <= timestampUtc) lo = mid + 1; else hi = mid; } return lo;
    }

    private void UpdateReplayCacheStatus()
    {
        if (_replaySession?.Cache is null) { ReplayCacheText.Text = "Disk-backed replay cache idle"; return; }
        long workingSet = 0; try { workingSet = Process.GetCurrentProcess().WorkingSet64; } catch { }
        string mode = _replaySession.CacheWasReused ? "reused" : "built"; string skipped = _replaySession.SkippedRows > 0 ? $" • {_replaySession.SkippedRows:N0} skipped rows" : "";
        ReplayCacheText.Text = $"Cache {mode}: {_replaySession.Cache.ChunkCount:N0} × {ReplayCacheStore.ChunkSeconds}s chunks • {FormatBytes(_replaySession.CacheBytes)} disk • window {_replayWindow?.Records.Count ?? 0:N0} records • process {FormatBytes(workingSet)}{skipped}";
        ReplayCacheText.ToolTip = _replaySession.Cache.CacheDirectory;
    }

    private void AppendReplayConsole(string text)
    {
        RawLogBox.AppendText(text);
        AppendLcarsTraffic(text);
        if (RawLogBox.LineCount > 1200)
        {
            string[] lines = RawLogBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            RawLogBox.Text = string.Join(Environment.NewLine, lines.TakeLast(1000));
        }
        RawLogBox.ScrollToEnd();
    }

    private static string ReplaySignalKey(ReplayRecord record) => ReplaySignalKey(record.Request, record.Name, string.IsNullOrWhiteSpace(record.Source) ? "Replay" : record.Source);
    private static string ReplaySignalKey(string request, string name, string source)
    {
        string normalizedRequest = string.IsNullOrWhiteSpace(request) ? "REPLAY:" + name.Trim().ToUpperInvariant() : request.Trim().ToUpperInvariant();
        return $"{normalizedRequest}|{name.Trim()}|{source.Trim().ToUpperInvariant()}";
    }
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B"; double value = bytes; string[] units = { "KB", "MB", "GB", "TB" }; int index = -1;
        do { value /= 1024; index++; } while (value >= 1024 && index < units.Length - 1);
        return $"{value:0.0} {units[index]}";
    }

    private static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 24 ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}" : $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
