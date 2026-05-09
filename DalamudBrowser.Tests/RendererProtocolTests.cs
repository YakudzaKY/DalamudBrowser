using System;
using DalamudBrowser.Common;
using Xunit;

namespace DalamudBrowser.Tests;

public class RendererProtocolTests
{
    [Fact]
    public void SyncView_CreatesCorrectCommand()
    {
        // Arrange
        var viewId = Guid.NewGuid();
        var browserViewCommand = new BrowserViewCommand(
            viewId,
            "https://example.com",
            800,
            600,
            800,
            600,
            1.0f,
            1.0f,
            1,
            false,
            60,
            30,
            false);

        // Act
        var command = RendererCommand.SyncView(browserViewCommand);

        // Assert
        Assert.NotNull(command);
        Assert.Equal("sync_view", command.Kind);
        Assert.Equal(browserViewCommand, command.View);
        Assert.Null(command.ViewId);
    }
}
