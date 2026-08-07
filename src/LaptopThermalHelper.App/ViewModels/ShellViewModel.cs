using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LaptopThermalHelper.App.ViewModels;

/// <summary>
/// Owns application-wide navigation and the explicitly simulated controls that are
/// shown by the desktop prototype. No command in this view model changes EC, BIOS,
/// fan RPM, or Windows power settings.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(DashboardViewModel dashboard)
    {
        Dashboard = dashboard;
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
        ActivityLogs =
        [
            new ActivityLogEntry("刚刚", "硬件采样", "已刷新只读传感器快照", "信息"),
            new ActivityLogEntry("今天 10:22", "温度历史", "已开始记录本地 CSV 历史", "信息"),
            new ActivityLogEntry("今天 10:20", "自动降温", "演示模式：仅监测，未修改系统设置", "演示"),
        ];
        CurrentPage = NavigationItems[0].Page;
        SelectedNavigation = NavigationItems[0];
    }

    public DashboardViewModel Dashboard { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public IReadOnlyList<SensorReadingItem> SensorReadings { get; }

    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; }

    public IReadOnlyList<string> TemperatureRanges { get; } = ["30 分钟", "1 小时", "24 小时"];

    public IReadOnlyList<string> SamplingIntervals { get; } = ["1 秒", "2 秒（推荐）", "5 秒"];

    public IReadOnlyList<string> LogFilters { get; } = ["全部事件", "仅警告", "仅演示操作"];

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private AppPage _currentPage = new DashboardPage();

    [ObservableProperty]
    private bool _isAutoCoolingEnabled;

    [ObservableProperty]
    private string _coolingPolicy = "仅监测";

    [ObservableProperty]
    private string _autoCoolingStatus = "待命中（演示状态，不会修改 Windows 设置）";

    [ObservableProperty]
    private string _operationFeedback = "所有系统控制均为安全 Mock；传感器数据仍由当前 Provider 提供。";

    [ObservableProperty]
    private string _selectedTemperatureRange = "30 分钟";

    [ObservableProperty]
    private string _temperatureRangeSummary = "展示 30 分钟模拟趋势；真实详情历史将在后续版本接入。";

    [ObservableProperty]
    private string _samplingInterval = "2 秒（推荐）";

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _warningSoundEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _cpuHighThreshold = "90";

    [ObservableProperty]
    private string _gpuHighThreshold = "85";

    [ObservableProperty]
    private string _storageHighThreshold = "70";

    [ObservableProperty]
    private string _selectedLogFilter = "全部事件";

    [ObservableProperty]
    private string _logFilterSummary = "正在显示全部本地演示事件。";

    partial void OnSelectedNavigationChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }

    partial void OnIsAutoCoolingEnabledChanged(bool value)
    {
        AutoCoolingStatus = value
            ? $"已启用 {CoolingPolicy} 策略（演示状态，未修改 Windows 设置）"
            : "待命中（演示状态，不会修改 Windows 设置）";
        OperationFeedback = value
            ? "自动降温演示已开启。真实电源策略功能尚未启用。"
            : "自动降温演示已关闭；不会执行任何系统操作。";
        AddLog("自动降温", OperationFeedback, "演示");
    }

    partial void OnSelectedTemperatureRangeChanged(string value)
    {
        TemperatureRangeSummary = $"展示 {value} 模拟趋势；真实详情历史将在后续版本接入。";
    }

    partial void OnSelectedLogFilterChanged(string value)
    {
        LogFilterSummary = value switch
        {
            "仅警告" => "当前筛选仅用于 UI 演示；没有新的温度警告。",
            "仅演示操作" => "当前筛选仅用于 UI 演示；显示安全 Mock 操作。",
            _ => "正在显示全部本地演示事件。",
        };
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
        AutoCoolingStatus = IsAutoCoolingEnabled
            ? $"已启用 {CoolingPolicy} 策略（演示状态，未修改 Windows 设置）"
            : $"当前策略：{CoolingPolicy}（演示状态，尚未启用）";
        OperationFeedback = $"已选择“{CoolingPolicy}”演示策略，不会调用 Fn+Q、EC 或 Windows 电源接口。";
        AddLog("性能模式", OperationFeedback, "演示");
    }

    [RelayCommand]
    private void RefreshDetails()
    {
        TemperatureRangeSummary = $"已刷新 {SelectedTemperatureRange} 详情视图；数值在采样周期内更新。";
        OperationFeedback = "已请求只读传感器刷新。";
        AddLog("硬件采样", "用户从温度监控页请求刷新", "信息");
    }

    [RelayCommand]
    private void SaveSettings()
    {
        OperationFeedback = "设置已保存到当前演示会话；尚未写入系统设置。";
        AddLog("设置", "已保存界面阈值和通知偏好（演示会话）", "演示");
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        IsAutoCoolingEnabled = false;
        CoolingPolicy = "仅监测";
        SamplingInterval = "2 秒（推荐）";
        NotificationsEnabled = true;
        WarningSoundEnabled = false;
        StartWithWindows = false;
        CpuHighThreshold = "90";
        GpuHighThreshold = "85";
        StorageHighThreshold = "70";
        AutoCoolingStatus = "已恢复演示默认值；没有更改系统设置。";
        OperationFeedback = "已恢复默认演示配置，EC、BIOS、风扇和 Windows 电源设置均未更改。";
        AddLog("恢复默认", OperationFeedback, "演示");
    }

    [RelayCommand]
    private void ShowFanSafetyNotice()
    {
        OperationFeedback = "风扇转速由联想 BIOS/EC 自动控制。当前版本只读展示，直控功能规划中。";
        AddLog("风扇控制", "查看安全边界说明", "信息");
    }

    [RelayCommand]
    private void ExportTemperatureLog()
    {
        Dashboard.ExportHistoryCommand.Execute(null);
        OperationFeedback = "已发起本地温度 CSV 导出；结果会显示在总览和日志页。";
        AddLog("温度历史", "请求导出本地 CSV 温度记录", "信息");
    }

    [RelayCommand]
    private void ClearLogView()
    {
        LogFilterSummary = "已清除当前视图筛选；历史文件未删除。";
        OperationFeedback = "已重置日志页显示筛选，不会删除本地历史。";
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        OperationFeedback = "当前为 v0.1.0 演示版；在线更新检查尚未接入。";
        AddLog("关于", "请求检查更新（演示占位）", "信息");
    }

    [RelayCommand]
    private void ShowLicense()
    {
        OperationFeedback = "本项目使用 MIT 许可证，并按 MPL-2.0 声明引用 LibreHardwareMonitor。";
    }

    private void AddLog(string category, string message, string level)
    {
        ActivityLogs.Insert(0, new ActivityLogEntry("刚刚", category, message, level));
    }
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

public sealed class PerformancePage() : AppPage("性能模式", "安全 Mock：不会调用 Fn+Q、EC 或电源接口");

public sealed class SettingsPage() : AppPage("设置", "阈值、采样与通知偏好（演示会话）");

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
