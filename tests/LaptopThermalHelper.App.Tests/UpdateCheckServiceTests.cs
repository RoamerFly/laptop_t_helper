using System.Net;
using System.Net.Http;
using System.Text;
using LaptopThermalHelper.App.Services;

namespace LaptopThermalHelper.App.Tests;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_WhenNewerVersionAvailable_ReturnsUpdateAvailable()
    {
        string json = """
        {
            "tag_name": "v1.2.0",
            "html_url": "https://github.com/RoamerFly/laptop_t_helper/releases/tag/v1.2.0",
            "prerelease": false,
            "draft": false
        }
        """;
        var client = CreateClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckService(client, new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("https://github.com/RoamerFly/laptop_t_helper/releases/tag/v1.2.0", result.ReleaseUrl);
        Assert.Contains("发现新版本 v1.2.0", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0")]
    [InlineData("v0.9.0")]
    public async Task CheckAsync_WhenCurrentVersionIsEqualOrNewer_ReturnsUpToDate(string tag)
    {
        string json = $$"""
        {
            "tag_name": "{{tag}}",
            "html_url": "https://github.com/RoamerFly/laptop_t_helper/releases/tag/{{tag}}",
            "prerelease": false,
            "draft": false
        }
        """;
        var client = CreateClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckService(client, new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.Contains("已是最新版本", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v2", "2.0.0", true)]
    [InlineData("v1.5.0-beta.1", "1.5.0", true)]
    [InlineData("2.1.3.4", "2.1.3.4", true)]
    public async Task CheckAsync_HandlesDiverseTagFormats(string tag, string expectedPrefix, bool shouldBeNewer)
    {
        string json = $$"""
        {
            "tag_name": "{{tag}}",
            "html_url": "https://github.com/RoamerFly/laptop_t_helper/releases/tag/{{tag}}",
            "prerelease": false,
            "draft": false
        }
        """;
        var client = CreateClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckService(client, new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        if (shouldBeNewer)
        {
            Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
            Assert.StartsWith(expectedPrefix, result.LatestVersion, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CheckAsync_WhenServerError_ReturnsNetworkError()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "error");
        var service = new GitHubUpdateCheckService(client, new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.NetworkError, result.Outcome);
        Assert.Contains("500", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenTagInvalid_ReturnsNetworkError()
    {
        string json = """
        {
            "tag_name": "invalid-tag-format",
            "html_url": "https://github.com/RoamerFly/laptop_t_helper/releases",
            "prerelease": false,
            "draft": false
        }
        """;
        var client = CreateClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckService(client, new Version(1, 0, 0));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.NetworkError, result.Outcome);
        Assert.Contains("无法解析为版本号", result.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string content)
    {
        var handler = new MockHttpMessageHandler(statusCode, content);
        return new HttpClient(handler);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
