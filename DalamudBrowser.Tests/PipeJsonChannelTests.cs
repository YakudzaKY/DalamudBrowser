using System;
using System.Threading;
using System.Threading.Tasks;
using DalamudBrowser.Common;
using Xunit;

namespace DalamudBrowser.Tests;

public class PipeJsonChannelTests
{
    [Fact]
    public async Task ServerAndClientCanCommunicateWithCurrentUserOnly()
    {
        var pipeName = $"TestPipe_{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var serverTask = PipeJsonChannel.CreateServerAsync(pipeName, cts.Token);
        var clientTask = PipeJsonChannel.CreateClientAsync(pipeName, cts.Token);

        using var serverChannel = await serverTask;
        using var clientChannel = await clientTask;

        var tcs = new TaskCompletionSource<string>();
        clientChannel.LineReceived += line => tcs.TrySetResult(line);

        var testMessage = new RendererCommand { Kind = "test" };
        await serverChannel.SendAsync(testMessage, cts.Token);

        var receivedJson = await tcs.Task.WaitAsync(cts.Token);
        Assert.Contains("\"kind\":\"test\"", receivedJson);
    }
}
