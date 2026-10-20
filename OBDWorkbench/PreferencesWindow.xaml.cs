using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public sealed class PollPriorityPreferenceRow
{
    public string Request { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public int DefaultPriority { get; init; }
    public IReadOnlyList<string> PriorityChoices { get; init; } = Array.Empty<string>();
    public string SelectedPriority { get; set; } = "3 - Normal";
}

public partial class PreferencesWindow : Window
{
    public event Action? GpsRequested;
    private void GpsSettings_Click(object sender, RoutedEventArgs e) => GpsRequested?.Invoke();

    private static readonly string[] PollingPriorityChoices =
    {
        "1 - Low",
        "2 - Background",
        "3 - Normal",
        "4 - High",
        "5 - Live / foreground"
    };

    private readonly AppSettingsData _settings;
    private readonly Dictionary<string, ComboBox> _boxes;
    private readonly ObservableCollection<PollPriorityPreferenceRow> _priorityRows = new();

    public PreferencesWindow(AppSettingsData settings, IEnumerable<PidDefinition> definitions)
    {
        InitializeComponent();
        ThemeService.Apply(this, settings);
        _settings = settings;
        _boxes = new()
        {
            ["Temperature"] = TemperatureCombo,
            ["Pressure"] = PressureCombo,
            ["Speed"] = SpeedCombo,
            ["Distance"] = DistanceCombo,
            ["Flow"] = FlowCombo
        };

        IcarusThemeCheck.IsChecked = settings.UseIcarusTheme;
        LcarsSkinCheck.IsChecked = settings.UseLcarsSkin;
        FollowThemeCheck.IsChecked = settings.FollowSystemTheme;
        ConfirmCommandsCheck.IsChecked = settings.ConfirmDisruptiveCommands;
        AutoCollapseCheck.IsChecked = settings.AutoCollapseConnectionPanel;
        UnitSystemCombo.SelectedIndex = settings.UnitSystem == "Metric" ? 1 : 0;
        foreach (var pair in _boxes)
        {
            pair.Value.Items.Add("Default");
            foreach (string choice in UnitConverter.Choices[pair.Key]) pair.Value.Items.Add(choice);
            pair.Value.SelectedItem = settings.UnitOverrides.TryGetValue(pair.Key, out string? value) ? value : "Default";
        }

        foreach (var group in definitions.Where(d => !d.DiscoveryOnly)
                     .GroupBy(d => d.Request, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.First().Category).ThenBy(g => g.Key))
        {
            PidDefinition[] members = group.ToArray();
            PidDefinition[] semantic = members.Where(d => !LooksRaw(d)).ToArray();
            if (semantic.Length == 0) semantic = members.Where(d => !d.DerivedOnly).ToArray();
            if (semantic.Length == 0) semantic = members;

            string[] names = semantic.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string primary = names.Length == 0 ? group.Key : names[0];
            if (names.Length > 1) primary += $" (+{names.Length - 1})";
            string description = names.Length > 0 ? string.Join("; ", names) : "Raw diagnostic request";

            int defaultPriority = Math.Clamp(members.Max(d => d.DefaultPriority), 1, 5);
            int selected = settings.PollPriorityOverrides.TryGetValue(group.Key, out int saved)
                ? Math.Clamp(saved, 1, 5)
                : defaultPriority;
            _priorityRows.Add(new PollPriorityPreferenceRow
            {
                Request = group.Key,
                Name = primary,
                Description = description,
                Category = semantic.FirstOrDefault()?.Category ?? members[0].Category,
                DefaultPriority = defaultPriority,
                PriorityChoices = PollingPriorityChoices,
                SelectedPriority = ChoiceFor(selected)
            });
        }
        PollingPriorityGrid.ItemsSource = _priorityRows;
    }


    private static bool LooksRaw(PidDefinition definition)
    {
        string name = definition.Name ?? "";
        return name.Contains(" Raw", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Raw", StringComparison.OrdinalIgnoreCase) ||
               definition.DisplayKind.Equals("RawText", StringComparison.OrdinalIgnoreCase);
    }

    private static string ChoiceFor(int priority) => PollingPriorityChoices[Math.Clamp(priority, 1, 5) - 1];

    private static int PriorityFromChoice(string? choice)
    {
        if (!string.IsNullOrWhiteSpace(choice) && char.IsDigit(choice[0]))
            return Math.Clamp(choice[0] - '0', 1, 5);
        return 3;
    }

    private void ResetPollingPriorities_Click(object sender, RoutedEventArgs e)
    {
        foreach (PollPriorityPreferenceRow row in _priorityRows)
            row.SelectedPriority = ChoiceFor(row.DefaultPriority);
        PollingPriorityGrid.Items.Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.UseIcarusTheme = IcarusThemeCheck.IsChecked == true;
        _settings.UseLcarsSkin = LcarsSkinCheck.IsChecked == true;
        _settings.FollowSystemTheme = FollowThemeCheck.IsChecked == true;
        _settings.ConfirmDisruptiveCommands = ConfirmCommandsCheck.IsChecked == true;
        _settings.AutoCollapseConnectionPanel = AutoCollapseCheck.IsChecked == true;
        _settings.UnitSystem = UnitSystemCombo.SelectedIndex == 1 ? "Metric" : "Imperial";
        foreach (var pair in _boxes)
        {
            string value = pair.Value.SelectedItem?.ToString() ?? "Default";
            if (value == "Default") _settings.UnitOverrides.Remove(pair.Key);
            else _settings.UnitOverrides[pair.Key] = value;
        }

        _settings.PollPriorityOverrides.Clear();
        foreach (PollPriorityPreferenceRow row in _priorityRows)
        {
            int priority = PriorityFromChoice(row.SelectedPriority);
            if (priority != row.DefaultPriority)
                _settings.PollPriorityOverrides[row.Request] = priority;
        }
        DialogResult = true;
    }
}
