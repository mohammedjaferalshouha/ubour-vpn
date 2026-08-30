using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ubour.Services;
using Xunit;

namespace Ubour.Tests;

public class UpdateManagerTests
{
    [Theory]
    [InlineData("v1.6.1", "1.6.1")]
    [InlineData("V1.6.1", "1.6.1")]
    [InlineData("1.6.1", "1.6.1")]
    [InlineData("", "0.0.0")]
    [InlineData("   ", "0.0.0")]
    public void CleanVersion_ShouldStripPrefixAndTrim(string input, string expected)
    {
        var result = UpdateManager.CleanVersion(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.6.1", "1.6.2", true)]
    [InlineData("1.6.1", "1.7.0", true)]
    [InlineData("1.6.1", "2.0.0", true)]
    [InlineData("1.6.1", "v1.6.2", true)]
    [InlineData("1.6.1", "1.6.1", false)]
    [InlineData("1.6.1", "v1.6.1", false)]
    [InlineData("1.6.1", "1.6.0", false)]
    [InlineData("1.6.1", "1.5.9", false)]
    [InlineData("1.6.1", "0.9.9", false)]
    public void IsNewerVersion_ShouldCompareCorrectly(string current, string remote, bool expected)
    {
        var result = UpdateManager.IsNewerVersion(current, remote);
        Assert.Equal(expected, result);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = json;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewRelease_ShouldReturnHasUpdateTrue()
    {
        string mockJson = """
        {
            "tag_name": "v1.7.0",
            "html_url": "https://github.com/mohammedjaferalshouha/ubour-vpn/releases/tag/v1.7.0",
            "body": "New feature release",
            "assets": [
                {
                    "name": "Ubour-windows-x64.zip",
                    "browser_download_url": "https://github.com/download/x64.zip"
                },
                {
                    "name": "Ubour-windows-x86.zip",
                    "browser_download_url": "https://github.com/download/x86.zip"
                }
            ]
        }
        """;

        var client = new HttpClient(new MockHttpMessageHandler(mockJson));
        var info = await UpdateManager.CheckForUpdatesAsync(client);

        Assert.True(info.HasUpdate);
        Assert.Equal("1.7.0", info.LatestVersion);
        Assert.Equal("https://github.com/download/x64.zip", info.DownloadUrlX64);
        Assert.Equal("https://github.com/download/x86.zip", info.DownloadUrlX86);
        Assert.Equal("https://github.com/mohammedjaferalshouha/ubour-vpn/releases/tag/v1.7.0", info.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenSameVersion_ShouldReturnHasUpdateFalse()
    {
        string mockJson = $$"""
        {
            "tag_name": "v{{UpdateManager.CurrentVersion}}",
            "html_url": "https://github.com/mohammedjaferalshouha/ubour-vpn/releases/tag/v{{UpdateManager.CurrentVersion}}",
            "body": "Current release",
            "assets": []
        }
        """;

        var client = new HttpClient(new MockHttpMessageHandler(mockJson));
        var info = await UpdateManager.CheckForUpdatesAsync(client);

        Assert.False(info.HasUpdate);
        Assert.Equal(UpdateManager.CurrentVersion, info.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenHttpError_ShouldHandleGracefully()
    {
        var client = new HttpClient(new MockHttpMessageHandler("Not Found", HttpStatusCode.NotFound));
        var info = await UpdateManager.CheckForUpdatesAsync(client);

        Assert.False(info.HasUpdate);
        Assert.NotNull(info.ErrorMessage);
    }
}
