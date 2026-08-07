using LaptopThermalHelper.Application.Hardware;

namespace LaptopThermalHelper.Application.Tests;

public sealed class HardwareRuntimeOptionsTests
{
    [Fact]
    public void Parse_WithoutHardwareFlags_UsesRealHardwareByDefault()
    {
        HardwareRuntimeOptions result = HardwareRuntimeOptions.Parse([]);

        Assert.False(result.UseMock);
        Assert.False(result.UsesDeprecatedRealHardwareFlag);
    }

    [Fact]
    public void Parse_MockFlag_UsesMockProvider()
    {
        HardwareRuntimeOptions result = HardwareRuntimeOptions.Parse(["--mock"]);

        Assert.True(result.UseMock);
    }

    [Fact]
    public void Parse_DeprecatedRealHardwareFlag_RemainsRealAndReportsCompatibilityUse()
    {
        HardwareRuntimeOptions result = HardwareRuntimeOptions.Parse(["--real-hardware"]);

        Assert.False(result.UseMock);
        Assert.True(result.UsesDeprecatedRealHardwareFlag);
    }

    [Fact]
    public void Parse_MockTakesPrecedenceWhenBothFlagsArePresent()
    {
        HardwareRuntimeOptions result = HardwareRuntimeOptions.Parse(["--real-hardware", "--mock"]);

        Assert.True(result.UseMock);
        Assert.True(result.UsesDeprecatedRealHardwareFlag);
    }
}
