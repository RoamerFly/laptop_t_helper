using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Core.Thermal;

namespace LaptopThermalHelper.Core.Tests;

public sealed class ThermalStateMachineTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Observe_ElevatedTemperature_RequiresTwentySeconds()
    {
        var machine = CreateNormalCpuMachine();

        Assert.Equal(ThermalLevel.Normal, machine.Observe(80, Start.AddSeconds(1)));
        Assert.Equal(ThermalLevel.Normal, machine.Observe(80, Start.AddSeconds(20)));
        Assert.Equal(ThermalLevel.Elevated, machine.Observe(80, Start.AddSeconds(21)));
    }

    [Fact]
    public void Observe_CriticalTemperature_RequiresTenSeconds()
    {
        var machine = CreateNormalCpuMachine();

        Assert.Equal(ThermalLevel.Normal, machine.Observe(98, Start.AddSeconds(1)));
        Assert.Equal(ThermalLevel.Critical, machine.Observe(98, Start.AddSeconds(11)));
    }

    [Fact]
    public void Observe_ShortSpike_DoesNotEscalate()
    {
        var machine = CreateNormalCpuMachine();

        machine.Observe(92, Start.AddSeconds(1));
        machine.Observe(70, Start.AddSeconds(10));

        Assert.Equal(ThermalLevel.Normal, machine.Observe(92, Start.AddSeconds(25)));
    }

    [Fact]
    public void Observe_Recovery_RequiresThreeDegreeHysteresisAndThirtySeconds()
    {
        var machine = CreateNormalCpuMachine();
        machine.Observe(92, Start.AddSeconds(1));
        machine.Observe(92, Start.AddSeconds(21));
        Assert.Equal(ThermalLevel.High, machine.CurrentLevel);

        Assert.Equal(ThermalLevel.High, machine.Observe(87.5, Start.AddSeconds(22)));
        Assert.Equal(ThermalLevel.High, machine.Observe(86, Start.AddSeconds(23)));
        Assert.Equal(ThermalLevel.High, machine.Observe(86, Start.AddSeconds(52)));
        Assert.Equal(ThermalLevel.Elevated, machine.Observe(86, Start.AddSeconds(53)));
    }

    [Fact]
    public void Observe_CriticalRecovery_OnlyDropsOneLevel()
    {
        var machine = CreateNormalCpuMachine();
        machine.Observe(98, Start.AddSeconds(1));
        machine.Observe(98, Start.AddSeconds(11));

        machine.Observe(60, Start.AddSeconds(12));
        ThermalLevel level = machine.Observe(60, Start.AddSeconds(42));

        Assert.Equal(ThermalLevel.High, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(151d)]
    public void Observe_InvalidTemperature_ReturnsUnknown(double? temperature)
    {
        var machine = CreateNormalCpuMachine();

        Assert.Equal(ThermalLevel.Unknown, machine.Observe(temperature, Start.AddSeconds(1)));
    }

    private static ThermalStateMachine CreateNormalCpuMachine()
    {
        var machine = new ThermalStateMachine(TemperatureThresholds.CpuDefault);
        machine.Observe(60, Start);
        return machine;
    }
}
