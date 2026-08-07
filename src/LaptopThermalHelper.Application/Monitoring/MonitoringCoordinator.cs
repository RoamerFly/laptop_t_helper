using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Core.Collections;
using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Core.Statistics;
using LaptopThermalHelper.Core.Thermal;

namespace LaptopThermalHelper.Application.Monitoring;

public sealed class MonitoringCoordinator : IDisposable
{
    private const int TrendCapacity = 300;
    private static readonly TimeSpan HistoryWriteInterval = TimeSpan.FromSeconds(5);
    private readonly IHardwareMonitorProvider _provider;
    private readonly ITemperatureHistoryStore _historyStore;
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private DateTimeOffset? _nextHistoryWriteAt;

    public MonitoringCoordinator(IHardwareMonitorProvider provider)
        : this(provider, NullTemperatureHistoryStore.Instance)
    {
    }

    public MonitoringCoordinator(
        IHardwareMonitorProvider provider,
        ITemperatureHistoryStore historyStore)
    {
        _provider = provider;
        _historyStore = historyStore;
    }

    public Exception? LastHistoryWriteError { get; private set; }

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

                var deviceSnapshot = new DeviceSnapshot(
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
                    deviceSnapshot,
                    state.Statistics.Maximum,
                    state.Statistics.Average,
                    state.Trend));
            }

            ThermalLevel systemLevel = AggregateSystemLevel(devices);
            DateTimeOffset timestamp = samples.Count == 0
                ? DateTimeOffset.UtcNow
                : samples.Max(static sample => sample.Timestamp);

            var snapshot = new MonitoringSnapshot(devices, systemLevel, timestamp);
            await RecordHistoryIfDueAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return snapshot;
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

    private async Task RecordHistoryIfDueAsync(
        MonitoringSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Devices.Count == 0 ||
            (_nextHistoryWriteAt is DateTimeOffset nextWrite && snapshot.Timestamp < nextWrite))
        {
            return;
        }

        try
        {
            await _historyStore.AppendAsync(snapshot, cancellationToken).ConfigureAwait(false);
            _nextHistoryWriteAt = snapshot.Timestamp + HistoryWriteInterval;
            LastHistoryWriteError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 历史文件故障不能中断硬件采样；保留错误供表现层和日志读取。
            LastHistoryWriteError = exception;
        }
    }

    private sealed record DeviceState(
        ThermalStateMachine StateMachine,
        RunningStatistics Statistics,
        FixedRingBuffer<TemperaturePoint> Trend);

    private sealed class NullTemperatureHistoryStore : ITemperatureHistoryStore
    {
        public static NullTemperatureHistoryStore Instance { get; } = new();

        public Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<HistoryExportResult> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HistoryExportResult.Empty);
    }
}
