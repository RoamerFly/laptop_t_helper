using CommunityToolkit.Mvvm.ComponentModel;
using LaptopThermalHelper.App.Controls;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace LaptopThermalHelper.App.ViewModels;

public partial class HardwareCardViewModel : ObservableObject
{
    private readonly BatchObservableCollection<double> _chartValues = [];

    public HardwareCardViewModel(string title, string icon, SKColor color)
    {
        Title = title;
        Icon = icon;
        Series =
        [
            new LineSeries<double>
            {
                Values = _chartValues,
                GeometrySize = 0,
                LineSmoothness = 0.35,
                Stroke = new SolidColorPaint(color, 2),
                Fill = new SolidColorPaint(color.WithAlpha(28)),
                AnimationsSpeed = TimeSpan.FromMilliseconds(300),
            },
        ];
        XAxes =
        [
            new Axis
            {
                IsVisible = false,
            },
        ];
        YAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                MinStep = 50,
                ForceStepToMin = true,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(new SKColor(145, 158, 174)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(100, 116, 139, 55), 1),
            },
        ];
    }

    public string Title { get; }

    public string Icon { get; }

    public ISeries[] Series { get; }

    public Axis[] XAxes { get; }

    public Axis[] YAxes { get; }

    [ObservableProperty]
    private string _deviceName = "正在识别…";

    [ObservableProperty]
    private double _temperature = double.NaN;

    [ObservableProperty]
    private string _currentText = "--";

    [ObservableProperty]
    private string _maximumText = "--";

    [ObservableProperty]
    private string _averageText = "--";

    [ObservableProperty]
    private string _loadText = "--";

    [ObservableProperty]
    private string _powerText = "--";

    [ObservableProperty]
    private string _fanText = "未开放";

    [ObservableProperty]
    private ThermalLevel _level = ThermalLevel.Unknown;

    [ObservableProperty]
    private string _levelText = "未获取";

    public void Update(MonitoredDeviceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            DeviceName = "未检测到";
            Temperature = double.NaN;
            CurrentText = "未检测到";
            MaximumText = "--";
            AverageText = "--";
            LoadText = "--";
            PowerText = "--";
            FanText = "未开放";
            Level = ThermalLevel.Unknown;
            LevelText = "未获取";
            _chartValues.ReplaceWith([]);
            return;
        }

        DeviceSnapshot device = snapshot.Device;
        DeviceName = device.DisplayName;
        double? validTemperature = device.Temperature is double measured && double.IsFinite(measured)
            ? measured
            : null;
        bool hasTemperature = validTemperature is not null;
        Temperature = validTemperature ?? double.NaN;
        CurrentText = hasTemperature ? FormatTemperature(validTemperature) : "不可用";
        MaximumText = FormatTemperature(snapshot.MaximumTemperature);
        AverageText = FormatTemperature(snapshot.AverageTemperature);
        LoadText = device.Load is double load ? $"{load:0}%" : "--";
        PowerText = device.Power is double power ? $"{power:0}W" : "--";
        FanText = device.FanRpm is double fan ? $"{fan:0} RPM" : "未开放";
        Level = hasTemperature ? device.ThermalLevel : ThermalLevel.Unknown;
        LevelText = hasTemperature ? LevelToText(device.ThermalLevel) : "不可用";

        int first = Math.Max(0, snapshot.Trend.Count - 60);
        _chartValues.ReplaceWith(hasTemperature
            ? snapshot.Trend.Skip(first).Select(static item => item.Value)
            : []);
    }

    private static string FormatTemperature(double? value) =>
        value is double number && double.IsFinite(number) ? $"{number:0}°C" : "--";

    private static string LevelToText(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => "正常",
        ThermalLevel.Elevated => "温度偏高",
        ThermalLevel.High => "温度过高",
        ThermalLevel.Critical => "严重过热",
        _ => "未获取",
    };
}
