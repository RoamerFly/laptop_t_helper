using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
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
                Sample("cpu", DeviceKind.Cpu, 101, start.AddSeconds(1)),
                Sample("gpu", DeviceKind.Gpu, 60, start.AddSeconds(1)),
                Sample("ssd", DeviceKind.Storage, 40, start.AddSeconds(1)),
            ]),
            Task.FromResult<IReadOnlyList<DeviceSample>>(
            [
                Sample("cpu", DeviceKind.Cpu, 101, start.AddSeconds(11)),
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

    [Fact]
    public async Task PollAsync_RecordsHistoryAtFiveSecondIntervals()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        IHardwareMonitorProvider provider = CreateSequentialProvider(
            Sample("cpu", DeviceKind.Cpu, 60, start),
            Sample("cpu", DeviceKind.Cpu, 61, start.AddSeconds(2)),
            Sample("cpu", DeviceKind.Cpu, 62, start.AddSeconds(4)),
            Sample("cpu", DeviceKind.Cpu, 63, start.AddSeconds(6)));
        ITemperatureHistoryStore historyStore = Substitute.For<ITemperatureHistoryStore>();
        historyStore.AppendAsync(Arg.Any<MonitoringSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var coordinator = new MonitoringCoordinator(provider, historyStore);

        await coordinator.PollAsync();
        await coordinator.PollAsync();
        await coordinator.PollAsync();
        await coordinator.PollAsync();

        await historyStore.Received(2).AppendAsync(
            Arg.Any<MonitoringSnapshot>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollAsync_WhenHistoryWriteFails_KeepsSnapshotAndRetries()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        IHardwareMonitorProvider provider = CreateSequentialProvider(
            Sample("cpu", DeviceKind.Cpu, 60, start),
            Sample("cpu", DeviceKind.Cpu, 61, start.AddSeconds(2)));
        ITemperatureHistoryStore historyStore = Substitute.For<ITemperatureHistoryStore>();
        historyStore.AppendAsync(Arg.Any<MonitoringSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("磁盘不可用")), Task.CompletedTask);
        var coordinator = new MonitoringCoordinator(provider, historyStore);

        MonitoringSnapshot first = await coordinator.PollAsync();
        MonitoringSnapshot second = await coordinator.PollAsync();

        Assert.Single(first.Devices);
        Assert.Single(second.Devices);
        Assert.Null(coordinator.LastHistoryWriteError);
        await historyStore.Received(2).AppendAsync(
            Arg.Any<MonitoringSnapshot>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollAsync_WhenRealProviderReturnsNoTemperature_ReportsUnavailableInsteadOfMockData()
    {
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DeviceSample>>(
            [Sample("cpu", DeviceKind.Cpu, null, DateTimeOffset.UtcNow)]));
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        Assert.Equal(MonitoringAvailability.Unavailable, snapshot.Status.Availability);
        Assert.Equal(HardwareProviderMode.RealHardware, snapshot.Status.Mode);
        Assert.Equal("cpu", Assert.Single(snapshot.Devices).Device.DeviceId);
        Assert.Null(Assert.Single(snapshot.Devices).Device.Temperature);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task PollAsync_WhenProviderReturnsInvalidTemperature_ExposesUnavailableInsteadOfAReading(double temperature)
    {
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DeviceSample>>(
            [Sample("storage", DeviceKind.Storage, temperature, DateTimeOffset.UtcNow)]));
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        MonitoredDeviceSnapshot device = Assert.Single(snapshot.Devices);
        Assert.Equal(MonitoringAvailability.Unavailable, snapshot.Status.Availability);
        Assert.Equal(ThermalLevel.Unknown, device.Device.ThermalLevel);
        Assert.Null(device.Device.Temperature);
        Assert.Empty(device.Trend);
    }

    [Fact]
    public async Task PollAsync_PreservesValidTemperatureSensorSourceForDetailViews()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var sample = Sample("storage", DeviceKind.Storage, 68, now) with
        {
            PrimaryTemperatureSensorName = "Composite",
            TemperatureSensors =
            [
                new SensorReading(
                    "storage",
                    DeviceKind.Storage,
                    "NVMe",
                    "storage/temperature/0",
                    "Composite",
                    SensorMetric.Temperature,
                    68,
                    "°C",
                    now,
                    ReadingQuality.Good),
            ],
        };
        IHardwareMonitorProvider provider = CreateSequentialProvider(sample);
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        MonitoredDeviceSnapshot device = Assert.Single(snapshot.Devices);
        Assert.Equal("Composite", device.PrimaryTemperatureSensorName);
        SensorReading sensor = Assert.Single(device.TemperatureSensors);
        Assert.Equal("storage/temperature/0", sensor.SensorId);
        Assert.Equal(68, sensor.Value);
    }

    [Fact]
    public async Task PollAsync_WhenProviderFails_ReportsExplicitError()
    {
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        provider.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<DeviceSample>>>(
            _ => throw new InvalidOperationException("provider unavailable"));
        var coordinator = new MonitoringCoordinator(provider);

        MonitoringSnapshot snapshot = await coordinator.PollAsync();

        Assert.Empty(snapshot.Devices);
        Assert.Equal(ThermalLevel.Unknown, snapshot.SystemLevel);
        Assert.Equal(MonitoringAvailability.Error, snapshot.Status.Availability);
        Assert.Equal(HardwareProviderMode.RealHardware, snapshot.Status.Mode);
    }

    [Fact]
    public async Task UpdateThresholds_DynamicallyUpdatesStateMachinesForDevices()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var sample1 = Sample("cpu", DeviceKind.Cpu, 78, now);
        var sample2 = Sample("cpu", DeviceKind.Cpu, 78, now.AddSeconds(2));
        var sample3 = Sample("cpu", DeviceKind.Cpu, 78, now.AddSeconds(25));
        IHardwareMonitorProvider provider = CreateSequentialProvider(sample1, sample2, sample3);
        var coordinator = new MonitoringCoordinator(provider);

        // First poll: 78°C with default CPU thresholds (High=95) is Normal
        MonitoringSnapshot snap1 = await coordinator.PollAsync();
        Assert.Equal(ThermalLevel.Normal, snap1.Devices[0].Device.ThermalLevel);

        // Update thresholds: CPU High becomes 75°C (so 78°C is in High range)
        coordinator.UpdateThresholds(75, 80, 70);

        // Second poll: within delay period (20s delay), still Normal
        MonitoringSnapshot snap2 = await coordinator.PollAsync();
        Assert.Equal(ThermalLevel.Normal, snap2.Devices[0].Device.ThermalLevel);

        // Third poll: after 23 seconds (> 20s delay), transitions to High
        MonitoringSnapshot snap3 = await coordinator.PollAsync();
        Assert.Equal(ThermalLevel.High, snap3.Devices[0].Device.ThermalLevel);
    }

    [Fact]
    public async Task PollAsync_TracksRunningStatisticsForIndividualSensors()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SensorReading CreateReading(double val, DateTimeOffset ts) => new(
            "gpu",
            DeviceKind.Gpu,
            "GPU Core",
            "gpu/temperature/0",
            "Core Temperature",
            SensorMetric.Temperature,
            val,
            "°C",
            ts,
            ReadingQuality.Good);

        var sample1 = Sample("gpu", DeviceKind.Gpu, 50, now) with
        {
            TemperatureSensors = [CreateReading(50, now)],
        };
        var sample2 = Sample("gpu", DeviceKind.Gpu, 70, now.AddSeconds(2)) with
        {
            TemperatureSensors = [CreateReading(70, now.AddSeconds(2))],
        };
        var sample3 = Sample("gpu", DeviceKind.Gpu, 60, now.AddSeconds(4)) with
        {
            TemperatureSensors = [CreateReading(60, now.AddSeconds(4))],
        };

        IHardwareMonitorProvider provider = CreateSequentialProvider(sample1, sample2, sample3);
        var coordinator = new MonitoringCoordinator(provider);

        await coordinator.PollAsync();
        await coordinator.PollAsync();
        MonitoringSnapshot snap = await coordinator.PollAsync();

        MonitoredDeviceSnapshot device = Assert.Single(snap.Devices);
        Assert.Equal(50, device.MinimumTemperature);
        Assert.Equal(70, device.MaximumTemperature);
        Assert.Equal(60, device.AverageTemperature);

        SensorReading sensor = Assert.Single(device.TemperatureSensors);
        Assert.Equal(50, sensor.Minimum);
        Assert.Equal(70, sensor.Maximum);
        Assert.Equal(60, sensor.Average);
    }

    private static IHardwareMonitorProvider CreateSequentialProvider(params DeviceSample[] samples)
    {
        IHardwareMonitorProvider provider = Substitute.For<IHardwareMonitorProvider>();
        var queue = new Queue<DeviceSample>(samples);
        provider.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<DeviceSample>>([queue.Dequeue()]));
        return provider;
    }

    private static DeviceSample Sample(
        string id,
        DeviceKind kind,
        double? temperature,
        DateTimeOffset timestamp) =>
        new(id, kind, id, temperature, null, null, null, timestamp);
}
