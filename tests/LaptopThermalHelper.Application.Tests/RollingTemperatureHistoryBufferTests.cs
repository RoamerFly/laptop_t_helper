using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.Tests;

public sealed class RollingTemperatureHistoryBufferTests
{
    [Fact]
    public void Query_TenMinuteWindow_ExcludesOlderSamples()
    {
        var buffer = new RollingTemperatureHistoryBuffer();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        buffer.Append(Snapshot(40, now.AddMinutes(-12)));
        buffer.Append(Snapshot(55, now.AddMinutes(-4)));

        TemperatureHistoryQueryResult result = buffer.Query(TemperatureHistoryRange.TenMinutes);

        TemperatureHistorySeries series = Assert.Single(result.Series);
        TemperaturePoint point = Assert.Single(series.Points);
        Assert.Equal(55, point.Value);
    }

    [Fact]
    public void Query_DownsamplesWhileRetainingBucketExtrema()
    {
        var buffer = new RollingTemperatureHistoryBuffer();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        double[] values = [40, 99, 45, 42, 35, 80, 44, 43, 32, 74];
        for (int index = 0; index < values.Length; index++)
        {
            buffer.Append(Snapshot(values[index], now.AddSeconds(-values.Length + index)));
        }

        TemperatureHistoryQueryResult result = buffer.Query(TemperatureHistoryRange.OneHour, maxPointsPerDevice: 4);

        IReadOnlyList<TemperaturePoint> points = Assert.Single(result.Series).Points;
        Assert.InRange(points.Count, 1, 4);
        Assert.Contains(points, static point => point.Value == 99);
        Assert.Contains(points, static point => point.Value == 32);
    }

    [Fact]
    public void Append_AtCapacity_DropsTheOldestSamples()
    {
        var buffer = new RollingTemperatureHistoryBuffer(maximumSamplesPerDevice: 3);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int value = 1; value <= 4; value++)
        {
            buffer.Append(Snapshot(value, now.AddSeconds(value)));
        }

        TemperatureHistoryQueryResult result = buffer.Query(TemperatureHistoryRange.TwentyFourHours, 10);

        Assert.Equal([2d, 3d, 4d], Assert.Single(result.Series).Points.Select(static point => point.Value));
    }

    [Fact]
    public void Query_WithOnePointLimit_RespectsTheLimit()
    {
        var buffer = new RollingTemperatureHistoryBuffer();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        buffer.Append(Snapshot(40, now.AddSeconds(-2)));
        buffer.Append(Snapshot(90, now.AddSeconds(-1)));

        TemperatureHistoryQueryResult result = buffer.Query(TemperatureHistoryRange.OneHour, maxPointsPerDevice: 1);

        Assert.Single(Assert.Single(result.Series).Points);
    }

    [Fact]
    public void Query_SupportsAllRequestedWindows()
    {
        var buffer = new RollingTemperatureHistoryBuffer();
        buffer.Append(Snapshot(60, DateTimeOffset.UtcNow));

        Assert.True(buffer.Query(TemperatureHistoryRange.TenMinutes).HasData);
        Assert.True(buffer.Query(TemperatureHistoryRange.OneHour).HasData);
        Assert.True(buffer.Query(TemperatureHistoryRange.SixHours).HasData);
        Assert.True(buffer.Query(TemperatureHistoryRange.TwentyFourHours).HasData);
    }

    private static MonitoringSnapshot Snapshot(double temperature, DateTimeOffset timestamp)
    {
        var device = new DeviceSnapshot(
            "cpu",
            DeviceKind.Cpu,
            "CPU",
            temperature,
            null,
            null,
            null,
            ThermalLevel.Normal,
            timestamp);
        return new MonitoringSnapshot(
            [new MonitoredDeviceSnapshot(device, temperature, temperature, [])],
            ThermalLevel.Normal,
            timestamp);
    }
}
