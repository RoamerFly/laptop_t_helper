using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.Monitoring;

public sealed record TemperaturePoint(DateTimeOffset Timestamp, double Value);

public sealed record MonitoredDeviceSnapshot(
    DeviceSnapshot Device,
    double? MaximumTemperature,
    double? AverageTemperature,
    IReadOnlyList<TemperaturePoint> Trend);

public sealed record MonitoringSnapshot(
    IReadOnlyList<MonitoredDeviceSnapshot> Devices,
    ThermalLevel SystemLevel,
    DateTimeOffset Timestamp)
{
    public static MonitoringSnapshot Empty { get; } =
        new([], ThermalLevel.Unknown, DateTimeOffset.MinValue);
}
