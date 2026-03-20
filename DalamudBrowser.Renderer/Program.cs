using DalamudBrowser.Common;
using System.Reflection;
using System.Runtime.Loader;

namespace DalamudBrowser.Renderer;

internal static class Program
{
    private static readonly Dictionary<Guid, RemoteBrowserView> Views = new();
    private static readonly object SyncRoot = new();

    private static PipeJsonChannel? channel;
    private static RendererLaunchOptions? launchOptions;
    private static CancellationTokenSource? shutdownTokenSource;

    private static async Task<int> Main(string[] args)
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        RegisterAssemblyResolver();

        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing renderer launch options.");
            return 1;
        }

        shutdownTokenSource = new CancellationTokenSource();

        try
        {
            launchOptions = RendererLaunchOptions.FromBase64Json(args[0]);

            _ = Task.Run(() => WatchParentProcessAsync(launchOptions.ParentProcessId, shutdownTokenSource.Token));

            if (!DxHandler.Initialize(launchOptions.AdapterLuidLow, launchOptions.AdapterLuidHigh))
            {
                return 2;
            }

            CefRuntime.Initialize(launchOptions.CefCacheDirectory, launchOptions.ParentProcessId);

            channel = await PipeJsonChannel.CreateClientAsync(launchOptions.PipeName, shutdownTokenSource.Token);
            channel.LineReceived += OnLineReceived;
            channel.Faulted += ex => Console.Error.WriteLine(ex);
            channel.Closed += () => shutdownTokenSource.Cancel();

            await channel.SendAsync(RendererEvent.Ready("Renderer connected."), shutdownTokenSource.Token);
            await channel.Completion;
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (channel != null)
            {
                try
                {
                    await channel.SendAsync(RendererEvent.Fatal(ex.Message));
                }
                catch (Exception sendEx)
                {
                    Console.Error.WriteLine(sendEx);
                }
            }

            return 3;
        }
        finally
        {
            lock (SyncRoot)
            {
                foreach (var view in Views.Values)
                {
                    view.Dispose();
                }

                Views.Clear();
            }

            channel?.Dispose();
            try
            {
                CefRuntime.Shutdown();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }

            DxHandler.Shutdown();
            shutdownTokenSource?.Dispose();
        }
    }

    private static void RegisterAssemblyResolver()
    {
        AssemblyLoadContext.Default.Resolving += static (_, assemblyName) =>
        {
            var candidatePath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName.Name}.dll");
            if (!File.Exists(candidatePath))
            {
                return null;
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath);
        };
    }

    private static void OnLineReceived(string line)
    {
        RendererCommand? command;
        try
        {
            command = System.Text.Json.JsonSerializer.Deserialize<RendererCommand>(line, JsonProtocol.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to deserialize renderer command: {ex}");
            return;
        }

        if (command == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            switch (command.Kind)
            {
                case "sync_view" when command.View != null:
                    SyncView(command.View);
                    break;
                case "remove_view" when command.ViewId.HasValue:
                    RemoveView(command.ViewId.Value);
                    break;
                case "shutdown":
                    shutdownTokenSource?.Cancel();
                    break;
            }
        }
    }

    private static void SyncView(BrowserViewCommand command)
    {
        if (!Views.TryGetValue(command.ViewId, out var view))
        {
            view = new RemoteBrowserView(command.ViewId, launchOptions!.CefCacheDirectory);
            Views.Add(command.ViewId, view);
        }

        var textureChanged = view.Apply(command);
        if (textureChanged)
        {
            _ = NotifyTextureReadyAsync(command.ViewId, view.SharedTextureHandle, view.PixelWidth, view.PixelHeight);
        }
    }

    private static void RemoveView(Guid viewId)
    {
        if (!Views.Remove(viewId, out var view))
        {
            return;
        }

        view.Dispose();
    }

    private static async Task NotifyTextureReadyAsync(Guid viewId, IntPtr textureHandle, int width, int height)
    {
        if (channel == null || shutdownTokenSource == null)
        {
            return;
        }

        try
        {
            await channel.SendAsync(RendererEvent.TextureReady(viewId, textureHandle, width, height), shutdownTokenSource.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to send texture handle for {viewId}: {ex}");
        }
    }

    private static async Task WatchParentProcessAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        try
        {
            using var parent = System.Diagnostics.Process.GetProcessById(parentProcessId);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (parent.HasExited)
                {
                    shutdownTokenSource?.Cancel();
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch
        {
            shutdownTokenSource?.Cancel();
        }
    }
}
