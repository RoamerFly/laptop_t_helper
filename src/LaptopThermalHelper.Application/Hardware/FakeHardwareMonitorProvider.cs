using System.Diagnostics;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Application.Hardware;

public sealed class FakeHardwareMonitorProvider : IHardwareMonitorProvider
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    public Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double seconds = _uptime.Elapsed.TotalSeconds;
        DateTimeOffset now = _startedAt + _uptime.Elapsed;

        IReadOnlyList<DeviceSample> samples =
        [
            Create("cpu", DeviceKind.Cpu, "Intel Core i7-11800H", 45, 98, 65, seconds + 18, now, 23, 28, 2100),
            Create("gpu", DeviceKind.Gpu, "NVIDIA GeForce RTX 3060", 40, 88, 58, seconds + 18, now, 35, 61, 2000),
            Create("ssd", DeviceKind.Storage, "西数 SN730 1TB", 35, 76, 45, seconds + 12, now, null, null, null),
        ];

        return Task.FromResult(samples);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DeviceSample Create(
        string id,
        DeviceKind kind,
        string name,
        double low,
        double peak,
        double recovered,
        double seconds,
        DateTimeOffset timestamp,
        double? load,
        double? power,
        double? fanRpm)
    {
        double temperature = InterpolateCycle(low, peak, recovered, seconds);
        double wave = Math.Sin(seconds / 7) * 3;

        return new DeviceSample(
            id,
            kind,
            name,
            Math.Round(temperature, 1),
            load is null ? null : Math.Clamp(load.Value + wave, 0, 100),
            power is null ? null : Math.Max(0, power.Value + wave),
            fanRpm is null ? null : Math.Max(0, fanRpm.Value + wave * 35),
            timestamp);
    }

    private static double InterpolateCycle(double low, double peak, double recovered, double seconds)
    {
        double position = seconds % 120;
        return position switch
        {
            < 40 => Lerp(low, peak, position / 40),
            < 80 => Lerp(peak, recovered, (position - 40) / 40),
            _ => Lerp(recovered, low, (position - 80) / 40),
        };
    }

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);
}
