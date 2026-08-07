using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Core.Thermal;

namespace LaptopThermalHelper.Core.Tests;

public sealed class TemperatureThresholdsTests
{
    [Theory]
    [InlineData(DeviceKind.Cpu, 74.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Cpu, 75, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Cpu, 90, ThermalLevel.High)]
    [InlineData(DeviceKind.Cpu, 95, ThermalLevel.Critical)]
    [InlineData(DeviceKind.Gpu, 69.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Gpu, 70, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Gpu, 82, ThermalLevel.High)]
    [InlineData(DeviceKind.Gpu, 86, ThermalLevel.Critical)]
    [InlineData(DeviceKind.Storage, 54.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Storage, 55, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Storage, 70, ThermalLevel.High)]
    [InlineData(DeviceKind.Storage, 75, ThermalLevel.Critical)]
    public void Classify_UsesDeviceDefaults(
        DeviceKind kind,
        double temperature,
        ThermalLevel expected)
    {
        ThermalLevel actual = TemperatureThresholds.For(kind).Classify(temperature);

        Assert.Equal(expected, actual);
    }
}
