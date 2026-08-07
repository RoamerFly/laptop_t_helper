using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Infrastructure.Platform;

namespace LaptopThermalHelper.Infrastructure.Tests;

public sealed class WindowsSystemInformationMapperTests
{
    [Fact]
    public void Map_MapsPowerAndBatteryInformation()
    {
        var data = new WindowsSystemInformationRawData(
            "Windows 11 Pro 24H2",
            "Lenovo",
            "Legion Y9000P",
            new SystemPowerStatusRawData(1, 8, 88, 7200),
            new PowerPlanRawData(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "平衡"),
            []);

        SystemInformationSnapshot result = WindowsSystemInformationMapper.Map(data, DateTimeOffset.UnixEpoch);

        Assert.Equal(SystemInformationAvailability.Ready, result.Availability);
        Assert.Equal("Lenovo", result.Manufacturer);
        Assert.Equal(PowerSourceKind.Ac, result.PowerSource);
        Assert.Equal(BatteryChargeState.Charging, result.Battery.State);
        Assert.Equal(88, result.Battery.ChargePercent);
        Assert.Equal(TimeSpan.FromHours(2), result.Battery.RemainingTime);
        Assert.Equal("平衡", result.PowerPlan.DisplayName);
    }

    [Fact]
    public void Map_MissingValues_ReturnsExplicitUnavailableFields()
    {
        var data = new WindowsSystemInformationRawData(null, null, null, null, null, ["query timed out"]);

        SystemInformationSnapshot result = WindowsSystemInformationMapper.Map(data, DateTimeOffset.UnixEpoch);

        Assert.Equal(SystemInformationAvailability.Unavailable, result.Availability);
        Assert.Equal(SystemInformationSnapshot.UnavailableText, result.OperatingSystem);
        Assert.Equal(SystemInformationSnapshot.UnavailableText, result.Manufacturer);
        Assert.Equal(PowerSourceKind.Unknown, result.PowerSource);
        Assert.Equal(BatteryChargeState.Unknown, result.Battery.State);
        Assert.Contains("query timed out", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void MapBattery_NoSystemBattery_ReportsNotPresent()
    {
        BatteryInformation result = WindowsSystemInformationMapper.MapBattery(
            new SystemPowerStatusRawData(1, 128, byte.MaxValue, uint.MaxValue));

        Assert.False(result.IsPresent);
        Assert.Equal(BatteryChargeState.NotPresent, result.State);
        Assert.Null(result.ChargePercent);
        Assert.Null(result.RemainingTime);
    }
}
