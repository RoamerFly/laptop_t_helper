using LaptopThermalHelper.Core.Domain;
using Xunit;

namespace LaptopThermalHelper.Hardware.Linux.Tests;

public sealed class LinuxHardwareMonitorProviderTests : IDisposable
{
    private readonly string _tempDir;

    public LinuxHardwareMonitorProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lth_linux_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ReadAsync_ParsesCoretempHwmonCorrectly()
    {
        string hwmonDir = Path.Combine(_tempDir, "hwmon0");
        Directory.CreateDirectory(hwmonDir);
        File.WriteAllText(Path.Combine(hwmonDir, "name"), "coretemp\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_input"), "52000\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_label"), "Package id 0\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp2_input"), "48000\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp2_label"), "Core 0\n");

        string procStat = Path.Combine(_tempDir, "stat");
        File.WriteAllText(procStat, "cpu  100 0 50 850 0 0 0 0 0 0\n");

        var provider = new LinuxHardwareMonitorProvider(_tempDir, Path.Combine(_tempDir, "thermal"), procStat);
        IReadOnlyList<DeviceSample> samples = await provider.ReadAsync(CancellationToken.None);

        Assert.Single(samples);
        DeviceSample cpu = samples[0];
        Assert.Equal(DeviceKind.Cpu, cpu.Kind);
        Assert.Equal("Intel Core CPU", cpu.DisplayName);
        Assert.Equal(52.0, cpu.Temperature);
        Assert.Equal("Package id 0", cpu.PrimaryTemperatureSensorName);
        Assert.Equal(2, cpu.TemperatureSensors.Count);
        Assert.Equal(48.0, cpu.TemperatureSensors.First(s => s.SensorName == "Core 0").Value);
    }

    [Fact]
    public async Task ReadAsync_ParsesAmdGpuAndFanCorrectly()
    {
        string hwmonDir = Path.Combine(_tempDir, "hwmon1");
        Directory.CreateDirectory(hwmonDir);
        File.WriteAllText(Path.Combine(hwmonDir, "name"), "amdgpu\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_input"), "61500\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_label"), "edge\n");
        File.WriteAllText(Path.Combine(hwmonDir, "fan1_input"), "2100\n");
        File.WriteAllText(Path.Combine(hwmonDir, "power1_average"), "35000000\n"); // 35W

        var provider = new LinuxHardwareMonitorProvider(_tempDir, Path.Combine(_tempDir, "thermal"), Path.Combine(_tempDir, "stat"));
        IReadOnlyList<DeviceSample> samples = await provider.ReadAsync(CancellationToken.None);

        Assert.Single(samples);
        DeviceSample gpu = samples[0];
        Assert.Equal(DeviceKind.Gpu, gpu.Kind);
        Assert.Equal("AMD Radeon GPU", gpu.DisplayName);
        Assert.Equal(61.5, gpu.Temperature);
        Assert.Equal(2100, gpu.FanRpm);
        Assert.Equal(35.0, gpu.Power);
    }

    [Fact]
    public async Task ReadAsync_ParsesNvmeStorageCorrectly()
    {
        string hwmonDir = Path.Combine(_tempDir, "hwmon2");
        Directory.CreateDirectory(hwmonDir);
        File.WriteAllText(Path.Combine(hwmonDir, "name"), "nvme\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_input"), "41900\n");
        File.WriteAllText(Path.Combine(hwmonDir, "temp1_label"), "Composite\n");

        var provider = new LinuxHardwareMonitorProvider(_tempDir, Path.Combine(_tempDir, "thermal"), Path.Combine(_tempDir, "stat"));
        IReadOnlyList<DeviceSample> samples = await provider.ReadAsync(CancellationToken.None);

        Assert.Single(samples);
        DeviceSample ssd = samples[0];
        Assert.Equal(DeviceKind.Storage, ssd.Kind);
        Assert.Equal("NVMe SSD", ssd.DisplayName);
        Assert.Equal(41.9, ssd.Temperature);
    }

    [Fact]
    public async Task ReadAsync_HandlesEmptyOrMissingDirectoryGracefully()
    {
        string missingDir = Path.Combine(_tempDir, "does_not_exist");
        var provider = new LinuxHardwareMonitorProvider(missingDir, missingDir, missingDir);

        IReadOnlyList<DeviceSample> samples = await provider.ReadAsync(CancellationToken.None);
        Assert.NotNull(samples);
    }
}
