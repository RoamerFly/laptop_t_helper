using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.System;

/// <summary>
/// Retrieves read-only operating-system and device metadata. Implementations
/// must not run potentially slow operating-system queries on the UI thread.
/// </summary>
public interface ISystemInformationProvider
{
    Task<SystemInformationSnapshot> GetAsync(CancellationToken cancellationToken);
}
