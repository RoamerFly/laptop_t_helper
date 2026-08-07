namespace LaptopThermalHelper.Core.Statistics;

public sealed class RunningStatistics
{
    private double _sum;

    public long Count { get; private set; }

    public double? Maximum { get; private set; }

    public double? Average => Count == 0 ? null : _sum / Count;

    public void Add(double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        Count++;
        _sum += value.Value;
        Maximum = Maximum is null ? value : Math.Max(Maximum.Value, value.Value);
    }
}
