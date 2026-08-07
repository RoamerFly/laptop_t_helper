using System.IO;
using System.Text.Json;

namespace LaptopThermalHelper.App.Services;

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public JsonApplicationSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoamerFly",
            "LaptopThermalHelper",
            "settings.json"))
    {
    }

    public JsonApplicationSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new SettingsLoadResult(ApplicationSettings.Default);
        }

        try
        {
            await using FileStream stream = File.OpenRead(_settingsPath);
            ApplicationSettings? settings = await JsonSerializer
                .DeserializeAsync<ApplicationSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (settings is null)
            {
                return await RecoverFromInvalidFileAsync("设置文件为空，已恢复默认设置。", cancellationToken)
                    .ConfigureAwait(false);
            }

            return new SettingsLoadResult(settings.Normalize());
        }
        catch (JsonException)
        {
            return await RecoverFromInvalidFileAsync("设置文件无法读取，已备份并恢复默认设置。", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("无法确定设置目录。");
        Directory.CreateDirectory(directory);

        string temporaryPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4_096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings.Normalize(),
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<SettingsLoadResult> RecoverFromInvalidFileAsync(
        string notice,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("无法确定设置目录。");
        Directory.CreateDirectory(directory);
        string backupPath = Path.Combine(
            directory,
            $"settings.invalid-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.json");

        try
        {
            File.Move(_settingsPath, backupPath, true);
        }
        catch (IOException)
        {
            return new SettingsLoadResult(
                ApplicationSettings.Default,
                "设置文件无法读取，且备份失败；已在内存中使用默认设置。");
        }

        await SaveAsync(ApplicationSettings.Default, cancellationToken).ConfigureAwait(false);
        return new SettingsLoadResult(ApplicationSettings.Default, notice);
    }
}
