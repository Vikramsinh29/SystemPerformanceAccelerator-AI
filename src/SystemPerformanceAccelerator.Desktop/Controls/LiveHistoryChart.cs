using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SystemPerformanceAccelerator.Desktop.ViewModels;

namespace SystemPerformanceAccelerator.Desktop.Controls;

public sealed class LiveHistoryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IReadOnlyList<double>),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                Array.Empty<double>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                Brushes.Goldenrod,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridLineProperty =
        DependencyProperty.Register(
            nameof(GridLine),
            typeof(Brush),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                Brushes.DimGray,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumSamplesProperty =
        DependencyProperty.Register(
            nameof(MaximumSamples),
            typeof(int),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                SystemMonitorHistoryBuffer.DefaultCapacity,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty TransitionProgressProperty =
        DependencyProperty.Register(
            nameof(TransitionProgress),
            typeof(double),
            typeof(LiveHistoryChart),
            new FrameworkPropertyMetadata(
                1d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush GridLine
    {
        get => (Brush)GetValue(GridLineProperty);
        set => SetValue(GridLineProperty, value);
    }

    public int MaximumSamples
    {
        get => (int)GetValue(MaximumSamplesProperty);
        set => SetValue(MaximumSamplesProperty, value);
    }

    private double TransitionProgress
    {
        get => (double)GetValue(TransitionProgressProperty);
        set => SetValue(TransitionProgressProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        DrawReferenceGrid(drawingContext, bounds);

        var values = Values;
        if (values.Count == 0)
        {
            return;
        }

        var capacity = Math.Max(2, MaximumSamples);
        var step = bounds.Width / (capacity - 1);
        var shift = (1 - Math.Clamp(TransitionProgress, 0, 1)) * step;
        var points = new List<Point>(values.Count);

        for (var index = 0; index < values.Count; index++)
        {
            var age = values.Count - 1 - index;
            var x = bounds.Right - (age * step) + shift;
            var value = Math.Clamp(values[index], 0, 100);
            var y = bounds.Bottom - (value / 100d * bounds.Height);
            points.Add(new Point(x, y));
        }

        drawingContext.PushClip(new RectangleGeometry(bounds));
        DrawArea(drawingContext, points, bounds.Bottom);
        DrawLine(drawingContext, points);
        drawingContext.Pop();
    }

    private void DrawReferenceGrid(DrawingContext drawingContext, Rect bounds)
    {
        var pen = new Pen(GridLine, 1) { DashStyle = DashStyles.Dot };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        foreach (var percentage in new[] { 0.25, 0.5, 0.75 })
        {
            var y = bounds.Bottom - (bounds.Height * percentage);
            drawingContext.DrawLine(pen, new Point(0, y), new Point(bounds.Right, y));
        }

        for (var division = 1; division < 4; division++)
        {
            var x = bounds.Width * division / 4d;
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Bottom));
        }
    }

    private void DrawArea(
        DrawingContext drawingContext,
        IReadOnlyList<Point> points,
        double bottom)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, bottom), true, true);
            context.LineTo(points[0], true, false);
            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], true, false);
            }
            context.LineTo(new Point(points[^1].X, bottom), true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(Fill, null, geometry);
    }

    private void DrawLine(DrawingContext drawingContext, IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            var point = points[0];
            drawingContext.DrawEllipse(Stroke, null, point, 2.5, 2.5);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], true, false);
            }
        }

        geometry.Freeze();
        var pen = new Pen(Stroke, 2);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static void OnValuesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var chart = (LiveHistoryChart)dependencyObject;
        chart.BeginAnimation(TransitionProgressProperty, null);
        chart.TransitionProgress = 1;
        chart.BeginAnimation(
            TransitionProgressProperty,
            new DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            });
    }
}
