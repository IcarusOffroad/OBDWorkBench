using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OBDWorkbench.Models;
using OBDWorkbench.Services;

namespace OBDWorkbench.Controls;

public sealed class DashboardGaugeControl : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(DashboardGaugeState), typeof(DashboardGaugeControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public DashboardGaugeState? State
    {
        get => (DashboardGaugeState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public DashboardGaugeControl()
    {
        MinHeight = 150;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (DashboardGaugeControl)d;
        if (e.OldValue is DashboardGaugeState oldState)
            oldState.PropertyChanged -= gauge.State_PropertyChanged;
        if (e.NewValue is DashboardGaugeState newState)
            newState.PropertyChanged += gauge.State_PropertyChanged;
        gauge.InvalidateVisual();
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 80 || ActualHeight < 80) return;

        Brush textBrush = Application.Current.TryFindResource("PrimaryTextBrush") as Brush ?? Brushes.Black;
        Brush secondaryBrush = Application.Current.TryFindResource("SecondaryTextBrush") as Brush ?? Brushes.Gray;
        Brush trackBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        Brush accent = State?.Accent ?? new SolidColorBrush(Color.FromRgb(217, 154, 108));

        double width = ActualWidth;
        double height = ActualHeight;
        double cx = width / 2;
        double cy = Math.Min(height * 0.64, height - 42);
        double radius = Math.Max(22, Math.Min(width * 0.40, height * 0.42));
        const double startDeg = 180;
        const double sweepDeg = 180;

        // The LCARS display uses a segmented outer scale while keeping the live
        // progress arc and user-configured warning bands on the inner scale.
        if (ThemeService.IsLcars)
        {
            Brush[] segments = {
                (Brush)Application.Current.Resources["LcarsBlueBrush"],
                (Brush)Application.Current.Resources["LcarsLilacBrush"],
                (Brush)Application.Current.Resources["LcarsPeachBrush"]
            };
            for (int i = 0; i < 12; i++)
                DrawArc(dc, new Pen(segments[i / 4], Math.Max(5, radius * .065)),
                    cx, cy, radius * 1.12, startDeg + i * 15 + 1.5, 12);
        }

        var trackPen = new Pen(trackBrush, Math.Max(2, radius * 0.025));
        if (trackPen.CanFreeze) trackPen.Freeze();
        DrawArc(dc, trackPen, cx, cy, radius, startDeg, sweepDeg);

        double min = State?.RangeMin ?? 0;
        double max = State?.RangeMax ?? 1;
        if (State is not null && max > min)
        {
            DrawBand(dc, State.LowRangeBrush, State.LowRangeMin, State.LowRangeMax, min, max, cx, cy, radius, startDeg, sweepDeg);
            DrawBand(dc, State.HighRangeBrush, State.HighRangeMin, State.HighRangeMax, min, max, cx, cy, radius, startDeg, sweepDeg);
        }
        double? value = State?.CurrentValue;
        double fraction = value.HasValue && max > min
            ? Math.Clamp((value.Value - min) / (max - min), 0, 1)
            : 0;

        // Keep the original progress arc, but deliberately make the warning bands
        // much wider so their colour remains visible on both sides of the progress arc.
        var valuePen = new Pen(accent, ThemeService.IsLcars ? Math.Max(6, radius * 0.06) : Math.Max(2, radius * 0.025));
        if (valuePen.CanFreeze) valuePen.Freeze();
        if (fraction > 0.001)
            DrawArc(dc, valuePen, cx, cy, radius, startDeg, sweepDeg * fraction);

        int tickCount = ThemeService.IsLcars ? 10 : 30;
        for (int tick = 0; tick <= tickCount; tick++)
        {
            double angle = startDeg + sweepDeg * tick / tickCount;
            bool major = tick % 5 == 0;
            dc.DrawLine(new Pen(secondaryBrush, major ? 1.5 : 0.7),
                Polar(cx, cy, radius * (major ? .88 : .94), angle), Polar(cx, cy, radius, angle));
            if (tick > 0 && tick < tickCount && major)
                DrawCentered(dc, FormatRange(min + (max-min)*tick/tickCount), secondaryBrush, 10,
                    Polar(cx, cy, radius*.73, angle));
        }

        if (!ThemeService.IsLcars)
        {
            double needleAngle = DegreesToRadians(startDeg + sweepDeg * fraction);
            Point needleEnd = new(cx + Math.Cos(needleAngle) * radius * 0.78, cy + Math.Sin(needleAngle) * radius * 0.78);
            var needlePen = new Pen(accent, Math.Max(2, radius * 0.025));
            if (needlePen.CanFreeze) needlePen.Freeze();
            dc.DrawLine(needlePen, new Point(cx, cy), needleEnd);
            dc.DrawEllipse(accent, null, new Point(cx, cy), 4, 4);
        }

        string current = State?.CurrentText ?? "--";
        DrawCentered(dc, current, ThemeService.IsLcars ? accent : textBrush, ThemeService.IsLcars ? Math.Max(28, Math.Min(52, radius * .52)) : Math.Max(22, Math.Min(40, width / 7.5)), ThemeService.IsLcars ? new Point(cx, cy - radius * .25) : new Point(cx, cy + 22));
        if (!string.IsNullOrWhiteSpace(State?.Unit))
            DrawCentered(dc, State!.Unit, secondaryBrush, 11, ThemeService.IsLcars ? new Point(cx, cy + 17) : new Point(cx, cy + 48));

        Point minPoint = Polar(cx, cy, radius * 1.03, startDeg);
        Point maxPoint = Polar(cx, cy, radius * 1.03, startDeg + sweepDeg);
        DrawCentered(dc, FormatRange(min), secondaryBrush, 10.5, new Point(minPoint.X, minPoint.Y + 10));
        DrawCentered(dc, FormatRange(max), secondaryBrush, 10.5, new Point(maxPoint.X, maxPoint.Y + 10));
    }

    private static void DrawBand(DrawingContext dc, Brush brush, double? bandMin, double? bandMax, double rangeMin, double rangeMax, double cx, double cy, double radius, double startDeg, double sweepDeg)
    {
        if (!bandMin.HasValue || !bandMax.HasValue || !double.IsFinite(bandMin.Value) || !double.IsFinite(bandMax.Value) || bandMax.Value <= bandMin.Value) return;
        double a = Math.Clamp((bandMin.Value - rangeMin) / (rangeMax - rangeMin), 0, 1);
        double b = Math.Clamp((bandMax.Value - rangeMin) / (rangeMax - rangeMin), 0, 1);
        if (b <= a) return;
        var pen = new Pen(brush, ThemeService.IsLcars ? Math.Max(13, radius * 0.14) : Math.Max(8, radius * 0.085));
        if (pen.CanFreeze) pen.Freeze();
        DrawArc(dc, pen, cx, cy, radius, startDeg + sweepDeg * a, sweepDeg * (b - a));
    }

    private static void DrawArc(DrawingContext dc, Pen pen, double cx, double cy, double radius, double startDegrees, double sweepDegrees)
    {
        int segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweepDegrees) / 5.0));
        Point previous = Polar(cx, cy, radius, startDegrees);
        for (int i = 1; i <= segments; i++)
        {
            double angle = startDegrees + sweepDegrees * i / segments;
            Point next = Polar(cx, cy, radius, angle);
            dc.DrawLine(pen, previous, next);
            previous = next;
        }
    }

    private static Point Polar(double cx, double cy, double radius, double degrees)
    {
        double radians = DegreesToRadians(degrees);
        return new Point(cx + Math.Cos(radians) * radius, cy + Math.Sin(radians) * radius);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static string FormatRange(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
        if (abs >= 10) return value.ToString("0.#", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static void DrawCentered(DrawingContext dc, string text, Brush brush, double size, Point center)
    {
        FormattedText formatted = CreateText(text, brush, size);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private static FormattedText CreateText(string text, Brush brush, double size)
    {
        double pixelsPerDip = 1.0;
        if (Application.Current?.MainWindow is Visual visual)
            pixelsPerDip = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, pixelsPerDip);
    }
}
