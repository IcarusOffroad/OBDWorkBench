using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OBDWorkbench;

public partial class MainWindow
{
    private static readonly string[] LcarsSections = { "LIVE DATA", "GAUGES", "TRENDS", "CONSOLE", "TROUBLE CODES", "MODULES", "PID EXPLORER" };
    private int? _classicDashboardColumns;
    private bool _changingLcarsColumns;

    private void ApplyLcarsLayout()
    {
        bool enabled = _settings.UseLcarsSkin;
        if (enabled && _classicDashboardColumns is null)
        {
            _classicDashboardColumns = DashboardGridColumns;
            _changingLcarsColumns = true;
            DashboardGridColumns = 2;
            DashboardGridCombo.SelectedIndex = 0;
            _changingLcarsColumns = false;
        }
        else if (!enabled && _classicDashboardColumns is int classicColumns)
        {
            _classicDashboardColumns = null;
            _changingLcarsColumns = true;
            DashboardGridColumns = classicColumns;
            DashboardGridCombo.SelectedIndex = classicColumns - 2;
            _changingLcarsColumns = false;
        }
        LcarsSidebarColumn.Width = new GridLength(enabled ? 196 : 0);
        ClassicTopBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ApplyPill(ReplayOpenButton, enabled, "LcarsPeachBrush");
        ApplyPill(ReplayPlayPauseButton, enabled, "LcarsBlueBrush");
        ApplyPill(LcarsTrendLiveButton, enabled, "LcarsActionBrush");
        ApplyPill(LoggingButton, enabled, "LcarsActionBrush");
        LoggingButton.SetResourceReference(Control.ForegroundProperty,
            enabled ? "LcarsControlTextBrush" : "AccentBrush");
        LcarsTrendRailColumn.Width = new GridLength(enabled ? 290 : 0);
        LiveDataRailColumn.Width = new GridLength(enabled ? 280 : 240);
        TrendBottomLegend.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ClassicPinnedHeader.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        DashboardStatusText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        TrendStatusText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LiveDataTable.RowHeight = enabled ? 72 : 36;
        LiveDataTable.ColumnHeaderHeight = enabled ? 58 : 62;
        for (int i = 1; i <= 5; i++) LiveDataTable.Columns[i].Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LiveDataTable.Columns[6].Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LiveDataTitleRow.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (enabled && LcarsSignalFilterBox is not null && LcarsSignalFilterBox.Text != _liveSignalFilter)
            LcarsSignalFilterBox.Text = _liveSignalFilter;
        if (!enabled && SignalFilterBox is not null && SignalFilterBox.Text != _liveSignalFilter)
            SignalFilterBox.Text = _liveSignalFilter;
        ReplayDetailsRow.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LiveDataTable.FontSize = enabled ? 15 : 13;
        LiveDataTable.AlternationCount = enabled ? 3 : 0;
        LiveDataTable.RowStyle = enabled ? (Style)FindResource("LcarsLiveRowStyle") : null;
        MinWidth = enabled ? 1200 : 360;
        // A skin/layout refresh must not reopen replay's initially collapsed panel.
        // Keep the user's replay toggle state; live mode retains its existing skin policy.
        if (IsLoaded) SetConnectionPanelCollapsed(_isReplayMode ? _connectionPanelCollapsed : enabled);
        if (enabled && RawLogBox is not null)
        {
            ResetLcarsTraffic();
            string recent = RawLogBox.Text;
            AppendLcarsTraffic(recent.Length > 4000 ? recent[^4000..] : recent);
        }
        LcarsSidebar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ClassicNavigation.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ClassicBrandTitle.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (!enabled && ClassicNavigation.Content is System.Windows.Controls.WrapPanel classicItems)
            foreach (object item in classicItems.Children)
                if (item is RadioButton radio && radio.Tag?.ToString() == MainTabs.SelectedIndex.ToString())
                    radio.IsChecked = true;
        UpdateLcarsSelection();
    }

    private void ApplyPill(Button button, bool enabled, string brushKey)
    {
        if (enabled) button.Style = (Style)FindResource("LcarsPillButtonStyle");
        else button.ClearValue(Button.StyleProperty);
        if (enabled) button.SetResourceReference(Control.BackgroundProperty, brushKey);
        else button.ClearValue(Control.BackgroundProperty);
    }

    private void LcarsNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int index))
            MainTabs.SelectedIndex = index;
    }

    private void MainTabs_LcarsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainTabs)) UpdateLcarsSelection();
    }

    private void UpdateLcarsSelection()
    {
        if (MainTabs is null || LcarsSectionTitle is null || LcarsNavigation is null) return;
        int index = MainTabs.SelectedIndex;
        LcarsSectionTitle.Text = index >= 0 && index < LcarsSections.Length ? LcarsSections[index] : "WORKBENCH";
        string accentKey = index switch
        {
            0 or 4 => "LcarsLilacBrush",
            1 or 5 => "LcarsBlueBrush",
            _ => "LcarsPeachBrush"
        };
        LcarsSectionHeader.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, accentKey);
        foreach (object item in LcarsNavigation.Children)
        {
            if (item is not Button button) continue;
            bool selected = button.Tag?.ToString() == index.ToString();
            button.BorderBrush = selected ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.Transparent;
            button.BorderThickness = selected ? new Thickness(3) : new Thickness(0);
            button.FontSize = selected ? 16 : 14;
        }
    }
}
