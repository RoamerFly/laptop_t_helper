using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Application.System;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.ViewModels;

/// <summary>
/// Owns application-wide navigation and settings presentation. The opt-in automatic
/// cooling flow is delegated to a guarded service and may use only public Windows
/// processor power management; no command changes EC, BIOS, fan RPM, or OEM APIs.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly ISystemInformationProvider _systemInformationProvider;
    private readonly SystemIntegrationService _systemIntegrationService;
    private readonly IApplicationEventLog _eventLog;
    private readonly IApplicationRuntimeInfo _runtimeInfo;
    private bool _applyingStoredSettings;
    private bool _isLogDisplayCleared;

    public ShellViewModel(
        DashboardViewModel dashboard,
        ITemperatureHistoryBuffer historyBuffer,
        ISystemInformationProvider systemInformationProvider,
        SystemIntegrationService systemIntegrationService,
        IApplicationEventLog eventLog,
        IApplicationRuntimeInfo runtimeInfo)
    {
        Dashboard = dashboard;
        TemperatureDetail = new TemperatureDetailViewModel(dashboard, historyBuffer);
        _systemInformationProvider = systemInformationProvider;
        _systemIntegrationService = systemIntegrationService;
        _eventLog = eventLog;
        _runtimeInfo = runtimeInfo;
        NavigationItems =
        [
            new NavigationItem("dashboard", "总览", "\uE80F", new DashboardPage()),
            new NavigationItem("temperature", "温度监控", "\uE9CA", new TemperaturePage()),
            new NavigationItem("fan", "风扇控制", "\uE9C7", new FanPage()),
            new NavigationItem("performance", "性能模式", "\uE9D2", new PerformancePage()),
            new NavigationItem("settings", "设置", "\uE713", new SettingsPage()),
            new NavigationItem("logs", "日志", "\uE9D5", new LogsPage()),
            new NavigationItem("about", "关于", "\uE946", new AboutPage()),
        ];
        SensorReadings =
        [
            new SensorReadingItem("CPU", "CPU Package", "核心温度", "当前主传感器", "--", "--", "--", "--", "cpu/0/temperature/0"),
            new SensorReadingItem("CPU", "Core Max", "核心温度", "备用传感器", "--", "--", "--", "--", "cpu/0/temperature/1"),
            new SensorReadingItem("GPU", "GPU Core", "核心温度", "当前主传感器", "--", "--", "--", "--", "gpu-nvidia/0/temperature/0"),
            new SensorReadingItem("SSD", "Composite", "磁盘温度", "当前主传感器", "--", "--", "--", "--", "storage/0/temperature/0"),
            new SensorReadingItem("风扇", "GPU Fan", "转速", "设备未开放", "未开放", "--", "--", "--", "controller/0/fan/0"),
        ];
        ActivityLogs = [];
        _eventLog.EventWritten += EventLog_EventWritten;
        ApplicationVersionText = _runtimeInfo.VersionText;
        HardwareModeText = _runtimeInfo.HardwareModeText;
        CurrentPage = NavigationItems[0].Page;
        SelectedNavigation = NavigationItems[0];
    }

    public DashboardViewModel Dashboard { get; }

    public TemperatureDetailViewModel TemperatureDetail { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public IReadOnlyList<SensorReadingItem> SensorReadings { get; }

    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; }

    public IReadOnlyList<string> TemperatureRanges { get; } = ["30 分钟", "1 小时", "24 小时"];

    public IReadOnlyList<string> SamplingIntervals { get; } = ["1 秒", "2 秒（推荐）", "5 秒"];

    public IReadOnlyList<string> LogFilters { get; } = ["全部事件", "警告和错误", "自动降温", "系统设置"];

    public event EventHandler<int>? SamplingIntervalChanged;

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private AppPage _currentPage = new DashboardPage();

    [ObservableProperty]
    private bool _isAutoCoolingEnabled;

    [ObservableProperty]
    private string _coolingPolicy = "仅监测";

    [ObservableProperty]
    private string _autoCoolingStatus = "自动降温服务正在初始化。";

    [ObservableProperty]
    private string _operationFeedback = "正在初始化本机设置与安全服务。";

    [ObservableProperty]
    private string _selectedTemperatureRange = "30 分钟";

    [ObservableProperty]
    private string _temperatureRangeSummary = "选择时间范围后，将从本地温度历史缓冲读取数据。";

    [ObservableProperty]
    private string _samplingInterval = "2 秒（推荐）";

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _warningSoundEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private string _cpuHighThreshold = "90";

    [ObservableProperty]
    private string _gpuHighThreshold = "85";

    [ObservableProperty]
    private string _storageHighThreshold = "70";

    [ObservableProperty]
    private string _selectedLogFilter = "全部事件";

    [ObservableProperty]
    private string _logFilterSummary = "正在显示当前应用会话事件。";

    [ObservableProperty]
    private string _startupStatusText = "尚未读取开机启动状态。";

    [ObservableProperty]
    private string _applicationLogExportText = "尚未导出应用事件。";

    [ObservableProperty]
    private string _applicationVersionText = "0.1.0";

    [ObservableProperty]
    private string _hardwareModeText = "正在读取运行模式。";

    [ObservableProperty]
    private bool _isSystemInformationLoading;

    [ObservableProperty]
    private string _operatingSystemText = "正在读取系统信息…";

    [ObservableProperty]
    private string _deviceModelText = "正在读取设备型号…";

    [ObservableProperty]
    private string _batteryText = "电池状态读取中…";

    [ObservableProperty]
    private string _powerSourceText = "电源状态读取中…";

    [ObservableProperty]
    private string _powerPlanText = "电源计划读取中…";

    [ObservableProperty]
    private string _systemInformationAvailabilityText = "系统信息读取中…";

    [ObservableProperty]
    private string? _systemInformationDiagnostic;

    partial void OnSelectedNavigationChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }

    partial void OnIsAutoCoolingEnabledChanged(bool value)
    {
        if (!_applyingStoredSettings)
        {
            PersistAutoCoolingPreferenceAsync(value);
        }
    }

    partial void OnSelectedTemperatureRangeChanged(string value)
    {
        TemperatureRangeSummary = $"已选择 {value} 范围。";
    }

    partial void OnSelectedLogFilterChanged(string value)
    {
        _isLogDisplayCleared = false;
        RefreshActivityLogs();
    }

    /// <summary>
    /// Loads read-only Windows metadata after the main window is available.
    /// The provider performs WMI and system calls off the dispatcher; this method
    /// only publishes the completed snapshot to bindings.
    /// </summary>
    public async Task LoadSystemInformationAsync(CancellationToken cancellationToken = default)
    {
        if (IsSystemInformationLoading)
        {
            return;
        }

        IsSystemInformationLoading = true;
        try
        {
            SystemInformationSnapshot snapshot = await _systemInformationProvider.GetAsync(cancellationToken);
            OperatingSystemText = snapshot.OperatingSystem;
            DeviceModelText = FormatDevice(snapshot.Manufacturer, snapshot.Model);
            BatteryText = FormatBattery(snapshot.Battery);
            PowerSourceText = FormatPowerSource(snapshot.PowerSource);
            PowerPlanText = snapshot.PowerPlan.DisplayName;
            SystemInformationAvailabilityText = FormatAvailability(snapshot.Availability);
            SystemInformationDiagnostic = snapshot.Diagnostic;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            OperatingSystemText = SystemInformationSnapshot.UnavailableText;
            DeviceModelText = SystemInformationSnapshot.UnavailableText;
            BatteryText = "电池：不可用";
            PowerSourceText = "电源状态不可用";
            PowerPlanText = SystemInformationSnapshot.UnavailableText;
            SystemInformationAvailabilityText = "系统信息不可用";
            SystemInformationDiagnostic = "读取系统信息时发生错误。";
        }
        finally
        {
            IsSystemInformationLoading = false;
        }
    }

    [RelayCommand]
    private void Navigate(string? pageId)
    {
        NavigationItem? item = NavigationItems.FirstOrDefault(item =>
            string.Equals(item.Id, pageId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SelectedNavigation = item;
        }
    }

    [RelayCommand]
    private void SelectCoolingPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            return;
        }

        CoolingPolicy = policy;
        OperationFeedback = $"已选择“{CoolingPolicy}”策略。启用后只会使用 Windows 公开的处理器电源管理能力。";
        AddLog("性能模式", OperationFeedback, ApplicationEventLevel.Information);
    }

    [RelayCommand]
    private void RefreshDetails()
    {
        TemperatureDetail.RefreshFromDashboard();
        TemperatureRangeSummary = $"已刷新 {SelectedTemperatureRange} 详情视图。";
        OperationFeedback = "已请求只读传感器刷新。";
        AddLog("硬件采样", "用户从温度监控页请求刷新", ApplicationEventLevel.Information);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!TryCreateSettings(out ApplicationSettings settings, out string? validationError))
        {
            OperationFeedback = validationError ?? "设置值无效。";
            return;
        }

        SettingsSaveResult result = await _systemIntegrationService.SaveSettingsAsync(settings);
        ApplySettings(result.Settings);
        OperationFeedback = result.Message;
        StartupStatusText = result.Succeeded
            ? (result.Settings.StartWithWindows ? "当前用户开机启动已启用。" : "当前用户开机启动已关闭。")
            : result.Message;
        if (result.Succeeded)
        {
            SamplingIntervalChanged?.Invoke(this, result.Settings.SamplingIntervalSeconds);
        }
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        SettingsSaveResult result = await _systemIntegrationService.SaveSettingsAsync(ApplicationSettings.Default);
        ApplySettings(result.Settings);
        OperationFeedback = result.Succeeded
            ? "已恢复默认应用设置；如自动降温曾修改电源设置，已请求恢复原始状态。"
            : result.Message;
        StartupStatusText = result.Succeeded ? "当前用户开机启动已关闭。" : result.Message;
    }

    [RelayCommand]
    private void ShowFanSafetyNotice()
    {
        OperationFeedback = "风扇转速由设备固件自动控制。当前版本仅提供只读状态，不使用 EC、BIOS、厂商专有接口或风扇直控。";
        AddLog("风扇控制", "查看安全边界说明", ApplicationEventLevel.Information);
    }

    [RelayCommand]
    private void ExportTemperatureLog()
    {
        Dashboard.ExportHistoryCommand.Execute(null);
        OperationFeedback = "已发起本地温度 CSV 导出；结果会显示在总览和日志页。";
        AddLog("温度历史", "请求导出本地 CSV 温度记录", ApplicationEventLevel.Information);
    }

    [RelayCommand]
    private void ClearLogView()
    {
        _isLogDisplayCleared = true;
        ActivityLogs.Clear();
        LogFilterSummary = "已清除当前日志显示；内存源、磁盘日志和温度历史均未删除。";
        OperationFeedback = "已清除当前日志显示，不会删除本地文件。";
    }

    [RelayCommand]
    private async Task ExportApplicationLogAsync()
    {
        ApplicationEventExportResult result = await _eventLog.ExportAsync();
        ApplicationLogExportText = result.HasData
            ? $"已导出 {result.RecordCount} 条应用事件：{result.FilePath}"
            : "当前没有可导出的应用事件。";
        OperationFeedback = ApplicationLogExportText;
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        OperationFeedback = $"当前为 v{ApplicationVersionText}；在线更新检查尚未接入。";
        AddLog("关于", "请求检查更新；在线更新尚未接入。", ApplicationEventLevel.Information);
    }

    [RelayCommand]
    private void ShowLicense()
    {
        OperationFeedback = "本项目使用 MIT 许可证，并按 MPL-2.0 声明引用 LibreHardwareMonitor。";
        AddLog("关于", OperationFeedback, ApplicationEventLevel.Information);
    }

    public async Task InitializeSystemIntegrationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SettingsLoadResult result = await _systemIntegrationService.InitializeAsync(cancellationToken);
            ApplySettings(result.Settings);
            StartupStatusText = result.Notice ?? "已加载本机设置。";
            AutoCoolingStatus = _systemIntegrationService.AutoCoolingStatus.Message;
            RefreshActivityLogs();
            OperationFeedback = result.Notice ?? "本机设置、通知与安全服务已初始化。";
            SamplingIntervalChanged?.Invoke(this, result.Settings.SamplingIntervalSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            OperationFeedback = $"初始化系统服务失败：{exception.Message}";
            AutoCoolingStatus = "自动降温未初始化，因此不会修改任何系统设置。";
            AddLog("应用", OperationFeedback, ApplicationEventLevel.Error);
        }
    }

    public async Task ObserveSystemIntegrationAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _systemIntegrationService.ObserveAsync(snapshot, cancellationToken);
        AutoCoolingStatus = _systemIntegrationService.AutoCoolingStatus.Message;
    }

    public Task ShutdownSystemIntegrationAsync(CancellationToken cancellationToken = default) =>
        _systemIntegrationService.ShutdownAsync(cancellationToken);

    private async void PersistAutoCoolingPreferenceAsync(bool enabled)
    {
        ApplicationSettings requested = _systemIntegrationService.Settings with { AutoCoolingEnabled = enabled };
        SettingsSaveResult result = await _systemIntegrationService.SaveSettingsAsync(requested);
        AutoCoolingStatus = _systemIntegrationService.AutoCoolingStatus.Message;
        OperationFeedback = result.Message;
        if (!result.Succeeded)
        {
            _applyingStoredSettings = true;
            IsAutoCoolingEnabled = _systemIntegrationService.Settings.AutoCoolingEnabled;
            _applyingStoredSettings = false;
        }
    }

    private bool TryCreateSettings(out ApplicationSettings settings, out string? error)
    {
        if (!int.TryParse(CpuHighThreshold, out int cpu) || !int.TryParse(GpuHighThreshold, out int gpu) ||
            !int.TryParse(StorageHighThreshold, out int storage))
        {
            settings = ApplicationSettings.Default;
            error = "CPU、GPU 和 SSD 阈值必须是整数。";
            return false;
        }

        int interval = SamplingInterval.Length > 0 && SamplingInterval[0] == '1' ? 1
            : SamplingInterval.Length > 0 && SamplingInterval[0] == '5' ? 5
            : 2;
        int processorLimit = CoolingPolicy == "主动降温" ? 85 : 90;
        settings = _systemIntegrationService.Settings with
        {
            SamplingIntervalSeconds = interval,
            NotificationsEnabled = NotificationsEnabled,
            CriticalAlertSoundEnabled = WarningSoundEnabled,
            StartWithWindows = StartWithWindows,
            MinimizeToTray = MinimizeToTray,
            CpuHighThresholdCelsius = cpu,
            GpuHighThresholdCelsius = gpu,
            StorageHighThresholdCelsius = storage,
            AutoCoolingEnabled = IsAutoCoolingEnabled,
            AutoCoolingTriggerCelsius = cpu,
            AutoCoolingMaxProcessorStatePercent = processorLimit,
        };
        error = null;
        return true;
    }

    private void ApplySettings(ApplicationSettings settings)
    {
        _applyingStoredSettings = true;
        try
        {
            SamplingInterval = settings.SamplingIntervalSeconds switch
            {
                1 => "1 秒",
                5 => "5 秒",
                _ => "2 秒（推荐）",
            };
            NotificationsEnabled = settings.NotificationsEnabled;
            WarningSoundEnabled = settings.CriticalAlertSoundEnabled;
            StartWithWindows = settings.StartWithWindows;
            MinimizeToTray = settings.MinimizeToTray;
            CpuHighThreshold = settings.CpuHighThresholdCelsius.ToString(CultureInfo.InvariantCulture);
            GpuHighThreshold = settings.GpuHighThresholdCelsius.ToString(CultureInfo.InvariantCulture);
            StorageHighThreshold = settings.StorageHighThresholdCelsius.ToString(CultureInfo.InvariantCulture);
            IsAutoCoolingEnabled = settings.AutoCoolingEnabled;
        }
        finally
        {
            _applyingStoredSettings = false;
        }
    }

    private void EventLog_EventWritten(object? sender, ApplicationEvent entry)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => AddEventToDisplay(entry));
            return;
        }

        AddEventToDisplay(entry);
    }

    private void RefreshActivityLogs()
    {
        ActivityLogs.Clear();
        if (_isLogDisplayCleared)
        {
            return;
        }

        foreach (ApplicationEvent entry in _eventLog.GetSnapshot().Where(MatchesSelectedLogFilter))
        {
            ActivityLogs.Add(ToActivityLogEntry(entry));
        }

        LogFilterSummary = $"显示 {ActivityLogs.Count} 条{SelectedLogFilter}事件。";
    }

    private void AddEventToDisplay(ApplicationEvent entry)
    {
        if (_isLogDisplayCleared || !MatchesSelectedLogFilter(entry))
        {
            return;
        }

        ActivityLogs.Insert(0, ToActivityLogEntry(entry));
        LogFilterSummary = $"显示 {ActivityLogs.Count} 条{SelectedLogFilter}事件。";
    }

    private bool MatchesSelectedLogFilter(ApplicationEvent entry) => SelectedLogFilter switch
    {
        "警告和错误" => entry.Level is ApplicationEventLevel.Warning or ApplicationEventLevel.Error,
        "自动降温" => entry.Category == "自动降温",
        "系统设置" => entry.Category == "设置",
        _ => true,
    };

    private static ActivityLogEntry ToActivityLogEntry(ApplicationEvent entry) => new(
        entry.Timestamp.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
        entry.Category,
        entry.Message,
        entry.Level switch
        {
            ApplicationEventLevel.Warning => "警告",
            ApplicationEventLevel.Error => "错误",
            _ => "信息",
        });

    private void AddLog(string category, string message, ApplicationEventLevel level) =>
        _eventLog.Write(level, category, message);

    private static string FormatDevice(string manufacturer, string model)
    {
        if (manufacturer == SystemInformationSnapshot.UnavailableText && model == SystemInformationSnapshot.UnavailableText)
        {
            return SystemInformationSnapshot.UnavailableText;
        }

        string[] values = [manufacturer, model];
        return string.Join(' ', values.Where(static value => value != SystemInformationSnapshot.UnavailableText));
    }

    private static string FormatBattery(BatteryInformation battery)
    {
        if (!battery.IsPresent)
        {
            return "电池：未检测到";
        }

        string charge = battery.ChargePercent is int percent ? $"{percent}%" : "不可用";
        string state = battery.State switch
        {
            BatteryChargeState.Charging => "正在充电",
            BatteryChargeState.Discharging => "电池供电",
            BatteryChargeState.Full => "已充满",
            _ => "状态未知",
        };
        string remaining = battery.RemainingTime is TimeSpan time && time > TimeSpan.Zero
            ? $" · 剩余 {time.Hours:D2}:{time.Minutes:D2}"
            : string.Empty;
        return $"电池：{charge} · {state}{remaining}";
    }

    private static string FormatPowerSource(PowerSourceKind source) => source switch
    {
        PowerSourceKind.Ac => "已连接电源",
        PowerSourceKind.Battery => "使用电池",
        _ => "电源状态不可用",
    };

    private static string FormatAvailability(SystemInformationAvailability availability) => availability switch
    {
        SystemInformationAvailability.Ready => "系统信息已就绪",
        SystemInformationAvailability.Partial => "系统信息部分可用",
        _ => "系统信息不可用",
    };
}

public sealed class NavigationItem(string id, string title, string icon, AppPage page)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Icon { get; } = icon;

    public AppPage Page { get; } = page;
}

public abstract class AppPage(string title, string subtitle)
{
    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;
}

public sealed class DashboardPage() : AppPage("总览", "传感器实时状态与温度概览");

public sealed class TemperaturePage() : AppPage("温度监控", "传感器详情、主传感器与历史趋势");

public sealed class FanPage() : AppPage("风扇控制", "只读说明与硬件支持状态");

public sealed class PerformancePage() : AppPage("性能模式", "用户主动启用后，仅使用 Windows 公开处理器电源管理能力");

public sealed class SettingsPage() : AppPage("设置", "阈值、采样与通知偏好");

public sealed class LogsPage() : AppPage("日志", "本地温度历史与应用事件");

public sealed class AboutPage() : AppPage("关于", "版本、许可证和数据使用说明");

public sealed record SensorReadingItem(
    string Device,
    string Name,
    string Metric,
    string Role,
    string Current,
    string Minimum,
    string Maximum,
    string Average,
    string Identifier);

public sealed record ActivityLogEntry(string Time, string Category, string Message, string Level);
