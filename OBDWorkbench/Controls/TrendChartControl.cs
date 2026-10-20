using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using OBDWorkbench.Models;

namespace OBDWorkbench.Controls;

public sealed class TrendChartControl : FrameworkElement
{
    public IReadOnlyList<TrendSeriesState>? Series { get; set; }
    public DateTime? ViewEndUtc { get; set; }
    public bool Stacked { get; set; } = true;
    private Point? _dragStart;
    private Point? _dragCurrent;
    private Rect _lastPlot;
    private DateTime? _zoomStartUtc;
    private DateTime? _zoomEndUtc;
    private double _zoomTopFraction;
    private double _zoomBottomFraction = 1;
    public bool HasZoom => _zoomStartUtc.HasValue && _zoomEndUtc.HasValue && _zoomEndUtc.Value > _zoomStartUtc.Value;
    protected override Size MeasureOverride(Size availableSize)
    {
        double desiredHeight = Stacked ? Math.Max(300, (Series?.Count ?? 0) * 150) : 320;
        return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? desiredHeight : Math.Max(desiredHeight, availableSize.Height));
    }

    public int WindowSeconds { get; set; } = 60;

    public TrendChartControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        MinHeight = 260;
        MouseLeftButtonDown += ChartMouseDown;
        MouseMove += ChartMouseMove;
        MouseLeftButtonUp += ChartMouseUp;
    }

    public void ResetZoom()
    {
        _zoomStartUtc = null; _zoomEndUtc = null; _zoomTopFraction = 0; _zoomBottomFraction = 1;
        _dragStart = _dragCurrent = null; ReleaseMouseCapture(); InvalidateVisual();
    }

    private void ChartMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 || _lastPlot.IsEmpty) return;
        Point p = e.GetPosition(this);
        if (!_lastPlot.Contains(p)) return;
        _dragStart = _dragCurrent = p;
        CaptureMouse();
        e.Handled = true;
    }

    private void ChartMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragStart.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
        Point p = e.GetPosition(this);
        p.X = Math.Clamp(p.X, _lastPlot.Left, _lastPlot.Right);
        p.Y = Math.Clamp(p.Y, _lastPlot.Top, _lastPlot.Bottom);
        _dragCurrent = p;
        InvalidateVisual();
        e.Handled = true;
    }

    private void ChartMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragStart.HasValue || !_dragCurrent.HasValue) return;
        Point a = _dragStart.Value, b = _dragCurrent.Value;
        _dragStart = _dragCurrent = null; ReleaseMouseCapture();
        var rect = new Rect(new Point(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y)), new Point(Math.Max(a.X,b.X), Math.Max(a.Y,b.Y)));
        if (rect.Width >= 12 && rect.Height >= 12 && _lastPlot.Width > 1 && _lastPlot.Height > 1)
        {
            bool wasZoomed = HasZoom;
            double previousTop = _zoomTopFraction;
            double previousBottom = _zoomBottomFraction;
            DateTime currentEnd = wasZoomed ? _zoomEndUtc!.Value : (ViewEndUtc ?? DateTime.UtcNow);
            DateTime currentStart = wasZoomed ? _zoomStartUtc!.Value : currentEnd.AddSeconds(-Math.Clamp(WindowSeconds, 5, 3600));
            double x0 = Math.Clamp((rect.Left - _lastPlot.Left) / _lastPlot.Width, 0, 1);
            double x1 = Math.Clamp((rect.Right - _lastPlot.Left) / _lastPlot.Width, 0, 1);
            double span = (currentEnd - currentStart).TotalSeconds;
            _zoomStartUtc = currentStart.AddSeconds(span * x0);
            _zoomEndUtc = currentStart.AddSeconds(span * x1);
            double selectedTop = Math.Clamp((rect.Top - _lastPlot.Top) / _lastPlot.Height, 0, 1);
            double selectedBottom = Math.Clamp((rect.Bottom - _lastPlot.Top) / _lastPlot.Height, 0, 1);
            double oldTop = wasZoomed ? previousTop : 0;
            double oldBottom = wasZoomed ? previousBottom : 1;
            double oldSpan = Math.Max(0.0001, oldBottom - oldTop);
            _zoomTopFraction = oldTop + oldSpan * selectedTop;
            _zoomBottomFraction = oldTop + oldSpan * selectedBottom;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 160 || height < 100) return;

        Brush textBrush = Application.Current.TryFindResource("PrimaryTextBrush") as Brush ?? Brushes.Black;
        Brush secondaryBrush = Application.Current.TryFindResource("SecondaryTextBrush") as Brush ?? Brushes.Gray;
        Brush borderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        Brush backgroundBrush = Application.Current.TryFindResource("ControlBackgroundBrush") as Brush ?? Brushes.White;
        dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));

        const double left = 54;
        const double right = 76; // reserve room for live value labels at the lead edge
        const double top = 18;
        const double bottom = 34;
        Rect plot = new(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));
        _lastPlot = plot;
        dc.DrawRectangle(null, new Pen(borderBrush, 1), plot);

        DateTime now = HasZoom ? _zoomEndUtc!.Value : (ViewEndUtc ?? DateTime.UtcNow);
        int seconds = HasZoom ? Math.Max(1, (int)Math.Ceiling((_zoomEndUtc!.Value - _zoomStartUtc!.Value).TotalSeconds)) : Math.Clamp(WindowSeconds, 5, 3600);
        DateTime start = HasZoom ? _zoomStartUtc!.Value : now.AddSeconds(-seconds);

        var visible = (Series ?? Array.Empty<TrendSeriesState>())
            .Select(series => BuildVisibleSeries(series, start, now))
            .Where(x => x.Samples.Length > 0)
            .ToArray();

        if (Stacked && (Series?.Count ?? 0) > 0)
        {
            DrawStacked(dc, start, now, seconds, width, height, textBrush, secondaryBrush, borderBrush);
            DrawSelection(dc);
            return;
        }

        DrawGrid(dc, plot);
        DrawTimeAxis(dc, plot, seconds, secondaryBrush, now);

        if (visible.Length == 0)
        {
            DrawCenteredText(dc, "Add a numeric Data value to Trend to begin plotting.", secondaryBrush, plot);
            return;
        }

        // A single trace gets conventional Y-axis labels. Multiple traces each have their
        // own scale, so a shared numeric Y label would be misleading.
        if (visible.Length == 1)
        {
            (double min, double max, double step) = ApplyZoomRange(GetRange(visible[0].Series, visible[0].Samples));
            DrawSingleScaleLabels(dc, plot, min, max, step, visible[0].Series.Stroke);
        }
        else
        {
            DrawText(dc, "per-series Y scales", secondaryBrush, new Point(4, 1), 10.5);
        }

        var labelPoints = new List<(TrendSeriesState Series, Point Point, string Text)>();
        dc.PushClip(new RectangleGeometry(plot));
        foreach (var entry in visible)
        {
            (double yMin, double yMax, _) = ApplyZoomRange(GetRange(entry.Series, entry.Samples));
            TrendSample[] samples = entry.Samples;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                Point first = Transform(samples[0], start, seconds, plot, yMin, yMax);
                ctx.BeginFigure(first, false, false);
                var points = new List<Point>(samples.Length + 1);
                for (int i = 1; i < samples.Length; i++)
                    points.Add(Transform(samples[i], start, seconds, plot, yMin, yMax));

                // Hold the last sampled value to the right edge. Slow PIDs therefore remain
                // visible as a continuous last-known-value trace instead of a lone dot.
                TrendSample last = samples[^1];
                Point lastPoint = Transform(last, start, seconds, plot, yMin, yMax);
                if (points.Count > 0 || samples.Length > 1)
                {
                    // lastPoint is already in the polyline when samples.Length > 1.
                }
                Point lead = new(plot.Right, lastPoint.Y);
                if (samples.Length == 1)
                    points.Add(lead);
                else if (Math.Abs(lastPoint.X - plot.Right) > 0.25)
                    points.Add(lead);
                if (points.Count > 0)
                    ctx.PolyLineTo(points, true, false);
            }
            geometry.Freeze();
            var pen = new Pen(entry.Series.Stroke, 2);
            if (pen.CanFreeze) pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            if (entry.Series.ShowDataPoints)
            {
                foreach (TrendSample sample in samples)
                {
                    Point dp = Transform(sample, start, seconds, plot, yMin, yMax);
                    dc.DrawEllipse(entry.Series.Stroke, null, dp, 2.2, 2.2);
                }
            }

            TrendSample latest = samples[^1];
            Point latestPoint = Transform(latest, start, seconds, plot, yMin, yMax);
            Point edgePoint = new(plot.Right, latestPoint.Y);
            dc.DrawEllipse(entry.Series.Stroke, null, edgePoint, 2.5, 2.5);
            labelPoints.Add((entry.Series, edgePoint, FormatLiveValue(latest.Value, entry.Series.DecimalPlaces)));
        }
        dc.Pop();

        DrawLeadLabels(dc, plot, labelPoints, backgroundBrush);
        DrawSelection(dc);
    }

    private void DrawSelection(DrawingContext dc)
    {
        if (!_dragStart.HasValue || !_dragCurrent.HasValue) return;
        Point a = _dragStart.Value, b = _dragCurrent.Value;
        var rect = new Rect(new Point(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y)), new Point(Math.Max(a.X,b.X), Math.Max(a.Y,b.Y)));
        var fill = new SolidColorBrush(Color.FromArgb(42, 217, 154, 108)); fill.Freeze();
        var penBrush = new SolidColorBrush(Color.FromArgb(220, 217, 154, 108)); penBrush.Freeze();
        dc.DrawRectangle(fill, new Pen(penBrush, 1.3), rect);
    }

    private void DrawStacked(DrawingContext dc, DateTime start, DateTime now, int seconds,
        double width, double height, Brush text, Brush secondary, Brush border)
    {
        var all = Series!;
        double rowHeight = height / all.Count;
        for (int index = 0; index < all.Count; index++)
        {
            var series = all[index];
            double y = index * rowHeight;
            var plot = new Rect(66, y + 29, Math.Max(1, width - 150), Math.Max(25, rowHeight - 58));
            DrawText(dc, $"{series.LegendLabel} ({series.Unit})", series.Stroke, new Point(10, y + 5), 12);
            DrawGrid(dc, plot);
            DrawTimeAxis(dc, plot, seconds, secondary, now);
            dc.DrawRectangle(null, new Pen(border, 0.5), plot);
            var samples = BuildVisibleSeries(series, start, now).Samples;
            if (samples.Length == 0)
            {
                DrawCenteredText(dc, "Waiting for samples…", secondary, plot);
                continue;
            }
            var (min, max, step) = ApplyZoomRange(GetRange(series, samples));
            DrawSingleScaleLabels(dc, plot, min, max, step, secondary);
            var pen = new Pen(series.Stroke, 1.8);
            dc.PushClip(new RectangleGeometry(plot));
            Point previous = Transform(samples[0], start, seconds, plot, min, max);
            foreach (var sample in samples.Skip(1))
            {
                Point next = Transform(sample, start, seconds, plot, min, max);
                dc.DrawLine(pen, previous, next);
                previous = next;
            }
            if (series.ShowDataPoints)
                foreach (TrendSample sample in samples)
                {
                    Point dp = Transform(sample, start, seconds, plot, min, max);
                    dc.DrawEllipse(series.Stroke, null, dp, 2.1, 2.1);
                }
            dc.DrawLine(pen, previous, new Point(plot.Right, previous.Y));
            dc.Pop();
            DrawText(dc, FormatLiveValue(samples[^1].Value, series.DecimalPlaces), series.Stroke,
                new Point(plot.Right + 8, Math.Clamp(previous.Y - 8, plot.Top, plot.Bottom - 15)), 12);
        }
    }

    private static (TrendSeriesState Series, TrendSample[] Samples) BuildVisibleSeries(TrendSeriesState series, DateTime start, DateTime now)
    {
        TrendSample? anchor = series.Samples.LastOrDefault(s => s.TimestampUtc < start && s.TimestampUtc <= now);
        TrendSample[] inWindow = series.Samples.Where(s => s.TimestampUtc >= start && s.TimestampUtc <= now).ToArray();
        if (anchor is null) return (series, inWindow);

        var samples = new TrendSample[inWindow.Length + 1];
        samples[0] = anchor;
        Array.Copy(inWindow, 0, samples, 1, inWindow.Length);
        return (series, samples);
    }

    private (double Min, double Max, double Step) ApplyZoomRange((double Min, double Max, double Step) range)
    {
        if (!HasZoom || _zoomBottomFraction <= _zoomTopFraction + 0.01) return range;
        double span = range.Max - range.Min;
        if (span <= 0) return range;
        double max = range.Max - span * _zoomTopFraction;
        double min = range.Max - span * _zoomBottomFraction;
        if (max <= min) return range;
        return (min, max, NiceStep((max - min) / 5.0));
    }

    private static (double Min, double Max, double Step) GetRange(TrendSeriesState series, TrendSample[] samples)
    {
        if (!series.AutoScale && series.ManualMin.HasValue && series.ManualMax.HasValue &&
            double.IsFinite(series.ManualMin.Value) && double.IsFinite(series.ManualMax.Value) && series.ManualMax.Value > series.ManualMin.Value)
        {
            double min = series.ManualMin.Value;
            double max = series.ManualMax.Value;
            return (min, max, NiceStep((max - min) / 5.0));
        }

        double dataMin = samples.Min(s => s.Value);
        double dataMax = samples.Max(s => s.Value);
        var range = MakeNiceRange(dataMin, dataMax);
        return SignalScaleDefaults.StartsAtZero(series.Request, series.SignalName)
            ? (0, Math.Max(1, range.Max), range.Step) : range;
    }

    private static void DrawGrid(DrawingContext dc, Rect plot)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 128, 128, 128)), 1);
        if (gridPen.Brush.CanFreeze) gridPen.Brush.Freeze();
        if (gridPen.CanFreeze) gridPen.Freeze();
        const int yDivisions = 5;
        for (int i = 0; i <= yDivisions; i++)
        {
            double y = plot.Bottom - plot.Height * i / yDivisions;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private static void DrawSingleScaleLabels(DrawingContext dc, Rect plot, double min, double max, double step, Brush brush)
    {
        const int yDivisions = 5;
        for (int i = 0; i <= yDivisions; i++)
        {
            double fraction = i / (double)yDivisions;
            double y = plot.Bottom - plot.Height * fraction;
            double value = min + (max - min) * fraction;
            DrawText(dc, FormatAxisValue(value, step), brush, new Point(4, y - 8), 10.5);
        }
    }

    private static void DrawLeadLabels(DrawingContext dc, Rect plot, List<(TrendSeriesState Series, Point Point, string Text)> labels, Brush background)
    {
        if (labels.Count == 0) return;
        labels.Sort((a, b) => a.Point.Y.CompareTo(b.Point.Y));
        const double spacing = 15;
        var adjusted = new double[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            double desired = Math.Clamp(labels[i].Point.Y, plot.Top + 8, plot.Bottom - 8);
            adjusted[i] = i == 0 ? desired : Math.Max(desired, adjusted[i - 1] + spacing);
        }
        if (adjusted[^1] > plot.Bottom - 8)
        {
            double shift = adjusted[^1] - (plot.Bottom - 8);
            for (int i = 0; i < adjusted.Length; i++) adjusted[i] -= shift;
        }

        for (int i = 0; i < labels.Count; i++)
        {
            var entry = labels[i];
            double y = Math.Clamp(adjusted[i], plot.Top + 8, plot.Bottom - 8);
            var pen = new Pen(entry.Series.Stroke, 1);
            if (pen.CanFreeze) pen.Freeze();
            dc.DrawLine(pen, entry.Point, new Point(plot.Right + 5, y));
            FormattedText ft = CreateText(entry.Text, entry.Series.Stroke, 11.5);
            var bg = new Rect(plot.Right + 6, y - ft.Height / 2 - 1, ft.Width + 4, ft.Height + 2);
            dc.DrawRectangle(background, null, bg);
            dc.DrawText(ft, new Point(plot.Right + 8, y - ft.Height / 2));
        }
    }

    private static Point Transform(TrendSample sample, DateTime start, int seconds, Rect plot, double yMin, double yMax)
    {
        double xFraction = Math.Clamp((sample.TimestampUtc - start).TotalSeconds / seconds, 0, 1);
        double yFraction = yMax > yMin ? Math.Clamp((sample.Value - yMin) / (yMax - yMin), 0, 1) : 0.5;
        return new Point(plot.Left + xFraction * plot.Width, plot.Bottom - yFraction * plot.Height);
    }

    private static (double Min, double Max, double Step) MakeNiceRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return (0, 1, 0.2);
        if (max < min) (min, max) = (max, min);

        double range = max - min;
        if (range < 1e-9)
        {
            double pad = Math.Max(Math.Abs(max) * 0.05, 1.0);
            double step0 = NiceStep((pad * 2) / 10.0);
            double low0 = Math.Floor((min - pad) / step0) * step0;
            double high0 = Math.Ceiling((max + pad) / step0) * step0;
            if (high0 <= low0) high0 = low0 + step0;
            return (low0, high0, step0);
        }

        double step = NiceStep(range / 20.0);
        double low = Math.Floor(min / step) * step;
        double high = Math.Ceiling((max + range * 0.03) / step) * step;
        if (high <= low) high = low + step;
        return (low, high, step);
    }

    private static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0) return 1;
        double exponent = Math.Floor(Math.Log10(raw));
        double power = Math.Pow(10, exponent);
        double fraction = raw / power;
        double nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
        return nice * power;
    }

    private static string FormatAxisValue(double value, double step)
    {
        double absStep = Math.Abs(step);
        if (absStep >= 10) return value.ToString("0", CultureInfo.InvariantCulture);
        if (absStep >= 1) return value.ToString("0.#", CultureInfo.InvariantCulture);
        if (absStep >= 0.1) return value.ToString("0.##", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatLiveValue(double value, int decimals = -1)
    {
        if (decimals >= 0) return value.ToString("F" + Math.Clamp(decimals, 0, 6).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        double abs = Math.Abs(value);
        if (abs >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
        if (abs >= 10) return value.ToString("0.#", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void DrawTimeAxis(DrawingContext dc, Rect plot, int seconds, Brush secondaryBrush, DateTime endUtc)
    {
        const int xDivisions = 4;
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)), 1);
        if (gridPen.Brush.CanFreeze) gridPen.Brush.Freeze();
        if (gridPen.CanFreeze) gridPen.Freeze();
        for (int i = 0; i <= xDivisions; i++)
        {
            double fraction = i / (double)xDivisions;
            double x = plot.Left + plot.Width * fraction;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            double secondsAgo = seconds * (1.0 - fraction);
            string label = endUtc.AddSeconds(-secondsAgo).ToLocalTime().ToString("HH:mm:ss");
            DrawText(dc, label, secondaryBrush, new Point(x - (i == xDivisions ? 22 : 15), plot.Bottom + 6), 10.5);
        }
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Brush brush, Rect area)
    {
        var formatted = CreateText(text, brush, 13);
        dc.DrawText(formatted, new Point(area.Left + (area.Width - formatted.Width) / 2, area.Top + (area.Height - formatted.Height) / 2));
    }

    private static void DrawText(DrawingContext dc, string text, Brush brush, Point point, double size) =>
        dc.DrawText(CreateText(text, brush, size), point);

    private static FormattedText CreateText(string text, Brush brush, double size)
    {
        double pixelsPerDip = 1.0;
        if (Application.Current?.MainWindow is Visual visual)
            pixelsPerDip = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, pixelsPerDip);
    }
}
