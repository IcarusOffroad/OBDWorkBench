namespace OBDWorkbench.Models;

public sealed partial class PidLiveValue
{
    public void ResetReplayState()
    {
        _displayValue = "--";
        _raw = "";
        _updated = null;
        _lastRttMs = null;
        _lastFirstByteMs = null;
        _lastTailMs = null;
        _actualHz = null;
        _lastRateSample = null;
        _latestNumericValue = null;
        _unadjustedNumericValue = null;
        _displayOffset = 0;
        _recentIntervalsSeconds.Clear();
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(Raw));
        OnPropertyChanged(nameof(Updated));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(LastRttMs));
        OnPropertyChanged(nameof(LastFirstByteMs));
        OnPropertyChanged(nameof(LastTailMs));
        OnPropertyChanged(nameof(ActualHz));
        OnPropertyChanged(nameof(PerformanceText));
        OnPropertyChanged(nameof(LatestNumericValue));
        OnPropertyChanged(nameof(UnadjustedNumericValue));
        OnPropertyChanged(nameof(DisplayOffset));
        OnPropertyChanged(nameof(OffsetText));
    }
}
