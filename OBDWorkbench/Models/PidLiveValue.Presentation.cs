using System.Windows.Media;
namespace OBDWorkbench.Models;
public sealed partial class PidLiveValue
{
    private string _sourceDisplay = "";
    private int _moduleOrder = 2;
    public string SourceDisplay { get => string.IsNullOrEmpty(_sourceDisplay) ? Source : _sourceDisplay; set { _sourceDisplay = value; OnPropertyChanged(); } }
    public int ModuleOrder { get => _moduleOrder; set { _moduleOrder = value; OnPropertyChanged(); } }
    private bool _isPinned;
    private string _signalColor = "#D99A6C";
    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned == value) return; _isPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(PinGlyph)); OnPropertyChanged(nameof(PinBrush)); }
    }
    public Brush PinBrush => IsPinned ? Brushes.Gold : Brushes.Gray;
    public string PinGlyph => IsPinned ? "★" : "☆";
    public string ColorHex
    {
        get => _signalColor;
        set { string color = TrendPalette.NormalizeHex(value); if (_signalColor == color) return; _signalColor = color; OnPropertyChanged(); OnPropertyChanged(nameof(AccentBrush)); }
    }
    public Brush AccentBrush => TrendPalette.BrushForHex(ColorHex);
}
