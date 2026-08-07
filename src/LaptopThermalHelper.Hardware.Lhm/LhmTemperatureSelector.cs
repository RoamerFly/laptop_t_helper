using LibreHardwareMonitor.Hardware;

namespace LaptopThermalHelper.Hardware.Lhm;

/// <summary>
/// Selects a dashboard temperature only from a physical device's current
/// temperature sensors. In particular, NVMe SMART "Warning Temperature" and
/// "Critical Temperature" entries are fixed threshold metadata, not measured
/// drive temperatures, and must never be shown as a live reading.
/// </summary>
public static class LhmTemperatureSelector
{
    public static LhmTemperatureSensorCandidate? SelectPrimary(
        HardwareType hardwareType,
        IEnumerable<LhmTemperatureSensorCandidate> candidates) =>
        candidates
            .Where(candidate => IsEligible(hardwareType, candidate))
            .OrderByDescending(candidate => GetPriority(hardwareType, candidate.Name))
            .ThenByDescending(static candidate => candidate.Value)
            .ThenBy(static candidate => candidate.SensorId, StringComparer.Ordinal)
            .FirstOrDefault();

    public static bool IsEligible(HardwareType hardwareType, LhmTemperatureSensorCandidate candidate) =>
        IsPlausibleTemperature(candidate.Value) &&
        !(hardwareType == HardwareType.Storage && IsStorageThresholdSensor(candidate.Name));

    public static int GetPriority(HardwareType hardwareType, string? sensorName)
    {
        string name = sensorName ?? string.Empty;
        if (hardwareType == HardwareType.Cpu)
        {
            if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))
            {
                return 1_000;
            }

            if (name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            {
                return 900;
            }

            if (ContainsAny(name, "Tctl", "Tdie"))
            {
                return 850;
            }

            if (name.Equals("Core Max", StringComparison.OrdinalIgnoreCase))
            {
                return 800;
            }

            return name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 700 : 100;
        }

        if (hardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 1_000;
            }

            if (name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase))
            {
                return 950;
            }

            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            {
                return 900;
            }

            if (ContainsAny(name, "Hot Spot", "Hotspot"))
            {
                return 800;
            }

            return name.Contains("Memory Junction", StringComparison.OrdinalIgnoreCase) ? 600 : 100;
        }

        if (hardwareType == HardwareType.Storage)
        {
            if (name.Contains("Composite", StringComparison.OrdinalIgnoreCase))
            {
                return 1_000;
            }

            if (name.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            {
                return 900;
            }

            if (name.Contains("Drive Temperature", StringComparison.OrdinalIgnoreCase))
            {
                return 800;
            }

            return name.StartsWith("Temperature", StringComparison.OrdinalIgnoreCase) ? 700 : 100;
        }

        return 0;
    }

    public static bool IsStorageThresholdSensor(string? sensorName)
    {
        string name = sensorName ?? string.Empty;
        return ContainsAny(
            name,
            "Warning Temperature",
            "Critical Temperature",
            "Warning Threshold",
            "Critical Threshold");
    }

    public static bool IsPlausibleTemperature(float? value) =>
        value is float temperature &&
        !float.IsNaN(temperature) &&
        !float.IsInfinity(temperature) &&
        temperature is >= -20 and <= 150;

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}

public sealed record LhmTemperatureSensorCandidate(string SensorId, string Name, float? Value);

/// <summary>
/// Small testable traversal used by the provider to ensure sub-hardware is
/// refreshed and sampled exactly as its owning hardware node, rather than
/// being folded into an unrelated parent device.
/// </summary>
public static class LhmHardwareTraversal
{
    public static IEnumerable<T> DepthFirst<T>(
        IEnumerable<T> roots,
        Func<T, IEnumerable<T>> childrenSelector)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(childrenSelector);

        foreach (T root in roots)
        {
            yield return root;
            foreach (T child in DepthFirst(childrenSelector(root), childrenSelector))
            {
                yield return child;
            }
        }
    }
}
