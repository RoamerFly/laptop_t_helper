using LaptopThermalHelper.Core.Collections;

namespace LaptopThermalHelper.Core.Tests;

public sealed class FixedRingBufferTests
{
    [Fact]
    public void Add_WhenFull_DropsOldestItem()
    {
        var buffer = new FixedRingBuffer<int>(3);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal([2, 3, 4], buffer);
    }

    [Fact]
    public void Constructor_WithInvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRingBuffer<int>(0));
    }
}
