using System.Globalization;
using System.IO;
using System.Text;

namespace LaptopThermalHelper.App.Services;

public enum ApplicationEventLevel
{
    Information,
    Warning,
    Error,
}

public sealed record ApplicationEvent(
    DateTimeOffset Timestamp,
    ApplicationEventLevel Level,
    string Category,
    string Message);

public sealed record ApplicationEventExportResult(bool HasData, string? FilePath, int RecordCount)
{
    public static ApplicationEventExportResult Empty { get; } = new(false, null, 0);
}

public interface IApplicationEventLog
{
    event EventHandler<ApplicationEvent>? EventWritten;

    IReadOnlyList<ApplicationEvent> GetSnapshot();

    void Write(ApplicationEventLevel level, string category, string message);

    Task<ApplicationEventExportResult> ExportAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryApplicationEventLog : IApplicationEventLog
{
    private const int MaximumEntries = 500;
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly object _syncRoot = new();
    private readonly List<ApplicationEvent> _events = [];
    private readonly string _exportDirectory;

    public InMemoryApplicationEventLog()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoamerFly",
            "LaptopThermalHelper",
            "exports"))
    {
    }

    public InMemoryApplicationEventLog(string exportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);
        _exportDirectory = Path.GetFullPath(exportDirectory);
    }

    public event EventHandler<ApplicationEvent>? EventWritten;

    public IReadOnlyList<ApplicationEvent> GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _events.ToArray();
        }
    }

    public void Write(ApplicationEventLevel level, string category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var entry = new ApplicationEvent(DateTimeOffset.Now, level, category.Trim(), message.Trim());
        lock (_syncRoot)
        {
            _events.Insert(0, entry);
            if (_events.Count > MaximumEntries)
            {
                _events.RemoveRange(MaximumEntries, _events.Count - MaximumEntries);
            }
        }

        EventWritten?.Invoke(this, entry);
    }

    public async Task<ApplicationEventExportResult> ExportAsync(CancellationToken cancellationToken = default)
    {
        ApplicationEvent[] entries;
        lock (_syncRoot)
        {
            entries = _events.ToArray();
        }

        if (entries.Length == 0)
        {
            return ApplicationEventExportResult.Empty;
        }

        Directory.CreateDirectory(_exportDirectory);
        string path = Path.Combine(
            _exportDirectory,
            $"application-events-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.csv");

        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4_096,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, Utf8WithoutBom);
        await writer.WriteLineAsync("timestamp,level,category,message".AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (ApplicationEvent entry in entries.OrderBy(static entry => entry.Timestamp))
        {
            string line = string.Join(
                ',',
                Csv(entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                Csv(entry.Level.ToString()),
                Csv(entry.Category),
                Csv(entry.Message));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ApplicationEventExportResult(true, path, entries.Length);
    }

    private static string Csv(string value)
    {
        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.IndexOfAny([',', '"']) >= 0
            ? $"\"{sanitized.Replace("\"", "\"\"")}\""
            : sanitized;
    }
}
