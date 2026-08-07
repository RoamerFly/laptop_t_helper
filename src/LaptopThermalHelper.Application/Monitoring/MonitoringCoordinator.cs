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
    private readonly ITemperatureHistoryBuffer _historyBuffer;
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private DateTimeOffset? _nextHistoryWriteAt;

    public MonitoringCoordinator(IHardwareMonitorProvider provider)
        : this(provider, NullTemperatureHistoryStore.Instance, new RollingTemperatureHistoryBuffer())
    {
    }

    public MonitoringCoordinator(
        IHardwareMonitorProvider provider,
        ITemperatureHistoryStore historyStore)
        : this(provider, historyStore, new RollingTemperatureHistoryBuffer())
    {
    }

    public MonitoringCoordinator(
        IHardwareMonitorProvider provider,
        ITemperatureHistoryStore historyStore,
        ITemperatureHistoryBuffer historyBuffer)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _historyBuffer = historyBuffer ?? throw new ArgumentNullException(nameof(historyBuffer));
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
            HardwareProviderMode mode = GetProviderMode();
            IReadOnlyList<DeviceSample> samples;
            try
            {
                samples = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new MonitoringSnapshot(
                    [],
                    ThermalLevel.Unknown,
                    DateTimeOffset.UtcNow,
                    MonitoringAcquisitionStatus.Error(mode, ToSafeMessage(exception)));
            }

            var devices = new List<MonitoredDeviceSnapshot>(samples.Count);

            foreach (DeviceSample sample in samples)
            {
                DeviceState state = GetOrCreateState(sample);
                double? temperature = NormalizeTemperature(sample.Temperature);
                ThermalLevel level = state.StateMachine.Observe(temperature, sample.Timestamp);
                state.Statistics.Add(temperature);

                if (temperature is double validTemperature)
                {
                    state.Trend.Add(new TemperaturePoint(sample.Timestamp, validTemperature));
                }

                var deviceSnapshot = new DeviceSnapshot(
                    sample.DeviceId,
                    sample.Kind,
                    sample.DisplayName,
                    temperature,
                    sample.Load,
                    sample.Power,
                    sample.FanRpm,
                    level,
                    sample.Timestamp);

                devices.Add(new MonitoredDeviceSnapshot(
                    deviceSnapshot,
                    state.Statistics.Maximum,
                    state.Statistics.Average,
                    state.Trend)
                {
                    TemperatureSensors = sample.TemperatureSensors
                        .Where(static sensor =>
                            sensor.Metric == SensorMetric.Temperature &&
                            sensor.Quality == ReadingQuality.Good &&
                            sensor.Value is double value &&
                            IsFinite(value))
                        .ToArray(),
                    PrimaryTemperatureSensorName = temperature is null
                        ? null
                        : sample.PrimaryTemperatureSensorName,
                });
            }

            ThermalLevel systemLevel = AggregateSystemLevel(devices);
            DateTimeOffset timestamp = samples.Count == 0
                ? DateTimeOffset.UtcNow
                : samples.Max(static sample => sample.Timestamp);

            MonitoringAcquisitionStatus status = CreateAcquisitionStatus(mode, devices);
            var snapshot = new MonitoringSnapshot(devices, systemLevel, timestamp, status);
            _historyBuffer.Append(snapshot);
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

    private HardwareProviderMode GetProviderMode() =>
        _provider is IHardwareMonitorProviderMetadata metadata
            ? metadata.Mode
            : HardwareProviderMode.RealHardware;

    private static MonitoringAcquisitionStatus CreateAcquisitionStatus(
        HardwareProviderMode mode,
        IReadOnlyList<MonitoredDeviceSnapshot> devices)
    {
        if (mode == HardwareProviderMode.Mock)
        {
            return MonitoringAcquisitionStatus.Ready(mode);
        }

        bool hasTemperature = devices.Any(static device =>
            device.Device.Kind is DeviceKind.Cpu or DeviceKind.Gpu or DeviceKind.Storage &&
            device.Device.Temperature is double temperature && IsFinite(temperature));
        return hasTemperature
            ? MonitoringAcquisitionStatus.Ready(mode)
            : MonitoringAcquisitionStatus.Unavailable();
    }

    private static string ToSafeMessage(Exception exception) =>
        exception is UnauthorizedAccessException
            ? "无法读取真实硬件传感器：访问被拒绝。"
            : "无法读取真实硬件传感器。请检查驱动、权限或硬件支持。";

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

    private static double? NormalizeTemperature(double? temperature) =>
        temperature is double value && IsFinite(value) && value is >= -20 and <= 150
            ? value
            : null;

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
