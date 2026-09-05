using System.Diagnostics;
using System.Globalization;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Hardware.Mac;

public sealed class MacHardwareMonitorProvider : IHardwareMonitorProvider
{
    private readonly Func<string, string[], string?> _commandRunner;

    public MacHardwareMonitorProvider()
        : this(RunShellCommand)
    {
    }

    public MacHardwareMonitorProvider(Func<string, string[], string?> commandRunner)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<IReadOnlyList<DeviceSample>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(ReadCore, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private List<DeviceSample> ReadCore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var samples = new List<DeviceSample>();

        string cpuName = _commandRunner("sysctl", ["-n", "machdep.cpu.brand_string"])?.Trim()
            ?? _commandRunner("sysctl", ["-n", "hw.model"])?.Trim()
            ?? "Apple Processor";

        double? cpuTemp = ReadMacCpuTemperature();
        double? cpuLoad = ReadMacCpuLoad();
        double? fanRpm = ReadMacFanRpm();

        var cpuSensors = new List<SensorReading>();
        if (cpuTemp.HasValue)
        {
            cpuSensors.Add(new SensorReading(
                "mac_cpu",
                DeviceKind.Cpu,
                cpuName,
                "mac_cpu_temp",
                "CPU Package",
                SensorMetric.Temperature,
                cpuTemp.Value,
                "°C",
                now,
                ReadingQuality.Good));
        }

        samples.Add(new DeviceSample(
            "mac_cpu",
            DeviceKind.Cpu,
            cpuName,
            cpuTemp,
            cpuLoad,
            null,
            fanRpm,
            now)
        {
            TemperatureSensors = cpuSensors,
            PrimaryTemperatureSensorName = cpuSensors.FirstOrDefault()?.SensorName,
        });

        return samples;
    }

    private double? ReadMacCpuTemperature()
    {
        // Try common macOS thermal sensor utilities: smc, osx-cpu-temp, or powermetrics
        string? output = _commandRunner("osx-cpu-temp", []);
        if (!string.IsNullOrWhiteSpace(output))
        {
            string clean = output.Replace("°C", string.Empty, StringComparison.Ordinal).Trim();
            if (double.TryParse(clean, CultureInfo.InvariantCulture, out double temp))
            {
                return Math.Round(temp, 1);
            }
        }

        // Try smc CLI: smc -k TC0P -r
        string? smcOutput = _commandRunner("smc", ["-k", "TC0P", "-r"]);
        if (!string.IsNullOrWhiteSpace(smcOutput))
        {
            double? parsedSmc = ParseSmcTemperature(smcOutput);
            if (parsedSmc.HasValue)
            {
                return parsedSmc;
            }
        }

        return null;
    }

    private double? ReadMacFanRpm()
    {
        string? smcOutput = _commandRunner("smc", ["-k", "F0Ac", "-r"]);
        if (!string.IsNullOrWhiteSpace(smcOutput))
        {
            return ParseSmcFan(smcOutput);
        }

        return null;
    }

    private double? ReadMacCpuLoad()
    {
        // Sample top for single iteration: top -l 1 -n 0
        string? topOutput = _commandRunner("top", ["-l", "1", "-n", "0"]);
        if (!string.IsNullOrWhiteSpace(topOutput))
        {
            // Format: CPU usage: 3.2% user, 6.4% sys, 90.4% idle
            int idleIdx = topOutput.IndexOf("% idle", StringComparison.OrdinalIgnoreCase);
            if (idleIdx > 0)
            {
                int start = topOutput.LastIndexOf(',', idleIdx);
                if (start >= 0 && start < idleIdx)
                {
                    string idleStr = topOutput.Substring(start + 1, idleIdx - start - 1).Trim();
                    if (double.TryParse(idleStr, CultureInfo.InvariantCulture, out double idlePercent))
                    {
                        return Math.Clamp(Math.Round(100.0 - idlePercent, 1), 0.0, 100.0);
                    }
                }
            }
        }

        return null;
    }

    public static double? ParseSmcTemperature(string smcLine)
    {
        // Example: "  TC0P  [sp78]  45.25 (bytes 2d 40)"
        int bracketIdx = smcLine.IndexOf(']', StringComparison.Ordinal);
        if (bracketIdx > 0)
        {
            string remainder = smcLine[(bracketIdx + 1)..].Trim();
            int parenIdx = remainder.IndexOf('(', StringComparison.Ordinal);
            string valStr = (parenIdx > 0 ? remainder[..parenIdx] : remainder).Trim();
            if (double.TryParse(valStr, CultureInfo.InvariantCulture, out double temp))
            {
                return Math.Round(temp, 1);
            }
        }

        return null;
    }

    public static double? ParseSmcFan(string smcLine)
    {
        int bracketIdx = smcLine.IndexOf(']', StringComparison.Ordinal);
        if (bracketIdx > 0)
        {
            string remainder = smcLine[(bracketIdx + 1)..].Trim();
            int parenIdx = remainder.IndexOf('(', StringComparison.Ordinal);
            string valStr = (parenIdx > 0 ? remainder[..parenIdx] : remainder).Trim();
            if (double.TryParse(valStr, CultureInfo.InvariantCulture, out double rpm))
            {
                return Math.Round(rpm, 0);
            }
        }

        return null;
    }

    private static string? RunShellCommand(string fileName, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(1000))
            {
                process.Kill();
                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
