using System.Globalization;
using System.Text;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Infrastructure.History;

public sealed class CsvTemperatureHistoryStore : ITemperatureHistoryStore, IDisposable
{
    private const string Header =
        "timestamp,device_id,device_kind,device_name,temperature_c,load_percent,power_w,fan_rpm,thermal_level";
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly string _dataRoot;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public CsvTemperatureHistoryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoamerFly",
            "LaptopThermalHelper"))
    {
    }

    public CsvTemperatureHistoryStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
    }

    public async Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Devices.Count == 0)
        {
            return;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string historyDirectory = Path.Combine(_dataRoot, "history");
            Directory.CreateDirectory(historyDirectory);
            string path = Path.Combine(
                historyDirectory,
                $"temperature-{snapshot.Timestamp.ToLocalTime():yyyyMMdd}.csv");

            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            bool writeHeader = stream.Length == 0;
            await using var writer = new StreamWriter(stream, Utf8WithoutBom);

            if (writeHeader)
            {
                await writer.WriteLineAsync(Header.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            foreach (MonitoredDeviceSnapshot monitoredDevice in snapshot.Devices)
            {
                string line = FormatRecord(monitoredDevice.Device);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<HistoryExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string historyDirectory = Path.Combine(_dataRoot, "history");
            if (!Directory.Exists(historyDirectory))
            {
                return HistoryExportResult.Empty;
            }

            string[] sourceFiles = Directory
                .EnumerateFiles(historyDirectory, "temperature-*.csv", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (sourceFiles.Length == 0)
            {
                return HistoryExportResult.Empty;
            }

            string exportDirectory = Path.Combine(_dataRoot, "exports");
            Directory.CreateDirectory(exportDirectory);
            string exportPath = Path.Combine(
                exportDirectory,
                $"temperature-history-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.csv");
            string temporaryPath = exportPath + $".{Guid.NewGuid():N}.tmp";
            int recordCount = 0;

            try
            {
                await using (var output = new StreamWriter(temporaryPath, false, Utf8WithoutBom))
                {
                    await output.WriteLineAsync(Header.AsMemory(), cancellationToken).ConfigureAwait(false);
                    foreach (string sourceFile in sourceFiles)
                    {
                        recordCount += await AppendSourceRowsAsync(sourceFile, output, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, exportPath);
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }

            return new HistoryExportResult(true, exportPath, sourceFiles.Length, recordCount);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        _fileLock.Dispose();
    }

    private static async Task<int> AppendSourceRowsAsync(
        string sourceFile,
        StreamWriter output,
        CancellationToken cancellationToken)
    {
        using var input = new StreamReader(sourceFile, Utf8WithoutBom, true);
        int recordCount = 0;
        bool firstLine = true;

        while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (firstLine)
            {
                firstLine = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await output.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            recordCount++;
        }

        return recordCount;
    }

    private static string FormatRecord(DeviceSnapshot device) => string.Join(
        ',',
        CsvField(device.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        CsvField(device.DeviceId),
        device.Kind.ToString(),
        CsvField(device.DisplayName),
        FormatNumber(device.Temperature),
        FormatNumber(device.Load),
        FormatNumber(device.Power),
        FormatNumber(device.FanRpm),
        device.ThermalLevel.ToString());

    private static string FormatNumber(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string CsvField(string value)
    {
        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.IndexOfAny([',', '"']) >= 0
            ? $"\"{sanitized.Replace("\"", "\"\"")}\""
            : sanitized;
    }
}
