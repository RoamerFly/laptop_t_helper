using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using SkiaSharp;

namespace LaptopThermalHelper.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MonitoringCoordinator _coordinator;
    private readonly ITemperatureHistoryStore _historyStore;

    public DashboardViewModel(
        MonitoringCoordinator coordinator,
        ITemperatureHistoryStore historyStore)
    {
        _coordinator = coordinator;
        _historyStore = historyStore;
        Cpu = new HardwareCardViewModel("CPU", "\uE950", new SKColor(49, 216, 67));
        Gpu = new HardwareCardViewModel("GPU", "\uE7F8", new SKColor(49, 216, 67));
        Storage = new HardwareCardViewModel("SSD", "\uEDA2", new SKColor(38, 201, 124));
    }

    public HardwareCardViewModel Cpu { get; }

    public HardwareCardViewModel Gpu { get; }

    public HardwareCardViewModel Storage { get; }

    public MonitoringSnapshot LastSnapshot { get; private set; } = MonitoringSnapshot.Empty;

    [ObservableProperty]
    private ThermalLevel _systemLevel = ThermalLevel.Unknown;

    [ObservableProperty]
    private string _systemStatus = "传感器未就绪";

    [ObservableProperty]
    private string _systemMessage = "正在获取硬件温度，请稍候…";

    [ObservableProperty]
    private string _lastUpdatedText = "尚未更新";

    [ObservableProperty]
    private string _runtimeText = "00:00:00";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _historyExportActionText = "▤  导出温度日志";

    [ObservableProperty]
    private string? _lastHistoryExportPath;

    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            MonitoringSnapshot snapshot = await _coordinator.PollAsync();
            LastSnapshot = snapshot;
            Cpu.Update(SelectDashboardDevice(snapshot.Devices, DeviceKind.Cpu));
            Gpu.Update(SelectDashboardDevice(snapshot.Devices, DeviceKind.Gpu));
            Storage.Update(SelectDashboardDevice(snapshot.Devices, DeviceKind.Storage));
            SystemLevel = snapshot.SystemLevel;
            (SystemStatus, SystemMessage) = SystemText(snapshot.SystemLevel, snapshot.Status);
            LastUpdatedText = snapshot.Timestamp == DateTimeOffset.MinValue
                ? "尚未更新"
                : $"更新于 {snapshot.Timestamp:HH:mm:ss}";
            RuntimeText = (DateTimeOffset.Now - _startedAt).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExportHistoryAsync()
    {
        try
        {
            HistoryExportResult result = await _historyStore.ExportAsync(CancellationToken.None);
            if (!result.HasData)
            {
                HistoryExportActionText = "○  暂无温度历史";
                LastHistoryExportPath = null;
                return;
            }

            HistoryExportActionText = $"✓  已导出 {result.RecordCount} 条记录";
            LastHistoryExportPath = result.FilePath;
        }
        catch
        {
            HistoryExportActionText = "×  导出失败，请稍后重试";
        }
    }

    private static (string Status, string Message) SystemText(
        ThermalLevel level,
        MonitoringAcquisitionStatus acquisitionStatus)
    {
        if (acquisitionStatus.IsMock)
        {
            return ("模拟传感器数据", $"{acquisitionStatus.Message}；温度来源于 --mock 运行模式，不代表本机硬件。");
        }

        return acquisitionStatus.Availability switch
        {
            MonitoringAvailability.Unavailable => ("硬件传感器不可用", acquisitionStatus.Message),
            MonitoringAvailability.Error => ("硬件采样失败", acquisitionStatus.Message),
            _ => level switch
            {
                ThermalLevel.Normal => ("一切正常", "所有硬件温度正常，请继续保持良好的使用习惯"),
                ThermalLevel.Elevated => ("温度偏高", "部分硬件温度偏高，建议留意散热与后台负载"),
                ThermalLevel.High => ("温度过高", "建议降低负载并检查进风口是否通畅"),
                ThermalLevel.Critical => ("严重过热", "请立即保存工作并降低系统负载"),
                _ => ("传感器未就绪", acquisitionStatus.Message),
            },
        };
    }

    /// <summary>
    /// The fixed dashboard cards must prefer an actual temperature-capable
    /// device of the requested kind. A USB disk or an iGPU without a sensor
    /// must not hide a later NVMe/dGPU that has a usable reading.
    /// </summary>
    private static MonitoredDeviceSnapshot? SelectDashboardDevice(
        IEnumerable<MonitoredDeviceSnapshot> devices,
        DeviceKind kind) =>
        devices.FirstOrDefault(item =>
            item.Device.Kind == kind && item.Device.Temperature is double temperature && double.IsFinite(temperature))
        ?? devices.FirstOrDefault(item => item.Device.Kind == kind);
}
