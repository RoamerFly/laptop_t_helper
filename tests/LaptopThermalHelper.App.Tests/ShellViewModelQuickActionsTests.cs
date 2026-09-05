using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Application.System;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Tests;

public sealed class ShellViewModelQuickActionsTests
{
    [Fact]
    public async Task ToggleAutoCoolingCommand_TogglesSettingAndUpdatesActionText()
    {
        using var fixture = new ShellFixture();
        ShellViewModel shell = fixture.Shell;

        Assert.False(shell.IsAutoCoolingEnabled);
        Assert.Equal("⚙  启用自动降温", shell.QuickActionAutoCoolingText);

        await shell.ToggleAutoCoolingCommand.ExecuteAsync(null);

        Assert.True(shell.IsAutoCoolingEnabled);
        Assert.Equal("⚙  停用自动降温", shell.QuickActionAutoCoolingText);
        Assert.Contains("已开启自动降温", shell.OperationFeedback);

        await shell.ToggleAutoCoolingCommand.ExecuteAsync(null);

        Assert.False(shell.IsAutoCoolingEnabled);
        Assert.Equal("⚙  启用自动降温", shell.QuickActionAutoCoolingText);
        Assert.Contains("已关闭自动降温", shell.OperationFeedback);
    }

    [Fact]
    public async Task CycleCoolingPolicyCommand_CyclesThroughPoliciesAndUpdatesActionText()
    {
        using var fixture = new ShellFixture();
        ShellViewModel shell = fixture.Shell;

        Assert.Equal("仅监测", shell.CoolingPolicy);
        Assert.Equal("⚖  切换策略：仅监测", shell.QuickActionCoolingPolicyText);

        // Cycle 1: 仅监测 -> 温和降温
        await shell.CycleCoolingPolicyCommand.ExecuteAsync(null);
        Assert.Equal("温和降温", shell.CoolingPolicy);
        Assert.Equal("⚖  切换策略：温和降温", shell.QuickActionCoolingPolicyText);
        Assert.Contains("温和降温", shell.OperationFeedback);

        // Cycle 2: 温和降温 -> 主动降温
        await shell.CycleCoolingPolicyCommand.ExecuteAsync(null);
        Assert.Equal("主动降温", shell.CoolingPolicy);
        Assert.Equal("⚖  切换策略：主动降温", shell.QuickActionCoolingPolicyText);
        Assert.Contains("主动降温", shell.OperationFeedback);

        // Cycle 3: 主动降温 -> 仅监测
        await shell.CycleCoolingPolicyCommand.ExecuteAsync(null);
        Assert.Equal("仅监测", shell.CoolingPolicy);
        Assert.Equal("⚖  切换策略：仅监测", shell.QuickActionCoolingPolicyText);
        Assert.Contains("仅监测", shell.OperationFeedback);
    }

    private sealed class ShellFixture : IDisposable
    {
        private readonly string _logDir;

        public ShellFixture()
        {
            _logDir = Path.Combine(Path.GetTempPath(), "ShellTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDir);

            var eventLog = new InMemoryApplicationEventLog(Path.Combine(_logDir, "events.log"));
            var settingsStore = new MemorySettingsStore();
            var startupService = new StubStartupRegistrationService();
            var notifications = new StubNotificationSink();
            var notificationService = new ThermalNotificationService(notifications, new StubSoundPlayer(), eventLog);
            var powerAdapter = new StubPowerPlanAdapter();
            var recoveryStore = new StubRecoveryStore();
            var autoCooling = new AutoCoolingService(powerAdapter, recoveryStore, eventLog);
            var integrationService = new SystemIntegrationService(
                settingsStore,
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

        public void Dispose()
        {
            if (Directory.Exists(_logDir))
            {
                try { Directory.Delete(_logDir, true); } catch { }
            }
        }
    }

    private sealed class MemorySettingsStore : IApplicationSettingsStore
    {
        private ApplicationSettings _settings = ApplicationSettings.Default with { AutoCoolingEnabled = false, CoolingPolicy = CoolingPolicyKind.MonitorOnly };

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(_settings));

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
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
