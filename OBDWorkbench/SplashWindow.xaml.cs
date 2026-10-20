using System.Reflection;
using System.Windows;
namespace OBDWorkbench;
public partial class SplashWindow : Window
{
    public event Action? ReplayRequested;
    public event Action? SkipRequested;
    public event Action? StopRequested;
    public event Action<StartupConnectionFamily>? ConnectionModeSelected;

    private void Replay_Click(object sender, RoutedEventArgs e) => ReplayRequested?.Invoke();

    private void Skip_Click(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void OBDII_Click(object sender, RoutedEventArgs e) => SelectMode(StartupConnectionFamily.ObdII);
    private void TD5_Click(object sender, RoutedEventArgs e) => SelectMode(StartupConnectionFamily.Td5);
    private void SSM_Click(object sender, RoutedEventArgs e) => SelectMode(StartupConnectionFamily.Ssm);
    private void GEMS_Click(object sender, RoutedEventArgs e) => SelectMode(StartupConnectionFamily.Gems);

    private void SelectMode(StartupConnectionFamily mode)
    {
        ConnectionModeSelected?.Invoke(mode);
        ModeSelectionPanel.Visibility = Visibility.Collapsed;
        ConnectionPhasePanel.Visibility = Visibility.Visible;
        StartupStatus.Text = mode switch
        {
            StartupConnectionFamily.Td5 => "TD5 selected • preparing native K-Line connection…",
            StartupConnectionFamily.Ssm => "SSM selected • preparing native 4800-baud K-Line connection…",
            StartupConnectionFamily.Gems => "GEMS selected • preparing ELM/STN dealer-session connection…",
            _ => "OBDII selected • preparing ELM327 connection…"
        };
    }

    public SplashWindow()
    {
        InitializeComponent();
        OBDWorkbench.Services.ThemeService.Apply(this, true);
        OBDWorkbench.Services.WindowPlacement.Attach(this, 1200, 720, maximize: false);
        SizeChanged += (_, _) =>
        {
            // Scale the complete image once. Reserve its upper 64% for the logo,
            // then place the mode/connection controls over the continuous lower artwork.
            var image = SplashBackgroundImage.Source;
            double headerAspect = image.Width / (image.Height * 0.64);
            double availableWidth = Math.Max(1, ActualWidth - 32);
            double headerHeight = Math.Clamp(availableWidth / headerAspect, 90, Math.Max(90, ActualHeight * 0.58));
            SplashArtwork.Height = headerHeight;
            SplashBackgroundImage.Width = headerHeight * headerAspect;
            SplashBackgroundImage.Height = headerHeight / 0.64;
            ConnectionScroll.MaxHeight = Math.Max(100, ActualHeight * 0.38);
        };
        VersionLabel.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version}";
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) e.Handled = true; };
    }
    public void SetStatus(string status) => StartupStatus.Text = status;
}
