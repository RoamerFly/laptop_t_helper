using System.Text.Json;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Domain;
using LibreHardwareMonitor.Hardware;

namespace LaptopThermalHelper.Hardware.Lhm;

public sealed class LhmHardwareMonitorProvider : IHardwareMonitorProvider, IHardwareMonitorProviderMetadata
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

    public HardwareProviderMode Mode => HardwareProviderMode.RealHardware;

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
            var emittedDeviceIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (IHardware hardware in EnumerateHardware(_computer.Hardware))
            {
                DeviceKind? kind = MapDeviceKind(hardware.HardwareType);
                if (kind is null || kind is DeviceKind.Memory or DeviceKind.Battery)
                {
                    continue;
                }

                string deviceId = hardware.Identifier.ToString();
                if (!emittedDeviceIds.Add(deviceId))
                {
                    continue;
                }

                IReadOnlyList<ISensor> sensors = EnumerateSensors(hardware).ToArray();
                LhmTemperatureSensorCandidate? primaryTemperature = LhmTemperatureSelector.SelectPrimary(
                    hardware.HardwareType,
                    sensors
                        .Where(static sensor => sensor.SensorType == SensorType.Temperature)
                        .Select(static sensor => new LhmTemperatureSensorCandidate(
                            sensor.Identifier.ToString(),
                            sensor.Name,
                            sensor.Value)));
                ISensor? temperatureSensor = primaryTemperature is null
                    ? null
                    : sensors.FirstOrDefault(sensor => string.Equals(
                        sensor.Identifier.ToString(),
                        primaryTemperature.SensorId,
                        StringComparison.Ordinal));
                ISensor? loadSensor = SelectMetricSensor(SensorType.Load, sensors, "Total", "Core");
                ISensor? powerSensor = SelectMetricSensor(SensorType.Power, sensors, "Package", "Core");
                ISensor? fanSensor = SelectMetricSensor(SensorType.Fan, sensors);

                samples.Add(new DeviceSample(
                    deviceId,
                    kind.Value,
                    hardware.Name,
                    ValueOf(temperatureSensor),
                    ValueOf(loadSensor),
                    ValueOf(powerSensor),
                    ValueOf(fanSensor),
                    now)
                {
                    TemperatureSensors = CreateTemperatureReadings(hardware, kind.Value, sensors, now),
                    PrimaryTemperatureSensorName = temperatureSensor?.Name,
                });

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

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> roots) =>
        LhmHardwareTraversal.DepthFirst(roots, static hardware => hardware.SubHardware);

    private static ISensor[] EnumerateSensors(IHardware hardware) => hardware.Sensors;

    private static DeviceKind? MapDeviceKind(HardwareType hardwareType) => hardwareType switch
    {
        HardwareType.Cpu => DeviceKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => DeviceKind.Gpu,
        HardwareType.Storage => DeviceKind.Storage,
        HardwareType.Memory => DeviceKind.Memory,
        HardwareType.Battery => DeviceKind.Battery,
        _ => null,
    };

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

    private static SensorReading[] CreateTemperatureReadings(
        IHardware hardware,
        DeviceKind kind,
        IEnumerable<ISensor> sensors,
        DateTimeOffset timestamp) =>
        sensors
            .Where(static sensor => sensor.SensorType == SensorType.Temperature)
            .Select(sensor => new
            {
                Sensor = sensor,
                Candidate = new LhmTemperatureSensorCandidate(
                    sensor.Identifier.ToString(),
                    sensor.Name,
                    sensor.Value),
            })
            .Where(item => LhmTemperatureSelector.IsEligible(hardware.HardwareType, item.Candidate))
            .OrderByDescending(item => LhmTemperatureSelector.GetPriority(hardware.HardwareType, item.Sensor.Name))
            .ThenBy(item => item.Sensor.Identifier.ToString(), StringComparer.Ordinal)
            .Select(item => new SensorReading(
                hardware.Identifier.ToString(),
                kind,
                hardware.Name,
                item.Sensor.Identifier.ToString(),
                item.Sensor.Name,
                SensorMetric.Temperature,
                item.Sensor.Value,
                "°C",
                timestamp,
                ReadingQuality.Good))
            .ToArray();

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
                GetSelectionReason(hardware, sensor, selected)));
        }
    }

    private static string GetSelectionReason(IHardware hardware, ISensor sensor, bool selected)
    {
        if (sensor.SensorType != SensorType.Temperature)
        {
            return "非温度传感器，不参与总览温度选择";
        }

        var candidate = new LhmTemperatureSensorCandidate(
            sensor.Identifier.ToString(),
            sensor.Name,
            sensor.Value);
        if (hardware.HardwareType == HardwareType.Storage &&
            LhmTemperatureSelector.IsStorageThresholdSensor(sensor.Name))
        {
            return "SMART 预警阈值，不是当前温度，已排除";
        }

        if (!LhmTemperatureSelector.IsPlausibleTemperature(sensor.Value))
        {
            return "温度值不可用或超出合理范围，已排除";
        }

        if (!LhmTemperatureSelector.IsEligible(hardware.HardwareType, candidate))
        {
            return "不适合作为当前温度，已排除";
        }

        return selected ? "选为总览主温度传感器" : "可用温度传感器，未选为总览主温度传感器";
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
