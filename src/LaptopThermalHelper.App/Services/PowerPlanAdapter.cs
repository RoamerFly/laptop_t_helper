using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LaptopThermalHelper.App.Services;

public sealed record PowerPlanSnapshot(
    string SchemeGuid,
    int AcMaximumProcessorStatePercent,
    int DcMaximumProcessorStatePercent);

public interface IPowerPlanAdapter
{
    bool IsDryRun { get; }

    Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default);

    Task ApplyConservativeLimitAsync(
        PowerPlanSnapshot snapshot,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(PowerPlanSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the narrowly scoped public Windows processor-power APIs used by the
/// adapter. Implementations must not call OEM, EC, BIOS, or fan-control interfaces.
/// </summary>
public interface IProcessorPowerPlanApi
{
    Task<string> GetActiveSchemeGuidAsync(CancellationToken cancellationToken = default);

    Task<ProcessorMaximumState> ReadMaximumProcessorStateAsync(
        string schemeGuid,
        CancellationToken cancellationToken = default);

    Task WriteAcMaximumProcessorStateAsync(
        string schemeGuid,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default);

    Task WriteDcMaximumProcessorStateAsync(
        string schemeGuid,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Originally called <c>PowerSetActiveScheme</c> to force the written
    /// processor-state values to take effect. However, that API re-applies
    /// <em>all</em> power-settings sub-groups—including display brightness—
    /// which caused the screen to flicker between brightness levels every
    /// time auto-cooling applied or restored a limit.
    /// <para>
    /// <c>PowerWriteACValueIndex</c> / <c>PowerWriteDCValueIndex</c> already
    /// apply to the active scheme and take effect immediately, so this call
    /// is now a no-op that only validates the GUID.
    /// </para>
    /// </summary>
    Task ReapplyActiveSchemeAsync(string schemeGuid, CancellationToken cancellationToken = default);
}

public sealed record ProcessorMaximumState(int AcPercent, int DcPercent);

/// <summary>
/// Raised when a user or Windows changes the active power plan after a snapshot was
/// captured. The caller must retain the snapshot and never switch the user back.
/// </summary>
public sealed class ActivePowerPlanChangedException : InvalidOperationException
{
    public ActivePowerPlanChangedException(string capturedSchemeGuid, string activeSchemeGuid)
        : base($"当前活动电源计划已从 {capturedSchemeGuid} 更改为 {activeSchemeGuid}；为避免覆盖用户选择，未写入或恢复原计划设置。")
    {
    }
}

/// <summary>
/// Limits only the public Windows processor maximum-state setting on the active
/// scheme. It never invokes EC, BIOS, OEM utilities, fan control, or elevation.
/// </summary>
public sealed class PowerCfgPowerPlanAdapter : IPowerPlanAdapter
{
    private readonly IProcessorPowerPlanApi _powerPlanApi;

    public PowerCfgPowerPlanAdapter()
        : this(new WindowsProcessorPowerPlanApi())
    {
    }

    public PowerCfgPowerPlanAdapter(IProcessorPowerPlanApi powerPlanApi)
    {
        _powerPlanApi = powerPlanApi ?? throw new ArgumentNullException(nameof(powerPlanApi));
    }

    public bool IsDryRun => false;

    public async Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        string schemeGuid = NormalizeSchemeGuid(
            await _powerPlanApi.GetActiveSchemeGuidAsync(cancellationToken).ConfigureAwait(false));
        ProcessorMaximumState values = await _powerPlanApi
            .ReadMaximumProcessorStateAsync(schemeGuid, cancellationToken)
            .ConfigureAwait(false);
        return new PowerPlanSnapshot(schemeGuid, values.AcPercent, values.DcPercent);
    }

    public async Task ApplyConservativeLimitAsync(
        PowerPlanSnapshot snapshot,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int cappedPercent = Math.Clamp(maximumProcessorStatePercent, 50, 99);

        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi
            .WriteAcMaximumProcessorStateAsync(snapshot.SchemeGuid, cappedPercent, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi
            .WriteDcMaximumProcessorStateAsync(snapshot.SchemeGuid, cappedPercent, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi.ReapplyActiveSchemeAsync(snapshot.SchemeGuid, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(PowerPlanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Never reactivate a previously captured plan after the user selected another one.
        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi
            .WriteAcMaximumProcessorStateAsync(
                snapshot.SchemeGuid,
                snapshot.AcMaximumProcessorStatePercent,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi
            .WriteDcMaximumProcessorStateAsync(
                snapshot.SchemeGuid,
                snapshot.DcMaximumProcessorStatePercent,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSnapshotSchemeIsStillActiveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _powerPlanApi.ReapplyActiveSchemeAsync(snapshot.SchemeGuid, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSnapshotSchemeIsStillActiveAsync(
        PowerPlanSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string capturedSchemeGuid = NormalizeSchemeGuid(snapshot.SchemeGuid);
        string activeSchemeGuid = NormalizeSchemeGuid(
            await _powerPlanApi.GetActiveSchemeGuidAsync(cancellationToken).ConfigureAwait(false));
        if (!string.Equals(capturedSchemeGuid, activeSchemeGuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActivePowerPlanChangedException(capturedSchemeGuid, activeSchemeGuid);
        }
    }

    private static string NormalizeSchemeGuid(string schemeGuid)
    {
        if (!Guid.TryParse(schemeGuid, out Guid parsed))
        {
            throw new InvalidOperationException("无法识别 Windows 电源计划 GUID。");
        }

        return parsed.ToString("D");
    }
}

/// <summary>
/// Uses PowrProf's public read/write APIs when present. The strictly parsed,
/// full-path powercfg fallback exists only for platforms where those entry points
/// are unavailable.
/// </summary>
public sealed class WindowsProcessorPowerPlanApi : IProcessorPowerPlanApi
{
    private static readonly Guid ProcessorSubGroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorMaximumState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private readonly IPowerCfgCommandRunner _powerCfgCommandRunner;
    private bool _usePowerCfgFallback;

    public WindowsProcessorPowerPlanApi()
        : this(new WindowsPowerCfgCommandRunner())
    {
    }

    public WindowsProcessorPowerPlanApi(IPowerCfgCommandRunner powerCfgCommandRunner)
    {
        _powerCfgCommandRunner = powerCfgCommandRunner ?? throw new ArgumentNullException(nameof(powerCfgCommandRunner));
    }

    public async Task<string> GetActiveSchemeGuidAsync(CancellationToken cancellationToken = default)
    {
        if (!await ShouldUsePowerCfgFallbackAsync(cancellationToken).ConfigureAwait(false))
        {
            return GetActiveSchemeGuidWithPowrProf();
        }

        PowerCfgCommandResult result = await _powerCfgCommandRunner
            .RunAsync(["/getactivescheme"], cancellationToken)
            .ConfigureAwait(false);
        Match match = PowerCfgProcessorStateParser.SchemeGuidPattern.Match(result.StandardOutput);
        if (!match.Success)
        {
            throw new InvalidOperationException("无法从 powercfg 输出中识别当前电源计划。");
        }

        return new Guid(match.Value).ToString("D");
    }

    public async Task<ProcessorMaximumState> ReadMaximumProcessorStateAsync(
        string schemeGuid,
        CancellationToken cancellationToken = default)
    {
        Guid parsedSchemeGuid = ParseSchemeGuid(schemeGuid);
        if (!await ShouldUsePowerCfgFallbackAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ProcessorMaximumState(
                ReadAcMaximumProcessorStateWithPowrProf(parsedSchemeGuid),
                ReadDcMaximumProcessorStateWithPowrProf(parsedSchemeGuid));
        }

        PowerCfgCommandResult result = await _powerCfgCommandRunner
            .RunAsync(
                ["/query", parsedSchemeGuid.ToString("D"), "SUB_PROCESSOR", "PROCTHROTTLEMAX"],
                cancellationToken)
            .ConfigureAwait(false);
        return PowerCfgProcessorStateParser.Parse(result.StandardOutput);
    }

    public async Task WriteAcMaximumProcessorStateAsync(
        string schemeGuid,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default)
    {
        Guid parsedSchemeGuid = ParseSchemeGuid(schemeGuid);
        uint value = checked((uint)Math.Clamp(maximumProcessorStatePercent, 0, 100));
        if (!await ShouldUsePowerCfgFallbackAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid processorSubGroup = ProcessorSubGroup;
            Guid processorMaximumState = ProcessorMaximumState;
            ThrowIfPowerStatusFailed(
                NativeMethods.PowerWriteACValueIndex(
                    IntPtr.Zero,
                    ref parsedSchemeGuid,
                    ref processorSubGroup,
                    ref processorMaximumState,
                    value),
                "写入当前电源计划的交流处理器最大状态");
            return;
        }

        await _powerCfgCommandRunner
            .RunAsync(
                [
                    "/setacvalueindex",
                    parsedSchemeGuid.ToString("D"),
                    "SUB_PROCESSOR",
                    "PROCTHROTTLEMAX",
                    value.ToString(CultureInfo.InvariantCulture),
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WriteDcMaximumProcessorStateAsync(
        string schemeGuid,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default)
    {
        Guid parsedSchemeGuid = ParseSchemeGuid(schemeGuid);
        uint value = checked((uint)Math.Clamp(maximumProcessorStatePercent, 0, 100));
        if (!await ShouldUsePowerCfgFallbackAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid processorSubGroup = ProcessorSubGroup;
            Guid processorMaximumState = ProcessorMaximumState;
            ThrowIfPowerStatusFailed(
                NativeMethods.PowerWriteDCValueIndex(
                    IntPtr.Zero,
                    ref parsedSchemeGuid,
                    ref processorSubGroup,
                    ref processorMaximumState,
                    value),
                "写入当前电源计划的直流处理器最大状态");
            return;
        }

        await _powerCfgCommandRunner
            .RunAsync(
                [
                    "/setdcvalueindex",
                    parsedSchemeGuid.ToString("D"),
                    "SUB_PROCESSOR",
                    "PROCTHROTTLEMAX",
                    value.ToString(CultureInfo.InvariantCulture),
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReapplyActiveSchemeAsync(string schemeGuid, CancellationToken cancellationToken = default)
    {
        // PowerWriteACValueIndex / PowerWriteDCValueIndex already apply to the
        // active scheme and take effect immediately. Previously this method
        // called PowerSetActiveScheme to "force" the new values, but that API
        // re-applies *all* power-settings sub-groups (display brightness,
        // adaptive brightness, etc.), which caused the screen to flicker.
        // The scheme GUID is still validated so that a stale snapshot is
        // detected early, but no system call is made.
        await Task.Run(() => ParseSchemeGuid(schemeGuid), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ShouldUsePowerCfgFallbackAsync(CancellationToken cancellationToken)
    {
        if (_usePowerCfgFallback)
        {
            return true;
        }

        try
        {
            _ = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeSchemePointer);
            if (activeSchemePointer != IntPtr.Zero)
            {
                _ = NativeMethods.LocalFree(activeSchemePointer);
            }

            return false;
        }
        catch (Exception exception) when (IsPowrProfUnavailable(exception))
        {
            _usePowerCfgFallback = true;
            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }
    }

    private static string GetActiveSchemeGuidWithPowrProf()
    {
        ThrowIfPowerStatusFailed(
            NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeSchemePointer),
            "读取当前活动电源计划");
        if (activeSchemePointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows 未返回当前活动电源计划。");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(activeSchemePointer).ToString("D");
        }
        finally
        {
            _ = NativeMethods.LocalFree(activeSchemePointer);
        }
    }

    private static int ReadAcMaximumProcessorStateWithPowrProf(Guid schemeGuid)
    {
        Guid processorSubGroup = ProcessorSubGroup;
        Guid processorMaximumState = ProcessorMaximumState;
        ThrowIfPowerStatusFailed(
            NativeMethods.PowerReadACValueIndex(
                IntPtr.Zero,
                ref schemeGuid,
                ref processorSubGroup,
                ref processorMaximumState,
                out uint value),
            "读取当前电源计划的交流处理器最大状态");
        return ValidateProcessorMaximumState(value);
    }

    private static int ReadDcMaximumProcessorStateWithPowrProf(Guid schemeGuid)
    {
        Guid processorSubGroup = ProcessorSubGroup;
        Guid processorMaximumState = ProcessorMaximumState;
        ThrowIfPowerStatusFailed(
            NativeMethods.PowerReadDCValueIndex(
                IntPtr.Zero,
                ref schemeGuid,
                ref processorSubGroup,
                ref processorMaximumState,
                out uint value),
            "读取当前电源计划的直流处理器最大状态");
        return ValidateProcessorMaximumState(value);
    }

    private static Guid ParseSchemeGuid(string schemeGuid)
    {
        if (!Guid.TryParse(schemeGuid, out Guid parsed))
        {
            throw new InvalidOperationException("无法识别 Windows 电源计划 GUID。");
        }

        return parsed;
    }

    private static int ValidateProcessorMaximumState(uint value)
    {
        if (value > 100)
        {
            throw new InvalidOperationException("Windows 返回的处理器最大状态不在 0 到 100 范围内。");
        }

        return checked((int)value);
    }

    private static void ThrowIfPowerStatusFailed(uint result, string operation)
    {
        if (result != 0)
        {
            throw new Win32Exception(checked((int)result), $"{operation}失败。");
        }
    }

    private static bool IsPowrProfUnavailable(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or PlatformNotSupportedException;

    private static class NativeMethods
    {
        [DllImport("PowrProf.dll")]
        internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("PowrProf.dll")]
        internal static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("PowrProf.dll")]
        internal static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("PowrProf.dll")]
        internal static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("PowrProf.dll")]
        internal static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}

public sealed record PowerCfgCommandResult(string StandardOutput, string StandardError);

public interface IPowerCfgCommandRunner
{
    Task<PowerCfgCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process fallback for older/limited PowrProf environments. The executable path is
/// fixed to %SystemRoot%\System32\powercfg.exe; no shell expansion or elevation is used.
/// </summary>
public sealed class WindowsPowerCfgCommandRunner : IPowerCfgCommandRunner
{
    private static readonly string PowerCfgPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "powercfg.exe");

    public async Task<PowerCfgCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PowerCfgPath) || !Path.IsPathFullyQualified(PowerCfgPath))
        {
            throw new InvalidOperationException("无法确定 %SystemRoot%\\System32\\powercfg.exe 的完整路径。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = PowerCfgPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows powercfg 工具。");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string standardOutput = await standardOutputTask.ConfigureAwait(false);
        string standardError = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new InvalidOperationException($"powercfg 执行失败（退出代码 {process.ExitCode}）：{details.Trim()}");
        }

        return new PowerCfgCommandResult(standardOutput, standardError);
    }
}

public static class PowerCfgProcessorStateParser
{
    internal static readonly Regex SchemeGuidPattern = new(
        "(?i)\\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AcValuePattern = new(
        "(?im)^[^\\r\\n]*(?:\\bCurrent\\b|当前)[^\\r\\n]*(?:\\bAC\\b|交流)[^\\r\\n]*?\\b0x(?<value>[0-9a-f]{1,8})\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DcValuePattern = new(
        "(?im)^[^\\r\\n]*(?:\\bCurrent\\b|当前)[^\\r\\n]*(?:\\bDC\\b|直流)[^\\r\\n]*?\\b0x(?<value>[0-9a-f]{1,8})\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ProcessorMaximumState Parse(string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        return new ProcessorMaximumState(
            ParseSingleIndex(AcValuePattern, output, "交流"),
            ParseSingleIndex(DcValuePattern, output, "直流"));
    }

    private static int ParseSingleIndex(Regex pattern, string output, string kind)
    {
        MatchCollection matches = pattern.Matches(output);
        if (matches.Count != 1 || !int.TryParse(
                matches[0].Groups["value"].Value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int value) ||
            value is < 0 or > 100)
        {
            throw new InvalidOperationException($"无法从 powercfg 输出中严格识别当前{kind}处理器最大状态。");
        }

        return value;
    }
}

public sealed class DryRunPowerPlanAdapter : IPowerPlanAdapter
{
    private readonly PowerPlanSnapshot _snapshot;

    public DryRunPowerPlanAdapter(PowerPlanSnapshot? snapshot = null)
    {
        _snapshot = snapshot ?? new PowerPlanSnapshot("00000000-0000-0000-0000-000000000000", 100, 100);
    }

    public bool IsDryRun => true;

    public int ApplyCount { get; private set; }

    public int RestoreCount { get; private set; }

    public Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_snapshot);
    }

    public Task ApplyConservativeLimitAsync(
        PowerPlanSnapshot snapshot,
        int maximumProcessorStatePercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCount++;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(PowerPlanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        RestoreCount++;
        return Task.CompletedTask;
    }
}
