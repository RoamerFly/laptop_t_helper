using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace LaptopThermalHelper.App.Services;

/// <summary>
/// Result of an update check against GitHub Releases.
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    string Message,
    string? LatestVersion = null,
    string? ReleaseUrl = null);

public enum UpdateCheckOutcome
{
    UpToDate,

    UpdateAvailable,

    NetworkError,
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Queries the GitHub Releases API for the latest published release and
/// compares its tag against the running assembly version. The comparison only
/// reads public release metadata; no telemetry and no automatic download.
/// </summary>
public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    private const string Owner = "RoamerFly";
    private const string Repository = "laptop_t_helper";
    private const string ReleasesPage = $"https://github.com/{Owner}/{Repository}/releases";

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
    private static readonly Version FallbackVersion = new(0, 1, 0);

    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;

    public GitHubUpdateCheckService(HttpClient? httpClient = null, Version? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _currentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? FallbackVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        // GitHub API requires a User-Agent; a plain product header avoids any auth token.
        request.Headers.UserAgent.ParseAdd($"{Owner}-{Repository}/{_currentVersion.ToString(3)}");

        GitHubReleasePayload? payload;
        try
        {
            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckOutcome.NetworkError,
                    $"无法查询更新（GitHub 返回 {(int)response.StatusCode}）。请稍后重试或访问发布页。");
            }

            payload = await response.Content
                .ReadFromJsonAsync(GitHubReleasePayloadContext.Default.GitHubReleasePayload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.NetworkError,
                "无法连接到 GitHub，请检查网络后重试；也可直接打开发布页查看最新版本。");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.TagName))
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.NetworkError,
                "仓库尚未发布任何 Release，暂无可比对的版本信息。");
        }

        if (!TryParseVersion(payload.TagName, out Version latest))
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.NetworkError,
                $"最新 Release 标签（{payload.TagName}）无法解析为版本号。");
        }

        string releaseUrl = string.IsNullOrWhiteSpace(payload.HtmlUrl) ? ReleasesPage : payload.HtmlUrl;
        int comparison = latest.CompareTo(_currentVersion);
        if (comparison <= 0)
        {
            return new UpdateCheckResult(
                UpdateCheckOutcome.UpToDate,
                $"已是最新版本（v{FormatVersion(_currentVersion)}）。", latest.ToString(), releaseUrl);
        }

        return new UpdateCheckResult(
            UpdateCheckOutcome.UpdateAvailable,
            $"发现新版本 v{FormatVersion(latest)}（当前 v{FormatVersion(_currentVersion)}），请前往发布页下载。",
            latest.ToString(),
            releaseUrl);
    }

    private static string FormatVersion(Version version) =>
        version.Build >= 0 ? version.ToString(3) : version.ToString();

    /// <summary>Accepts tags like “1”, “1.2.3”, “v1.2.3” or “1.2.3-beta”.</summary>
    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        int dash = trimmed.IndexOf('-');
        if (dash >= 0)
        {
            trimmed = trimmed[..dash];
        }

        trimmed = trimmed.TrimStart('v', 'V');
        string[] parts = trimmed.Split('.');
        if (parts.Length == 1)
        {
            trimmed = $"{parts[0]}.0.0";
        }
        else if (parts.Length == 2)
        {
            trimmed = $"{parts[0]}.{parts[1]}.0";
        }

        return Version.TryParse(trimmed, out version!);
    }
}

public sealed record GitHubReleasePayload(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("draft")] bool Draft);

[JsonSerializable(typeof(GitHubReleasePayload))]
internal sealed partial class GitHubReleasePayloadContext : JsonSerializerContext;
