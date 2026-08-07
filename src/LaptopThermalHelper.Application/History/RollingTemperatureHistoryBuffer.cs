using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.History;

/// <summary>
/// Thread-safe, in-memory temperature history retained for the most recent
/// twenty-four hours. It holds raw samples for accurate short ranges and emits
/// a bounded min/max-preserving series for charts.
/// </summary>
public sealed class RollingTemperatureHistoryBuffer : ITemperatureHistoryBuffer
{
    public const int DefaultMaximumSamplesPerDevice = 86_400;
    public const int DefaultMaximumChartPoints = 360;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceHistory> _histories = new(StringComparer.Ordinal);
    private readonly int _maximumSamplesPerDevice;
    private readonly TimeProvider _timeProvider;

    public RollingTemperatureHistoryBuffer(
        int maximumSamplesPerDevice = DefaultMaximumSamplesPerDevice,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSamplesPerDevice);

        _maximumSamplesPerDevice = maximumSamplesPerDevice;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Append(MonitoringSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DateTimeOffset retentionCutoff = snapshot.Timestamp - Retention;

        lock (_gate)
        {
            foreach (MonitoredDeviceSnapshot monitoredDevice in snapshot.Devices)
            {
                DeviceSnapshot device = monitoredDevice.Device;
                if (device.Temperature is not double temperature || !IsFinite(temperature))
                {
                    continue;
                }

                if (!_histories.TryGetValue(device.DeviceId, out DeviceHistory? history))
                {
                    history = new DeviceHistory(device.DeviceId, device.Kind, device.DisplayName);
                    _histories.Add(device.DeviceId, history);
                }

                history.DisplayName = device.DisplayName;
                history.Points.Enqueue(new TemperaturePoint(device.Timestamp, temperature));
                Trim(history.Points, retentionCutoff, _maximumSamplesPerDevice);
            }
        }
    }

    public TemperatureHistoryQueryResult Query(
        TemperatureHistoryRange range,
        int maxPointsPerDevice = DefaultMaximumChartPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPointsPerDevice);

        DateTimeOffset to = _timeProvider.GetUtcNow();
        DateTimeOffset from = to - ToDuration(range);
        TemperatureHistorySeries[] series;

        lock (_gate)
        {
            series = _histories.Values
                .Select(history => new TemperatureHistorySeries(
                    history.DeviceId,
                    history.DeviceKind,
                    history.DisplayName,
                    Downsample(history.Points.Where(point => point.Timestamp >= from), maxPointsPerDevice)))
                .OrderBy(static item => item.DeviceKind)
                .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
                .ToArray();
        }

        return new TemperatureHistoryQueryResult(range, from, to, series);
    }

    private static void Trim(
        Queue<TemperaturePoint> points,
        DateTimeOffset retentionCutoff,
        int maximumSamples)
    {
        while (points.Count > 0 && points.Peek().Timestamp < retentionCutoff)
        {
            points.Dequeue();
        }

        while (points.Count > maximumSamples)
        {
            points.Dequeue();
        }
    }

    private static IReadOnlyList<TemperaturePoint> Downsample(
        IEnumerable<TemperaturePoint> points,
        int maximumPoints)
    {
        TemperaturePoint[] source = points.ToArray();
        if (source.Length <= maximumPoints)
        {
            return source;
        }

        if (maximumPoints == 1)
        {
            return [source[^1]];
        }

        // Two extrema per bucket retain short thermal spikes without rendering
        // thousands of points when the visible window is twenty-four hours.
        int bucketCount = Math.Max(1, maximumPoints / 2);
        int bucketSize = (int)Math.Ceiling(source.Length / (double)bucketCount);
        var result = new List<TemperaturePoint>(maximumPoints);

        for (int start = 0; start < source.Length; start += bucketSize)
        {
            int endExclusive = Math.Min(source.Length, start + bucketSize);
            int minimumIndex = start;
            int maximumIndex = start;
            for (int index = start + 1; index < endExclusive; index++)
            {
                if (source[index].Value < source[minimumIndex].Value)
                {
                    minimumIndex = index;
                }

                if (source[index].Value > source[maximumIndex].Value)
                {
                    maximumIndex = index;
                }
            }

            if (minimumIndex <= maximumIndex)
            {
                result.Add(source[minimumIndex]);
                if (maximumIndex != minimumIndex)
                {
                    result.Add(source[maximumIndex]);
                }
            }
            else
            {
                result.Add(source[maximumIndex]);
                result.Add(source[minimumIndex]);
            }
        }

        return result;
    }

    private static TimeSpan ToDuration(TemperatureHistoryRange range) => range switch
    {
        TemperatureHistoryRange.TenMinutes => TimeSpan.FromMinutes(10),
        TemperatureHistoryRange.OneHour => TimeSpan.FromHours(1),
        TemperatureHistoryRange.SixHours => TimeSpan.FromHours(6),
        TemperatureHistoryRange.TwentyFourHours => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(range)),
    };

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed class DeviceHistory(string deviceId, DeviceKind deviceKind, string displayName)
    {
        public string DeviceId { get; } = deviceId;

        public DeviceKind DeviceKind { get; } = deviceKind;

        public string DisplayName { get; set; } = displayName;

        public Queue<TemperaturePoint> Points { get; } = new();
    }
}
