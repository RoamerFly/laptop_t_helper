using Xunit;

namespace LaptopThermalHelper.Hardware.Linux.Tests;

public sealed class LinuxSysfsPowerPlanAdapterTests : IDisposable
{
    private readonly string _tempDir;

    public LinuxSysfsPowerPlanAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lth_pwr_test_" + Guid.NewGuid().ToString("N"));
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
    public void DetectAvailableMechanism_IdentifiesIntelPstate()
    {
        string intelDir = Path.Combine(_tempDir, "intel_pstate");
        Directory.CreateDirectory(intelDir);
        File.WriteAllText(Path.Combine(intelDir, "no_turbo"), "0");

        var adapter = new LinuxSysfsPowerPlanAdapter(_tempDir);
        LinuxCoolingMechanism mechanism = adapter.DetectAvailableMechanism();

        Assert.Equal(LinuxCoolingMechanism.IntelPstateNoTurbo, mechanism);
    }

    [Fact]
    public void DetectAvailableMechanism_IdentifiesAmdBoost()
    {
        string amdDir = Path.Combine(_tempDir, "cpufreq");
        Directory.CreateDirectory(amdDir);
        File.WriteAllText(Path.Combine(amdDir, "boost"), "1");

        var adapter = new LinuxSysfsPowerPlanAdapter(_tempDir);
        LinuxCoolingMechanism mechanism = adapter.DetectAvailableMechanism();

        Assert.Equal(LinuxCoolingMechanism.AmdCpufreqBoost, mechanism);
    }

    [Fact]
    public async Task IntelNoTurbo_CaptureApplyRestore_WorksCorrectly()
    {
        string intelDir = Path.Combine(_tempDir, "intel_pstate");
        Directory.CreateDirectory(intelDir);
        string noTurboFile = Path.Combine(intelDir, "no_turbo");
        await File.WriteAllTextAsync(noTurboFile, "0");

        var adapter = new LinuxSysfsPowerPlanAdapter(_tempDir);
        LinuxPowerPlanSnapshot snapshot = await adapter.CaptureAsync();

        Assert.Equal(LinuxCoolingMechanism.IntelPstateNoTurbo, snapshot.Mechanism);
        Assert.Equal("0", snapshot.OriginalValue);

        // Apply cooling: writes "1" to no_turbo
        await adapter.ApplyConservativeLimitAsync(snapshot, 90);
        string appliedVal = (await File.ReadAllTextAsync(noTurboFile)).Trim();
        Assert.Equal("1", appliedVal);

        // Restore: writes "0" back
        await adapter.RestoreAsync(snapshot);
        string restoredVal = (await File.ReadAllTextAsync(noTurboFile)).Trim();
        Assert.Equal("0", restoredVal);
    }

    [Fact]
    public async Task AmdBoost_CaptureApplyRestore_WorksCorrectly()
    {
        string amdDir = Path.Combine(_tempDir, "cpufreq");
        Directory.CreateDirectory(amdDir);
        string boostFile = Path.Combine(amdDir, "boost");
        await File.WriteAllTextAsync(boostFile, "1");

        var adapter = new LinuxSysfsPowerPlanAdapter(_tempDir);
        LinuxPowerPlanSnapshot snapshot = await adapter.CaptureAsync();

        Assert.Equal(LinuxCoolingMechanism.AmdCpufreqBoost, snapshot.Mechanism);
        Assert.Equal("1", snapshot.OriginalValue);

        // Apply cooling: writes "0" to boost
        await adapter.ApplyConservativeLimitAsync(snapshot, 90);
        string appliedVal = (await File.ReadAllTextAsync(boostFile)).Trim();
        Assert.Equal("0", appliedVal);

        // Restore: writes "1" back
        await adapter.RestoreAsync(snapshot);
        string restoredVal = (await File.ReadAllTextAsync(boostFile)).Trim();
        Assert.Equal("1", restoredVal);
    }
}
