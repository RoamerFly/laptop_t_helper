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
/// Information about the Intel integrated GPU driver state.
/// </summary>
public sealed record IntelGpuDriverInfo(
    bool IsIntelGpuPresent,
    string? DriverVersion,
    DateTime? DriverDate,
    bool IsTooOld,
    string Summary)
{
    /// <summary>
    /// Driver versions older than this date are considered too old for IGCL.
    /// IGCL requires Intel graphics driver 31.0.101.2127 or later (2022-10+).
    /// </summary>
    public static readonly DateTime MinimumDriverDate = new(2022, 10, 1);

    public static readonly string MinimumRecommendedVersion = "31.0.101.2127";

    public static IntelGpuDriverInfo NotPresent() =>
        new(false, null, null, false, "未检测到 Intel 核显");

    public static IntelGpuDriverInfo Unknown(string summary) =>
        new(true, null, null, true, summary);
}
