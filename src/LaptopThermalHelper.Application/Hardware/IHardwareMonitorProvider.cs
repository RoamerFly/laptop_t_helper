using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.Hardware;

public interface IHardwareMonitorProvider : IAsyncDisposable
{
    Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken);
}
