using System;
using DalamudBrowser.Models;
using Xunit;

namespace DalamudBrowser.Tests.Models;

public class BrowserViewConfigTests
{
    [Fact]
    public void EnsureInitialized_InitializesId_WhenEmpty()
    {
        // Arrange
        var config = new BrowserViewConfig { Id = Guid.Empty };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.NotEqual(Guid.Empty, config.Id);
    }

    [Fact]
    public void EnsureInitialized_RetainsId_WhenNotEmpty()
    {
        // Arrange
        var id = Guid.NewGuid();
        var config = new BrowserViewConfig { Id = id };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(id, config.Id);
    }

    [Theory]
    [InlineData(null, "Browser View")]
    [InlineData("", "Browser View")]
    [InlineData("   ", "Browser View")]
    [InlineData(" My View ", "My View")]
    [InlineData("My View", "My View")]
    public void EnsureInitialized_NormalizesTitle(string? inputTitle, string expectedTitle)
    {
        // Arrange
        var config = new BrowserViewConfig { Title = inputTitle! };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedTitle, config.Title);
    }

    [Fact]
    public void EnsureInitialized_NormalizesUrl_WhenNull()
    {
        // Arrange
        var config = new BrowserViewConfig { Url = null! };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(string.Empty, config.Url);
    }

    [Fact]
    public void EnsureInitialized_RetainsUrl_WhenNotNull()
    {
        // Arrange
        var url = "https://example.com";
        var config = new BrowserViewConfig { Url = url };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(url, config.Url);
    }

    [Theory]
    [InlineData((BrowserViewPerformancePreset)(-1), BrowserViewPerformancePreset.Balanced)]
    [InlineData((BrowserViewPerformancePreset)100, BrowserViewPerformancePreset.Balanced)]
    [InlineData(BrowserViewPerformancePreset.Responsive, BrowserViewPerformancePreset.Responsive)]
    [InlineData(BrowserViewPerformancePreset.Balanced, BrowserViewPerformancePreset.Balanced)]
    [InlineData(BrowserViewPerformancePreset.Eco, BrowserViewPerformancePreset.Eco)]
    public void EnsureInitialized_NormalizesPerformancePreset(BrowserViewPerformancePreset inputPreset, BrowserViewPerformancePreset expectedPreset)
    {
        // Arrange
        var config = new BrowserViewConfig { PerformancePreset = inputPreset };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedPreset, config.PerformancePreset);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(100, 60)]
    public void EnsureInitialized_NormalizesFrameRates(int inputFrameRate, int expectedFrameRate)
    {
        // Arrange
        var config = new BrowserViewConfig
        {
            InteractiveFrameRate = inputFrameRate,
            PassiveFrameRate = inputFrameRate,
            HiddenFrameRate = inputFrameRate
        };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedFrameRate, config.InteractiveFrameRate);
        Assert.Equal(expectedFrameRate, config.PassiveFrameRate);
        Assert.Equal(expectedFrameRate, config.HiddenFrameRate);
    }

    [Theory]
    [InlineData(0f, 25f)]
    [InlineData(25f, 25f)]
    [InlineData(100f, 100f)]
    [InlineData(500f, 500f)]
    [InlineData(600f, 500f)]
    public void EnsureInitialized_NormalizesZoomPercent(float inputZoom, float expectedZoom)
    {
        // Arrange
        var config = new BrowserViewConfig { ZoomPercent = inputZoom };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedZoom, config.ZoomPercent);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(50f, 50f)]
    [InlineData(100f, 100f)]
    [InlineData(150f, 100f)]
    public void EnsureInitialized_NormalizesOpacityPercent(float inputOpacity, float expectedOpacity)
    {
        // Arrange
        var config = new BrowserViewConfig { OpacityPercent = inputOpacity };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedOpacity, config.OpacityPercent);
    }

    [Theory]
    [InlineData(0f, 320f)]
    [InlineData(319f, 320f)]
    [InlineData(320f, 320f)]
    [InlineData(640f, 640f)]
    public void EnsureInitialized_NormalizesWidth(float inputWidth, float expectedWidth)
    {
        // Arrange
        var config = new BrowserViewConfig { Width = inputWidth };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedWidth, config.Width);
    }

    [Theory]
    [InlineData(0f, 200f)]
    [InlineData(199f, 200f)]
    [InlineData(200f, 200f)]
    [InlineData(420f, 420f)]
    public void EnsureInitialized_NormalizesHeight(float inputHeight, float expectedHeight)
    {
        // Arrange
        var config = new BrowserViewConfig { Height = inputHeight };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedHeight, config.Height);
    }

    [Theory]
    [InlineData(-1f, -1f)]
    [InlineData(-10f, -1f)]
    [InlineData(0f, 0f)]
    [InlineData(50f, 50f)]
    [InlineData(100f, 100f)]
    [InlineData(150f, 100f)]
    public void EnsureInitialized_NormalizesPercents(float inputPercent, float expectedPercent)
    {
        // Arrange
        var config = new BrowserViewConfig
        {
            PositionXPercent = inputPercent,
            PositionYPercent = inputPercent,
            WidthPercent = inputPercent,
            HeightPercent = inputPercent
        };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedPercent, config.PositionXPercent);
        Assert.Equal(expectedPercent, config.PositionYPercent);
        Assert.Equal(expectedPercent, config.WidthPercent);
        Assert.Equal(expectedPercent, config.HeightPercent);
    }
}
