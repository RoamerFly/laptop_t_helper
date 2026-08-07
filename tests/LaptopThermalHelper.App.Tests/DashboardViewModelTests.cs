using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task RefreshAsync_RealProviderWithoutTemperatures_ShowsAvailabilityDiagnostic()
    {
        var dashboard = new DashboardViewModel(
            new MonitoringCoordinator(new StubProvider(HardwareProviderMode.RealHardware, [])),
            new EmptyHistoryStore());

        await dashboard.RefreshAsync();

        Assert.Equal("硬件传感器不可用", dashboard.SystemStatus);
        Assert.Contains("传感器", dashboard.SystemMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_ExplicitMockProvider_IsNeverLabelledAsNormalHardware()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var dashboard = new DashboardViewModel(
            new MonitoringCoordinator(new StubProvider(
                HardwareProviderMode.Mock,
                [new DeviceSample("cpu", DeviceKind.Cpu, "CPU", 58, null, null, null, now)])),
            new EmptyHistoryStore());

        await dashboard.RefreshAsync();

        Assert.Equal("模拟传感器数据", dashboard.SystemStatus);
        Assert.Contains("--mock", dashboard.SystemMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_PrefersTemperatureCapableStorageOverEarlierUnavailableDevice()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var dashboard = new DashboardViewModel(
            new MonitoringCoordinator(new StubProvider(
                HardwareProviderMode.RealHardware,
                [
                    new DeviceSample("usb-storage", DeviceKind.Storage, "USB 存储", null, null, null, null, now),
                    new DeviceSample("nvme-storage", DeviceKind.Storage, "NVMe 存储", 68, null, null, null, now),
                ])),
            new EmptyHistoryStore());

        await dashboard.RefreshAsync();

        Assert.Equal("NVMe 存储", dashboard.Storage.DeviceName);
        Assert.Equal("68°C", dashboard.Storage.CurrentText);
    }

    private sealed class StubProvider(
        HardwareProviderMode mode,
        IReadOnlyList<DeviceSample> samples) : IHardwareMonitorProvider, IHardwareMonitorProviderMetadata
    {
        public HardwareProviderMode Mode { get; } = mode;

        public Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(samples);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyHistoryStore : LaptopThermalHelper.Application.History.ITemperatureHistoryStore
    {
        public Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LaptopThermalHelper.Application.History.HistoryExportResult> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LaptopThermalHelper.Application.History.HistoryExportResult.Empty);
    }
}
