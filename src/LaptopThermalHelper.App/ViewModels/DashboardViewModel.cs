using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using SkiaSharp;

namespace LaptopThermalHelper.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MonitoringCoordinator _coordinator;

    public DashboardViewModel(MonitoringCoordinator coordinator)
    {
        _coordinator = coordinator;
        Cpu = new HardwareCardViewModel("CPU", "\uE950", new SKColor(49, 216, 67));
        Gpu = new HardwareCardViewModel("GPU", "\uE7F8", new SKColor(49, 216, 67));
        Storage = new HardwareCardViewModel("SSD", "\uEDA2", new SKColor(38, 201, 124));
    }

    public HardwareCardViewModel Cpu { get; }

    public HardwareCardViewModel Gpu { get; }

    public HardwareCardViewModel Storage { get; }

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

    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            MonitoringSnapshot snapshot = await _coordinator.PollAsync();
            Cpu.Update(snapshot.Devices.FirstOrDefault(static item => item.Device.Kind == DeviceKind.Cpu));
            Gpu.Update(snapshot.Devices.FirstOrDefault(static item => item.Device.Kind == DeviceKind.Gpu));
            Storage.Update(snapshot.Devices.FirstOrDefault(static item => item.Device.Kind == DeviceKind.Storage));
            SystemLevel = snapshot.SystemLevel;
            (SystemStatus, SystemMessage) = SystemText(snapshot.SystemLevel);
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

    private static (string Status, string Message) SystemText(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => ("一切正常", "所有硬件温度正常，请继续保持良好的使用习惯"),
        ThermalLevel.Elevated => ("温度偏高", "部分硬件温度偏高，建议留意散热与后台负载"),
        ThermalLevel.High => ("温度过高", "建议降低负载并检查进风口是否通畅"),
        ThermalLevel.Critical => ("严重过热", "请立即保存工作并降低系统负载"),
        _ => ("传感器未就绪", "未获取到关键温度，不能判断整机状态"),
    };
}
