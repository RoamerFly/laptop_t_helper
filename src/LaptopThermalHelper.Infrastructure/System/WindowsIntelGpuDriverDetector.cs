using System.Globalization;
using System.Management;
using LaptopThermalHelper.Application.System;

namespace LaptopThermalHelper.Infrastructure.Platform;

/// <summary>
/// Windows-only Intel GPU driver detector using WMI to query
/// Win32_VideoController for Intel display adapters and their
/// driver version/date.
/// </summary>
public sealed class WindowsIntelGpuDriverDetector : IIntelGpuDriverDetector
{
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(3);

    public async Task<IntelGpuDriverInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntelGpuDriverInfo.NotPresent();
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DefaultQueryTimeout);
            return await Task.Run(QueryIntelGpuDriver, timeoutCts.Token)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return IntelGpuDriverInfo.Unknown("无法检测 Intel 核显驱动版本，建议手动检查驱动更新。");
        }
    }

    private static IntelGpuDriverInfo QueryIntelGpuDriver()
    {
        var options = new global::System.Management.EnumerationOptions
        {
            ReturnImmediately = true,
            Rewindable = false,
            Timeout = DefaultQueryTimeout,
        };

        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"root\CIMV2"),
            new ObjectQuery(
                "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"),
            options);

        using ManagementObjectCollection results = searcher.Get();

        ManagementBaseObject? intelGpu = null;

        foreach (ManagementBaseObject item in results)
        {
            string? name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture);
            if (name is not null &&
                name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
            {
                intelGpu = item;
                break;
            }
        }

        if (intelGpu is null)
        {
            return IntelGpuDriverInfo.NotPresent();
        }

        string? driverVersion = Convert.ToString(
            intelGpu["DriverVersion"], CultureInfo.InvariantCulture);

        DateTime? driverDate = ParseDriverDate(
            Convert.ToString(intelGpu["DriverDate"], CultureInfo.InvariantCulture));

        bool isTooOld = IsDriverTooOld(driverVersion, driverDate);

        string summary = isTooOld
            ? $"Intel 核显驱动版本 {driverVersion} 过旧（{FormatDate(driverDate)}），导致核显温度无法读取。请更新到 {IntelGpuDriverInfo.MinimumRecommendedVersion} 或更高版本。"
            : $"Intel 核显驱动版本 {driverVersion}（{FormatDate(driverDate)}），版本满足要求。";

        return new IntelGpuDriverInfo(
            isTooOld ? IntelGpuDriverState.TooOld : IntelGpuDriverState.Ok,
            driverVersion,
            driverDate,
            summary);
    }

    /// <summary>
    /// WMI DriverDate is stored as a string like "20220606000000.000000-000"
    /// (YYYYMMDDHHMMSS.ffffff±UTC). Extract the first 8 digits as YYYYMMDD.
    /// </summary>
    private static DateTime? ParseDriverDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate) || rawDate.Length < 8)
        {
            return null;
        }

        string datePart = rawDate.Replace(".", "").TrimStart();
        if (datePart.Length < 8 ||
            !int.TryParse(datePart.AsSpan(0, 4), out int year) ||
            !int.TryParse(datePart.AsSpan(4, 2), out int month) ||
            !int.TryParse(datePart.AsSpan(6, 2), out int day))
        {
            return null;
        }

        try
        {
            return new DateTime(year, month, 1).AddDays(day - 1);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDriverTooOld(string? version, DateTime? date)
    {
        // If we can't determine version or date, assume not too old
        // (let the temperature reading failure be the signal).
        if (date is null && string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        // Check by date: if the driver date is before the minimum, it's too old.
        if (date is DateTime validDate)
        {
            return validDate < IntelGpuDriverInfo.MinimumDriverDate;
        }

        // Fallback: check by version string comparison.
        return IsVersionTooOld(version);
    }

    private static bool IsVersionTooOld(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        // Intel driver versions look like "31.0.101.1999".
        // The last number is the build; 2127+ is required.
        string[] parts = version.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[^1], out int build))
        {
            return build < 2127;
        }

        return false;
    }

    private static string FormatDate(DateTime? date) =>
        date is DateTime validDate
            ? validDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : "未知日期";
}
