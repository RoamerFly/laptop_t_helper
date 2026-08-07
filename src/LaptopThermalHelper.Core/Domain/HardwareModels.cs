namespace LaptopThermalHelper.Core.Domain;

public enum DeviceKind
{
    Cpu,
    Gpu,
    Storage,
    Memory,
    Battery,
    System,
}

public enum SensorMetric
{
    Temperature,
    Load,
    Power,
    FanSpeed,
    MemoryUsage,
}

public enum ReadingQuality
{
    Good,
    Missing,
    Invalid,
    Stale,
}

public enum ThermalLevel
{
    Unknown = -1,
    Normal = 0,
    Elevated = 1,
    High = 2,
    Critical = 3,
}

public sealed record SensorReading(
    string DeviceId,
    DeviceKind DeviceKind,
    string DeviceName,
    string SensorId,
    string SensorName,
    SensorMetric Metric,
    double? Value,
    string Unit,
    DateTimeOffset Timestamp,
    ReadingQuality Quality);

public sealed record DeviceSample(
    string DeviceId,
    DeviceKind Kind,
    string DisplayName,
    double? Temperature,
    double? Load,
    double? Power,
    double? FanRpm,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Temperature readings exposed by the provider for this physical device.
    /// These are source readings, not derived dashboard values, so a detail UI
    /// can identify the exact sensor used for a displayed temperature.
    /// </summary>
    public IReadOnlyList<SensorReading> TemperatureSensors { get; init; } = [];

    /// <summary>
    /// Name of the sensor selected as the device's dashboard temperature.
    /// A null value means the device is known but did not expose a usable
    /// temperature reading.
    /// </summary>
    public string? PrimaryTemperatureSensorName { get; init; }
}

public sealed record DeviceSnapshot(
    string DeviceId,
    DeviceKind Kind,
    string DisplayName,
    double? Temperature,
    double? Load,
    double? Power,
    double? FanRpm,
    ThermalLevel ThermalLevel,
    DateTimeOffset Timestamp);
