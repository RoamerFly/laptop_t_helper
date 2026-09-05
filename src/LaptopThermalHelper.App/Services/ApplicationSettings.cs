using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Services;

/// <summary>
/// User-selectable automatic-cooling aggressiveness. MonitorOnly never writes
/// any power setting; the other two differ in how far the processor state is
/// allowed to drop while cooling is active.
/// </summary>
public enum CoolingPolicyKind
{
    MonitorOnly,

    Moderate,

    Aggressive,
}

public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int SamplingIntervalSeconds { get; init; } = 2;

    public bool NotificationsEnabled { get; init; } = true;

    public bool CriticalAlertSoundEnabled { get; init; }

    public ThermalLevel NotificationThreshold { get; init; } = ThermalLevel.High;

    public int NotificationCooldownSeconds { get; init; } = 600;

    public bool StartWithWindows { get; init; }

    public bool MinimizeToTray { get; init; } = true;

    public bool ShowFloatingWindow { get; init; }

    public double? FloatingWindowLeft { get; init; }

    public double? FloatingWindowTop { get; init; }

    public int CpuHighThresholdCelsius { get; init; } = 90;

    public int GpuHighThresholdCelsius { get; init; } = 85;

    public int StorageHighThresholdCelsius { get; init; } = 70;

    public bool AutoCoolingEnabled { get; init; }

    /// <summary>Cooling aggressiveness selected on the performance page.</summary>
    public CoolingPolicyKind CoolingPolicy { get; init; } = CoolingPolicyKind.MonitorOnly;

    public int AutoCoolingTriggerCelsius { get; init; } = 90;

    public int AutoCoolingSustainSeconds { get; init; } = 30;

    public int AutoCoolingRecoverySeconds { get; init; } = 120;

    public int AutoCoolingHysteresisCelsius { get; init; } = 3;

    public int AutoCoolingMaxProcessorStatePercent { get; init; } = 90;

    public static ApplicationSettings Default { get; } = new();

    public ApplicationSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        SamplingIntervalSeconds = SamplingIntervalSeconds is 1 or 2 or 5
            ? SamplingIntervalSeconds
            : Default.SamplingIntervalSeconds,
        NotificationThreshold = NotificationThreshold is ThermalLevel.Elevated or ThermalLevel.High or ThermalLevel.Critical
            ? NotificationThreshold
            : Default.NotificationThreshold,
        NotificationCooldownSeconds = Math.Clamp(NotificationCooldownSeconds, 60, 3_600),
        CpuHighThresholdCelsius = Math.Clamp(CpuHighThresholdCelsius, 50, 110),
        GpuHighThresholdCelsius = Math.Clamp(GpuHighThresholdCelsius, 50, 110),
        StorageHighThresholdCelsius = Math.Clamp(StorageHighThresholdCelsius, 40, 100),
        AutoCoolingTriggerCelsius = Math.Clamp(AutoCoolingTriggerCelsius, 60, 105),
        AutoCoolingSustainSeconds = Math.Clamp(AutoCoolingSustainSeconds, 10, 300),
        AutoCoolingRecoverySeconds = Math.Clamp(AutoCoolingRecoverySeconds, 30, 900),
        AutoCoolingHysteresisCelsius = Math.Clamp(AutoCoolingHysteresisCelsius, 1, 15),
        AutoCoolingMaxProcessorStatePercent = Math.Clamp(AutoCoolingMaxProcessorStatePercent, 50, 99),
    };

    /// <summary>
    /// Processor-state ceiling applied while automatic cooling is active.
    /// Derived from the cooling policy so the three levels stay clearly
    /// differentiated: monitor-only keeps 100% (no limit), moderate drops to
    /// 90%, aggressive drops to 80%. The stored value is ignored when the
    /// policy is MonitorOnly because no power setting is ever written.
    /// </summary>
    public int EffectiveAutoCoolingMaxProcessorStatePercent => CoolingPolicy switch
    {
        CoolingPolicyKind.Aggressive => Math.Min(AutoCoolingMaxProcessorStatePercent, 80),
        CoolingPolicyKind.Moderate => Math.Min(AutoCoolingMaxProcessorStatePercent, 90),
        _ => 100,
    };
}

public sealed record SettingsLoadResult(ApplicationSettings Settings, string? Notice = null);

public interface IApplicationSettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
