using System;
using System.Collections.Generic;
using Xunit;

namespace DalamudBrowser.Models.Tests;

public class BrowserCollectionConfigTests
{
    [Fact]
    public void EnsureInitialized_EmptyId_GeneratesNewGuid()
    {
        // Arrange
        var config = new BrowserCollectionConfig { Id = Guid.Empty };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.NotEqual(Guid.Empty, config.Id);
    }

    [Fact]
    public void EnsureInitialized_ExistingId_PreservesId()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var config = new BrowserCollectionConfig { Id = expectedId };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(expectedId, config.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureInitialized_InvalidName_DefaultsToCollection(string? invalidName)
    {
        // Arrange
        var config = new BrowserCollectionConfig { Name = invalidName! };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal("Collection", config.Name);
    }

    [Fact]
    public void EnsureInitialized_ValidName_TrimsWhitespace()
    {
        // Arrange
        var config = new BrowserCollectionConfig { Name = "  My Custom Collection  " };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal("My Custom Collection", config.Name);
    }

    [Fact]
    public void EnsureInitialized_WithChildViews_InitializesViews()
    {
        // Arrange
        var config = new BrowserCollectionConfig
        {
            Views = new List<BrowserViewConfig>
            {
                new BrowserViewConfig { Id = Guid.Empty, Title = "  Child 1  " },
                new BrowserViewConfig { Id = Guid.Empty, Title = null! }
            }
        };

        // Act
        config.EnsureInitialized();

        // Assert
        Assert.Equal(2, config.Views.Count);

        Assert.NotEqual(Guid.Empty, config.Views[0].Id);
        Assert.Equal("Child 1", config.Views[0].Title);

        Assert.NotEqual(Guid.Empty, config.Views[1].Id);
        Assert.Equal("Browser View", config.Views[1].Title);
    }

    [Fact]
    public void CreateDefault_SetsNameCorrectly()
    {
        // Arrange & Act
        var name = "Test Collection";
        var config = BrowserCollectionConfig.CreateDefault(name);

        // Assert
        Assert.Equal(name, config.Name);
        Assert.True(config.IsEnabled);
        Assert.Empty(config.Views);
        Assert.NotEqual(Guid.Empty, config.Id);
    }
}
