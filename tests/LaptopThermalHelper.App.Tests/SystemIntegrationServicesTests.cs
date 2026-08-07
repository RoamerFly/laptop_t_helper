using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Tests;

public sealed class SystemIntegrationServicesTests
{
    [Fact]
    public async Task SettingsStore_InvalidJson_BacksUpFileAndReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ invalid json");
        var store = new JsonApplicationSettingsStore(settingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationSettings.Default.SamplingIntervalSeconds, result.Settings.SamplingIntervalSeconds);
        Assert.NotNull(result.Notice);
        Assert.True(File.Exists(settingsPath));
        Assert.Single(Directory.GetFiles(directory.Path, "settings.invalid-*.json"));
    }

    [Fact]
    public async Task ThermalNotifications_DebouncesRepeatedLevelButAllowsCriticalEscalation()
    {
        var notifications = new RecordingNotificationSink();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var service = new ThermalNotificationService(notifications, new NoOpSoundPlayer(), events);
        ApplicationSettings settings = ApplicationSettings.Default with
        {
            NotificationThreshold = ThermalLevel.High,
            NotificationCooldownSeconds = 600,
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await service.ProcessAsync([Device("cpu", ThermalLevel.High, 90, now)], settings, now);
        await service.ProcessAsync([Device("cpu", ThermalLevel.High, 91, now.AddMinutes(1))], settings, now.AddMinutes(1));
        await service.ProcessAsync([Device("cpu", ThermalLevel.Critical, 97, now.AddMinutes(2))], settings, now.AddMinutes(2));

        Assert.Equal(2, notifications.Notifications.Count);
        Assert.Equal(ThermalLevel.High, notifications.Notifications[0].Level);
        Assert.Equal(ThermalLevel.Critical, notifications.Notifications[1].Level);
    }

    [Fact]
    public async Task AutoCooling_RequiresSustainedHeatThenRestoresAfterHysteresis()
    {
        var adapter = new RecordingPowerPlanAdapter();
        var recoveryStore = new MemoryRecoveryStore();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var service = new AutoCoolingService(adapter, recoveryStore, events);
        ApplicationSettings settings = ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingTriggerCelsius = 90,
            AutoCoolingSustainSeconds = 30,
            AutoCoolingRecoverySeconds = 120,
            AutoCoolingHysteresisCelsius = 3,
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;

        AutoCoolingStatus initial = await service.ObserveAsync(95, settings, now);
        AutoCoolingStatus beforeDelay = await service.ObserveAsync(95, settings, now.AddSeconds(29));
        AutoCoolingStatus applied = await service.ObserveAsync(95, settings, now.AddSeconds(30));
        AutoCoolingStatus recovering = await service.ObserveAsync(87, settings, now.AddSeconds(31));
        AutoCoolingStatus restored = await service.ObserveAsync(87, settings, now.AddSeconds(151));

        Assert.Equal(AutoCoolingState.Monitoring, initial.State);
        Assert.Equal(AutoCoolingState.Monitoring, beforeDelay.State);
        Assert.Equal(AutoCoolingState.ReducingPerformance, applied.State);
        Assert.Equal(AutoCoolingState.Recovering, recovering.State);
        Assert.Equal(AutoCoolingState.Disabled, restored.State);
        Assert.Equal(1, adapter.ApplyCount);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Null(recoveryStore.Record);
    }

    [Fact]
    public async Task AutoCooling_ApplyFailure_AttemptsRecoveryAndReportsFailure()
    {
        var adapter = new RecordingPowerPlanAdapter { ThrowOnApply = true };
        var recoveryStore = new MemoryRecoveryStore();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var service = new AutoCoolingService(adapter, recoveryStore, events);
        ApplicationSettings settings = ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingSustainSeconds = 10,
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await service.ObserveAsync(95, settings, now);
        AutoCoolingStatus result = await service.ObserveAsync(95, settings, now.AddSeconds(10));

        Assert.Equal(AutoCoolingState.Failed, result.State);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Null(recoveryStore.Record);
        Assert.Contains("未执行", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(UntrustedAcquisitionStatuses))]
    public async Task SystemIntegration_UntrustedAcquisition_NeverCapturesOrApplies(
        MonitoringAcquisitionStatus status)
    {
        var adapter = new RecordingPowerPlanAdapter();
        var recoveryStore = new MemoryRecoveryStore();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var cooling = new AutoCoolingService(adapter, recoveryStore, events);
        var settingsStore = new RecordingSettingsStore(ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingSustainSeconds = 10,
        });
        var integration = CreateSystemIntegration(settingsStore, cooling, events);
        await integration.InitializeAsync();

        await integration.ObserveAsync(MonitoringSnapshotFor(status, 98, DateTimeOffset.UtcNow));
        await integration.ObserveAsync(MonitoringSnapshotFor(status, 98, DateTimeOffset.UtcNow.AddMinutes(1)));

        Assert.Equal(0, adapter.CaptureCount);
        Assert.Equal(0, adapter.ApplyCount);
        Assert.False(integration.AutoCoolingStatus.IsPowerPlanModified);
    }

    [Fact]
    public async Task SystemIntegration_UntrustedAcquisition_RestoresExistingChange()
    {
        var adapter = new RecordingPowerPlanAdapter();
        var recoveryStore = new MemoryRecoveryStore();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var cooling = new AutoCoolingService(adapter, recoveryStore, events);
        var settingsStore = new RecordingSettingsStore(ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingSustainSeconds = 10,
        });
        var integration = CreateSystemIntegration(settingsStore, cooling, events);
        await integration.InitializeAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await integration.ObserveAsync(MonitoringSnapshotFor(
            MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware),
            98,
            now));
        await integration.ObserveAsync(MonitoringSnapshotFor(
            MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware),
            98,
            now.AddSeconds(10)));
        await integration.ObserveAsync(MonitoringSnapshotFor(
            MonitoringAcquisitionStatus.Error(HardwareProviderMode.RealHardware, "读取失败"),
            98,
            now.AddSeconds(11)));

        Assert.Equal(1, adapter.ApplyCount);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.False(integration.AutoCoolingStatus.IsPowerPlanModified);
        Assert.Equal(AutoCoolingState.Disabled, integration.AutoCoolingStatus.State);
    }

    [Fact]
    public async Task AutoCooling_StartupRecoveryFailure_LocksNewWritesUntilDisableRetriesSuccessfully()
    {
        var adapter = new RecordingPowerPlanAdapter { ThrowOnRestore = true };
        var original = new PowerPlanSnapshot("scheme-before-failure", 100, 100);
        var recoveryStore = new MemoryRecoveryStore
        {
            Record = new AutoCoolingRecoveryRecord(original, DateTimeOffset.UtcNow),
        };
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var service = new AutoCoolingService(adapter, recoveryStore, events);
        ApplicationSettings settings = ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingSustainSeconds = 10,
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await service.InitializeAsync();
        AutoCoolingStatus locked = await service.ObserveAsync(98, settings, now.AddSeconds(10));
        await service.ObserveAsync(98, settings, now.AddSeconds(20));
        AutoCoolingStatus firstRetry = await service.DisableAsync();

        Assert.Equal(AutoCoolingState.Failed, locked.State);
        Assert.Equal(0, adapter.CaptureCount);
        Assert.Equal(0, adapter.ApplyCount);
        Assert.Equal(2, adapter.RestoreCount);
        Assert.Equal(AutoCoolingState.Failed, firstRetry.State);
        Assert.Equal(original, recoveryStore.Record?.Snapshot);

        adapter.ThrowOnRestore = false;
        AutoCoolingStatus recovered = await service.DisableAsync();

        Assert.Equal(AutoCoolingState.Disabled, recovered.State);
        Assert.Equal(3, adapter.RestoreCount);
        Assert.Null(recoveryStore.Record);
    }

    [Fact]
    public async Task PowerPlanAdapter_UserChangesActivePlan_RefusesApplyAndRestore()
    {
        var powerPlanApi = new RecordingProcessorPowerPlanApi();
        var adapter = new PowerCfgPowerPlanAdapter(powerPlanApi);
        PowerPlanSnapshot snapshot = await adapter.CaptureAsync();
        powerPlanApi.ActiveSchemeGuid = RecordingProcessorPowerPlanApi.SecondSchemeGuid;

        await Assert.ThrowsAsync<ActivePowerPlanChangedException>(
            () => adapter.ApplyConservativeLimitAsync(snapshot, 85));
        await Assert.ThrowsAsync<ActivePowerPlanChangedException>(
            () => adapter.RestoreAsync(snapshot));

        Assert.Equal(0, powerPlanApi.WriteAcCount);
        Assert.Equal(0, powerPlanApi.WriteDcCount);
        Assert.Equal(0, powerPlanApi.ReapplyCount);
    }

    [Theory]
    [MemberData(nameof(PowerCfgProcessorStateOutputs))]
    public void PowerCfgParser_StrictlySelectsCurrentAcAndDcLines(string output, int expectedAc, int expectedDc)
    {
        ProcessorMaximumState result = PowerCfgProcessorStateParser.Parse(output);

        Assert.Equal(expectedAc, result.AcPercent);
        Assert.Equal(expectedDc, result.DcPercent);
    }

    [Fact]
    public async Task SaveSettings_DisablingAutoCooling_RestoresBeforeFailingPersistence()
    {
        var adapter = new RecordingPowerPlanAdapter();
        var recoveryStore = new MemoryRecoveryStore();
        var events = new InMemoryApplicationEventLog(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var cooling = new AutoCoolingService(adapter, recoveryStore, events);
        var settingsStore = new RecordingSettingsStore(ApplicationSettings.Default with
        {
            AutoCoolingEnabled = true,
            AutoCoolingSustainSeconds = 10,
        })
        {
            ThrowOnSave = true,
        };
        var integration = CreateSystemIntegration(settingsStore, cooling, events);
        await integration.InitializeAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MonitoringAcquisitionStatus realReady = MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware);
        await integration.ObserveAsync(MonitoringSnapshotFor(realReady, 98, now));
        await integration.ObserveAsync(MonitoringSnapshotFor(realReady, 98, now.AddSeconds(10)));

        SettingsSaveResult result = await integration.SaveSettingsAsync(integration.Settings with { AutoCoolingEnabled = false });

        Assert.False(result.Succeeded);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.False(cooling.Status.IsPowerPlanModified);
        Assert.True(integration.Settings.AutoCoolingEnabled);
        Assert.Equal(1, settingsStore.SaveCount);
    }

    public static IEnumerable<object[]> UntrustedAcquisitionStatuses()
    {
        yield return [MonitoringAcquisitionStatus.Ready(HardwareProviderMode.Mock)];
        yield return [MonitoringAcquisitionStatus.Unavailable(HardwareProviderMode.RealHardware, "传感器不可用")];
        yield return [MonitoringAcquisitionStatus.Error(HardwareProviderMode.RealHardware, "采集错误")];
    }

    public static IEnumerable<object[]> PowerCfgProcessorStateOutputs()
    {
        yield return
        [
            "Power Setting GUID: bc5038f7-23e0-4960-96da-33abaf5935ec\r\n" +
            "Possible Power Setting Index: 0x00000001\r\n" +
            "Current AC Power Setting Index: 0x00000064\r\n" +
            "Current DC Power Setting Index: 0x00000055\r\n" +
            "Other range sample: 0x00000010",
            100,
            85,
        ];
        yield return
        [
            "电源设置 GUID: bc5038f7-23e0-4960-96da-33abaf5935ec\r\n" +
            "可能的设置索引: 0x00000001\r\n" +
            "当前交流电源设置索引: 0x0000005A\r\n" +
            "当前直流电源设置索引: 0x0000004B\r\n" +
            "额外范围样本: 0x00000064",
            90,
            75,
        ];
    }

    private static DeviceSnapshot Device(string id, ThermalLevel level, double temperature, DateTimeOffset timestamp) =>
        new(id, DeviceKind.Cpu, "CPU", temperature, null, null, null, level, timestamp);

    private static MonitoringSnapshot MonitoringSnapshotFor(
        MonitoringAcquisitionStatus status,
        double cpuTemperature,
        DateTimeOffset timestamp) =>
        new(
            [new MonitoredDeviceSnapshot(Device("cpu", ThermalLevel.Critical, cpuTemperature, timestamp), cpuTemperature, cpuTemperature, [])],
            ThermalLevel.Critical,
            timestamp,
            status);

    private static SystemIntegrationService CreateSystemIntegration(
        IApplicationSettingsStore settingsStore,
        AutoCoolingService cooling,
        IApplicationEventLog events) =>
        new(
            settingsStore,
            new NoOpStartupRegistrationService(),
            new ThermalNotificationService(new RecordingNotificationSink(), new NoOpSoundPlayer(), events),
            cooling,
            events);

    private sealed class RecordingNotificationSink : IUserNotificationSink
    {
        public List<ThermalNotification> Notifications { get; } = [];

        public Task<bool> ShowAsync(ThermalNotification notification, CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.FromResult(true);
        }
    }

    private sealed class NoOpSoundPlayer : ICriticalAlertSoundPlayer
    {
        public void Play()
        {
        }
    }

    private sealed class RecordingPowerPlanAdapter : IPowerPlanAdapter
    {
        private static readonly PowerPlanSnapshot Snapshot = new("scheme", 100, 100);

        public bool ThrowOnApply { get; set; }

        public bool ThrowOnRestore { get; set; }

        public int CaptureCount { get; private set; }

        public int ApplyCount { get; private set; }

        public int RestoreCount { get; private set; }

        public bool IsDryRun => true;

        public Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult(Snapshot);
        }

        public Task ApplyConservativeLimitAsync(
            PowerPlanSnapshot snapshot,
            int maximumProcessorStatePercent,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            if (ThrowOnApply)
            {
                throw new InvalidOperationException("模拟 powercfg 失败");
            }

            return Task.CompletedTask;
        }

        public Task RestoreAsync(PowerPlanSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            RestoreCount++;
            if (ThrowOnRestore)
            {
                throw new InvalidOperationException("模拟恢复失败");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class MemoryRecoveryStore : IAutoCoolingRecoveryStore
    {
        public AutoCoolingRecoveryRecord? Record { get; set; }

        public Task<AutoCoolingRecoveryRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Record);

        public Task SaveAsync(AutoCoolingRecoveryRecord record, CancellationToken cancellationToken = default)
        {
            Record = record;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Record = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSettingsStore : IApplicationSettingsStore
    {
        public RecordingSettingsStore(ApplicationSettings settings)
        {
            Settings = settings;
        }

        public ApplicationSettings Settings { get; private set; }

        public bool ThrowOnSave { get; set; }

        public int SaveCount { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(Settings));

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ThrowOnSave)
            {
                throw new IOException("模拟持久化失败");
            }

            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpStartupRegistrationService : IUserStartupRegistrationService
    {
        public Task<StartupRegistrationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StartupRegistrationResult(true, "未修改开机启动。"));

        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingProcessorPowerPlanApi : IProcessorPowerPlanApi
    {
        public const string FirstSchemeGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string SecondSchemeGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        public string ActiveSchemeGuid { get; set; } = FirstSchemeGuid;

        public int WriteAcCount { get; private set; }

        public int WriteDcCount { get; private set; }

        public int ReapplyCount { get; private set; }

        public Task<string> GetActiveSchemeGuidAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveSchemeGuid);

        public Task<ProcessorMaximumState> ReadMaximumProcessorStateAsync(
            string schemeGuid,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessorMaximumState(100, 100));

        public Task WriteAcMaximumProcessorStateAsync(
            string schemeGuid,
            int maximumProcessorStatePercent,
            CancellationToken cancellationToken = default)
        {
            WriteAcCount++;
            return Task.CompletedTask;
        }

        public Task WriteDcMaximumProcessorStateAsync(
            string schemeGuid,
            int maximumProcessorStatePercent,
            CancellationToken cancellationToken = default)
        {
            WriteDcCount++;
            return Task.CompletedTask;
        }

        public Task ReapplyActiveSchemeAsync(string schemeGuid, CancellationToken cancellationToken = default)
        {
            ReapplyCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LaptopThermalHelper.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
