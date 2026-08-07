using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Collections;
using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Core.Statistics;
using LaptopThermalHelper.Core.Thermal;

namespace LaptopThermalHelper.Application.Monitoring;

public sealed class MonitoringCoordinator : IDisposable
{
    private const int TrendCapacity = 300;
    private readonly IHardwareMonitorProvider _provider;
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    public MonitoringCoordinator(IHardwareMonitorProvider provider)
    {
        _provider = provider;
    }

    public void Dispose()
    {
        _pollLock.Dispose();
    }

    public async ValueTask<MonitoringSnapshot> PollAsync(CancellationToken cancellationToken = default)
    {
        await _pollLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<DeviceSample> samples = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            var devices = new List<MonitoredDeviceSnapshot>(samples.Count);

            foreach (DeviceSample sample in samples)
            {
                DeviceState state = GetOrCreateState(sample);
                ThermalLevel level = state.StateMachine.Observe(sample.Temperature, sample.Timestamp);
                state.Statistics.Add(sample.Temperature);

                if (sample.Temperature is double temperature && IsFinite(temperature))
                {
                    state.Trend.Add(new TemperaturePoint(sample.Timestamp, temperature));
                }

                var snapshot = new DeviceSnapshot(
                    sample.DeviceId,
                    sample.Kind,
                    sample.DisplayName,
                    sample.Temperature,
                    sample.Load,
                    sample.Power,
                    sample.FanRpm,
                    level,
                    sample.Timestamp);

                devices.Add(new MonitoredDeviceSnapshot(
                    snapshot,
                    state.Statistics.Maximum,
                    state.Statistics.Average,
                    state.Trend));
            }

            ThermalLevel systemLevel = AggregateSystemLevel(devices);
            DateTimeOffset timestamp = samples.Count == 0
                ? DateTimeOffset.UtcNow
                : samples.Max(static sample => sample.Timestamp);

            return new MonitoringSnapshot(devices, systemLevel, timestamp);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private DeviceState GetOrCreateState(DeviceSample sample)
    {
        if (_states.TryGetValue(sample.DeviceId, out DeviceState? state))
        {
            return state;
        }

        state = new DeviceState(
            new ThermalStateMachine(TemperatureThresholds.For(sample.Kind)),
            new RunningStatistics(),
            new FixedRingBuffer<TemperaturePoint>(TrendCapacity));
        _states.Add(sample.DeviceId, state);
        return state;
    }

    private static ThermalLevel AggregateSystemLevel(IEnumerable<MonitoredDeviceSnapshot> devices)
    {
        ThermalLevel[] validLevels = devices
            .Where(static item => item.Device.Kind is DeviceKind.Cpu or DeviceKind.Gpu or DeviceKind.Storage)
            .Select(static item => item.Device.ThermalLevel)
            .Where(static level => level != ThermalLevel.Unknown)
            .ToArray();

        return validLevels.Length == 0 ? ThermalLevel.Unknown : validLevels.Max();
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed record DeviceState(
        ThermalStateMachine StateMachine,
        RunningStatistics Statistics,
        FixedRingBuffer<TemperaturePoint> Trend);
}
