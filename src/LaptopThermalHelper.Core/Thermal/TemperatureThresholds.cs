using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Core.Thermal;

public sealed record TemperatureThresholds(
    double ElevatedAt,
    double HighAt,
    double CriticalAt,
    TimeSpan ElevatedDelay,
    TimeSpan HighDelay,
    TimeSpan CriticalDelay,
    double RecoveryHysteresis,
    TimeSpan RecoveryDelay)
{
    public static TemperatureThresholds CpuDefault { get; } = Create(75, 90, 95);

    public static TemperatureThresholds GpuDefault { get; } = Create(70, 82, 86);

    public static TemperatureThresholds StorageDefault { get; } = Create(55, 70, 75);

    public static TemperatureThresholds For(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => CpuDefault,
        DeviceKind.Gpu => GpuDefault,
        DeviceKind.Storage => StorageDefault,
        _ => CpuDefault,
    };

    public void Validate()
    {
        if (ElevatedAt >= HighAt || HighAt >= CriticalAt)
        {
            throw new InvalidOperationException("温度阈值必须按偏高、过高、严重过热递增。");
        }

        if (RecoveryHysteresis < 0 || ElevatedDelay < TimeSpan.Zero ||
            HighDelay < TimeSpan.Zero || CriticalDelay < TimeSpan.Zero || RecoveryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("延迟和迟滞参数不能为负数。");
        }
    }

    public ThermalLevel Classify(double temperature) => temperature switch
    {
        _ when temperature >= CriticalAt => ThermalLevel.Critical,
        _ when temperature >= HighAt => ThermalLevel.High,
        _ when temperature >= ElevatedAt => ThermalLevel.Elevated,
        _ => ThermalLevel.Normal,
    };

    public double LowerBound(ThermalLevel level) => level switch
    {
        ThermalLevel.Elevated => ElevatedAt,
        ThermalLevel.High => HighAt,
        ThermalLevel.Critical => CriticalAt,
        _ => double.NegativeInfinity,
    };

    public TimeSpan EscalationDelay(ThermalLevel level) => level switch
    {
        ThermalLevel.Critical => CriticalDelay,
        ThermalLevel.High => HighDelay,
        ThermalLevel.Elevated => ElevatedDelay,
        _ => TimeSpan.Zero,
    };

    private static TemperatureThresholds Create(double elevatedAt, double highAt, double criticalAt) =>
        new(
            elevatedAt,
            highAt,
            criticalAt,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(30));
}
