using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Services;

public sealed record ThermalNotification(
    string DeviceName,
    ThermalLevel Level,
    double? Temperature,
    string Title,
    string Message);

public interface IUserNotificationSink
{
    Task<bool> ShowAsync(ThermalNotification notification, CancellationToken cancellationToken = default);
}

public interface ICriticalAlertSoundPlayer
{
    void Play();
}

public sealed class TrayNotificationSink(ITrayIconService trayIconService) : IUserNotificationSink
{
    public Task<bool> ShowAsync(ThermalNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(trayIconService.ShowNotification(
            notification.Title,
            notification.Message,
            notification.Level == ThermalLevel.Critical));
    }
}

public sealed class SystemCriticalAlertSoundPlayer : ICriticalAlertSoundPlayer
{
    public void Play()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch (Exception)
        {
            // 声音支持因设备和会话而异，失败不应影响温度监控或通知。
        }
    }
}

public sealed class ThermalNotificationService
{
    private readonly IUserNotificationSink _notificationSink;
    private readonly ICriticalAlertSoundPlayer _soundPlayer;
    private readonly IApplicationEventLog _eventLog;
    private readonly Dictionary<string, DeviceNotificationState> _states = new(StringComparer.Ordinal);

    public ThermalNotificationService(
        IUserNotificationSink notificationSink,
        ICriticalAlertSoundPlayer soundPlayer,
        IApplicationEventLog eventLog)
    {
        _notificationSink = notificationSink;
        _soundPlayer = soundPlayer;
        _eventLog = eventLog;
    }

    public async Task ProcessAsync(
        IEnumerable<DeviceSnapshot> devices,
        ApplicationSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!settings.NotificationsEnabled)
        {
            TrackLevels(devices);
            return;
        }

        foreach (DeviceSnapshot device in devices.Where(static device => device.Kind is DeviceKind.Cpu or DeviceKind.Gpu or DeviceKind.Storage))
        {
            if (!ShouldNotify(device, settings, now))
            {
                continue;
            }

            ThermalNotification notification = CreateNotification(device);
            bool displayed = await _notificationSink.ShowAsync(notification, cancellationToken).ConfigureAwait(false);
            if (!displayed)
            {
                _eventLog.Write(ApplicationEventLevel.Warning, "通知", "系统通知当前不可用，已安全跳过显示。");
                continue;
            }

            if (settings.CriticalAlertSoundEnabled && device.ThermalLevel == ThermalLevel.Critical)
            {
                _soundPlayer.Play();
            }

            _eventLog.Write(
                ApplicationEventLevel.Warning,
                "温度预警",
                notification.Message);
        }
    }

    private bool ShouldNotify(DeviceSnapshot device, ApplicationSettings settings, DateTimeOffset now)
    {
        if (!_states.TryGetValue(device.DeviceId, out DeviceNotificationState? state))
        {
            state = new DeviceNotificationState(ThermalLevel.Unknown, null);
            _states.Add(device.DeviceId, state);
        }

        ThermalLevel previous = state.LastObservedLevel;
        state.LastObservedLevel = device.ThermalLevel;

        bool reachedThreshold = device.ThermalLevel >= settings.NotificationThreshold;
        bool crossedThreshold = previous < settings.NotificationThreshold && reachedThreshold;
        bool escalatedToCritical = device.ThermalLevel == ThermalLevel.Critical && previous < ThermalLevel.Critical;
        bool cooldownExpired = state.LastNotificationAt is null ||
            now - state.LastNotificationAt >= TimeSpan.FromSeconds(settings.NotificationCooldownSeconds);

        if (!reachedThreshold || (!crossedThreshold && !escalatedToCritical && !cooldownExpired))
        {
            return false;
        }

        state.LastNotificationAt = now;
        return true;
    }

    private void TrackLevels(IEnumerable<DeviceSnapshot> devices)
    {
        foreach (DeviceSnapshot device in devices)
        {
            if (_states.TryGetValue(device.DeviceId, out DeviceNotificationState? state))
            {
                state.LastObservedLevel = device.ThermalLevel;
            }
            else
            {
                _states.Add(device.DeviceId, new DeviceNotificationState(device.ThermalLevel, null));
            }
        }
    }

    private static ThermalNotification CreateNotification(DeviceSnapshot device)
    {
        string levelText = device.ThermalLevel == ThermalLevel.Critical ? "严重过热" : "温度过高";
        string temperatureText = device.Temperature is double temperature ? $"{temperature:0.0}°C" : "温度未获取";
        return new ThermalNotification(
            device.DisplayName,
            device.ThermalLevel,
            device.Temperature,
            $"{device.DisplayName}：{levelText}",
            $"当前温度 {temperatureText}。请降低负载并检查散热条件。");
    }

    private sealed class DeviceNotificationState(ThermalLevel lastObservedLevel, DateTimeOffset? lastNotificationAt)
    {
        public ThermalLevel LastObservedLevel { get; set; } = lastObservedLevel;

        public DateTimeOffset? LastNotificationAt { get; set; } = lastNotificationAt;
    }
}
