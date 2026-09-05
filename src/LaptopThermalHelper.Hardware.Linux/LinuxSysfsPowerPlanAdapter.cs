using System.Globalization;

namespace LaptopThermalHelper.Hardware.Linux;

public enum LinuxCoolingMechanism
{
    None,
    IntelPstateNoTurbo,
    AmdCpufreqBoost,
    ScalingMaxFreq,
}

public sealed record LinuxPowerPlanSnapshot(
    LinuxCoolingMechanism Mechanism,
    string ControlPath,
    string OriginalValue);

public sealed class LinuxSysfsPowerPlanAdapter
{
    private readonly string _sysCpuPath;

    public LinuxSysfsPowerPlanAdapter(string sysCpuPath = "/sys/devices/system/cpu")
    {
        _sysCpuPath = sysCpuPath;
    }

    public LinuxCoolingMechanism DetectAvailableMechanism()
    {
        string intelNoTurbo = Path.Combine(_sysCpuPath, "intel_pstate", "no_turbo");
        if (File.Exists(intelNoTurbo))
        {
            return LinuxCoolingMechanism.IntelPstateNoTurbo;
        }

        string amdBoost = Path.Combine(_sysCpuPath, "cpufreq", "boost");
        if (File.Exists(amdBoost))
        {
            return LinuxCoolingMechanism.AmdCpufreqBoost;
        }

        string cpu0MaxFreq = Path.Combine(_sysCpuPath, "cpu0", "cpufreq", "scaling_max_freq");
        if (File.Exists(cpu0MaxFreq))
        {
            return LinuxCoolingMechanism.ScalingMaxFreq;
        }

        return LinuxCoolingMechanism.None;
    }

    public async Task<LinuxPowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LinuxCoolingMechanism mechanism = DetectAvailableMechanism();

        switch (mechanism)
        {
            case LinuxCoolingMechanism.IntelPstateNoTurbo:
            {
                string path = Path.Combine(_sysCpuPath, "intel_pstate", "no_turbo");
                string value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
                return new LinuxPowerPlanSnapshot(mechanism, path, value);
            }
            case LinuxCoolingMechanism.AmdCpufreqBoost:
            {
                string path = Path.Combine(_sysCpuPath, "cpufreq", "boost");
                string value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
                return new LinuxPowerPlanSnapshot(mechanism, path, value);
            }
            case LinuxCoolingMechanism.ScalingMaxFreq:
            {
                string path = Path.Combine(_sysCpuPath, "cpu0", "cpufreq", "scaling_max_freq");
                string value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
                return new LinuxPowerPlanSnapshot(mechanism, path, value);
            }
            default:
                return new LinuxPowerPlanSnapshot(LinuxCoolingMechanism.None, string.Empty, string.Empty);
        }
    }

    public async Task ApplyConservativeLimitAsync(
        LinuxPowerPlanSnapshot snapshot,
        int processorLimitPercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.Mechanism == LinuxCoolingMechanism.None || string.IsNullOrWhiteSpace(snapshot.ControlPath))
        {
            return;
        }

        switch (snapshot.Mechanism)
        {
            case LinuxCoolingMechanism.IntelPstateNoTurbo:
                // Writing "1" disables Intel Turbo Boost, capping at base clock
                await WriteSysfsAsync(snapshot.ControlPath, "1", cancellationToken).ConfigureAwait(false);
                break;

            case LinuxCoolingMechanism.AmdCpufreqBoost:
                // Writing "0" disables AMD Core Performance Boost
                await WriteSysfsAsync(snapshot.ControlPath, "0", cancellationToken).ConfigureAwait(false);
                break;

            case LinuxCoolingMechanism.ScalingMaxFreq:
                // Scale down max frequency across all available CPU cores
                await ApplyFrequencyLimitAsync(processorLimitPercent, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public async Task RestoreAsync(
        LinuxPowerPlanSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.Mechanism == LinuxCoolingMechanism.None || string.IsNullOrWhiteSpace(snapshot.ControlPath))
        {
            return;
        }

        switch (snapshot.Mechanism)
        {
            case LinuxCoolingMechanism.IntelPstateNoTurbo:
            case LinuxCoolingMechanism.AmdCpufreqBoost:
                await WriteSysfsAsync(snapshot.ControlPath, snapshot.OriginalValue, cancellationToken).ConfigureAwait(false);
                break;

            case LinuxCoolingMechanism.ScalingMaxFreq:
                await RestoreAllCpuFrequenciesAsync(snapshot.OriginalValue, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ApplyFrequencyLimitAsync(int percent, CancellationToken cancellationToken)
    {
        int clampedPercent = Math.Clamp(percent, 50, 99);
        string[] cpuDirs = GetCpuCoreDirectories();

        foreach (string cpuDir in cpuDirs)
        {
            string infoMaxPath = Path.Combine(cpuDir, "cpufreq", "cpuinfo_max_freq");
            string scalingMaxPath = Path.Combine(cpuDir, "cpufreq", "scaling_max_freq");

            if (File.Exists(infoMaxPath) && File.Exists(scalingMaxPath))
            {
                string infoMaxStr = (await File.ReadAllTextAsync(infoMaxPath, cancellationToken).ConfigureAwait(false)).Trim();
                if (long.TryParse(infoMaxStr, CultureInfo.InvariantCulture, out long maxKhz))
                {
                    long targetKhz = (long)(maxKhz * (clampedPercent / 100.0));
                    await WriteSysfsAsync(scalingMaxPath, targetKhz.ToString(CultureInfo.InvariantCulture), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RestoreAllCpuFrequenciesAsync(string originalValue, CancellationToken cancellationToken)
    {
        string[] cpuDirs = GetCpuCoreDirectories();
        foreach (string cpuDir in cpuDirs)
        {
            string scalingMaxPath = Path.Combine(cpuDir, "cpufreq", "scaling_max_freq");
            if (File.Exists(scalingMaxPath))
            {
                await WriteSysfsAsync(scalingMaxPath, originalValue, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string[] GetCpuCoreDirectories()
    {
        try
        {
            return Directory.GetDirectories(_sysCpuPath, "cpu[0-9]*");
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteSysfsAsync(string path, string content, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }
}
