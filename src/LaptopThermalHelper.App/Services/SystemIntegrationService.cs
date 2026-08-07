using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Services;

public sealed record SettingsSaveResult(bool Succeeded, ApplicationSettings Settings, string Message);

public sealed class SystemIntegrationService
{
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IUserStartupRegistrationService _startupRegistrationService;
    private readonly ThermalNotificationService _notificationService;
    private readonly AutoCoolingService _autoCoolingService;
    private readonly IApplicationEventLog _eventLog;
    private bool _initialized;

    public SystemIntegrationService(
        IApplicationSettingsStore settingsStore,
        IUserStartupRegistrationService startupRegistrationService,
        ThermalNotificationService notificationService,
        AutoCoolingService autoCoolingService,
        IApplicationEventLog eventLog)
    {
        _settingsStore = settingsStore;
        _startupRegistrationService = startupRegistrationService;
        _notificationService = notificationService;
        _autoCoolingService = autoCoolingService;
        _eventLog = eventLog;
    }

    public ApplicationSettings Settings { get; private set; } = ApplicationSettings.Default;

    public AutoCoolingStatus AutoCoolingStatus => _autoCoolingService.Status;

    public async Task<SettingsLoadResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return new SettingsLoadResult(Settings);
        }

        SettingsLoadResult result = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings = result.Settings.Normalize();
        _initialized = true;
        await _autoCoolingService.InitializeAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            bool registered = await _startupRegistrationService.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
            if (registered != Settings.StartWithWindows)
            {
                _eventLog.Write(
                    ApplicationEventLevel.Warning,
                    "设置",
                    "保存的开机启动偏好与当前注册表状态不一致；请在设置页确认后重新保存。");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _eventLog.Write(ApplicationEventLevel.Warning, "设置", $"无法读取开机启动状态：{exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(result.Notice))
        {
            _eventLog.Write(ApplicationEventLevel.Warning, "设置", result.Notice);
        }

        _eventLog.Write(ApplicationEventLevel.Information, "应用", "系统设置与安全服务已初始化。");
        return result;
    }

    public async Task<SettingsSaveResult> SaveSettingsAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplicationSettings normalized = settings.Normalize();

        // Restore first. A settings-write failure must never prevent recovery of a
        // previously modified public processor-power setting.
        if (!normalized.AutoCoolingEnabled)
        {
            AutoCoolingStatus restoreStatus = await _autoCoolingService
                .DisableAsync(cancellationToken)
                .ConfigureAwait(false);
            if (restoreStatus.State == AutoCoolingState.Failed || restoreStatus.IsPowerPlanModified)
            {
                string message = $"自动降温尚未安全恢复；保留启用偏好和恢复记录。{restoreStatus.Message}";
                _eventLog.Write(ApplicationEventLevel.Error, "设置", message);
                return new SettingsSaveResult(false, Settings, message);
            }
        }

        StartupRegistrationResult startupResult = normalized.StartWithWindows == Settings.StartWithWindows
            ? new StartupRegistrationResult(true, "开机启动状态未变更。")
            : await _startupRegistrationService
                .SetEnabledAsync(normalized.StartWithWindows, cancellationToken)
                .ConfigureAwait(false);
        if (!startupResult.Succeeded)
        {
            _eventLog.Write(ApplicationEventLevel.Error, "设置", startupResult.Message);
            return new SettingsSaveResult(false, Settings, startupResult.Message);
        }

        try
        {
            await _settingsStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            Settings = normalized;

            string message = $"设置已保存。{startupResult.Message}";
            _eventLog.Write(ApplicationEventLevel.Information, "设置", message);
            return new SettingsSaveResult(true, Settings, message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            string message = $"保存设置失败：{exception.Message}";
            _eventLog.Write(ApplicationEventLevel.Error, "设置", message);
            return new SettingsSaveResult(false, Settings, message);
        }
    }

    public async Task ObserveAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_initialized)
        {
            return;
        }

        IReadOnlyList<DeviceSnapshot> devices = snapshot.Devices.Select(static item => item.Device).ToArray();
        await _notificationService
            .ProcessAsync(devices, Settings, snapshot.Timestamp, cancellationToken)
            .ConfigureAwait(false);

        if (!CanUseAutomaticCooling(snapshot))
        {
            await _autoCoolingService
                .SuspendForUnsafeHardwareAsync(snapshot.Status.Message, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        double? cpuTemperature = devices.FirstOrDefault(static device => device.Kind == DeviceKind.Cpu)?.Temperature;
        await _autoCoolingService
            .ObserveAsync(cpuTemperature, Settings, snapshot.Timestamp, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<AutoCoolingStatus> DisableAutoCoolingAsync(CancellationToken cancellationToken = default) =>
        _autoCoolingService.DisableAsync(cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        _autoCoolingService.DisableAsync(cancellationToken);

    private static bool CanUseAutomaticCooling(MonitoringSnapshot snapshot) =>
        snapshot.Status.Mode == HardwareProviderMode.RealHardware &&
        snapshot.Status.Availability == MonitoringAvailability.Ready;
}
