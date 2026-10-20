using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;
public partial class MainWindow
{
    public ObservableCollection<PidLiveValue> PinnedReadings { get; } = new();
    private readonly HashSet<INotifyPropertyChanged> _presentationSubscriptions = new();
    private readonly GlobalConfigurationStore _globalConfigurationStore = new();
    private bool _syncingPresentation;
    private string _liveSortColumn = "";
    // 0 = default priority order, 1 = first-click order, 2 = reverse order.
    private int _liveSortState;
    private string _liveSignalFilter = "";

    private void InitializeSignalPresentation()
    {
        _settings.SignalColors ??= new();
        _settings.SignalOffsets ??= new(StringComparer.OrdinalIgnoreCase);
        _settings.CalculatedSignals ??= new();
        _settings.PinnedSignals ??= new();
        if (_settings.PresentationVersion < 18) { _settings.OverlayPlots = true; _settings.PresentationVersion = 18; }
        if (_settings.PresentationVersion < 19) { _settings.TrendWindowSeconds = 60; _settings.PresentationVersion = 19; SaveSettingsAndGlobalConfiguration(); }
        OverlayTrendCheck.IsChecked = _settings.OverlayPlots;
        TrendChart.Stacked = !_settings.OverlayPlots;
        LiveValues.CollectionChanged += (_, _) => ReconcileSignalPresentation();
        TrendSeries.CollectionChanged += (_, _) => { ReconcileSignalPresentation(); SaveGlobalConfiguration(); };
        DashboardGauges.CollectionChanged += (_, _) => { ReconcileSignalPresentation(); SaveGlobalConfiguration(); };
        ReconcileSignalPresentation();
        ApplyLiveDataView();
    }

    private static string PresentationKey(object signal) => signal switch
    {
        PidLiveValue live => TrendKey(live.Definition, live.Source),
        IConfigurableSignal configured => configured.Key,
        _ => ""
    };

    private void ReconcileSignalPresentation()
    {
        var active = LiveValues.Cast<INotifyPropertyChanged>()
            .Concat(TrendSeries).Concat(DashboardGauges).ToHashSet();
        foreach (var old in _presentationSubscriptions.Except(active).ToArray())
        {
            old.PropertyChanged -= SignalPresentationChanged;
            _presentationSubscriptions.Remove(old);
        }
        _syncingPresentation = true;
        try
        {
            foreach (var item in active)
            {
                if (!_presentationSubscriptions.Add(item)) continue;
                string key = PresentationKey(item);
                if (!_settings.SignalColors.TryGetValue(key, out string? color))
                {
                    uint hash = 2166136261;
                    foreach (char c in key) hash = unchecked((hash ^ c) * 16777619);
                    color = TrendPalette.HexFor((int)(hash % (uint)TrendPalette.Count));
                    // Only explicit edits are persisted; use the existing live color for new views.
                    if (item is not PidLiveValue)
                        color = LiveValues.FirstOrDefault(x => PresentationKey(x) == key)?.ColorHex ?? color;
                }
                SetPresentationColor(item, color);
                if (item is PidLiveValue live)
                {
                    live.IsPinned = _settings.PinnedSignals.Contains(key);
                    string unit = string.IsNullOrWhiteSpace(live.TrendUnit) ? UnitConverter.TargetUnit(live.Definition.Units, _settings) : live.TrendUnit;
                    SignalOffsetSetting adjustment = GetSignalAdjustment(key, unit);
                    live.DisplayGain = adjustment.Gain;
                    live.DisplayOffset = adjustment.Offset;
                    live.DecimalPlaces = adjustment.DecimalPlaces;
                }
                else if (item is IConfigurableSignal configured)
                {
                    SignalOffsetSetting adjustment = GetSignalAdjustment(key, configured.Unit);
                    configured.DisplayGain = adjustment.Gain;
                    configured.DisplayOffset = adjustment.Offset;
                    configured.DecimalPlaces = adjustment.DecimalPlaces;
                }
                item.PropertyChanged += SignalPresentationChanged;
            }
            RefreshPinnedReadings();
            RefreshModulePresentation();
        }
        finally { _syncingPresentation = false; }
    }

    private static void SetPresentationColor(object item, string color)
    {
        if (item is PidLiveValue live) live.ColorHex = color;
        else if (item is IConfigurableSignal configured) configured.ColorHex = color;
    }

    private void SignalPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingPresentation || sender is null) return;

        if (e.PropertyName == "ColorHex")
        {
            string key = PresentationKey(sender);
            string color = sender is PidLiveValue live ? live.ColorHex : ((IConfigurableSignal)sender).ColorHex;
            _syncingPresentation = true;
            try
            {
                _settings.SignalColors[key] = color;
                foreach (var item in _presentationSubscriptions.Where(x => PresentationKey(x) == key))
                    SetPresentationColor(item, color);
                SaveSettingsAndGlobalConfiguration();
                TrendChart.InvalidateVisual();
            }
            finally { _syncingPresentation = false; }
            return;
        }

        if (sender is IConfigurableSignal configuredSignal &&
            e.PropertyName is "DisplayGain" or "DisplayOffset" or "DecimalPlaces")
        {
            string key = PresentationKey(configuredSignal);
            _syncingPresentation = true;
            try
            {
                StoreSignalAdjustment(key, configuredSignal.Unit, configuredSignal.DisplayGain, configuredSignal.DisplayOffset, configuredSignal.DecimalPlaces);
                PropagateSignalCalibration(key, configuredSignal.Unit, configuredSignal.DisplayGain, configuredSignal.DisplayOffset, configuredSignal.DecimalPlaces, configuredSignal);
                SaveSettingsAndGlobalConfiguration();
                _logger.LogEvent("SIGNAL_CALIBRATION_CHANGED", ShortVehicleId(), source: configuredSignal.Source,
                    message: $"{configuredSignal.SignalName}; gain={configuredSignal.DisplayGain:0.######}; offset={configuredSignal.DisplayOffset:0.######} {configuredSignal.Unit}; decimals={configuredSignal.DecimalPlaces}");
                _sessionLog.WriteLine($"[{DateTime.Now:O}] SIGNAL CALIBRATION key={key} gain={configuredSignal.DisplayGain:0.######} offset={configuredSignal.DisplayOffset:0.######} unit={configuredSignal.Unit} decimals={configuredSignal.DecimalPlaces}");
                TrendChart.InvalidateMeasure();
                TrendChart.InvalidateVisual();
            }
            finally { _syncingPresentation = false; }
            return;
        }

        if (sender is TrendSeriesState or DashboardGaugeState &&
            e.PropertyName is "AutoScale" or "ManualMin" or "ManualMax" or "ShowDataPoints" or
                              "LowRangeMin" or "LowRangeMax" or "LowRangeColorHex" or
                              "HighRangeMin" or "HighRangeMax" or "HighRangeColorHex")
            SaveGlobalConfiguration();
    }

    private SignalOffsetSetting GetSignalAdjustment(string key, string unit)
    {
        if (!_settings.SignalOffsets.TryGetValue(key, out SignalOffsetSetting? setting) || setting is null)
            return new SignalOffsetSetting { Gain = 1.0, Offset = 0, Unit = unit ?? "", DecimalPlaces = -1 };
        if (!string.IsNullOrWhiteSpace(setting.Unit) && !string.Equals(setting.Unit, unit, StringComparison.OrdinalIgnoreCase))
            return new SignalOffsetSetting { Gain = 1.0, Offset = 0, Unit = unit ?? "", DecimalPlaces = setting.DecimalPlaces };
        double gain = double.IsFinite(setting.Gain) && Math.Abs(setting.Gain) > 1e-12 ? setting.Gain : 1.0;
        double offset = double.IsFinite(setting.Offset) ? setting.Offset : 0;
        return new SignalOffsetSetting { Gain = gain, Offset = offset, Unit = setting.Unit ?? unit ?? "", DecimalPlaces = Math.Clamp(setting.DecimalPlaces, -1, 6) };
    }

    private double GetSignalDisplayOffset(string key, string unit) => GetSignalAdjustment(key, unit).Offset;
    private double GetSignalDisplayGain(string key, string unit) => GetSignalAdjustment(key, unit).Gain;
    private int GetSignalDecimalPlaces(string key, string unit) => GetSignalAdjustment(key, unit).DecimalPlaces;

    private void StoreSignalAdjustment(string key, string unit, double gain, double offset, int decimals)
    {
        gain = double.IsFinite(gain) && Math.Abs(gain) > 1e-12 ? gain : 1.0;
        offset = double.IsFinite(offset) ? offset : 0;
        decimals = Math.Clamp(decimals, -1, 6);
        if (Math.Abs(gain - 1.0) < 1e-12 && Math.Abs(offset) < 1e-12 && decimals < 0)
        {
            _settings.SignalOffsets.Remove(key);
            return;
        }
        _settings.SignalOffsets[key] = new SignalOffsetSetting { Gain = gain, Offset = offset, Unit = unit ?? "", DecimalPlaces = decimals };
    }

    private void PropagateSignalCalibration(string key, string unit, double gain, double offset, int decimals, object? source = null)
    {
        foreach (var item in _presentationSubscriptions.Where(x => PresentationKey(x).Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            if (ReferenceEquals(item, source)) continue;
            if (item is TrendSeriesState trend)
            {
                if (!string.IsNullOrWhiteSpace(trend.Unit) && !string.Equals(trend.Unit, unit, StringComparison.OrdinalIgnoreCase))
                    continue;
                double oldGain = trend.DisplayGain, oldOffset = trend.DisplayOffset;
                trend.TransformCalibration(oldGain, oldOffset, gain, offset);
                trend.DecimalPlaces = decimals;
            }
            else if (item is DashboardGaugeState gauge)
            {
                if (!string.IsNullOrWhiteSpace(gauge.Unit) && !string.Equals(gauge.Unit, unit, StringComparison.OrdinalIgnoreCase))
                    continue;
                gauge.DisplayGain = gain;
                gauge.DisplayOffset = offset;
                gauge.DecimalPlaces = decimals;
            }
            else if (item is PidLiveValue live)
                ApplyCalibrationToLiveValue(live, unit, gain, offset, decimals);
        }
    }

    private void ApplyCalibrationToLiveValue(PidLiveValue live, string unit, double gain, double offset, int decimals)
    {
        if (!live.CanTrend)
        {
            live.DisplayGain = 1.0; live.DisplayOffset = 0; live.DecimalPlaces = decimals;
            return;
        }
        string liveUnit = string.IsNullOrWhiteSpace(live.TrendUnit) ? live.Units : live.TrendUnit;
        bool unitMatches = string.IsNullOrWhiteSpace(liveUnit) || string.Equals(liveUnit, unit, StringComparison.OrdinalIgnoreCase);
        double effectiveGain = unitMatches ? gain : 1.0;
        double effectiveOffset = unitMatches ? offset : 0;
        live.DisplayGain = effectiveGain;
        live.DisplayOffset = effectiveOffset;
        live.DecimalPlaces = decimals;
        if (!live.UnadjustedNumericValue.HasValue) return;
        double adjusted = live.UnadjustedNumericValue.Value * effectiveGain + effectiveOffset;
        live.LatestNumericValue = adjusted;
        live.DisplayValue = FormatValue(adjusted, liveUnit, decimals);
    }

    private void SetLiveSignalCalibration(PidLiveValue live, double gain, double offset, int decimals)
    {
        string key = PresentationKey(live);
        string unit = string.IsNullOrWhiteSpace(live.TrendUnit) ? UnitConverter.TargetUnit(live.Definition.Units, _settings) : live.TrendUnit;
        _syncingPresentation = true;
        try
        {
            StoreSignalAdjustment(key, unit, gain, offset, decimals);
            ApplyCalibrationToLiveValue(live, unit, gain, offset, decimals);
            PropagateSignalCalibration(key, unit, gain, offset, decimals, live);
            SaveSettingsAndGlobalConfiguration();
            _logger.LogEvent("SIGNAL_CALIBRATION_CHANGED", ShortVehicleId(), live.Definition.Request, live.Source,
                message: $"{live.Definition.Name}; gain={gain:0.######}; offset={offset:0.######} {unit}; decimals={decimals}");
            _sessionLog.WriteLine($"[{DateTime.Now:O}] SIGNAL CALIBRATION key={key} gain={gain:0.######} offset={offset:0.######} unit={unit} decimals={decimals}");
            TrendChart.InvalidateMeasure();
            TrendChart.InvalidateVisual();
            ApplyLiveDataView();
        }
        finally { _syncingPresentation = false; }
    }

    private void LiveDataTable_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source) return;
        DataGridRow? row = FindVisualAncestor<DataGridRow>(source);
        if (row is null) return;
        grid.SelectedItem = row.Item;
        row.IsSelected = true;
    }

    private void LiveValue_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<ButtonBase>(source) is not null) return;

        PidLiveValue? live = sender switch
        {
            DataGrid _ => FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as PidLiveValue,
            FrameworkElement element => element.DataContext as PidLiveValue,
            _ => null
        };
        if (live is null || !live.CanTrend) return;

        e.Handled = true;
        string unit = string.IsNullOrWhiteSpace(live.TrendUnit) ? UnitConverter.TargetUnit(live.Definition.Units, _settings) : live.TrendUnit;
        SignalOffsetSetting adjustment = GetSignalAdjustment(PresentationKey(live), unit);
        var dialog = new SignalOffsetWindow(live, adjustment.Gain, adjustment.Offset, adjustment.DecimalPlaces) { Owner = this };
        if (dialog.ShowDialog() == true)
            SetLiveSignalCalibration(live, dialog.SelectedGain, dialog.SelectedOffset, dialog.SelectedDecimalPlaces);
    }

    private void CreateCalculatedValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not PidLiveValue source || !source.CanTrend) return;
        CreateCalculatedValueFor(source);
    }

    private void CreateCalculatedFromConfiguredSignal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not IConfigurableSignal configured) return;
        PidLiveValue? source = LiveValues.FirstOrDefault(x => PresentationKey(x).Equals(configured.Key, StringComparison.OrdinalIgnoreCase));
        if (source is not null && source.CanTrend) CreateCalculatedValueFor(source);
    }

    private void CreateCalculatedValueFor(PidLiveValue source)
    {
        string sourceKey = PresentationKey(source);
        var dialog = new CalculatedValueWindow(source, sourceKey) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        _settings.CalculatedSignals.Add(dialog.Result);
        EnsureCalculatedLiveValue(dialog.Result);
        UpdateCalculatedSignalsFromSource(source);
        SaveSettingsAndGlobalConfiguration();
        _sessionLog.WriteLine($"[{DateTime.Now:O}] CALCULATED VALUE CREATE id={dialog.Result.Id} name={dialog.Result.Name} source={sourceKey} formula={dialog.Result.Formula}");
    }

    private void DeleteCalculatedValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.DataContext is not PidLiveValue live || !live.IsCalculated || string.IsNullOrWhiteSpace(live.CalculatedId)) return;
        _settings.CalculatedSignals.RemoveAll(x => x.Id.Equals(live.CalculatedId, StringComparison.OrdinalIgnoreCase));
        TrendSeriesState? trend = TrendSeries.FirstOrDefault(x => x.Request.Equals(live.Definition.Request, StringComparison.OrdinalIgnoreCase));
        if (trend is not null) TrendSeries.Remove(trend);
        DashboardGaugeState? gauge = DashboardGauges.FirstOrDefault(x => x.Request.Equals(live.Definition.Request, StringComparison.OrdinalIgnoreCase));
        if (gauge is not null) DashboardGauges.Remove(gauge);
        LiveValues.Remove(live);
        SaveSettingsAndGlobalConfiguration();
    }

    private PidLiveValue EnsureCalculatedLiveValue(CalculatedSignalSetting calc)
    {
        string request = "CALC:" + calc.Id;
        PidLiveValue? existing = LiveValues.FirstOrDefault(x => x.IsCalculated && x.CalculatedId.Equals(calc.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var def = new PidDefinition
        {
            Enabled = true, Name = calc.Name, Request = request, ResponseMode = "CALC", PID = calc.Id,
            Formula = calc.Formula, Units = calc.Units, PollMs = 0, DiscoveryOnly = false, DerivedOnly = true,
            DisplayKind = "Calculated", Priority = 5, DefaultPriority = 5, Category = "Calculated",
            Notes = $"User calculated from {calc.SourceName}. Formula: {calc.Formula}"
        };
        var live = new PidLiveValue
        {
            Definition = def, Source = "Calculated", SourceDisplay = "Calculated", CanTrend = true,
            TrendUnit = calc.Units, Units = calc.Units, IsCalculated = true, CalculatedId = calc.Id,
            DecimalPlaces = calc.DecimalPlaces
        };
        LiveValues.Add(live);
        return live;
    }

    private void UpdateCalculatedSignalsFromSource(PidLiveValue source)
    {
        if (!source.LatestNumericValue.HasValue) return;
        string sourceKey = PresentationKey(source);
        foreach (CalculatedSignalSetting calc in _settings.CalculatedSignals.Where(x => x.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            PidLiveValue live = EnsureCalculatedLiveValue(calc);
            if (!CalculatedExpression.TryEvaluate(calc.Formula, source.LatestNumericValue.Value, calc.B, calc.C, calc.D, out double baseValue, out string error))
            {
                live.DisplayValue = "FORMULA ERROR"; live.SampleStatus = error; live.LatestNumericValue = null; continue;
            }
            string key = PresentationKey(live);
            SignalOffsetSetting adjustment = GetSignalAdjustment(key, calc.Units);
            live.DisplayGain = adjustment.Gain; live.DisplayOffset = adjustment.Offset;
            live.DecimalPlaces = adjustment.DecimalPlaces >= 0 ? adjustment.DecimalPlaces : calc.DecimalPlaces;
            live.UnadjustedNumericValue = baseValue;
            double displayed = baseValue * live.DisplayGain + live.DisplayOffset;
            live.LatestNumericValue = displayed;
            live.DisplayValue = FormatValue(displayed, calc.Units, live.DecimalPlaces);
            live.Units = calc.Units; live.TrendUnit = calc.Units;
            live.Raw = $"{calc.Formula}; A={source.LatestNumericValue.Value:0.########}; B={calc.B:0.########}; C={calc.C:0.########}; D={calc.D:0.########}";
            live.SampleStatus = $"Calculated from {source.Definition.Name}";
            live.RecordSample(DateTime.Now, 0, 0, 0);
            RecordTrendSample(live, displayed, calc.Units);
            RecordDashboardSample(live, displayed, calc.Units);
            _logger.Log(live.Definition, baseValue, displayed, calc.Units, ShortVehicleId(), live.Raw, 0, 0, 0, "Calculated", sampleStatus: live.SampleStatus);
            UpdateCalculatedSignalsFromSource(live);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            try { current = VisualTreeHelper.GetParent(current); }
            catch (InvalidOperationException) { current = LogicalTreeHelper.GetParent(current); }
        }
        return null;
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PidLiveValue live }) return;
        live.IsPinned = !live.IsPinned;
        string key = PresentationKey(live);
        if (live.IsPinned) _settings.PinnedSignals.Add(key); else _settings.PinnedSignals.Remove(key);
        RefreshPinnedReadings();
        SaveSettingsAndGlobalConfiguration();
        if (_liveSortColumn.Equals("Star", StringComparison.OrdinalIgnoreCase))
            ApplyLiveDataView();
        e.Handled = true;
    }

    private void RefreshPinnedReadings()
    {
        foreach (var old in PinnedReadings.Where(x => !LiveValues.Contains(x) || !x.IsPinned).ToArray())
            PinnedReadings.Remove(old);
        foreach (var live in LiveValues.Where(x => x.IsPinned && !PinnedReadings.Contains(x)))
            PinnedReadings.Add(live);
    }

    private void LiveDataTable_Sorting(object sender, DataGridSortingEventArgs e)
    {
        string column = string.IsNullOrWhiteSpace(e.Column.SortMemberPath)
            ? e.Column.Header?.ToString() ?? ""
            : e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(column)) return;

        if (_liveSortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
            _liveSortState = _liveSortState == 1 ? 2 : _liveSortState == 2 ? 0 : 1;
        else
        {
            _liveSortColumn = column;
            _liveSortState = 1;
        }

        foreach (DataGridColumn gridColumn in LiveDataTable.Columns)
            gridColumn.SortDirection = null;

        if (_liveSortState == 0)
        {
            _liveSortColumn = "";
        }
        else
        {
            // Star's first click means pinned on top. For a boolean that corresponds to
            // descending; ordinary text/numeric columns use ascending on the first click.
            bool star = column.Equals("Star", StringComparison.OrdinalIgnoreCase);
            e.Column.SortDirection = _liveSortState == 1
                ? (star ? ListSortDirection.Descending : ListSortDirection.Ascending)
                : (star ? ListSortDirection.Ascending : ListSortDirection.Descending);
        }

        e.Handled = true;
        ApplyLiveDataView();
        SaveGlobalConfiguration();
    }

    private void SignalFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _liveSignalFilter = sender is TextBox box ? box.Text.Trim() : "";
        ApplyLiveDataView();
        SaveGlobalConfiguration();
    }

    private void SignalFilterBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Keep a click in the filter editor from being interpreted as a Signal-header sort.
        if (sender is TextBox box)
        {
            box.Focus();
            if (!box.IsKeyboardFocusWithin)
                Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        }
    }

    private void ApplyLiveDataView()
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(LiveValues);
        view.Filter = item => item is PidLiveValue live &&
            (string.IsNullOrWhiteSpace(_liveSignalFilter) ||
             live.Definition.Name.Contains(_liveSignalFilter, StringComparison.OrdinalIgnoreCase));

        if (view is ListCollectionView listView)
        {
            listView.CustomSort = new LiveDataComparer(_liveSortColumn, _liveSortState);
        }
        else
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(PidLiveValue.ModuleOrder), ListSortDirection.Ascending));
        }
        view.Refresh();
    }

    private sealed class LiveDataComparer : IComparer
    {
        private readonly string _column;
        private readonly int _state;

        public LiveDataComparer(string column, int state)
        {
            _column = column ?? "";
            _state = state;
        }

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is not PidLiveValue a) return -1;
            if (y is not PidLiveValue b) return 1;

            int result = _state == 0 || string.IsNullOrWhiteSpace(_column)
                ? CompareDefault(a, b)
                : CompareSelected(a, b);
            return result != 0 ? result : CompareDefault(a, b);
        }

        private int CompareSelected(PidLiveValue a, PidLiveValue b)
        {
            int result;
            switch (_column.ToUpperInvariant())
            {
                case "STAR":
                    // State 1 = starred first; state 2 = starred last.
                    result = a.IsPinned.CompareTo(b.IsPinned);
                    return _state == 1 ? -result : result;
                case "SIGNAL":
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
                    break;
                case "VALUE":
                    if (a.LatestNumericValue.HasValue && b.LatestNumericValue.HasValue)
                        result = a.LatestNumericValue.Value.CompareTo(b.LatestNumericValue.Value);
                    else if (a.LatestNumericValue.HasValue != b.LatestNumericValue.HasValue)
                        result = a.LatestNumericValue.HasValue ? -1 : 1;
                    else
                        result = StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayValue, b.DisplayValue);
                    break;
                case "UNIT":
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(a.Units, b.Units);
                    break;
                case "SOURCE":
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(a.SourceDisplay, b.SourceDisplay);
                    break;
                case "UPDATED":
                    result = Nullable.Compare(a.Updated, b.Updated);
                    break;
                default:
                    return CompareDefault(a, b);
            }
            return _state == 2 ? -result : result;
        }

        private static int CompareDefault(PidLiveValue a, PidLiveValue b)
        {
            int result = b.Definition.Priority.CompareTo(a.Definition.Priority);
            if (result != 0) return result;
            result = a.ModuleOrder.CompareTo(b.ModuleOrder);
            if (result != 0) return result;
            result = StringComparer.CurrentCultureIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
            if (result != 0) return result;
            result = StringComparer.CurrentCultureIgnoreCase.Compare(a.SourceDisplay, b.SourceDisplay);
            if (result != 0) return result;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Request, b.Definition.Request);
        }
    }

    private void SaveSettingsAndGlobalConfiguration()
    {
        _settingsStore.Save(_settings);
        SaveGlobalConfiguration();
    }

    private void SaveGlobalConfiguration()
    {
        if (_isReplayMode) return;
        try
        {
            _globalConfigurationStore.Save(
                _settings,
                TrendSeries,
                DashboardGauges,
                _currentVehicleSignature,
                _liveSortColumn,
                _liveSortState,
                _liveSignalFilter,
                CardViewCheck?.IsChecked == true);
        }
        catch
        {
            // The regular settings file remains authoritative in this first implementation.
            // A configuration snapshot failure must not interrupt live diagnostics.
        }
    }
}
