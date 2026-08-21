namespace LaptopThermalHelper.Application.System;

/// <summary>
/// Detects whether the Intel integrated GPU driver is too old for IGCL
/// (Intel Graphics Control Library) to initialize, which prevents the
/// integrated GPU temperature from being read.
/// </summary>
public interface IIntelGpuDriverDetector
{
    Task<IntelGpuDriverInfo> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The state of the Intel integrated GPU driver relative to temperature
/// sensing capability.
/// </summary>
public enum IntelGpuDriverState
{
    /// <summary>
    /// No Intel integrated GPU was detected.
    /// </summary>
    NotPresent,

    /// <summary>
    /// The driver version is too old for IGCL to provide temperature telemetry.
    /// </summary>
    TooOld,

    /// <summary>
    /// The driver version is sufficient, but the hardware/driver does not
    /// expose a temperature sensor (e.g. UHD Graphics on Tiger Lake).
    /// </summary>
    SupportedButNoTemperature,

    /// <summary>
    /// The driver version is sufficient and temperature should be available.
    /// </summary>
    Ok,

    /// <summary>
    /// Unable to determine the driver version.
    /// </summary>
    Unknown,
}

/// <summary>
/// Information about the Intel integrated GPU driver state.
/// </summary>
public sealed record IntelGpuDriverInfo(
    IntelGpuDriverState State,
    string? DriverVersion,
    DateTime? DriverDate,
    string Summary)
{
    // Backward-compatible boolean: true when the driver is too old.
    public bool IsTooOld => State == IntelGpuDriverState.TooOld;

    public bool IsIntelGpuPresent => State != IntelGpuDriverState.NotPresent;

    /// <summary>
    /// Driver versions older than this date are considered too old for IGCL.
    /// IGCL requires Intel graphics driver 31.0.101.2127 or later (2022-10+).
    /// </summary>
    public static readonly DateTime MinimumDriverDate = new(2022, 10, 1);

    public static readonly string MinimumRecommendedVersion = "31.0.101.2127";

    public static IntelGpuDriverInfo NotPresent() =>
        new(IntelGpuDriverState.NotPresent, null, null, "未检测到 Intel 核显");

    public static IntelGpuDriverInfo Unknown(string summary) =>
        new(IntelGpuDriverState.Unknown, null, null, summary);
}
