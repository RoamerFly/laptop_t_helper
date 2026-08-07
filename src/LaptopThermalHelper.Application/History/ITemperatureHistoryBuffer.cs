using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.History;

public enum TemperatureHistoryRange
{
    TenMinutes,
    OneHour,
    SixHours,
    TwentyFourHours,
}

public sealed record TemperatureHistorySeries(
    string DeviceId,
    DeviceKind DeviceKind,
    string DisplayName,
    IReadOnlyList<TemperaturePoint> Points);

/// <summary>
/// A bounded, downsampled read model for graphing. <see cref="Points"/> are
/// copied before leaving the buffer so a UI renderer never enumerates a mutable
/// collection while sampling is in progress.
/// </summary>
public sealed record TemperatureHistoryQueryResult(
    TemperatureHistoryRange Range,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<TemperatureHistorySeries> Series)
{
    public bool HasData => Series.Any(static series => series.Points.Count > 0);
}

public interface ITemperatureHistoryBuffer
{
    void Append(MonitoringSnapshot snapshot);

    TemperatureHistoryQueryResult Query(
        TemperatureHistoryRange range,
        int maxPointsPerDevice = 360);
}
