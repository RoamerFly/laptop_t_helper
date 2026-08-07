using System.Reflection;
using LaptopThermalHelper.Application.Hardware;

namespace LaptopThermalHelper.App.Services;

public interface IApplicationRuntimeInfo
{
    string VersionText { get; }

    string HardwareModeText { get; }

    bool UsesMockHardware { get; }
}

public sealed class ApplicationRuntimeInfo : IApplicationRuntimeInfo
{
    public ApplicationRuntimeInfo(HardwareRuntimeOptions hardwareOptions)
    {
        ArgumentNullException.ThrowIfNull(hardwareOptions);
        UsesMockHardware = hardwareOptions.UseMock;
        VersionText = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
        HardwareModeText = UsesMockHardware ? "模拟数据（--mock）" : "真实硬件只读采集（默认）";
    }

    public string VersionText { get; }

    public string HardwareModeText { get; }

    public bool UsesMockHardware { get; }
}
