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
    DateTimeOffset Timestamp);

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
