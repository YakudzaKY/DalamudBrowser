using System;
using DalamudBrowser.Services;
using Xunit;

namespace DalamudBrowser.Tests;

public class BrowserUrlUtilityTests
{
    [Theory]
    [InlineData("https://google.com", "https://google.com/")]
    [InlineData("https://google.com/", "https://google.com/")]
    [InlineData("  https://google.com  ", "https://google.com/")]
    [InlineData("not a url", "not a url")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_ReturnsExpected(string? input, string expected)
    {
        var result = BrowserUrlUtility.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://google.com", true, "https://google.com/")]
    [InlineData("http://localhost:8080", true, "http://localhost:8080/")]
    [InlineData("file:///C:/test.txt", true, "file:///C:/test.txt")]
    [InlineData("C:\\test.txt", true, "file:///C:/test.txt")]
    [InlineData("C:\\test.txt?param=1", true, "file:///C:/test.txt?param=1")]
    [InlineData("not a url", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryCreateAbsoluteUri_ReturnsExpected(string? input, bool expectedResult, string? expectedUri)
    {
        var result = BrowserUrlUtility.TryCreateAbsoluteUri(input, out var uri);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal(expectedUri, uri.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData("file:///C:/test.txt", true, "C:\\test.txt")]
    [InlineData("C:\\test.txt", true, "C:\\test.txt")]
    [InlineData("https://google.com", false, "")]
    [InlineData("not a url", false, "")]
    public void TryGetLocalFilePath_ReturnsExpected(string? input, bool expectedResult, string expectedPath)
    {
        var result = BrowserUrlUtility.TryGetLocalFilePath(input, out var path);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            // Normalizing separators for cross-platform comparison if necessary,
            // but the code uses uri.LocalPath which should be system-dependent.
            // On Windows it will likely be C:\test.txt
            Assert.Equal(expectedPath, path, ignoreCase: true);
        }
    }

    [Theory]
    [InlineData("https://example.com?OVERLAY_WS=ws://localhost:1234", true, "ws://localhost:1234/")]
    [InlineData("https://example.com?OVERLAY_WS=wss://localhost:1234", true, "wss://localhost:1234/")]
    [InlineData("https://example.com?OVERLAY_WS=http://localhost:1234", false, null)]
    [InlineData("https://example.com?another=param", false, null)]
    [InlineData("https://example.com", false, null)]
    public void TryGetActOverlayWebSocket_ReturnsExpected(string? input, bool expectedResult, string? expectedWsUri)
    {
        var result = BrowserUrlUtility.TryGetActOverlayWebSocket(input, out var wsUri);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal(expectedWsUri, wsUri.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData("https://example.com?OVERLAY_WS=ws://localhost:1234", true)]
    [InlineData("https://example.com/cactbot/index.html", true)]
    [InlineData("https://example.com/overlayplugin/index.html", true)]
    [InlineData("https://example.com/raidboss/index.html", true)]
    [InlineData("https://example.com/jobs/index.html", true)]
    [InlineData("https://example.com/normal/page.html", false)]
    public void IsLikelyActOverlay_ReturnsExpected(string? input, bool expected)
    {
        var result = BrowserUrlUtility.IsLikelyActOverlay(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://google.com", true)]
    [InlineData("https://google.com", true)]
    [InlineData("file:///C:/test.txt", true)]
    [InlineData("ws://localhost:1234", false)]
    public void IsNavigableScheme_ReturnsExpected(string input, bool expected)
    {
        var uri = new Uri(input);
        var result = BrowserUrlUtility.IsNavigableScheme(uri);
        Assert.Equal(expected, result);
    }
}
