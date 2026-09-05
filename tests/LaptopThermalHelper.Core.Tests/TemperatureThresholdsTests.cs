using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Core.Thermal;

namespace LaptopThermalHelper.Core.Tests;

public sealed class TemperatureThresholdsTests
{
    [Theory]
    [InlineData(DeviceKind.Cpu, 84.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Cpu, 85, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Cpu, 95, ThermalLevel.High)]
    [InlineData(DeviceKind.Cpu, 100, ThermalLevel.Critical)]
    [InlineData(DeviceKind.Gpu, 79.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Gpu, 80, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Gpu, 87, ThermalLevel.High)]
    [InlineData(DeviceKind.Gpu, 92, ThermalLevel.Critical)]
    [InlineData(DeviceKind.Storage, 59.9, ThermalLevel.Normal)]
    [InlineData(DeviceKind.Storage, 60, ThermalLevel.Elevated)]
    [InlineData(DeviceKind.Storage, 70, ThermalLevel.High)]
    [InlineData(DeviceKind.Storage, 75, ThermalLevel.High)]
    [InlineData(DeviceKind.Storage, 80, ThermalLevel.Critical)]
    public void Classify_UsesDeviceDefaults(
        DeviceKind kind,
        double temperature,
        ThermalLevel expected)
    {
        ThermalLevel actual = TemperatureThresholds.For(kind).Classify(temperature);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Classify_InvalidValue_ReturnsUnknown(double temperature)
    {
        ThermalLevel actual = TemperatureThresholds.StorageDefault.Classify(temperature);

        Assert.Equal(ThermalLevel.Unknown, actual);
    }

    [Fact]
    public void CreateFromHigh_ProducesValidThresholdsForCustomConfiguration()
    {
        TemperatureThresholds cpuThresholds = TemperatureThresholds.CreateFromHigh(DeviceKind.Cpu, 85);
        Assert.Equal(75, cpuThresholds.ElevatedAt);
        Assert.Equal(85, cpuThresholds.HighAt);
        Assert.Equal(90, cpuThresholds.CriticalAt);
        Assert.Equal(ThermalLevel.Normal, cpuThresholds.Classify(74.9));
        Assert.Equal(ThermalLevel.Elevated, cpuThresholds.Classify(75));
        Assert.Equal(ThermalLevel.High, cpuThresholds.Classify(85));
        Assert.Equal(ThermalLevel.Critical, cpuThresholds.Classify(90));

        TemperatureThresholds gpuThresholds = TemperatureThresholds.CreateFromHigh(DeviceKind.Gpu, 80);
        Assert.Equal(73, gpuThresholds.ElevatedAt);
        Assert.Equal(80, gpuThresholds.HighAt);
        Assert.Equal(85, gpuThresholds.CriticalAt);

        TemperatureThresholds storageThresholds = TemperatureThresholds.CreateFromHigh(DeviceKind.Storage, 65);
        Assert.Equal(55, storageThresholds.ElevatedAt);
        Assert.Equal(65, storageThresholds.HighAt);
        Assert.Equal(75, storageThresholds.CriticalAt);
    }
}
