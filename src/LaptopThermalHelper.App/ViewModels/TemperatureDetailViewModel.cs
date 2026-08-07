using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaptopThermalHelper.App.Controls;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace LaptopThermalHelper.App.ViewModels;

/// <summary>
/// Presents a bounded copy of the runtime temperature history. Querying the
/// history buffer is memory-only; sensor sampling and persistence stay outside
/// the UI thread.
/// </summary>
public partial class TemperatureDetailViewModel : ObservableObject
{
    private const int MaximumChartPointsPerDevice = 360;
    private readonly DashboardViewModel _dashboard;
    private readonly ITemperatureHistoryBuffer _historyBuffer;
    private readonly BatchObservableCollection<double> _cpuHistory = [];
    private readonly BatchObservableCollection<double> _gpuHistory = [];
    private readonly BatchObservableCollection<double> _storageHistory = [];
    private readonly BatchObservableCollection<TemperatureDeviceTreeItem> _deviceTree = [];
    private readonly LineSeries<double> _cpuSeries;
    private readonly LineSeries<double> _gpuSeries;
    private readonly LineSeries<double> _storageSeries;
    private DateTimeOffset _nextPeriodicHistoryQueryAt;
    private TemperatureHistoryRange? _lastLoadedRange;
    private DateTimeOffset _lastLoadedFrom;
    private DateTimeOffset _lastLoadedTo;

    public TemperatureDetailViewModel(
        DashboardViewModel dashboard,
        ITemperatureHistoryBuffer historyBuffer)
    {
        _dashboard = dashboard;
        _historyBuffer = historyBuffer;
        SensorReadings = [];
        DeviceTree = _deviceTree;
        _cpuSeries = CreateSeries("CPU", _cpuHistory, new SKColor(85, 163, 255));
        _gpuSeries = CreateSeries("GPU", _gpuHistory, new SKColor(143, 99, 255));
        _storageSeries = CreateSeries("存储", _storageHistory, new SKColor(49, 216, 67));
        HistorySeries = [_cpuSeries, _gpuSeries, _storageSeries];
        HistoryXAxes =
        [
            new Axis
            {
                IsVisible = false,
            },
        ];
        HistoryYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                MinStep = 25,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(new SKColor(145, 158, 174)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(100, 116, 139, 50), 1),
            },
        ];

        _dashboard.Cpu.PropertyChanged += DashboardCardPropertyChanged;
        _dashboard.Gpu.PropertyChanged += DashboardCardPropertyChanged;
        _dashboard.Storage.PropertyChanged += DashboardCardPropertyChanged;
        SyncCurrentReadings();
        SyncDeviceTree();
        LoadHistory(force: true);
    }

    public IReadOnlyList<string> Ranges { get; } = ["10 分钟", "1 小时", "6 小时", "24 小时"];

    public ObservableCollection<TemperatureSensorReadingItem> SensorReadings { get; }

    /// <summary>
    /// Device names are derived from the most recent provider snapshot. It is
    /// intentionally empty until a provider returns usable temperature devices.
    /// </summary>
    public BatchObservableCollection<TemperatureDeviceTreeItem> DeviceTree { get; }

    public ISeries[] HistorySeries { get; }

    public Axis[] HistoryXAxes { get; }

    public Axis[] HistoryYAxes { get; }

    [ObservableProperty]
    private string _selectedRange = "10 分钟";

    [ObservableProperty]
    private bool _isCpuVisible = true;

    [ObservableProperty]
    private bool _isGpuVisible = true;

    [ObservableProperty]
    private bool _isStorageVisible = true;

    [ObservableProperty]
    private TemperatureHistoryLoadState _historyState = TemperatureHistoryLoadState.Empty;

    [ObservableProperty]
    private string _historyStatusText = "正在读取温度历史…";

    [ObservableProperty]
    private string _cpuCurrentText = "--";

    [ObservableProperty]
    private string _gpuCurrentText = "--";

    [ObservableProperty]
    private string _storageCurrentText = "--";

    public bool HasHistoryData => HistoryState == TemperatureHistoryLoadState.Ready;

    public bool IsHistoryLoading => HistoryState == TemperatureHistoryLoadState.Loading;

    public bool IsHistoryEmpty => HistoryState == TemperatureHistoryLoadState.Empty;

    public bool HasHistoryError => HistoryState == TemperatureHistoryLoadState.Error;

    public bool IsDeviceTreeEmpty => DeviceTree.Count == 0;

    partial void OnSelectedRangeChanged(string value) => LoadHistory(force: true);

    partial void OnIsCpuVisibleChanged(bool value) => _cpuSeries.IsVisible = value;

    partial void OnIsGpuVisibleChanged(bool value) => _gpuSeries.IsVisible = value;

    partial void OnIsStorageVisibleChanged(bool value) => _storageSeries.IsVisible = value;

    partial void OnHistoryStateChanged(TemperatureHistoryLoadState value)
    {
        OnPropertyChanged(nameof(HasHistoryData));
        OnPropertyChanged(nameof(IsHistoryLoading));
        OnPropertyChanged(nameof(IsHistoryEmpty));
        OnPropertyChanged(nameof(HasHistoryError));
    }

    /// <summary>
    /// Called from the existing user-requested refresh path after the dashboard
    /// has received a fresh sample. It does not trigger hardware I/O itself.
    /// </summary>
    public void RefreshFromDashboard()
    {
        SyncCurrentReadings();
        SyncDeviceTree();
        LoadHistory(force: true);
    }

    /// <summary>
    /// Receives the regular sampler cadence without rebuilding a chart for every
    /// two-second UI tick. The history buffer writes at a slower cadence, so a
    /// bounded query is only made every five seconds and only applied when the
    /// buffer's time window has changed.
    /// </summary>
    public void RefreshAfterSampling()
    {
        SyncCurrentReadings();
        SyncDeviceTree();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < _nextPeriodicHistoryQueryAt)
        {
            return;
        }

        _nextPeriodicHistoryQueryAt = now.AddSeconds(5);
        LoadHistory(force: false);
    }

    private static LineSeries<double> CreateSeries(string name, IReadOnlyCollection<double> values, SKColor color) => new()
    {
        Name = name,
        Values = values,
        GeometrySize = 0,
        LineSmoothness = 0.25,
        Stroke = new SolidColorPaint(color, 2),
        Fill = new SolidColorPaint(color.WithAlpha(20)),
        AnimationsSpeed = TimeSpan.FromMilliseconds(160),
    };

    private void DashboardCardPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(HardwareCardViewModel.CurrentText)
            or nameof(HardwareCardViewModel.MaximumText)
            or nameof(HardwareCardViewModel.AverageText))
        {
            // Keep the detail table current without rebuilding the chart for
            // every sampler update. A chart query happens only on page refresh
            // or an explicit time-range change.
            SyncCurrentReadings();
        }
    }

    private void SyncCurrentReadings()
    {
        CpuCurrentText = _dashboard.Cpu.CurrentText;
        GpuCurrentText = _dashboard.Gpu.CurrentText;
        StorageCurrentText = _dashboard.Storage.CurrentText;
        SyncSensorReadings();
    }

    private void SyncDeviceTree()
    {
        MonitoringSnapshot snapshot = _dashboard.LastSnapshot;
        ReplaceDeviceTree(snapshot.Devices
            .Where(static item => item.Device.Kind is DeviceKind.Cpu or DeviceKind.Gpu or DeviceKind.Storage)
            .Select(item => new TemperatureDeviceTreeItem(
                DeviceKindText(item.Device.Kind),
                item.Device.DisplayName,
                GetDeviceRole(snapshot, item),
                FormatTemperature(item.Device.Temperature))));
    }

    private void ReplaceDeviceTree(IEnumerable<TemperatureDeviceTreeItem> items)
    {
        DeviceTree.ReplaceWith(items);
        OnPropertyChanged(nameof(IsDeviceTreeEmpty));
    }

    private static string DeviceKindText(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => "CPU",
        DeviceKind.Gpu => "GPU",
        DeviceKind.Storage => "存储",
        _ => "设备",
    };

    private void SyncSensorReadings()
    {
        MonitoringSnapshot snapshot = _dashboard.LastSnapshot;
        TemperatureSensorReadingItem[] items = snapshot.Devices
            .Where(static device => device.Device.Kind is DeviceKind.Cpu or DeviceKind.Gpu or DeviceKind.Storage)
            .SelectMany(device => device.TemperatureSensors
                .Where(static sensor =>
                    sensor.Metric == SensorMetric.Temperature &&
                    sensor.Quality == ReadingQuality.Good &&
                    sensor.Value is double value &&
                    double.IsFinite(value))
                .Select(sensor => CreateSensorReadingItem(device, sensor)))
            .ToArray();
        SensorReadings.Clear();
        foreach (TemperatureSensorReadingItem item in items)
        {
            SensorReadings.Add(item);
        }
    }

    private static TemperatureSensorReadingItem CreateSensorReadingItem(
        MonitoredDeviceSnapshot device,
        SensorReading sensor)
    {
        bool isPrimary = string.Equals(
            device.PrimaryTemperatureSensorName,
            sensor.SensorName,
            StringComparison.Ordinal);
        return new TemperatureSensorReadingItem(
            DeviceKindText(device.Device.Kind),
            sensor.SensorName,
            isPrimary ? "总览主传感器" : "有效温度传感器",
            FormatTemperature(sensor.Value),
            "--",
            isPrimary ? FormatTemperature(device.MaximumTemperature) : "--",
            isPrimary ? FormatTemperature(device.AverageTemperature) : "--",
            sensor.SensorId);
    }

    private static string GetDeviceRole(MonitoringSnapshot snapshot, MonitoredDeviceSnapshot device)
    {
        if (device.Device.Temperature is not double temperature || !double.IsFinite(temperature))
        {
            return "未提供有效温度传感器";
        }

        string source = string.IsNullOrWhiteSpace(device.PrimaryTemperatureSensorName)
            ? "当前温度传感器"
            : device.PrimaryTemperatureSensorName;
        return snapshot.Status.IsMock ? $"模拟 Provider（--mock）：{source}" : $"主传感器：{source}";
    }

    private static string FormatTemperature(double? value) =>
        value is double temperature && double.IsFinite(temperature) ? $"{temperature:0}°C" : "不可用";

    private void LoadHistory(bool force)
    {
        try
        {
            TemperatureHistoryRange range = MapRange(SelectedRange);
            TemperatureHistoryQueryResult result = _historyBuffer.Query(range, MaximumChartPointsPerDevice);
            if (!force && HistoryState == TemperatureHistoryLoadState.Ready && _lastLoadedRange == range && _lastLoadedFrom == result.From && _lastLoadedTo == result.To)
            {
                return;
            }

            HistoryState = TemperatureHistoryLoadState.Loading;
            HistoryStatusText = "正在读取本地温度历史…";

            if (!result.HasData)
            {
                ApplySnapshot(new TemperatureHistorySnapshot(
                    TemperatureHistoryLoadState.Empty,
                    $"{SelectedRange} 内暂无可用的温度历史。开始采样后会自动显示。",
                    [],
                    [],
                    []));
                return;
            }

            _lastLoadedRange = range;
            _lastLoadedFrom = result.From;
            _lastLoadedTo = result.To;

            ApplySnapshot(new TemperatureHistorySnapshot(
                TemperatureHistoryLoadState.Ready,
                $"展示 {SelectedRange} 温度历史（{result.From.LocalDateTime:t} – {result.To.LocalDateTime:t}）。",
                GetValues(result, DeviceKind.Cpu),
                GetValues(result, DeviceKind.Gpu),
                GetValues(result, DeviceKind.Storage)));
        }
        catch (Exception)
        {
            ApplySnapshot(new TemperatureHistorySnapshot(
                TemperatureHistoryLoadState.Error,
                "温度历史暂不可用；请稍后重试。",
                [],
                [],
                []));
        }
    }

    /// <summary>
    /// Maps a future non-buffer provider into the same lightweight UI contract.
    /// Empty and error snapshots deliberately clear prior data so stale curves
    /// are never presented as current information.
    /// </summary>
    public void ApplySnapshot(TemperatureHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        HistoryState = snapshot.State;
        HistoryStatusText = snapshot.Message;
        if (snapshot.State != TemperatureHistoryLoadState.Ready)
        {
            _cpuHistory.ReplaceWith([]);
            _gpuHistory.ReplaceWith([]);
            _storageHistory.ReplaceWith([]);
            return;
        }

        _cpuHistory.ReplaceWith(snapshot.Cpu);
        _gpuHistory.ReplaceWith(snapshot.Gpu);
        _storageHistory.ReplaceWith(snapshot.Storage);
    }

    private static double[] GetValues(TemperatureHistoryQueryResult result, DeviceKind deviceKind) =>
        result.Series.FirstOrDefault(series => series.DeviceKind == deviceKind)?.Points
            .Select(static point => point.Value)
            .ToArray()
        ?? [];

    private static TemperatureHistoryRange MapRange(string range) => range switch
    {
        "1 小时" => TemperatureHistoryRange.OneHour,
        "6 小时" => TemperatureHistoryRange.SixHours,
        "24 小时" => TemperatureHistoryRange.TwentyFourHours,
        _ => TemperatureHistoryRange.TenMinutes,
    };

}

public enum TemperatureHistoryLoadState
{
    Loading,
    Ready,
    Empty,
    Error,
}

public sealed record TemperatureHistorySnapshot(
    TemperatureHistoryLoadState State,
    string Message,
    IReadOnlyList<double> Cpu,
    IReadOnlyList<double> Gpu,
    IReadOnlyList<double> Storage);

public sealed record TemperatureDeviceTreeItem(
    string Device,
    string Name,
    string Role,
    string CurrentText);

public partial class TemperatureSensorReadingItem : ObservableObject
{
    public TemperatureSensorReadingItem(
        string device,
        string name,
        string role,
        string current,
        string minimum,
        string maximum,
        string average,
        string identifier)
    {
        Device = device;
        Name = name;
        Role = role;
        Current = current;
        Minimum = minimum;
        Maximum = maximum;
        Average = average;
        Identifier = identifier;
    }

    public string Device { get; }

    public string Name { get; }

    public string Role { get; }

    public string Identifier { get; }

    [ObservableProperty]
    private string _current = string.Empty;

    [ObservableProperty]
    private string _minimum = string.Empty;

    [ObservableProperty]
    private string _maximum = string.Empty;

    [ObservableProperty]
    private string _average = string.Empty;
}
