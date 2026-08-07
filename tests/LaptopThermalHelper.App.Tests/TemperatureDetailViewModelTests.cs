using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Tests;

public sealed class TemperatureDetailViewModelTests
{
    [Fact]
    public void SelectedRange_QueriesRuntimeBufferAndMapsTheMatchingRange()
    {
        var history = new RecordingHistoryBuffer();
        var detail = CreateDetail(history);

        Assert.Equal(TemperatureHistoryRange.TenMinutes, history.LastRange);
        Assert.Equal(TemperatureHistoryLoadState.Ready, detail.HistoryState);

        detail.SelectedRange = "6 小时";

        Assert.Equal(TemperatureHistoryRange.SixHours, history.LastRange);
        Assert.Equal(TemperatureHistoryLoadState.Ready, detail.HistoryState);
    }

    [Fact]
    public void EmptyRuntimeHistory_UsesExplicitEmptyState()
    {
        var history = new RecordingHistoryBuffer { ReturnEmpty = true };
        var detail = CreateDetail(history);

        Assert.True(detail.IsHistoryEmpty);
        Assert.False(detail.HasHistoryData);
        Assert.Contains("暂无", detail.HistoryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodicSamplerRefresh_IsThrottledBeforeRequeryingHistory()
    {
        var history = new RecordingHistoryBuffer();
        var detail = CreateDetail(history);
        int initialQueries = history.QueryCount;

        detail.RefreshAfterSampling();
        detail.RefreshAfterSampling();

        Assert.Equal(initialQueries + 1, history.QueryCount);
    }

    [Fact]
    public async Task RefreshFromDashboard_ShowsReportedSensorSourcesAndMarksUnavailableDevices()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceSample cpu = Sample("cpu", DeviceKind.Cpu, "CPU", "CPU Package", 64, now);
        DeviceSample storageWithoutTemperature = new("storage", DeviceKind.Storage, "NVMe", null, null, null, null, now);
        var dashboard = new DashboardViewModel(
            new MonitoringCoordinator(new SampleHardwareProvider([cpu, storageWithoutTemperature])),
            new EmptyHistoryStore());
        await dashboard.RefreshAsync();
        var detail = new TemperatureDetailViewModel(dashboard, new RecordingHistoryBuffer());

        detail.RefreshFromDashboard();

        TemperatureSensorReadingItem reading = Assert.Single(detail.SensorReadings);
        Assert.Equal("CPU Package", reading.Name);
        Assert.Equal("cpu/temperature/0", reading.Identifier);
        Assert.Equal("总览主传感器", reading.Role);
        TemperatureDeviceTreeItem storage = Assert.Single(detail.DeviceTree, item => item.Name == "NVMe");
        Assert.Equal("未提供有效温度传感器", storage.Role);
        Assert.Equal("不可用", storage.CurrentText);
    }

    private static TemperatureDetailViewModel CreateDetail(RecordingHistoryBuffer history)
    {
        var dashboard = new DashboardViewModel(
            new MonitoringCoordinator(new EmptyHardwareProvider()),
            new EmptyHistoryStore());
        return new TemperatureDetailViewModel(dashboard, history);
    }

    private sealed class RecordingHistoryBuffer : ITemperatureHistoryBuffer
    {
        public TemperatureHistoryRange LastRange { get; private set; }

        public int QueryCount { get; private set; }

        public bool ReturnEmpty { get; init; }

        public void Append(MonitoringSnapshot snapshot)
        {
        }

        public TemperatureHistoryQueryResult Query(TemperatureHistoryRange range, int maxPointsPerDevice = 360)
        {
            LastRange = range;
            QueryCount++;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<TemperatureHistorySeries> series = ReturnEmpty
                ? []
                :
                [
                    new TemperatureHistorySeries(
                        "cpu",
                        DeviceKind.Cpu,
                        "CPU",
                        [new TemperaturePoint(now.AddMinutes(-1), 61), new TemperaturePoint(now, 62)]),
                ];
            return new TemperatureHistoryQueryResult(range, now.AddMinutes(-10), now, series);
        }
    }

    private sealed class EmptyHardwareProvider : IHardwareMonitorProvider
    {
        public Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceSample>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SampleHardwareProvider(IReadOnlyList<DeviceSample> samples) : IHardwareMonitorProvider
    {
        public Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(samples);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DeviceSample Sample(
        string id,
        DeviceKind kind,
        string name,
        string sensorName,
        double temperature,
        DateTimeOffset timestamp) =>
        new DeviceSample(id, kind, name, temperature, null, null, null, timestamp)
        {
            PrimaryTemperatureSensorName = sensorName,
            TemperatureSensors =
            [
                new SensorReading(
                    id,
                    kind,
                    name,
                    $"{id}/temperature/0",
                    sensorName,
                    SensorMetric.Temperature,
                    temperature,
                    "°C",
                    timestamp,
                    ReadingQuality.Good),
            ],
        };

    private sealed class EmptyHistoryStore : ITemperatureHistoryStore
    {
        public Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HistoryExportResult> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HistoryExportResult.Empty);
    }
}
