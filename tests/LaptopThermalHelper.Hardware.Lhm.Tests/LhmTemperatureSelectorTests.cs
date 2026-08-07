using LaptopThermalHelper.Hardware.Lhm;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace LaptopThermalHelper.Hardware.Lhm.Tests;

public sealed class LhmTemperatureSelectorTests
{
    [Fact]
    public void SelectPrimary_CpuPrefersPackageOverHotterCoreMaximum()
    {
        LhmTemperatureSensorCandidate? result = LhmTemperatureSelector.SelectPrimary(
            HardwareType.Cpu,
            [
                Candidate("core-max", "Core Max", 93),
                Candidate("package", "CPU Package", 87),
            ]);

        Assert.NotNull(result);
        Assert.Equal("package", result.SensorId);
    }

    [Fact]
    public void SelectPrimary_GpuPrefersCoreOverHotSpot()
    {
        LhmTemperatureSensorCandidate? result = LhmTemperatureSelector.SelectPrimary(
            HardwareType.GpuNvidia,
            [
                Candidate("hotspot", "GPU Hot Spot", 96),
                Candidate("core", "GPU Core", 82),
            ]);

        Assert.NotNull(result);
        Assert.Equal("core", result.SensorId);
    }

    [Fact]
    public void SelectPrimary_StoragePrefersCompositeAndRejectsSmartThresholdMetadata()
    {
        LhmTemperatureSensorCandidate? result = LhmTemperatureSelector.SelectPrimary(
            HardwareType.Storage,
            [
                Candidate("warning", "Warning Temperature", 70),
                Candidate("critical", "Critical Temperature", 75),
                Candidate("composite", "Composite", 61),
            ]);

        Assert.NotNull(result);
        Assert.Equal("composite", result.SensorId);
        Assert.True(LhmTemperatureSelector.IsStorageThresholdSensor("Critical Temperature"));
    }

    [Fact]
    public void SelectPrimary_StorageWithOnlySmartThresholdMetadata_ReturnsUnavailable()
    {
        LhmTemperatureSensorCandidate? result = LhmTemperatureSelector.SelectPrimary(
            HardwareType.Storage,
            [
                Candidate("warning", "Warning Temperature", 70),
                Candidate("critical", "Critical Temperature", 75),
            ]);

        Assert.Null(result);
    }

    [Fact]
    public void SelectPrimary_SkipsInvalidSensorValuesAndUsesValidFallback()
    {
        LhmTemperatureSensorCandidate? result = LhmTemperatureSelector.SelectPrimary(
            HardwareType.Storage,
            [
                Candidate("composite", "Composite", float.NaN),
                Candidate("temperature-1", "Temperature 1", 56),
                Candidate("impossible", "Temperature 2", 151),
            ]);

        Assert.NotNull(result);
        Assert.Equal("temperature-1", result.SensorId);
    }

    [Fact]
    public void DepthFirst_EnumeratesSubHardwareWithoutDroppingSiblingDevices()
    {
        Node[] roots =
        [
            new Node("cpu", [new Node("cpu-package", [])]),
            new Node("gpu", []),
            new Node("storage", [new Node("nvme-0", []), new Node("nvme-1", [])]),
        ];

        string[] names = LhmHardwareTraversal.DepthFirst(roots, static node => node.Children)
            .Select(static node => node.Name)
            .ToArray();

        Assert.Equal(["cpu", "cpu-package", "gpu", "storage", "nvme-0", "nvme-1"], names);
    }

    private static LhmTemperatureSensorCandidate Candidate(string id, string name, float? value) => new(id, name, value);

    private sealed record Node(string Name, IReadOnlyList<Node> Children);
}
