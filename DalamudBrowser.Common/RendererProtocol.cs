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
    int ViewWidth,
    int ViewHeight,
    int PixelWidth,
    int PixelHeight,
    float DeviceScaleFactor,
    float ZoomFactor,
    int ReloadGeneration,
    bool Muted,
    int FrameRate,
    int HiddenFrameRate,
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
    private int disposeRequested;

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
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await server.WaitForConnectionAsync(cancellationToken);
        return new PipeJsonChannel(server);
    }

    public static async Task<PipeJsonChannel> CreateClientAsync(string pipeName, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await client.ConnectAsync(cancellationToken);
        return new PipeJsonChannel(client);
    }

    public async Task SendAsync<T>(T payload, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref disposeRequested) != 0 || disposeTokenSource.IsCancellationRequested)
        {
            return;
        }

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(disposeTokenSource.Token, cancellationToken);
        var line = JsonSerializer.Serialize(payload, JsonProtocol.Options);
        var lockTaken = false;

        try
        {
            await writeLock.WaitAsync(linkedTokenSource.Token);
            lockTaken = true;
            await writer.WriteLineAsync(line.AsMemory(), linkedTokenSource.Token);
            await writer.FlushAsync(linkedTokenSource.Token);
        }
        catch (OperationCanceledException) when (disposeTokenSource.IsCancellationRequested)
        {
            // The channel is being disposed.
        }
        catch (ObjectDisposedException) when (disposeTokenSource.IsCancellationRequested || Volatile.Read(ref disposeRequested) != 0)
        {
            // The channel is being disposed.
        }
        catch (IOException) when (disposeTokenSource.IsCancellationRequested || Volatile.Read(ref disposeRequested) != 0)
        {
            // The channel is being disposed.
        }
        finally
        {
            if (lockTaken)
            {
                try
                {
                    writeLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // The lock was already disposed.
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeRequested, 1) != 0)
        {
            return;
        }

        try
        {
            disposeTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source was already disposed.
        }

        try
        {
            writer.Dispose();
        }
        catch (Exception ex)
        {
            ReportFault(ex);
        }

        try
        {
            reader.Dispose();
        }
        catch (Exception ex)
        {
            ReportFault(ex);
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            ReportFault(ex);
        }

        disposeTokenSource.Dispose();
        writeLock.Dispose();
    }

    private void ReportFault(Exception ex)
    {
        try
        {
            Faulted?.Invoke(ex);
        }
        catch
        {
            // Ignore subscriber errors during disposal.
        }
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
            // Shutdown requested.
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
