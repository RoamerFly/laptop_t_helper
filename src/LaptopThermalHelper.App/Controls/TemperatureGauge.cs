using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Controls;

public sealed class TemperatureGauge : FrameworkElement
{
    public static readonly DependencyProperty TemperatureProperty = DependencyProperty.Register(
        nameof(Temperature),
        typeof(double),
        typeof(TemperatureGauge),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(ThermalLevel),
        typeof(TemperatureGauge),
        new FrameworkPropertyMetadata(ThermalLevel.Unknown, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Temperature
    {
        get => (double)GetValue(TemperatureProperty);
        set => SetValue(TemperatureProperty, value);
    }

    public ThermalLevel Level
    {
        get => (ThermalLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double size = Math.Min(ActualWidth, ActualHeight);
        Point center = new(ActualWidth / 2, (ActualHeight / 2) - 2);
        double radius = Math.Max(12, (size / 2) - 14);
        double thickness = Math.Max(8, size * 0.065);
        Brush track = FindBrush("GaugeTrackBrush", Brushes.DimGray);
        Brush accent = LevelBrush(Level);

        DrawArc(drawingContext, center, radius, 135, 270, new Pen(track, thickness));

        if (!double.IsNaN(Temperature))
        {
            double progress = Math.Clamp(Temperature, 0, 100) / 100;
            DrawArc(drawingContext, center, radius, 135, 270 * progress, new Pen(accent, thickness));
        }

        DrawCenteredText(
            drawingContext,
            double.IsNaN(Temperature) ? "--" : $"{Temperature:0}°C",
            center + new Vector(0, -4),
            size * 0.22,
            FindBrush("TextPrimaryBrush", Brushes.White),
            FontWeights.Bold);
        DrawCenteredText(
            drawingContext,
            LevelText(Level),
            center + new Vector(0, size * 0.18),
            size * 0.085,
            accent,
            FontWeights.SemiBold);
    }

    private static void DrawArc(
        DrawingContext context,
        Point center,
        double radius,
        double startAngle,
        double sweepAngle,
        Pen pen)
    {
        if (sweepAngle <= 0)
        {
            return;
        }

        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(start, false, false);
            geometryContext.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    private void DrawCenteredText(
        DrawingContext context,
        string text,
        Point center,
        double size,
        Brush brush,
        FontWeight weight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, new Point(center.X - (formatted.Width / 2), center.Y - (formatted.Height / 2)));
    }

    private static string LevelText(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => "正常",
        ThermalLevel.Elevated => "温度偏高",
        ThermalLevel.High => "温度过高",
        ThermalLevel.Critical => "严重过热",
        _ => "未获取",
    };

    private static Brush LevelBrush(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => FindBrush("SuccessBrush", Brushes.LimeGreen),
        ThermalLevel.Elevated => FindBrush("WarningBrush", Brushes.Gold),
        ThermalLevel.High => FindBrush("HighBrush", Brushes.DarkOrange),
        ThermalLevel.Critical => FindBrush("CriticalBrush", Brushes.Red),
        _ => FindBrush("TextMutedBrush", Brushes.Gray),
    };

    private static Brush FindBrush(string key, Brush fallback) =>
        System.Windows.Application.Current.TryFindResource(key) as Brush ?? fallback;
}
