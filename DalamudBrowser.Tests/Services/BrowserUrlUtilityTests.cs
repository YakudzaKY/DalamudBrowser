using System;
using Xunit;
using DalamudBrowser.Services;
using System.IO;

namespace DalamudBrowser.Tests.Services
{
    public class BrowserUrlUtilityTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("invalid url", "invalid url")]
        [InlineData("https://example.com", "https://example.com/")]
        [InlineData("http://test.com/path?q=1", "http://test.com/path?q=1")]
        public void Normalize_ReturnsExpected(string? input, string expected)
        {
            var result = BrowserUrlUtility.Normalize(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Normalize_LocalFilePath_ReturnsFileUri()
        {
            // Fully qualified paths depend on the OS, but we can assume C:\ on Windows
            if (OperatingSystem.IsWindows())
            {
                var input = @"C:\test\path.html";
                var expected = "file:///C:/test/path.html";
                var result = BrowserUrlUtility.Normalize(input);
                Assert.Equal(expected, result);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("invalid", false)]
        [InlineData("https://example.com", true)]
        public void TryCreateAbsoluteUri_ReturnsExpected(string? url, bool expectedSuccess)
        {
            var result = BrowserUrlUtility.TryCreateAbsoluteUri(url, out var uri);
            Assert.Equal(expectedSuccess, result);
            if (expectedSuccess)
            {
                Assert.NotNull(uri);
            }
            else
            {
                Assert.Null(uri);
            }
        }

        [Fact]
        public void TryCreateAbsoluteUri_LocalFilePathWithQuery_ReturnsTrue()
        {
            if (OperatingSystem.IsWindows())
            {
                var input = @"C:\test\path.html?query=1";
                var result = BrowserUrlUtility.TryCreateAbsoluteUri(input, out var uri);
                Assert.True(result);
                Assert.Equal("file:///C:/test/path.html?query=1", uri.AbsoluteUri);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("https://example.com", false)]
        [InlineData("file:///C:/test.html", true)]
        public void TryGetLocalFilePath_ReturnsExpected(string? url, bool expectedSuccess)
        {
            if (OperatingSystem.IsWindows() || !url?.StartsWith("file:///C:") == true)
            {
                var result = BrowserUrlUtility.TryGetLocalFilePath(url, out var path);
                Assert.Equal(expectedSuccess, result);
                if (expectedSuccess)
                {
                    Assert.NotEmpty(path);
                }
                else
                {
                    Assert.Empty(path);
                }
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("https://example.com", false)]
        [InlineData("file:///C:/test.html", false)]
        [InlineData("file:///C:/test.html?OVERLAY_WS=invalid", false)]
        [InlineData("file:///C:/test.html?OVERLAY_WS=http://localhost", false)]
        [InlineData("file:///C:/test.html?OVERLAY_WS=ws://localhost:10501/ws", true)]
        [InlineData("file:///C:/test.html?OVERLAY_WS=wss://localhost:10501/ws", true)]
        [InlineData("https://example.com/?OVERLAY_WS=ws://localhost:10501/ws", true)]
        public void TryGetActOverlayWebSocket_ReturnsExpected(string? url, bool expectedSuccess)
        {
            var result = BrowserUrlUtility.TryGetActOverlayWebSocket(url, out var uri);
            Assert.Equal(expectedSuccess, result);
            if (expectedSuccess)
            {
                Assert.NotNull(uri);
            }
            else
            {
                Assert.Null(uri);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("https://example.com", false)]
        [InlineData("file:///C:/test.html", false)]
        [InlineData("file:///C:/test.html?OVERLAY_WS=ws://localhost:10501/ws", true)]
        [InlineData("https://cactbot.test/ui/raidboss/raidboss.html", true)]
        [InlineData("https://example.com/overlayplugin/test", true)]
        [InlineData("https://example.com/raidboss", true)]
        [InlineData("https://example.com/jobs", true)]
        public void IsLikelyActOverlay_ReturnsExpected(string? url, bool expected)
        {
            var result = BrowserUrlUtility.IsLikelyActOverlay(url);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com", true)]
        [InlineData("file:///C:/test.html", true)]
        [InlineData("ftp://example.com", false)]
        [InlineData("ws://example.com", false)]
        public void IsNavigableScheme_ReturnsExpected(string url, bool expected)
        {
            var uri = new Uri(url);
            var result = BrowserUrlUtility.IsNavigableScheme(uri);
            Assert.Equal(expected, result);
        }
    }
}
