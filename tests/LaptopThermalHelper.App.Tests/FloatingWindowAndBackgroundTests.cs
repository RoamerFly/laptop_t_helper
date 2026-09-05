using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Application.System;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Tests;

public sealed class FloatingWindowAndBackgroundTests
{
    [Fact]
    public void ApplicationSettings_Defaults_EnableMinimizeToTrayForBackgroundRunning()
    {
        var settings = ApplicationSettings.Default;

        Assert.True(settings.MinimizeToTray, "MinimizeToTray 默认应为 true，以支持关闭窗口时后台驻留运行。");
        Assert.False(settings.ShowFloatingWindow, "ShowFloatingWindow 默认应为 false。");
        Assert.Null(settings.FloatingWindowLeft);
        Assert.Null(settings.FloatingWindowTop);
    }

    [Fact]
    public async Task ShellViewModel_ToggleFloatingWindow_FiresEventAndUpdatesState()
    {
        using var fixture = new ShellFixture();
        ShellViewModel shell = fixture.Shell;

        bool? lastFiredVisibility = null;
        shell.FloatingWindowVisibilityChanged += (_, visible) => lastFiredVisibility = visible;

        Assert.False(shell.ShowFloatingWindow);

        shell.ToggleFloatingWindowCommand.Execute(null);

        Assert.True(shell.ShowFloatingWindow);
        Assert.True(lastFiredVisibility);
        Assert.Contains("桌面温度悬浮窗", shell.OperationFeedback);

        shell.ToggleFloatingWindowCommand.Execute(null);

        Assert.False(shell.ShowFloatingWindow);
        Assert.False(lastFiredVisibility);
        Assert.Contains("关闭", shell.OperationFeedback);
    }

    [Fact]
    public async Task ShellViewModel_UpdateFloatingWindowPosition_PersistsCoordinates()
    {
        using var fixture = new ShellFixture();
        ShellViewModel shell = fixture.Shell;

        await shell.UpdateFloatingWindowPositionAsync(250, 180);

        Assert.Equal(250, fixture.SettingsStore.CurrentSettings.FloatingWindowLeft);
        Assert.Equal(180, fixture.SettingsStore.CurrentSettings.FloatingWindowTop);
    }

    private sealed class ShellFixture : IDisposable
    {
        private readonly string _logDir;

        public ShellFixture()
        {
            _logDir = Path.Combine(Path.GetTempPath(), "FloatingTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDir);

            var eventLog = new InMemoryApplicationEventLog(Path.Combine(_logDir, "events.log"));
            SettingsStore = new TestSettingsStore();
            var startupService = new StubStartupRegistrationService();
            var notifications = new StubNotificationSink();
            var notificationService = new ThermalNotificationService(notifications, new StubSoundPlayer(), eventLog);
            var powerAdapter = new StubPowerPlanAdapter();
            var recoveryStore = new StubRecoveryStore();
            var autoCooling = new AutoCoolingService(powerAdapter, recoveryStore, eventLog);
            var integrationService = new SystemIntegrationService(
                SettingsStore,
                startupService,
                notificationService,
                autoCooling,
                eventLog);

            var coordinator = new MonitoringCoordinator(new StubProvider());
            var historyBuffer = new RollingTemperatureHistoryBuffer(50);
            var dashboard = new DashboardViewModel(coordinator, new EmptyHistoryStore());

            Shell = new ShellViewModel(
                dashboard,
                historyBuffer,
                new StubSystemInfoProvider(),
                integrationService,
                eventLog,
                new StubRuntimeInfo(),
                new StubGpuDetector(),
                new StubUpdateCheckService());
        }

        public ShellViewModel Shell { get; }

        public TestSettingsStore SettingsStore { get; }

        public void Dispose()
        {
            if (Directory.Exists(_logDir))
            {
                try { Directory.Delete(_logDir, true); } catch { }
            }
        }
    }

    private sealed class TestSettingsStore : IApplicationSettingsStore
    {
        public ApplicationSettings CurrentSettings { get; private set; } = ApplicationSettings.Default;

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(CurrentSettings));

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubStartupRegistrationService : IUserStartupRegistrationService
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<StartupRegistrationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StartupRegistrationResult(true, "OK"));
    }

    private sealed class StubNotificationSink : IUserNotificationSink
    {
        public Task<bool> ShowAsync(ThermalNotification notification, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class StubSoundPlayer : ICriticalAlertSoundPlayer
    {
        public void Play() { }
    }

    private sealed class StubPowerPlanAdapter : IPowerPlanAdapter
    {
        public bool IsDryRun => false;
        public Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PowerPlanSnapshot("scheme", 100, 100));
        public Task ApplyConservativeLimitAsync(PowerPlanSnapshot snapshot, int maximumProcessorStatePercent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RestoreAsync(PowerPlanSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubRecoveryStore : IAutoCoolingRecoveryStore
    {
        public Task<AutoCoolingRecoveryRecord?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AutoCoolingRecoveryRecord?>(null);
        public Task SaveAsync(AutoCoolingRecoveryRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubSystemInfoProvider : ISystemInformationProvider
    {
        public Task<SystemInformationSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SystemInformationSnapshot.Unavailable(DateTimeOffset.UtcNow, "Unavailable"));
    }

    private sealed class StubRuntimeInfo : IApplicationRuntimeInfo
    {
        public string VersionText => "0.1.0";
        public string HardwareModeText => "Test";
        public bool UsesMockHardware => false;
    }

    private sealed class StubGpuDetector : IIntelGpuDriverDetector
    {
        public Task<IntelGpuDriverInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IntelGpuDriverInfo.NotPresent());
    }

    private sealed class StubUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, "最新版本"));
    }

    private sealed class StubProvider : IHardwareMonitorProvider, IHardwareMonitorProviderMetadata
    {
        public HardwareProviderMode Mode => HardwareProviderMode.RealHardware;
        public Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceSample>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyHistoryStore : ITemperatureHistoryStore
    {
        public Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HistoryExportResult> ExportAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HistoryExportResult.Empty);
    }
}
