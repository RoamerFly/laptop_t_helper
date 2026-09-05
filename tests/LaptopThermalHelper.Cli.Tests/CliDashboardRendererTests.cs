using System.Text.Json;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;
using Xunit;

namespace LaptopThermalHelper.Cli.Tests;

public sealed class CliDashboardRendererTests
{
    [Fact]
    public void Render_ProducesReadableTable()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var devices = new List<MonitoredDeviceSnapshot>
        {
            new(
                new DeviceSnapshot("cpu1", DeviceKind.Cpu, "Intel Core i7-13700H", 65.4, 25.0, 45.0, 2400, ThermalLevel.Normal, now),
                88.0,
                62.1,
                [])
            {
                MinimumTemperature = 40.0,
            },
            new(
                new DeviceSnapshot("gpu1", DeviceKind.Gpu, "NVIDIA GeForce RTX 4060", 55.0, 10.0, 60.0, 1800, ThermalLevel.Normal, now),
                70.0,
                52.0,
                []),
        };

        var snapshot = new MonitoringSnapshot(devices, ThermalLevel.Normal, now, MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware));
        string rendered = CliDashboardRenderer.Render(snapshot, "Linux 6.8.0", "Real Hardware");

        Assert.Contains("笔记本温控助手", rendered);
        Assert.Contains("Intel Core i7-13700H", rendered);
        Assert.Contains("65.4 °C", rendered);
        Assert.Contains("2400 RPM", rendered);
        Assert.Contains("NVIDIA GeForce", rendered);
    }

    [Fact]
    public void RenderStatusLine_ProducesCompactFormat()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var devices = new List<MonitoredDeviceSnapshot>
        {
            new(new DeviceSnapshot("cpu1", DeviceKind.Cpu, "CPU", 60.0, 30.0, null, null, ThermalLevel.Normal, now), null, null, []),
            new(new DeviceSnapshot("gpu1", DeviceKind.Gpu, "GPU", 50.0, 15.0, null, null, ThermalLevel.Normal, now), null, null, []),
        };

        var snapshot = new MonitoringSnapshot(devices, ThermalLevel.Normal, now, MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware));
        string status = CliDashboardRenderer.RenderStatusLine(snapshot);

        Assert.Equal("Cpu: 60°C (30%) | Gpu: 50°C (15%)", status);
    }

    [Fact]
    public void RenderJson_ProducesValidJsonDocument()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var devices = new List<MonitoredDeviceSnapshot>
        {
            new(new DeviceSnapshot("cpu1", DeviceKind.Cpu, "Test CPU", 70.5, 50.0, null, 2500, ThermalLevel.Elevated, now), 80.0, 65.0, []),
        };

        var snapshot = new MonitoringSnapshot(devices, ThermalLevel.Elevated, now, MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware));
        string json = CliDashboardRenderer.RenderJson(snapshot, "macOS Sonoma", "Real Hardware");

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("macOS Sonoma", root.GetProperty("OperatingSystem").GetString());
        Assert.Equal("Elevated", root.GetProperty("OverallThermalLevel").GetString());
        JsonElement devArray = root.GetProperty("Devices");
        Assert.Equal(1, devArray.GetArrayLength());
        Assert.Equal(70.5, devArray[0].GetProperty("TemperatureCelsius").GetDouble());
    }

    [Fact]
    public void Render_HandlesEmptyDevicesGracefully()
    {
        var snapshot = new MonitoringSnapshot([], ThermalLevel.Unknown, DateTimeOffset.UtcNow, MonitoringAcquisitionStatus.Ready(HardwareProviderMode.RealHardware));
        string rendered = CliDashboardRenderer.Render(snapshot, "Linux", "Mock");

        Assert.Contains("未检测到可用传感器设备", rendered);
    }
}
