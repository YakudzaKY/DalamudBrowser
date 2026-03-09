using System;
using System.IO;
using DalamudBrowser.Services;
using Xunit;

namespace DalamudBrowser.Tests.Services;

public class BrowserUrlUtilityTests
{
    [Theory]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/path", "https://example.com/path")]
    [InlineData("https://example.com/path?query=1", "https://example.com/path?query=1")]
    [InlineData("  https://example.com  ", "https://example.com/")]
    [InlineData("invalid_url", "invalid_url")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_ReturnsExpectedString(string? input, string expected)
    {
        var result = BrowserUrlUtility.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com/path", true)]
    [InlineData("file:///C:/path/to/file.html", true)]
    [InlineData("invalid_url", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void TryCreateAbsoluteUri_ReturnsExpectedResult(string? url, bool expectedSuccess)
    {
        var result = BrowserUrlUtility.TryCreateAbsoluteUri(url, out var uri);
        Assert.Equal(expectedSuccess, result);
        if (expectedSuccess)
        {
            Assert.NotNull(uri);
        }
    }

    [Fact]
    public void TryCreateAbsoluteUri_WithFullyQualifiedPath_ReturnsTrue()
    {
        var isWindows = Path.DirectorySeparatorChar == '\\';
        var path = isWindows ? @"C:\path\to\file.html" : "/path/to/file.html";

        var result = BrowserUrlUtility.TryCreateAbsoluteUri(path, out var uri);

        Assert.True(result);
        Assert.NotNull(uri);
        Assert.Equal("file", uri.Scheme);
    }

    [Fact]
    public void TryCreateAbsoluteUri_WithFullyQualifiedPathAndQuery_ReturnsTrue()
    {
        var isWindows = Path.DirectorySeparatorChar == '\\';
        var path = isWindows ? @"C:\path\to\file.html?query=1" : "/path/to/file.html?query=1";

        var result = BrowserUrlUtility.TryCreateAbsoluteUri(path, out var uri);

        Assert.True(result);
        Assert.NotNull(uri);
        Assert.Equal("file", uri.Scheme);

        var isUnix = !isWindows;
        if (isUnix)
        {
            // On Linux, `Uri` class treats the file path differently, sometimes encoding '?'
            Assert.EndsWith("%3Fquery=1", uri.AbsoluteUri);
        }
        else
        {
            Assert.Equal("?query=1", uri.Query);
        }
    }

    [Fact]
    public void TryGetLocalFilePath_WithFileUri_ReturnsTrueAndPath()
    {
        var isWindows = Path.DirectorySeparatorChar == '\\';
        var path = isWindows ? @"C:\path\to\file.html" : "/path/to/file.html";
        var uri = new Uri(path).AbsoluteUri;

        var result = BrowserUrlUtility.TryGetLocalFilePath(uri, out var resultPath);

        Assert.True(result);
        Assert.Equal(path, resultPath.Replace('/', Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("http://example.com/file.html")]
    [InlineData("invalid_url")]
    [InlineData(null)]
    public void TryGetLocalFilePath_WithNonFileUri_ReturnsFalse(string? url)
    {
        var result = BrowserUrlUtility.TryGetLocalFilePath(url, out var resultPath);

        Assert.False(result);
        Assert.Equal(string.Empty, resultPath);
    }

    [Theory]
    [InlineData("http://example.com/?OVERLAY_WS=ws://localhost:10501/ws", true, "ws://localhost:10501/ws")]
    [InlineData("http://example.com/?OVERLAY_WS=wss://localhost:10501/ws", true, "wss://localhost:10501/ws")]
    [InlineData("http://example.com/?OVERLAY_WS=ws%3A%2F%2Flocalhost%3A10501%2Fws", true, "ws://localhost:10501/ws")]
    [InlineData("http://example.com/?other=1&OVERLAY_WS=ws://localhost:10501/ws", true, "ws://localhost:10501/ws")]
    [InlineData("http://example.com/?OVERLAY_WS=http://localhost:10501/ws", false, null)]
    [InlineData("http://example.com/?OVERLAY_WS=", false, null)]
    [InlineData("http://example.com/?other=1", false, null)]
    [InlineData("invalid_url", false, null)]
    [InlineData(null, false, null)]
    public void TryGetActOverlayWebSocket_ReturnsExpectedResult(string? url, bool expectedSuccess, string? expectedWebSocketUri)
    {
        var result = BrowserUrlUtility.TryGetActOverlayWebSocket(url, out var webSocketUri);

        Assert.Equal(expectedSuccess, result);
        if (expectedSuccess)
        {
            Assert.NotNull(webSocketUri);
            Assert.Equal(expectedWebSocketUri, webSocketUri.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData("http://example.com/?OVERLAY_WS=ws://localhost:10501/ws", true)]
    [InlineData("http://example.com/cactbot/ui/raidboss/raidboss.html", true)]
    [InlineData("http://example.com/OverlayPlugin/ui/test.html", true)]
    [InlineData("http://example.com/ui/raidboss.html", true)]
    [InlineData("http://example.com/ui/jobs.html", true)]
    [InlineData("http://example.com/other/path.html", false)]
    [InlineData("invalid_url", false)]
    [InlineData(null, false)]
    public void IsLikelyActOverlay_ReturnsExpectedResult(string? url, bool expected)
    {
        var result = BrowserUrlUtility.IsLikelyActOverlay(url);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com", true)]
    [InlineData("file:///C:/path/to/file.html", true)]
    [InlineData("ws://localhost:10501", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("mailto:test@example.com", false)]
    public void IsNavigableScheme_ReturnsExpectedResult(string url, bool expected)
    {
        var uri = new Uri(url);
        var result = BrowserUrlUtility.IsNavigableScheme(uri);
        Assert.Equal(expected, result);
    }
}
