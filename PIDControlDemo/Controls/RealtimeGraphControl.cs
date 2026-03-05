using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PIDControlDemo.ViewModels;

namespace PIDControlDemo.Controls;

public class RealtimeGraphControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<GraphPoint>?> GraphPointsProperty =
        AvaloniaProperty.Register<RealtimeGraphControl, IReadOnlyList<GraphPoint>?>(nameof(GraphPoints));

    public IReadOnlyList<GraphPoint>? GraphPoints
    {
        get => GetValue(GraphPointsProperty);
        set => SetValue(GraphPointsProperty, value);
    }

    public static readonly StyledProperty<int> GraphVersionProperty =
        AvaloniaProperty.Register<RealtimeGraphControl, int>(nameof(GraphVersion));

    public int GraphVersion
    {
        get => GetValue(GraphVersionProperty);
        set => SetValue(GraphVersionProperty, value);
    }

    private const double MaxRpm = 3000;
    private const double MaxThrottle = 100;
    private const double LeftMargin = 50;
    private const double RightMargin = 40;
    private const double TopMargin = 25;
    private const double BottomMargin = 25;

    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
    private static readonly IPen TargetPen = new Pen(Brushes.DodgerBlue, 2);
    private static readonly IPen SensedPen = new Pen(Brushes.Red, 2);
    private static readonly IPen ThrottlePen = new Pen(Brushes.LimeGreen, 1.5);

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly Typeface LabelTypeface = new Typeface("Inter", FontStyle.Normal, FontWeight.Normal);

    static RealtimeGraphControl()
    {
        AffectsRender<RealtimeGraphControl>(GraphPointsProperty, GraphVersionProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        double w = bounds.Width;
        double h = bounds.Height;

        // 背景
        context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h), 4, 4);

        double plotW = w - LeftMargin - RightMargin;
        double plotH = h - TopMargin - BottomMargin;
        if (plotW <= 0 || plotH <= 0) return;

        // グリッド線 (Y軸: 500rpm刻み)
        for (int rpm = 0; rpm <= 3000; rpm += 500)
        {
            double y = TopMargin + plotH * (1.0 - rpm / MaxRpm);
            context.DrawLine(GridPen, new Point(LeftMargin, y), new Point(LeftMargin + plotW, y));

            var text = new FormattedText(
                rpm.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, 10, TextBrush);
            context.DrawText(text, new Point(LeftMargin - text.Width - 4, y - text.Height / 2));
        }

        // 右Y軸ラベル (0%, 50%, 100%)
        for (int pct = 0; pct <= 100; pct += 50)
        {
            double y = TopMargin + plotH * (1.0 - pct / MaxThrottle);
            var text = new FormattedText(
                $"{pct}%", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, 10, TextBrush);
            context.DrawText(text, new Point(LeftMargin + plotW + 4, y - text.Height / 2));
        }

        // 凡例
        DrawLegend(context, LeftMargin + 8, 4);

        var points = GraphPoints;
        if (points == null || points.Count < 2) return;

        double timeMin = points[0].Time;
        double timeMax = points[^1].Time;
        double timeSpan = timeMax - timeMin;
        if (timeSpan <= 0) timeSpan = 1;

        // 各系列を描画
        DrawSeries(context, points, plotW, plotH, timeMin, timeSpan,
            p => p.TargetRpm / MaxRpm, TargetPen);
        DrawSeries(context, points, plotW, plotH, timeMin, timeSpan,
            p => p.SensedRpm / MaxRpm, SensedPen);
        DrawSeries(context, points, plotW, plotH, timeMin, timeSpan,
            p => p.Throttle / MaxThrottle, ThrottlePen);
    }

    private void DrawSeries(DrawingContext context, IReadOnlyList<GraphPoint> points,
        double plotW, double plotH, double timeMin, double timeSpan,
        Func<GraphPoint, double> valueSelector, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool first = true;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                double x = LeftMargin + plotW * ((p.Time - timeMin) / timeSpan);
                double val = Math.Clamp(valueSelector(p), 0, 1);
                double y = TopMargin + plotH * (1.0 - val);

                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y));
                }
            }
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLegend(DrawingContext context, double x, double y)
    {
        var entries = new (string label, IBrush color)[]
        {
            ("目標回転数", Brushes.DodgerBlue),
            ("検知回転数", Brushes.Red),
            ("アクセル開度", Brushes.LimeGreen),
        };

        double offsetX = x;
        foreach (var (label, color) in entries)
        {
            context.DrawRectangle(color, null, new Rect(offsetX, y + 3, 12, 3));
            var text = new FormattedText(
                label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, 10, TextBrush);
            context.DrawText(text, new Point(offsetX + 16, y));
            offsetX += text.Width + 28;
        }
    }
}
