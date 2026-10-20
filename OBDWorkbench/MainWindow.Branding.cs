using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace OBDWorkbench;
public partial class MainWindow
{
    // Build each row's menu outside a Style setter. This avoids WPF markup
    // compiler event resolution inside the DataGrid's column/style context.
    // PlacementTarget binding follows the current row when virtualization reuses it.
    private void LiveDataTable_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        var menu = new ContextMenu();
        menu.SetBinding(FrameworkElement.DataContextProperty,
            new Binding("PlacementTarget.DataContext") { RelativeSource = RelativeSource.Self });
        var trend = new MenuItem { Header = "Add to Trend" };
        trend.SetBinding(UIElement.IsEnabledProperty, new Binding("CanTrend"));
        trend.Click += AddToTrend_Click;
        var dashboard = new MenuItem { Header = "Add to Dashboard" };
        dashboard.SetBinding(UIElement.IsEnabledProperty, new Binding("CanTrend"));
        dashboard.Click += AddToDashboard_Click;
        var both = new MenuItem { Header = "Add to Trend & Gauge" };
        both.SetBinding(UIElement.IsEnabledProperty, new Binding("CanTrend"));
        both.Click += AddToTrendAndGauge_Click;
        menu.Items.Add(trend);
        menu.Items.Add(dashboard);
        menu.Items.Add(both);
        e.Row.ContextMenu = menu;
    }

    private void LiveView_Changed(object sender, RoutedEventArgs e)
    {
        if (LiveTablePanel is null || LiveDataScrollViewer is null) return;
        bool cards = CardViewCheck.IsChecked == true;
        LiveTablePanel.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        LiveDataScrollViewer.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        SaveGlobalConfiguration();
    }
    private void TrendLayout_Changed(object sender, RoutedEventArgs e)
    {
        if (TrendChart is null) return;
        TrendChart.Stacked = OverlayTrendCheck.IsChecked != true;
        if (_settings is not null) { _settings.OverlayPlots = !TrendChart.Stacked; SaveSettingsAndGlobalConfiguration(); }
        TrendChart.InvalidateMeasure();
        TrendChart.InvalidateVisual();
    }
}
