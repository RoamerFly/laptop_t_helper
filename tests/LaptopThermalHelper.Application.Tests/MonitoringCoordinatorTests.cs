using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using NSubstitute;

namespace LaptopThermalHelper.Application.Tests;

public sealed class MonitoringCoordinatorTests
{
    [Fact]
    public async Task PollAsync_WhenNoValidKeyTemperature_ReturnsUnknownSystemLevel()
    {
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DeviceSample>>([]));
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        Assert.Equal(ThermalLevel.Unknown, snapshot.SystemLevel);
        Assert.Empty(snapshot.Devices);
    }

    [Fact]
    public async Task PollAsync_AggregatesHighestValidDeviceLevel()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<DeviceSample>>(
            [
                Sample("cpu", DeviceKind.Cpu, 60, start),
                Sample("gpu", DeviceKind.Gpu, 60, start),
                Sample("ssd", DeviceKind.Storage, 40, start),
            ]),
            Task.FromResult<IReadOnlyList<DeviceSample>>(
            [
                Sample("cpu", DeviceKind.Cpu, 98, start.AddSeconds(1)),
                Sample("gpu", DeviceKind.Gpu, 60, start.AddSeconds(1)),
                Sample("ssd", DeviceKind.Storage, 40, start.AddSeconds(1)),
            ]),
            Task.FromResult<IReadOnlyList<DeviceSample>>(
            [
                Sample("cpu", DeviceKind.Cpu, 98, start.AddSeconds(11)),
                Sample("gpu", DeviceKind.Gpu, 60, start.AddSeconds(11)),
                Sample("ssd", DeviceKind.Storage, 40, start.AddSeconds(11)),
            ]));
        var coordinator = new MonitoringCoordinator(provider);

        await coordinator.PollAsync();
        await coordinator.PollAsync();
        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        Assert.Equal(ThermalLevel.Critical, snapshot.SystemLevel);
    }

    [Fact]
    public async Task PollAsync_TrendNeverExceedsTenMinuteCapacity()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<DeviceSample>>(
                [Sample("cpu", DeviceKind.Cpu, 60, timestamp = timestamp.AddSeconds(2))]));
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = MonitoringSnapshot.Empty;
        for (int index = 0; index < 301; index++)
        {
            snapshot = await coordinator.PollAsync();
        }

        Assert.Equal(300, Assert.Single(snapshot.Devices).Trend.Count);
    }

    private static DeviceSample Sample(
        string id,
        DeviceKind kind,
        double? temperature,
        DateTimeOffset timestamp) =>
        new(id, kind, id, temperature, null, null, null, timestamp);
}
