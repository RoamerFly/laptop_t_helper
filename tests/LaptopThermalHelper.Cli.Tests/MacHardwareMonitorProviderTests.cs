using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Hardware.Mac;
using Xunit;

namespace LaptopThermalHelper.Cli.Tests;

public sealed class MacHardwareMonitorProviderTests
{
    [Fact]
    public void ParseSmcTemperature_ExtractsValueCorrectly()
    {
        string smcOutput = "  TC0P  [sp78]  54.25 (bytes 36 40)";
        double? temp = MacHardwareMonitorProvider.ParseSmcTemperature(smcOutput);

        Assert.Equal(54.2, temp);
    }

    [Fact]
    public void ParseSmcFan_ExtractsRpmCorrectly()
    {
        string smcOutput = "  F0Ac  [fpe2]  2150.0 (bytes 21 96)";
        double? fan = MacHardwareMonitorProvider.ParseSmcFan(smcOutput);

        Assert.Equal(2150.0, fan);
    }

    [Fact]
    public async Task ReadAsync_InvokesCustomCommandRunnerAndParses()
    {
        var provider = new MacHardwareMonitorProvider((cmd, args) =>
        {
            if (cmd == "sysctl")
            {
                return "Apple M2 Pro\n";
            }
            if (cmd == "osx-cpu-temp")
            {
                return "47.5°C\n";
            }
            if (cmd == "top")
            {
                return "CPU usage: 5.0% user, 5.0% sys, 90.0% idle\n";
            }
            return null;
        });

        IReadOnlyList<DeviceSample> samples = await provider.ReadAsync(CancellationToken.None);

        Assert.Single(samples);
        DeviceSample cpu = samples[0];
        Assert.Equal(DeviceKind.Cpu, cpu.Kind);
        Assert.Equal("Apple M2 Pro", cpu.DisplayName);
        Assert.Equal(47.5, cpu.Temperature);
        Assert.Equal(10.0, cpu.Load);
    }
}
