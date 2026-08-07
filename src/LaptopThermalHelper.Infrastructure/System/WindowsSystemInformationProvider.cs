using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using LaptopThermalHelper.Application.System;
using LaptopThermalHelper.Core.Domain;
using Microsoft.Win32;

namespace LaptopThermalHelper.Infrastructure.Platform;

/// <summary>
/// Windows-only, read-only metadata provider. WMI is intentionally confined to
/// a background task with both the WMI and caller timeouts applied, so a slow
/// provider can never stall the WPF dispatcher.
/// </summary>
public sealed partial class WindowsSystemInformationProvider : ISystemInformationProvider
{
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _queryTimeout;

    public WindowsSystemInformationProvider(
        TimeProvider? timeProvider = null,
        TimeSpan? queryTimeout = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _queryTimeout = queryTimeout ?? DefaultQueryTimeout;
        if (_queryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(queryTimeout));
        }
    }

    public async Task<SystemInformationSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset retrievedAt = _timeProvider.GetUtcNow();
        if (!OperatingSystem.IsWindows())
        {
            return SystemInformationSnapshot.Unavailable(retrievedAt, "当前运行环境不是 Windows。");
        }

        var diagnostics = new List<string>();
        ComputerSystemRawData? computer = await TryQueryAsync(
            () => QueryComputerSystem(_queryTimeout),
            "无法读取设备制造商或型号",
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        SystemPowerStatusRawData? powerStatus = await TryQueryAsync(
            ReadPowerStatus,
            "无法读取电池或外接电源状态",
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        PowerPlanRawData? powerPlan = await TryQueryAsync(
            ReadPowerPlan,
            "无法读取当前 Windows 电源计划",
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        string? operatingSystem = await TryQueryAsync(
            ReadOperatingSystem,
            "无法读取 Windows 版本",
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        return WindowsSystemInformationMapper.Map(
            new WindowsSystemInformationRawData(
                operatingSystem,
                computer?.Manufacturer,
                computer?.Model,
                powerStatus,
                powerPlan,
                diagnostics),
            retrievedAt);
    }

    private async Task<T?> TryQueryAsync<T>(
        Func<T> operation,
        string unavailableMessage,
        List<string> diagnostics,
        CancellationToken cancellationToken)
        where T : class?
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_queryTimeout);
            return await Task.Run(operation, timeout.Token)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add($"{unavailableMessage}（查询超时）");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(unavailableMessage);
        }

        return null;
    }

    private static ComputerSystemRawData QueryComputerSystem(TimeSpan timeout)
    {
        var options = new global::System.Management.EnumerationOptions
        {
            ReturnImmediately = true,
            Rewindable = false,
            Timeout = timeout,
        };
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope("root\\CIMV2"),
            new ObjectQuery("SELECT Manufacturer, Model FROM Win32_ComputerSystem"),
            options);
        using ManagementObjectCollection results = searcher.Get();
        ManagementBaseObject? result = results.Cast<ManagementBaseObject>().FirstOrDefault();
        return new ComputerSystemRawData(
            Convert.ToString(result?["Manufacturer"], CultureInfo.InvariantCulture),
            Convert.ToString(result?["Model"], CultureInfo.InvariantCulture));
    }

    private static SystemPowerStatusRawData? ReadPowerStatus()
    {
        return GetSystemPowerStatus(out SystemPowerStatus status)
            ? new SystemPowerStatusRawData(
                status.AcLineStatus,
                status.BatteryFlag,
                status.BatteryLifePercent,
                status.BatteryLifeTime)
            : null;
    }

    private static PowerPlanRawData? ReadPowerPlan()
    {
        const uint ErrorSuccess = 0;
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr rawGuid) != ErrorSuccess || rawGuid == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            Guid identifier = Marshal.PtrToStructure<Guid>(rawGuid);
            return new PowerPlanRawData(identifier, ReadPowerPlanFriendlyName(identifier));
        }
        finally
        {
            _ = LocalFree(rawGuid);
        }
    }

    private static string? ReadPowerPlanFriendlyName(Guid identifier)
    {
        const uint ErrorSuccess = 0;
        const uint ErrorMoreData = 234;
        uint byteCount = 0;
        uint status = PowerReadFriendlyName(
            IntPtr.Zero,
            ref identifier,
            IntPtr.Zero,
            IntPtr.Zero,
            null,
            ref byteCount);
        if (status != ErrorMoreData || byteCount == 0)
        {
            return null;
        }

        var buffer = new byte[byteCount];
        status = PowerReadFriendlyName(
            IntPtr.Zero,
            ref identifier,
            IntPtr.Zero,
            IntPtr.Zero,
            buffer,
            ref byteCount);
        return status == ErrorSuccess
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static string? ReadOperatingSystem()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            writable: false);
        string? productName = key?.GetValue("ProductName") as string;
        string? displayVersion = key?.GetValue("DisplayVersion") as string
            ?? key?.GetValue("ReleaseId") as string;
        return string.Join(
            ' ',
            new[] { productName, displayVersion }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [LibraryImport("PowrProf.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("PowrProf.dll")]
    private static partial uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private sealed record ComputerSystemRawData(string? Manufacturer, string? Model);
}

public sealed record SystemPowerStatusRawData(
    byte AcLineStatus,
    byte BatteryFlag,
    byte BatteryLifePercent,
    uint BatteryLifeTime);

public sealed record PowerPlanRawData(Guid Identifier, string? DisplayName);

public sealed record WindowsSystemInformationRawData(
    string? OperatingSystem,
    string? Manufacturer,
    string? Model,
    SystemPowerStatusRawData? PowerStatus,
    PowerPlanRawData? PowerPlan,
    IReadOnlyCollection<string> Diagnostics);

/// <summary>Pure mapping logic kept public for deterministic tests.</summary>
public static class WindowsSystemInformationMapper
{
    private const byte UnknownByte = byte.MaxValue;
    private const byte NoSystemBatteryFlag = 128;
    private const byte ChargingFlag = 8;

    public static SystemInformationSnapshot Map(
        WindowsSystemInformationRawData source,
        DateTimeOffset retrievedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        string operatingSystem = ValueOrUnavailable(source.OperatingSystem);
        string manufacturer = ValueOrUnavailable(source.Manufacturer);
        string model = ValueOrUnavailable(source.Model);
        BatteryInformation battery = MapBattery(source.PowerStatus);
        PowerSourceKind powerSource = MapPowerSource(source.PowerStatus?.AcLineStatus);
        PowerPlanInformation powerPlan = new(
            source.PowerPlan?.Identifier,
            ValueOrUnavailable(source.PowerPlan?.DisplayName));

        bool hasAnyInformation =
            operatingSystem != SystemInformationSnapshot.UnavailableText ||
            manufacturer != SystemInformationSnapshot.UnavailableText ||
            model != SystemInformationSnapshot.UnavailableText ||
            source.PowerStatus is not null ||
            source.PowerPlan is not null;
        bool hasMissingInformation =
            operatingSystem == SystemInformationSnapshot.UnavailableText ||
            manufacturer == SystemInformationSnapshot.UnavailableText ||
            model == SystemInformationSnapshot.UnavailableText ||
            source.PowerStatus is null ||
            source.PowerPlan is null ||
            powerPlan.DisplayName == SystemInformationSnapshot.UnavailableText ||
            source.Diagnostics.Count > 0;
        SystemInformationAvailability availability = !hasAnyInformation
            ? SystemInformationAvailability.Unavailable
            : hasMissingInformation
                ? SystemInformationAvailability.Partial
                : SystemInformationAvailability.Ready;

        return new SystemInformationSnapshot(
            operatingSystem,
            manufacturer,
            model,
            battery,
            powerSource,
            powerPlan,
            availability,
            source.Diagnostics.Count == 0 ? null : string.Join("；", source.Diagnostics),
            retrievedAt);
    }

    public static BatteryInformation MapBattery(SystemPowerStatusRawData? status)
    {
        if (status is null)
        {
            return new BatteryInformation(BatteryChargeState.Unknown, null, null);
        }

        if ((status.BatteryFlag & NoSystemBatteryFlag) != 0)
        {
            return new BatteryInformation(BatteryChargeState.NotPresent, null, null);
        }

        int? percentage = status.BatteryLifePercent == UnknownByte
            ? null
            : status.BatteryLifePercent;
        TimeSpan? remaining = status.BatteryLifeTime == uint.MaxValue
            ? null
            : TimeSpan.FromSeconds(status.BatteryLifeTime);
        BatteryChargeState state = (status.BatteryFlag & ChargingFlag) != 0
            ? BatteryChargeState.Charging
            : percentage == 100
                ? BatteryChargeState.Full
                : status.AcLineStatus == 0
                    ? BatteryChargeState.Discharging
                    : BatteryChargeState.Unknown;
        return new BatteryInformation(state, percentage, remaining);
    }

    public static PowerSourceKind MapPowerSource(byte? acLineStatus) => acLineStatus switch
    {
        0 => PowerSourceKind.Battery,
        1 => PowerSourceKind.Ac,
        _ => PowerSourceKind.Unknown,
    };

    private static string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? SystemInformationSnapshot.UnavailableText : value.Trim();
}
