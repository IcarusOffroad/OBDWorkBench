using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace OBDWorkbench;

public partial class MainWindow
{
    // Show only bytes that were actually written to the Workbench console. This
    // bounded display never transmits, polls, or invents diagnostic frames.
    private static readonly Regex TrafficBytes = new(
        @"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[\s:-]+){3,}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly Queue<string> _lcarsTrafficLines = new();
    private bool _lcarsTrafficRefreshQueued;

    private void AppendLcarsTraffic(string text)
    {
        if (!_settings.UseLcarsSkin || LcarsTrafficText is null) return;
        foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (Match match in TrafficBytes.Matches(line))
            {
                string direction = line.Contains(">>", StringComparison.Ordinal) ? "TX" :
                    line.Contains("<<", StringComparison.Ordinal) ? "RX" : "DATA";
                string bytes = Regex.Replace(match.Value.Trim(), @"[\s:-]+", " ").ToUpperInvariant();
                if (bytes.Length > 72) bytes = bytes[..72] + " …";
                _lcarsTrafficLines.Enqueue($"{direction,-4}  {bytes}");
                while (_lcarsTrafficLines.Count > 2) _lcarsTrafficLines.Dequeue();
            }
        }
        if (_lcarsTrafficLines.Count == 0 || _lcarsTrafficRefreshQueued) return;
        _lcarsTrafficRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _lcarsTrafficRefreshQueued = false;
            LcarsTrafficText.Text = string.Join(Environment.NewLine, _lcarsTrafficLines);
        }), DispatcherPriority.Background);
    }

    private void ResetLcarsTraffic()
    {
        _lcarsTrafficLines.Clear();
        if (LcarsTrafficText is not null) LcarsTrafficText.Text = "Waiting for diagnostic hex traffic";
    }
}
