namespace LaptopThermalHelper.Application.Hardware;

/// <summary>
/// Startup policy for selecting a hardware provider. Real hardware is the
/// default; deterministic sample readings require an explicit <c>--mock</c>.
/// </summary>
public sealed record HardwareRuntimeOptions(bool UseMock, bool UsesDeprecatedRealHardwareFlag)
{
    public static HardwareRuntimeOptions Parse(IEnumerable<string>? arguments)
    {
        string[] values = arguments?.ToArray() ?? [];
        bool useMock = values.Any(static argument =>
            string.Equals(argument, "--mock", StringComparison.OrdinalIgnoreCase));
        bool usesDeprecatedRealHardwareFlag = values.Any(static argument =>
            string.Equals(argument, "--real-hardware", StringComparison.OrdinalIgnoreCase));
        return new HardwareRuntimeOptions(useMock, usesDeprecatedRealHardwareFlag);
    }
}
