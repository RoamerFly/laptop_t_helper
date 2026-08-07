namespace LaptopThermalHelper.Application.Hardware;

/// <summary>
/// Identifies whether readings originate from physical hardware or the opt-in
/// demo provider. A real provider is never silently substituted with mock data.
/// </summary>
public enum HardwareProviderMode
{
    RealHardware,
    Mock,
}

public interface IHardwareMonitorProviderMetadata
{
    HardwareProviderMode Mode { get; }
}
