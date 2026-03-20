using System;
using System.Text.Json;
using DalamudBrowser.Common;
using Xunit;

namespace DalamudBrowser.Tests;

public class RendererLaunchOptionsTests
{
    [Fact]
    public void FromBase64Json_ValidPayload_ReturnsOptions()
    {
        var options = new RendererLaunchOptions("testPipe", 1234, "cacheDir", 5678u, 9012);
        var base64 = options.ToBase64Json();

        var result = RendererLaunchOptions.FromBase64Json(base64);

        Assert.Equal(options.PipeName, result.PipeName);
        Assert.Equal(options.ParentProcessId, result.ParentProcessId);
        Assert.Equal(options.CefCacheDirectory, result.CefCacheDirectory);
        Assert.Equal(options.AdapterLuidLow, result.AdapterLuidLow);
        Assert.Equal(options.AdapterLuidHigh, result.AdapterLuidHigh);
    }

    [Fact]
    public void FromBase64Json_InvalidBase64_ThrowsFormatException()
    {
        var invalidBase64 = "not-base-64";

        Assert.Throws<FormatException>(() => RendererLaunchOptions.FromBase64Json(invalidBase64));
    }

    [Fact]
    public void FromBase64Json_InvalidJson_ThrowsJsonException()
    {
        var invalidJson = "not-json-format";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(invalidJson));

        Assert.Throws<JsonException>(() => RendererLaunchOptions.FromBase64Json(base64));
    }

    [Fact]
    public void FromBase64Json_NullJson_ThrowsInvalidOperationException()
    {
        var nullJson = "null";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(nullJson));

        Assert.Throws<InvalidOperationException>(() => RendererLaunchOptions.FromBase64Json(base64));
    }
}
