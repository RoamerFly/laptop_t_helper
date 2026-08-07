using System.Text.Json;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Domain;
using LibreHardwareMonitor.Hardware;

namespace LaptopThermalHelper.Hardware.Lhm;

public sealed class LhmHardwareMonitorProvider : IHardwareMonitorProvider
{
    private static readonly JsonSerializerOptions DiscoveryJsonOptions = new() { WriteIndented = true };
    private readonly object _syncRoot = new();
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
        IsControllerEnabled = true,
        IsMotherboardEnabled = true,
    };
    private bool _isOpen;
    private bool _discoveryWritten;

    public async Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(ReadCore, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_syncRoot)
        {
            if (_isOpen)
            {
                _computer.Close();
                _isOpen = false;
            }
        }

        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<DeviceSample> ReadCore()
    {
        lock (_syncRoot)
        {
            EnsureOpen();
            foreach (IHardware hardware in _computer.Hardware)
            {
                UpdateRecursively(hardware);
            }

            DateTimeOffset now = DateTimeOffset.Now;
            var samples = new List<DeviceSample>();
            var discoveries = new List<SensorDiscoveryEntry>();

            foreach (IHardware hardware in EnumerateHardware(_computer.Hardware))
            {
                DeviceKind? kind = MapDeviceKind(hardware.HardwareType);
                if (kind is null || kind is DeviceKind.Memory or DeviceKind.Battery)
                {
                    continue;
                }

                IReadOnlyList<ISensor> sensors = EnumerateSensors(hardware).ToArray();
                ISensor? temperatureSensor = SelectTemperatureSensor(hardware.HardwareType, sensors);
                ISensor? loadSensor = SelectMetricSensor(SensorType.Load, sensors, "Total", "Core");
                ISensor? powerSensor = SelectMetricSensor(SensorType.Power, sensors, "Package", "Core");
                ISensor? fanSensor = SelectMetricSensor(SensorType.Fan, sensors);

                samples.Add(new DeviceSample(
                    hardware.Identifier.ToString(),
                    kind.Value,
                    hardware.Name,
                    ValueOf(temperatureSensor),
                    ValueOf(loadSensor),
                    ValueOf(powerSensor),
                    ValueOf(fanSensor),
                    now));

                AddDiscoveries(discoveries, hardware, sensors, temperatureSensor);
            }

            WriteDiscoveryLogOnce(discoveries);
            return samples;
        }
    }

    private void EnsureOpen()
    {
        if (_isOpen)
        {
            return;
        }

        _computer.Open();
        _isOpen = true;
    }

    private static void UpdateRecursively(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch
        {
            // 单个硬件刷新失败不应中断其他设备采样。
        }

        foreach (IHardware child in hardware.SubHardware)
        {
            UpdateRecursively(child);
        }
    }

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> roots)
    {
        foreach (IHardware hardware in roots)
        {
            yield return hardware;
        }
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            yield return sensor;
        }

        foreach (IHardware child in hardware.SubHardware)
        {
            foreach (ISensor sensor in EnumerateSensors(child))
            {
                yield return sensor;
            }
        }
    }

    private static DeviceKind? MapDeviceKind(HardwareType hardwareType) => hardwareType switch
    {
        HardwareType.Cpu => DeviceKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => DeviceKind.Gpu,
        HardwareType.Storage => DeviceKind.Storage,
        HardwareType.Memory => DeviceKind.Memory,
        HardwareType.Battery => DeviceKind.Battery,
        _ => null,
    };

    private static ISensor? SelectTemperatureSensor(
        HardwareType hardwareType,
        IReadOnlyList<ISensor> sensors)
    {
        return sensors
            .Where(static sensor => sensor.SensorType == SensorType.Temperature && IsUsable(sensor.Value))
            .OrderByDescending(sensor => TemperaturePriority(hardwareType, sensor.Name))
            .ThenByDescending(static sensor => sensor.Value)
            .FirstOrDefault();
    }

    private static int TemperaturePriority(HardwareType hardwareType, string name)
    {
        if (hardwareType == HardwareType.Cpu)
        {
            if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (name.Equals("Core Max", StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }

            if (ContainsAny(name, "Package", "Tctl", "Tdie"))
            {
                return 80;
            }

            return name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 60 : 50;
        }

        if (hardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }

            return ContainsAny(name, "Hot Spot", "Memory Junction") ? 40 : 50;
        }

        if (hardwareType == HardwareType.Storage)
        {
            return name.Contains("Composite", StringComparison.OrdinalIgnoreCase) ? 100 : 50;
        }

        return 0;
    }

    private static ISensor? SelectMetricSensor(
        SensorType sensorType,
        IReadOnlyList<ISensor> sensors,
        params string[] preferredNames)
    {
        return sensors
            .Where(sensor => sensor.SensorType == sensorType && IsUsable(sensor.Value))
            .OrderByDescending(sensor => preferredNames.Any(name =>
                sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(static sensor => sensor.Value)
            .FirstOrDefault();
    }

    private static double? ValueOf(ISensor? sensor) =>
        sensor?.Value is float value && IsUsable(value) ? value : null;

    private static bool IsUsable(float? value) =>
        value is float number && !float.IsNaN(number) && !float.IsInfinity(number);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static void AddDiscoveries(
        ICollection<SensorDiscoveryEntry> entries,
        IHardware hardware,
        IEnumerable<ISensor> sensors,
        ISensor? selectedTemperature)
    {
        foreach (ISensor sensor in sensors)
        {
            bool selected = ReferenceEquals(sensor, selectedTemperature);
            entries.Add(new SensorDiscoveryEntry(
                hardware.HardwareType.ToString(),
                hardware.Name,
                hardware.Identifier.ToString(),
                sensor.SensorType.ToString(),
                sensor.Name,
                sensor.Identifier.ToString(),
                sensor.Value,
                selected,
                selected ? "选为总览主温度传感器" : "未选为总览主温度传感器"));
        }
    }

    private void WriteDiscoveryLogOnce(IReadOnlyCollection<SensorDiscoveryEntry> entries)
    {
        if (_discoveryWritten)
        {
            return;
        }

        _discoveryWritten = true;
        try
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(root, "RoamerFly", "LaptopThermalHelper", "logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"sensor-discovery-{DateTime.Now:yyyyMMdd}.json");
            string temporaryPath = path + ".tmp";
            string json = JsonSerializer.Serialize(entries, DiscoveryJsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            // 发现日志写入失败不能影响温度采集。
        }
    }

    private sealed record SensorDiscoveryEntry(
        string HardwareType,
        string HardwareName,
        string HardwareIdentifier,
        string SensorType,
        string SensorName,
        string SensorIdentifier,
        float? CurrentValue,
        bool IsPrimary,
        string SelectionReason);
}
