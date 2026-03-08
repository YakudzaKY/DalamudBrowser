using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DalamudBrowser.Common;

public sealed record RendererLaunchOptions(
    string PipeName,
    int ParentProcessId,
    string CefCacheDirectory,
    uint AdapterLuidLow,
    int AdapterLuidHigh)
{
    public string ToBase64Json()
    {
        var json = JsonSerializer.Serialize(this, JsonProtocol.Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static RendererLaunchOptions FromBase64Json(string value)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
        return JsonSerializer.Deserialize<RendererLaunchOptions>(json, JsonProtocol.Options)
            ?? throw new InvalidOperationException("Renderer launch options payload is invalid.");
    }
}

public sealed record BrowserViewCommand(
    Guid ViewId,
    string Url,
    int Width,
    int Height,
    float ZoomFactor,
    bool Muted,
    int FrameRate,
    bool Hidden);

public sealed record RendererCommand
{
    public string Kind { get; init; } = string.Empty;
    public BrowserViewCommand? View { get; init; }
    public Guid? ViewId { get; init; }

    public static RendererCommand SyncView(BrowserViewCommand view) => new()
    {
        Kind = "sync_view",
        View = view,
    };

    public static RendererCommand RemoveView(Guid viewId) => new()
    {
        Kind = "remove_view",
        ViewId = viewId,
    };

    public static RendererCommand Shutdown() => new()
    {
        Kind = "shutdown",
    };
}

public sealed record RendererEvent
{
    public string Kind { get; init; } = string.Empty;
    public Guid? ViewId { get; init; }
    public long TextureHandle { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Message { get; init; }

    public static RendererEvent Ready(string? message = null) => new()
    {
        Kind = "ready",
        Message = message,
    };

    public static RendererEvent TextureReady(Guid viewId, IntPtr textureHandle, int width, int height) => new()
    {
        Kind = "texture_ready",
        ViewId = viewId,
        TextureHandle = textureHandle.ToInt64(),
        Width = width,
        Height = height,
    };

    public static RendererEvent Fatal(string message) => new()
    {
        Kind = "fatal",
        Message = message,
    };
}

public sealed class PipeJsonChannel : IDisposable
{
    private readonly Stream stream;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly CancellationTokenSource disposeTokenSource = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);

    private PipeJsonChannel(Stream stream)
    {
        this.stream = stream;
        reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
        };

        Completion = Task.Run(ReadLoopAsync);
    }

    public event Action<string>? LineReceived;
    public event Action<Exception>? Faulted;
    public event Action? Closed;

    public Task Completion { get; }

    public static async Task<PipeJsonChannel> CreateServerAsync(string pipeName, CancellationToken cancellationToken)
    {
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);
        return new PipeJsonChannel(server);
    }

    public static async Task<PipeJsonChannel> CreateClientAsync(string pipeName, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(cancellationToken);
        return new PipeJsonChannel(client);
    }

    public async Task SendAsync<T>(T payload, CancellationToken cancellationToken = default)
    {
        var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(disposeTokenSource.Token, cancellationToken);
        var line = JsonSerializer.Serialize(payload, JsonProtocol.Options);

        await writeLock.WaitAsync(linkedTokenSource.Token);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), linkedTokenSource.Token);
            await writer.FlushAsync(linkedTokenSource.Token);
        }
        finally
        {
            writeLock.Release();
            linkedTokenSource.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposeTokenSource.IsCancellationRequested)
        {
            return;
        }

        disposeTokenSource.Cancel();
        try
        {
            stream.Dispose();
        }
        catch
        {
        }

        reader.Dispose();
        writer.Dispose();
        writeLock.Dispose();
        disposeTokenSource.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!disposeTokenSource.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(disposeTokenSource.Token);
                if (line == null)
                {
                    break;
                }

                LineReceived?.Invoke(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
        finally
        {
            Closed?.Invoke();
        }
    }
}

public static class JsonProtocol
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
