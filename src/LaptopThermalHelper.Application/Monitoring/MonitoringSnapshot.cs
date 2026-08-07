using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.Monitoring;

public sealed record TemperaturePoint(DateTimeOffset Timestamp, double Value);

public sealed record MonitoredDeviceSnapshot(
    DeviceSnapshot Device,
    double? MaximumTemperature,
    double? AverageTemperature,
    IReadOnlyList<TemperaturePoint> Trend)
{
    /// <summary>
    /// All usable temperature sensors reported for this device in the current
    /// sample. The dashboard uses only the primary temperature; details can
    /// show every source sensor without inventing names or values.
    /// </summary>
    public IReadOnlyList<SensorReading> TemperatureSensors { get; init; } = [];

    public string? PrimaryTemperatureSensorName { get; init; }
}

public sealed record MonitoringSnapshot(
    IReadOnlyList<MonitoredDeviceSnapshot> Devices,
    ThermalLevel SystemLevel,
    DateTimeOffset Timestamp,
    MonitoringAcquisitionStatus Status)
{
    public MonitoringSnapshot(
        IReadOnlyList<MonitoredDeviceSnapshot> devices,
        ThermalLevel systemLevel,
        DateTimeOffset timestamp)
        : this(
            devices,
            systemLevel,
            timestamp,
            MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware))
    {
    }

    public static MonitoringSnapshot Empty { get; } =
        new([], ThermalLevel.Unknown, DateTimeOffset.MinValue, MonitoringAcquisitionStatus.Unavailable());
}

public enum MonitoringAvailability
{
    Ready,
    Unavailable,
    Error,
}

/// <summary>
/// Explicit state of the most recent hardware read. UI layers can distinguish a
/// missing real sensor from the explicitly requested <c>--mock</c> provider.
/// </summary>
public sealed record MonitoringAcquisitionStatus(
    HardwareProviderMode Mode,
    MonitoringAvailability Availability,
    string Message)
{
    public bool IsMock => Mode == HardwareProviderMode.Mock;

    public static MonitoringAcquisitionStatus Ready(HardwareProviderMode mode) =>
        new(
            mode,
            MonitoringAvailability.Ready,
            mode == HardwareProviderMode.Mock ? "模拟硬件数据（--mock）" : "真实硬件传感器可用");

    public static MonitoringAcquisitionStatus Unavailable(
        HardwareProviderMode mode = HardwareProviderMode.RealHardware,
        string message = "未发现可读取的真实温度传感器。请检查驱动、权限或硬件支持。") =>
        new(mode, MonitoringAvailability.Unavailable, message);

    public static MonitoringAcquisitionStatus Error(
        HardwareProviderMode mode,
        string message) =>
        new(mode, MonitoringAvailability.Error, message);
}
