using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using LaptopThermalHelper.Infrastructure.History;

namespace LaptopThermalHelper.Infrastructure.Tests;

public sealed class CsvTemperatureHistoryStoreTests
{
    [Fact]
    public async Task AppendAsync_WritesHeaderRowsAndEmptyMissingValues()
    {
        using var directory = new TemporaryDirectory();
        using var store = new CsvTemperatureHistoryStore(directory.Path);
        DateTimeOffset timestamp = DateTimeOffset.Now;
        var snapshot = new MonitoringSnapshot(
        [
            MonitoredDevice("cpu", DeviceKind.Cpu, "Intel Core i7-11800H", null, 23, 28, null, ThermalLevel.Unknown, timestamp),
            MonitoredDevice("gpu", DeviceKind.Gpu, "NVIDIA RTX 3060", 68.5, 35, 61, 2100, ThermalLevel.Normal, timestamp),
        ],
        ThermalLevel.Normal,
        timestamp);

        await store.AppendAsync(snapshot, CancellationToken.None);

        string historyFile = Assert.Single(Directory.GetFiles(System.IO.Path.Combine(directory.Path, "history")));
        string[] lines = await File.ReadAllLinesAsync(historyFile);
        Assert.Equal(3, lines.Length);
        Assert.Equal(
            "timestamp,device_id,device_kind,device_name,temperature_c,load_percent,power_w,fan_rpm,thermal_level",
            lines[0]);
        Assert.EndsWith(
            "cpu,Cpu,Intel Core i7-11800H,,23,28,,Unknown",
            lines[1],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "gpu,Gpu,NVIDIA RTX 3060,68.5,35,61,2100,Normal",
            lines[2],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppendAsync_EscapesDeviceNamesForCsv()
    {
        using var directory = new TemporaryDirectory();
        using var store = new CsvTemperatureHistoryStore(directory.Path);
        DateTimeOffset timestamp = DateTimeOffset.Now;
        MonitoringSnapshot snapshot = Snapshot(
            MonitoredDevice(
                "ssd",
                DeviceKind.Storage,
                "SSD \"Main\", 1TB",
                45,
                null,
                null,
                null,
                ThermalLevel.Normal,
                timestamp),
            timestamp);

        await store.AppendAsync(snapshot, CancellationToken.None);

        string historyFile = Assert.Single(Directory.GetFiles(System.IO.Path.Combine(directory.Path, "history")));
        string content = await File.ReadAllTextAsync(historyFile);
        Assert.Contains("\"SSD \"\"Main\"\", 1TB\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_MergesDailyFilesWithOneHeader()
    {
        using var directory = new TemporaryDirectory();
        using var store = new CsvTemperatureHistoryStore(directory.Path);
        DateTimeOffset firstDay = DateTimeOffset.Now.Date;
        DateTimeOffset secondDay = firstDay.AddDays(1);

        await store.AppendAsync(
            Snapshot(MonitoredDevice("cpu", DeviceKind.Cpu, "CPU", 60, null, null, null, ThermalLevel.Normal, firstDay), firstDay),
            CancellationToken.None);
        await store.AppendAsync(
            Snapshot(MonitoredDevice("gpu", DeviceKind.Gpu, "GPU", 65, null, null, null, ThermalLevel.Normal, secondDay), secondDay),
            CancellationToken.None);

        var result = await store.ExportAsync(CancellationToken.None);

        Assert.True(result.HasData);
        Assert.Equal(2, result.SourceFileCount);
        Assert.Equal(2, result.RecordCount);
        Assert.NotNull(result.FilePath);
        string[] lines = await File.ReadAllLinesAsync(result.FilePath);
        Assert.Equal(3, lines.Length);
        Assert.Single(lines, static line => line.StartsWith("timestamp,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_WhenHistoryIsEmpty_ReturnsEmptyResult()
    {
        using var directory = new TemporaryDirectory();
        using var store = new CsvTemperatureHistoryStore(directory.Path);

        var result = await store.ExportAsync(CancellationToken.None);

        Assert.False(result.HasData);
        Assert.Null(result.FilePath);
        Assert.Equal(0, result.RecordCount);
    }

    private static MonitoringSnapshot Snapshot(
        MonitoredDeviceSnapshot device,
        DateTimeOffset timestamp) =>
        new([device], device.Device.ThermalLevel, timestamp);

    private static MonitoredDeviceSnapshot MonitoredDevice(
        string id,
        DeviceKind kind,
        string name,
        double? temperature,
        double? load,
        double? power,
        double? fanRpm,
        ThermalLevel level,
        DateTimeOffset timestamp) =>
        new(
            new DeviceSnapshot(id, kind, name, temperature, load, power, fanRpm, level, timestamp),
            temperature,
            temperature,
            []);

    private sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LaptopThermalHelper.Tests");

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            string resolvedPath = System.IO.Path.GetFullPath(Path);
            string resolvedRoot = System.IO.Path.GetFullPath(TestRoot) + System.IO.Path.DirectorySeparatorChar;
            if (resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolvedPath))
            {
                Directory.Delete(resolvedPath, true);
            }
        }
    }
}
