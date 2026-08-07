using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace LaptopThermalHelper.App.ViewModels;

public partial class HardwareCardViewModel : ObservableObject
{
    private readonly ObservableCollection<double> _chartValues = [];

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
            Temperature = double.NaN;
            CurrentText = "--";
            MaximumText = "--";
            AverageText = "--";
            LoadText = "--";
            PowerText = "--";
            FanText = "未开放";
            Level = ThermalLevel.Unknown;
            LevelText = "未获取";
            _chartValues.Clear();
            return;
        }

        DeviceSnapshot device = snapshot.Device;
        DeviceName = device.DisplayName;
        Temperature = device.Temperature ?? double.NaN;
        CurrentText = FormatTemperature(device.Temperature);
        MaximumText = FormatTemperature(snapshot.MaximumTemperature);
        AverageText = FormatTemperature(snapshot.AverageTemperature);
        LoadText = device.Load is double load ? $"{load:0}%" : "--";
        PowerText = device.Power is double power ? $"{power:0}W" : "--";
        FanText = device.FanRpm is double fan ? $"{fan:0} RPM" : "未开放";
        Level = device.ThermalLevel;
        LevelText = LevelToText(device.ThermalLevel);

        _chartValues.Clear();
        int first = Math.Max(0, snapshot.Trend.Count - 60);
        for (int index = first; index < snapshot.Trend.Count; index++)
        {
            _chartValues.Add(snapshot.Trend[index].Value);
        }
    }

    private static string FormatTemperature(double? value) => value is double number ? $"{number:0}°C" : "--";

    private static string LevelToText(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => "正常",
        ThermalLevel.Elevated => "温度偏高",
        ThermalLevel.High => "温度过高",
        ThermalLevel.Critical => "严重过热",
        _ => "未获取",
    };
}
