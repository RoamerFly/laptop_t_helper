using System.IO;
using System.Text.Json;

namespace LaptopThermalHelper.App.Services;

public enum AutoCoolingState
{
    Disabled,
    Monitoring,
    ReducingPerformance,
    Recovering,
    Failed,
}

public sealed record AutoCoolingStatus(AutoCoolingState State, string Message, bool IsPowerPlanModified);

public sealed record AutoCoolingRecoveryRecord(PowerPlanSnapshot Snapshot, DateTimeOffset AppliedAt);

public interface IAutoCoolingRecoveryStore
{
    Task<AutoCoolingRecoveryRecord?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AutoCoolingRecoveryRecord record, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonAutoCoolingRecoveryStore : IAutoCoolingRecoveryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public JsonAutoCoolingRecoveryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoamerFly",
            "LaptopThermalHelper",
            "recovery-state.json"))
    {
    }

    public JsonAutoCoolingRecoveryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<AutoCoolingRecoveryRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<AutoCoolingRecoveryRecord>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(AutoCoolingRecoveryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("无法确定恢复状态目录。");
        Directory.CreateDirectory(directory);
        string temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4_096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, record, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }
}

public sealed class AutoCoolingService : IDisposable
{
    private readonly IPowerPlanAdapter _powerPlanAdapter;
    private readonly IAutoCoolingRecoveryStore _recoveryStore;
    private readonly IApplicationEventLog _eventLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PowerPlanSnapshot? _originalPowerPlan;
    private DateTimeOffset? _aboveThresholdSince;
    private DateTimeOffset? _belowRecoverySince;
    private bool _recoveryLocked;

    public AutoCoolingService(
        IPowerPlanAdapter powerPlanAdapter,
        IAutoCoolingRecoveryStore recoveryStore,
        IApplicationEventLog eventLog)
    {
        _powerPlanAdapter = powerPlanAdapter;
        _recoveryStore = recoveryStore;
        _eventLog = eventLog;
    }

    public AutoCoolingStatus Status { get; private set; } = new(
        AutoCoolingState.Disabled,
        "自动降温未启用。",
        false);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AutoCoolingRecoveryRecord? recovery = await _recoveryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (recovery is null)
            {
                return;
            }

            // Keep the captured snapshot in memory before attempting recovery. If the
            // restore fails, later observations must not capture/apply a new plan or
            // overwrite the only recovery record.
            _originalPowerPlan = recovery.Snapshot;
            _recoveryLocked = true;
            try
            {
                await _powerPlanAdapter.RestoreAsync(recovery.Snapshot, cancellationToken).ConfigureAwait(false);
                await _recoveryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
                _originalPowerPlan = null;
                _recoveryLocked = false;
                SetStatus(AutoCoolingState.Disabled, "检测到未恢复的电源设置，已恢复原始状态。", false);
                _eventLog.Write(ApplicationEventLevel.Information, "自动降温", Status.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetStatus(AutoCoolingState.Failed, $"检测到未恢复的电源设置，但恢复失败：{exception.Message}", true);
                _eventLog.Write(ApplicationEventLevel.Error, "自动降温", Status.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AutoCoolingStatus> ObserveAsync(
        double? cpuTemperature,
        ApplicationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!settings.AutoCoolingEnabled)
            {
                return await RestoreIfAppliedAsync("自动降温已由用户关闭，已恢复原始电源设置。", cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_recoveryLocked)
            {
                _aboveThresholdSince = null;
                _belowRecoverySince = null;
                return Status;
            }

            if (cpuTemperature is not double temperature || double.IsNaN(temperature) || double.IsInfinity(temperature))
            {
                _aboveThresholdSince = null;
                _belowRecoverySince = null;
                if (_originalPowerPlan is null)
                {
                    SetStatus(AutoCoolingState.Monitoring, "未获取到有效 CPU 温度，自动降温保持监测且不会修改设置。", false);
                }

                return Status;
            }

            if (_originalPowerPlan is null)
            {
                return await ObserveBeforeApplyAsync(temperature, settings, now, cancellationToken).ConfigureAwait(false);
            }

            return await ObserveForRecoveryAsync(temperature, settings, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AutoCoolingStatus> DisableAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RestoreIfAppliedAsync("自动降温已停止，已恢复原始电源设置。", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Suspends automatic cooling when the latest sample is not verified real and
    /// ready. Existing recovery work is still attempted, but no new capture/apply
    /// operation can be reached through this path.
    /// </summary>
    public async Task<AutoCoolingStatus> SuspendForUnsafeHardwareAsync(
        string acquisitionMessage,
        CancellationToken cancellationToken = default)
    {
        string reason = string.IsNullOrWhiteSpace(acquisitionMessage)
            ? "当前硬件采集状态不可信"
            : acquisitionMessage.Trim();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RestoreIfAppliedAsync(
                    $"自动降温已暂停：{reason}；已恢复原始电源设置。",
                    cancellationToken,
                    $"自动降温已暂停：{reason}；未修改电源设置。",
                    "自动降温已暂停，但恢复原始电源设置失败")
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AutoCoolingStatus> ObserveBeforeApplyAsync(
        double temperature,
        ApplicationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (temperature < settings.AutoCoolingTriggerCelsius)
        {
            _aboveThresholdSince = null;
            SetStatus(AutoCoolingState.Monitoring, "自动降温已启用，正在监测 CPU 温度。", false);
            return Status;
        }

        _aboveThresholdSince ??= now;
        TimeSpan elapsed = now - _aboveThresholdSince.Value;
        if (elapsed < TimeSpan.FromSeconds(settings.AutoCoolingSustainSeconds))
        {
            SetStatus(
                AutoCoolingState.Monitoring,
                $"CPU 温度持续偏高，达到 {settings.AutoCoolingSustainSeconds} 秒后才会执行保守降温。",
                false);
            return Status;
        }

        try
        {
            PowerPlanSnapshot snapshot = await _powerPlanAdapter.CaptureAsync(cancellationToken).ConfigureAwait(false);
            await _recoveryStore.SaveAsync(new AutoCoolingRecoveryRecord(snapshot, now), cancellationToken).ConfigureAwait(false);
            _originalPowerPlan = snapshot;
            await _powerPlanAdapter
                .ApplyConservativeLimitAsync(snapshot, settings.AutoCoolingMaxProcessorStatePercent, cancellationToken)
                .ConfigureAwait(false);
            _aboveThresholdSince = null;
            SetStatus(
                AutoCoolingState.ReducingPerformance,
                _powerPlanAdapter.IsDryRun
                    ? "自动降温干运行已触发；没有修改 Windows 电源设置。"
                    : $"已将当前电源计划的处理器最大状态临时限制为 {settings.AutoCoolingMaxProcessorStatePercent}%。",
                true);
            _eventLog.Write(ApplicationEventLevel.Warning, "自动降温", Status.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryRestoreAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(AutoCoolingState.Failed, $"自动降温未执行：{exception.Message}", _originalPowerPlan is not null);
            _eventLog.Write(ApplicationEventLevel.Error, "自动降温", Status.Message);
        }

        return Status;
    }

    private async Task<AutoCoolingStatus> ObserveForRecoveryAsync(
        double temperature,
        ApplicationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int recoveryTemperature = settings.AutoCoolingTriggerCelsius - settings.AutoCoolingHysteresisCelsius;
        if (temperature > recoveryTemperature)
        {
            _belowRecoverySince = null;
            SetStatus(
                AutoCoolingState.ReducingPerformance,
                $"已执行保守降温；CPU 低于 {recoveryTemperature}°C 并持续 {settings.AutoCoolingRecoverySeconds} 秒后恢复。",
                true);
            return Status;
        }

        _belowRecoverySince ??= now;
        if (now - _belowRecoverySince.Value < TimeSpan.FromSeconds(settings.AutoCoolingRecoverySeconds))
        {
            SetStatus(AutoCoolingState.Recovering, "温度已回落，正在等待迟滞恢复时间。", true);
            return Status;
        }

        return await RestoreIfAppliedAsync("CPU 温度已稳定回落，已恢复原始电源设置。", cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutoCoolingStatus> RestoreIfAppliedAsync(
        string successMessage,
        CancellationToken cancellationToken,
        string noAppliedMessage = "自动降温未修改电源设置。",
        string failurePrefix = "恢复原始电源设置失败")
    {
        _aboveThresholdSince = null;
        _belowRecoverySince = null;
        if (_originalPowerPlan is null)
        {
            _recoveryLocked = false;
            SetStatus(AutoCoolingState.Disabled, noAppliedMessage, false);
            return Status;
        }

        try
        {
            await _powerPlanAdapter.RestoreAsync(_originalPowerPlan, cancellationToken).ConfigureAwait(false);
            await _recoveryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            _originalPowerPlan = null;
            _recoveryLocked = false;
            SetStatus(AutoCoolingState.Disabled, successMessage, false);
            _eventLog.Write(ApplicationEventLevel.Information, "自动降温", Status.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _recoveryLocked = true;
            SetStatus(AutoCoolingState.Failed, $"{failurePrefix}：{exception.Message}", true);
            _eventLog.Write(ApplicationEventLevel.Error, "自动降温", Status.Message);
        }

        return Status;
    }

    private async Task TryRestoreAfterFailureAsync(CancellationToken cancellationToken)
    {
        if (_originalPowerPlan is null)
        {
            return;
        }

        try
        {
            await _powerPlanAdapter.RestoreAsync(_originalPowerPlan, cancellationToken).ConfigureAwait(false);
            await _recoveryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            _originalPowerPlan = null;
            _recoveryLocked = false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 保留恢复文件，让下次启动能继续尝试恢复；失败会由可见状态和日志报告。
            _recoveryLocked = true;
        }
    }

    private void SetStatus(AutoCoolingState state, string message, bool isPowerPlanModified) =>
        Status = new AutoCoolingStatus(state, message, isPowerPlanModified);

    public void Dispose() => _gate.Dispose();
}
