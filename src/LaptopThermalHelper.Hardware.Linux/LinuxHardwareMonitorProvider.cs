using System.Globalization;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Hardware.Linux;

public sealed class LinuxHardwareMonitorProvider : IHardwareMonitorProvider
{
    private readonly string _hwmonPath;
    private readonly string _thermalZonePath;
    private readonly string _procStatPath;
    private (long Total, long Idle)? _previousCpuStat;
    private readonly object _syncLock = new();

    public LinuxHardwareMonitorProvider(
        string hwmonPath = "/sys/class/hwmon",
        string thermalZonePath = "/sys/class/thermal",
        string procStatPath = "/proc/stat")
    {
        _hwmonPath = hwmonPath;
        _thermalZonePath = thermalZonePath;
        _procStatPath = procStatPath;
    }

    public async Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(ReadSamplesCore, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private List<DeviceSample> ReadSamplesCore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var samples = new List<DeviceSample>();
        double? overallCpuLoad = ReadCpuLoadPercent();

        if (Directory.Exists(_hwmonPath))
        {
            string[] hwmonDirs;
            try
            {
                hwmonDirs = Directory.GetDirectories(_hwmonPath, "hwmon*");
            }
            catch
            {
                hwmonDirs = [];
            }

            Array.Sort(hwmonDirs, StringComparer.Ordinal);
            foreach (string dir in hwmonDirs)
            {
                DeviceSample? sample = ParseHwmonDirectory(dir, now, overallCpuLoad);
                if (sample is not null)
                {
                    samples.Add(sample);
                }
            }
        }

        // If no CPU sample was discovered via hwmon, check /sys/class/thermal/thermal_zone*
        if (!samples.Any(s => s.Kind == DeviceKind.Cpu) && Directory.Exists(_thermalZonePath))
        {
            DeviceSample? thermalSample = ParseThermalZoneDirectory(_thermalZonePath, now, overallCpuLoad);
            if (thermalSample is not null)
            {
                samples.Add(thermalSample);
            }
        }

        // If still empty (e.g. non-Linux environment or minimal container), ensure a fallback is generated if running on Linux
        if (samples.Count == 0 && OperatingSystem.IsLinux())
        {
            samples.Add(new DeviceSample(
                "linux-cpu-fallback",
                DeviceKind.Cpu,
                "Linux Generic CPU",
                null,
                overallCpuLoad,
                null,
                null,
                now));
        }

        return samples;
    }

    private static DeviceSample? ParseHwmonDirectory(string dir, DateTimeOffset now, double? cpuLoad)
    {
        string dirName = Path.GetFileName(dir);
        string name = ReadFileString(Path.Combine(dir, "name")) ?? dirName;
        DeviceKind kind = ClassifyDevice(name);

        var tempSensors = new List<SensorReading>();
        string[] tempInputs;
        try
        {
            tempInputs = Directory.GetFiles(dir, "temp*_input");
        }
        catch
        {
            tempInputs = [];
        }

        Array.Sort(tempInputs, StringComparer.Ordinal);
        foreach (string inputFile in tempInputs)
        {
            string baseName = Path.GetFileNameWithoutExtension(inputFile); // e.g. "temp1_input"
            string prefix = baseName[..baseName.IndexOf('_', StringComparison.Ordinal)]; // e.g. "temp1"

            double? tempValue = ReadMilliDegrees(inputFile);
            if (tempValue is null)
            {
                continue;
            }

            string? label = ReadFileString(Path.Combine(dir, $"{prefix}_label"))
                ?? ReadFileString(Path.Combine(dir, $"{prefix}_name"))
                ?? $"{name} {prefix}";

            double? min = ReadMilliDegrees(Path.Combine(dir, $"{prefix}_min"));
            double? max = ReadMilliDegrees(Path.Combine(dir, $"{prefix}_max"))
                ?? ReadMilliDegrees(Path.Combine(dir, $"{prefix}_crit"));

            tempSensors.Add(new SensorReading(
                dirName,
                kind,
                name,
                $"{dirName}_{prefix}",
                label,
                SensorMetric.Temperature,
                tempValue,
                "°C",
                now,
                ReadingQuality.Good)
            {
                Minimum = min,
                Maximum = max,
            });
        }

        double? primaryTemp = null;
        string? primarySensorName = null;

        if (tempSensors.Count > 0)
        {
            // Pick preferred sensor: Package id 0, Tctl, Composite, edge, or highest value
            SensorReading preferred = tempSensors
                .OrderByDescending(s => GetSensorPriority(s.SensorName))
                .ThenByDescending(s => s.Value ?? 0)
                .First();

            primaryTemp = preferred.Value;
            primarySensorName = preferred.SensorName;
        }

        double? fanRpm = ReadFirstMatchingMetric(dir, "fan*_input", 1.0);
        double? powerWatts = ReadFirstMatchingMetric(dir, "power*_average", 1_000_000.0)
            ?? ReadFirstMatchingMetric(dir, "power*_input", 1_000_000.0);

        double? load = kind == DeviceKind.Cpu ? cpuLoad : null;

        // Skip devices that report no temperature, no fan, and no power
        if (tempSensors.Count == 0 && fanRpm is null && powerWatts is null)
        {
            return null;
        }

        return new DeviceSample(
            dirName,
            kind,
            FormatDeviceDisplayName(name, kind),
            primaryTemp,
            load,
            powerWatts,
            fanRpm,
            now)
        {
            TemperatureSensors = tempSensors,
            PrimaryTemperatureSensorName = primarySensorName,
        };
    }

    private static DeviceSample? ParseThermalZoneDirectory(string thermalPath, DateTimeOffset now, double? cpuLoad)
    {
        string[] zones;
        try
        {
            zones = Directory.GetDirectories(thermalPath, "thermal_zone*");
        }
        catch
        {
            zones = [];
        }

        var tempSensors = new List<SensorReading>();
        foreach (string zone in zones)
        {
            string type = ReadFileString(Path.Combine(zone, "type")) ?? Path.GetFileName(zone);
            double? temp = ReadMilliDegrees(Path.Combine(zone, "temp"));
            if (temp is not null)
            {
                tempSensors.Add(new SensorReading(
                    "thermal_zone",
                    DeviceKind.Cpu,
                    "ACPI Thermal Zone",
                    Path.GetFileName(zone),
                    type,
                    SensorMetric.Temperature,
                    temp,
                    "°C",
                    now,
                    ReadingQuality.Good));
            }
        }

        if (tempSensors.Count == 0)
        {
            return null;
        }

        SensorReading primary = tempSensors.OrderByDescending(s => s.Value ?? 0).First();
        return new DeviceSample(
            "thermal_zone_cpu",
            DeviceKind.Cpu,
            "Thermal Zone CPU",
            primary.Value,
            cpuLoad,
            null,
            null,
            now)
        {
            TemperatureSensors = tempSensors,
            PrimaryTemperatureSensorName = primary.SensorName,
        };
    }

    private double? ReadCpuLoadPercent()
    {
        lock (_syncLock)
        {
            if (!File.Exists(_procStatPath))
            {
                return null;
            }

            try
            {
                using var reader = new StreamReader(_procStatPath);
                string? firstLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    return null;
                }

                string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    return null;
                }

                long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
                long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
                long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

                long totalIdle = idle + iowait;
                long totalNonIdle = user + nice + system + irq + softirq + steal;
                long total = totalIdle + totalNonIdle;

                if (_previousCpuStat is null)
                {
                    _previousCpuStat = (total, totalIdle);
                    return null;
                }

                long totalDelta = total - _previousCpuStat.Value.Total;
                long idleDelta = totalIdle - _previousCpuStat.Value.Idle;
                _previousCpuStat = (total, totalIdle);

                if (totalDelta <= 0)
                {
                    return null;
                }

                double load = 100.0 * (totalDelta - idleDelta) / totalDelta;
                return Math.Clamp(Math.Round(load, 1), 0.0, 100.0);
            }
            catch
            {
                return null;
            }
        }
    }

    private static double? ReadMilliDegrees(string filePath)
    {
        string? content = ReadFileString(filePath);
        if (content is not null && double.TryParse(content, CultureInfo.InvariantCulture, out double value))
        {
            // /sys/class/hwmon temp is in millidegrees Celsius (e.g. 45000 = 45°C)
            double celsius = value > 1000 ? value / 1000.0 : value;
            if (celsius is >= -20 and <= 150)
            {
                return Math.Round(celsius, 1);
            }
        }

        return null;
    }

    private static double? ReadFirstMatchingMetric(string dir, string pattern, double divisor)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(dir, pattern);
        }
        catch
        {
            files = [];
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            string? content = ReadFileString(file);
            if (content is not null && double.TryParse(content, CultureInfo.InvariantCulture, out double raw))
            {
                double val = raw / divisor;
                if (val >= 0)
                {
                    return Math.Round(val, 1);
                }
            }
        }

        return null;
    }

    private static string? ReadFileString(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(filePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static DeviceKind ClassifyDevice(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("coretemp") || lower.Contains("k10temp") || lower.Contains("zenpower") ||
            lower.Contains("cpu") || lower.Contains("soc_thermal"))
        {
            return DeviceKind.Cpu;
        }

        if (lower.Contains("amdgpu") || lower.Contains("nouveau") || lower.Contains("nvidia") || lower.Contains("radeon"))
        {
            return DeviceKind.Gpu;
        }

        if (lower.Contains("nvme") || lower.Contains("drivetemp") || lower.Contains("storage") || lower.Contains("disk"))
        {
            return DeviceKind.Storage;
        }

        return DeviceKind.System;
    }

    private static int GetSensorPriority(string sensorName)
    {
        string lower = sensorName.ToLowerInvariant();
        if (lower.Contains("package") || lower.Contains("pkg")) return 100;
        if (lower.Contains("tctl")) return 95;
        if (lower.Contains("tdie")) return 90;
        if (lower.Contains("composite")) return 85;
        if (lower.Contains("edge")) return 80;
        if (lower.Contains("core 0") || lower.Contains("cpu")) return 75;
        return 10;
    }

    private static string FormatDeviceDisplayName(string rawName, DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu when rawName.Contains("coretemp", StringComparison.OrdinalIgnoreCase) => "Intel Core CPU",
        DeviceKind.Cpu when rawName.Contains("k10temp", StringComparison.OrdinalIgnoreCase) || rawName.Contains("zenpower", StringComparison.OrdinalIgnoreCase) => "AMD Ryzen CPU",
        DeviceKind.Gpu when rawName.Contains("amdgpu", StringComparison.OrdinalIgnoreCase) => "AMD Radeon GPU",
        DeviceKind.Gpu when rawName.Contains("nouveau", StringComparison.OrdinalIgnoreCase) || rawName.Contains("nvidia", StringComparison.OrdinalIgnoreCase) => "NVIDIA Dedicated GPU",
        DeviceKind.Storage when rawName.Contains("nvme", StringComparison.OrdinalIgnoreCase) => "NVMe SSD",
        _ => rawName,
    };
}
