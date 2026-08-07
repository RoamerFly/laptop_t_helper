using LaptopThermalHelper.Application.Monitoring;

namespace LaptopThermalHelper.Application.History;

public interface ITemperatureHistoryStore
{
    Task AppendAsync(MonitoringSnapshot snapshot, CancellationToken cancellationToken);

    Task<HistoryExportResult> ExportAsync(CancellationToken cancellationToken);
}

public sealed record HistoryExportResult(
    bool HasData,
    string? FilePath,
    int SourceFileCount,
    int RecordCount)
{
    public static HistoryExportResult Empty { get; } = new(false, null, 0, 0);
}
