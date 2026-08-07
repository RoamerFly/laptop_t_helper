using LaptopThermalHelper.Core.Statistics;

namespace LaptopThermalHelper.Core.Tests;

public sealed class RunningStatisticsTests
{
    [Fact]
    public void Add_IgnoresMissingAndInvalidValues()
    {
        var statistics = new RunningStatistics();

        statistics.Add(null);
        statistics.Add(double.NaN);
        statistics.Add(40);
        statistics.Add(60);

        Assert.Equal(2, statistics.Count);
        Assert.Equal(60, statistics.Maximum);
        Assert.Equal(50, statistics.Average);
    }
}
