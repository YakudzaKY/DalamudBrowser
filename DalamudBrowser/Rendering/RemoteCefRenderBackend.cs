using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudBrowser.Common;
using DalamudBrowser.Models;
using DalamudBrowser.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace DalamudBrowser.Rendering;

public sealed class RemoteCefRenderBackend : IBrowserRenderBackend
{
    private readonly record struct BrowserRuntimePolicy(int FrameRate, int HiddenFrameRate);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly Dictionary<Guid, RemoteViewState> views = new();
    private readonly HashSet<Guid> activeViewIds = [];
    private HashSet<Guid> knownViewIds = [];

    private BrowserRendererProcessHost? rendererProcess;
    private bool disposed;
    private string? lastError;

    public RemoteCefRenderBackend(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public string Name => "CEF renderer process";
    public bool SupportsJavaScript => true;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        rendererProcess?.Dispose();
        rendererProcess = null;

        foreach (var view in views.Values)
        {
            view.Dispose();
        }

        views.Clear();
    }

    public void BeginFrame(IReadOnlyCollection<Guid> knownViewIds)
    {
        this.knownViewIds = knownViewIds.Count == 0 ? [] : knownViewIds.ToHashSet();
        activeViewIds.Clear();
        rendererProcess?.Poll();
        EnsureRendererProcess();
    }

    public unsafe void Draw(BrowserRenderRequest request)
    {
        activeViewIds.Add(request.ViewId);

        if (!TryResolveLiveUrl(request, out var liveUrl, out var placeholderMessage))
        {
            if (views.TryGetValue(request.ViewId, out var hiddenViewState) && rendererProcess != null)
            {
                hiddenViewState.EnsureHidden(rendererProcess);
            }

            DrawPlaceholder(request.SurfaceSize, placeholderMessage, request.OpacityFactor);
            return;
        }

        EnsureRendererProcess();
        if (rendererProcess == null)
        {
            DrawPlaceholder(request.SurfaceSize, lastError ?? "Preparing browser renderer...", request.OpacityFactor);
            return;
        }

        if (!rendererProcess.IsConnected)
        {
            DrawPlaceholder(request.SurfaceSize, rendererProcess.LastError ?? lastError ?? "Starting browser renderer...", request.OpacityFactor);
            return;
        }

        var device = (ID3D11Device*)pluginInterface.UiBuilder.DeviceHandle;
        if (device == null)
        {
            DrawPlaceholder(request.SurfaceSize, "Game device handle is not available.", request.OpacityFactor);
            return;
        }

        var viewState = GetOrCreateViewState(request.ViewId);
        viewState.ApplyPendingTexture(device, log);
        var runtimePolicy = ResolveRuntimePolicy(request);
        var deviceScaleFactor = DisplayScaleUtility.GetScaleFactor();
        var viewWidth = Math.Max(1, (int)MathF.Round(MathF.Max(16f, request.SurfaceSize.X)));
        var viewHeight = Math.Max(1, (int)MathF.Round(MathF.Max(16f, request.SurfaceSize.Y)));
        var pixelWidth = Math.Max(1, (int)MathF.Round(viewWidth * deviceScaleFactor));
        var pixelHeight = Math.Max(1, (int)MathF.Round(viewHeight * deviceScaleFactor));

        var syncCommand = new BrowserViewCommand(
            request.ViewId,
            liveUrl,
            viewWidth,
            viewHeight,
            pixelWidth,
            pixelHeight,
            deviceScaleFactor,
            Math.Clamp(request.ZoomFactor, 0.25f, 5f),
            request.Status.ReloadGeneration,
            !request.SoundEnabled,
            runtimePolicy.FrameRate,
            runtimePolicy.HiddenFrameRate,
            Hidden: false);

        viewState.Sync(rendererProcess, syncCommand);
        if (!viewState.TryRender(request.SurfaceSize, request.OpacityFactor))
        {
            DrawPlaceholder(request.SurfaceSize, "Loading page...", request.OpacityFactor);
        }
    }

    public void EndFrame()
    {
        if (rendererProcess != null)
        {
            foreach (var pair in views)
            {
                if (!knownViewIds.Contains(pair.Key))
                {
                    pair.Value.Remove(rendererProcess);
                    pair.Value.Dispose();
                }
                else if (!activeViewIds.Contains(pair.Key))
                {
                    pair.Value.EnsureHidden(rendererProcess);
                }
            }
        }

        var removedIds = views.Keys.Where(id => !knownViewIds.Contains(id)).ToArray();
        foreach (var removedId in removedIds)
        {
            views.Remove(removedId);
        }
    }

    private unsafe void EnsureRendererProcess()
    {
        if (disposed)
        {
            return;
        }

        var rendererPath = GetRendererExecutablePath();
        if (!File.Exists(rendererPath))
        {
            lastError = $"Renderer executable was not found: {rendererPath}";
            return;
        }

        var device = (ID3D11Device*)pluginInterface.UiBuilder.DeviceHandle;
        if (device == null)
        {
            return;
        }

        rendererProcess ??= new BrowserRendererProcessHost(rendererPath, log);
        rendererProcess.EventReceived -= HandleRendererEvent;
        rendererProcess.EventReceived += HandleRendererEvent;

        var launchOptions = new RendererLaunchOptions(
            PipeName: string.Empty,
            ParentProcessId: Environment.ProcessId,
            CefCacheDirectory: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DalamudBrowser",
                "RendererCache"),
            AdapterLuidLow: GetAdapterLuid(device).LowPart,
            AdapterLuidHigh: GetAdapterLuid(device).HighPart);

        rendererProcess.EnsureStarted(launchOptions);
        if (!string.IsNullOrWhiteSpace(rendererProcess.LastError))
        {
            lastError = rendererProcess.LastError;
        }
    }

    private void HandleRendererEvent(RendererEvent message)
    {
        switch (message.Kind)
        {
            case "ready":
                lastError = null;
                break;
            case "fatal":
                lastError = message.Message ?? "Renderer process reported a fatal error.";
                break;
            case "texture_ready" when message.ViewId.HasValue:
                GetOrCreateViewState(message.ViewId.Value).QueueTexture(message.TextureHandle, message.Width, message.Height);
                break;
        }
    }

    private static bool TryResolveLiveUrl(BrowserRenderRequest request, out string liveUrl, out string placeholderMessage)
    {
        liveUrl = string.Empty;
        placeholderMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            placeholderMessage = "No URL configured.";
            return false;
        }

        var normalizedUrl = BrowserUrlUtility.Normalize(request.Url);
        if (!BrowserUrlUtility.TryCreateAbsoluteUri(normalizedUrl, out var navigationUri))
        {
            placeholderMessage = "Invalid URL.";
            return false;
        }

        if (!BrowserUrlUtility.IsNavigableScheme(navigationUri))
        {
            placeholderMessage = $"The '{navigationUri.Scheme}' scheme is not supported.";
            return false;
        }

        liveUrl = navigationUri.AbsoluteUri;
        if (navigationUri.IsFile && request.Status.Availability != BrowserAvailabilityState.Unavailable)
        {
            return true;
        }

        if (request.Status.Availability == BrowserAvailabilityState.Available || request.Status.IsChecking)
        {
            return true;
        }

        placeholderMessage = request.Status.Availability switch
        {
            BrowserAvailabilityState.Unavailable => string.IsNullOrWhiteSpace(request.Status.LastError)
                ? "The target is unavailable right now. Automatic retry is still active."
                : request.Status.LastError,
            BrowserAvailabilityState.Checking => "Checking link availability...",
            _ => "Waiting for the first successful availability check...",
        };

        return false;
    }

    private string GetRendererExecutablePath()
    {
        var pluginAssemblyDirectory = Path.GetDirectoryName(pluginInterface.AssemblyLocation?.ToString())
            ?? AppContext.BaseDirectory;

        var candidatePaths = new List<string>
        {
            Path.Combine(pluginAssemblyDirectory, "renderer", "DalamudBrowser.Renderer.exe"),
        };

        var configurationDirectory = new DirectoryInfo(pluginAssemblyDirectory);
        if (configurationDirectory.Parent is { Name: "bin" } binDirectory
            && (string.Equals(configurationDirectory.Name, "Debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configurationDirectory.Name, "Release", StringComparison.OrdinalIgnoreCase)))
        {
            candidatePaths.Add(Path.Combine(binDirectory.FullName, "x64", configurationDirectory.Name, "renderer", "DalamudBrowser.Renderer.exe"));
        }

        foreach (var candidatePath in candidatePaths)
        {
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return candidatePaths[0];
    }

    private RemoteViewState GetOrCreateViewState(Guid viewId)
    {
        if (views.TryGetValue(viewId, out var viewState))
        {
            return viewState;
        }

        viewState = new RemoteViewState(viewId);
        views.Add(viewId, viewState);
        return viewState;
    }

    private static void DrawPlaceholder(Vector2 requestedSize, string message, float opacityFactor)
    {
        var size = new Vector2(
            MathF.Max(16f, requestedSize.X),
            MathF.Max(16f, requestedSize.Y));
        var alpha = Math.Clamp(opacityFactor, 0.01f, 1f);

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.07f, 0.09f, 0.12f, alpha)), 8f);
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.22f, 0.27f, 0.34f, alpha)), 8f, 0, 1.5f);
        drawList.AddText(min + new Vector2(14f, 12f), ImGui.GetColorU32(new Vector4(0.87f, 0.9f, 0.96f, alpha)), message);
        ImGui.Dummy(size);
    }

    private static BrowserRuntimePolicy ResolveRuntimePolicy(BrowserRenderRequest request)
    {
        var actOptimized = request.ActOptimizations && BrowserUrlUtility.IsLikelyActOverlay(request.Url);
        var passiveOverlay = request.Locked && request.ClickThrough;
        var activeInteraction = request.IsWindowHovered || request.IsWindowFocused || !request.Locked;

        if (request.UseCustomFrameRates)
        {
            var interactiveFrameRate = NormalizeFrameRate(request.InteractiveFrameRate, fallback: 30);
            var passiveFrameRate = NormalizeFrameRate(request.PassiveFrameRate, fallback: Math.Min(interactiveFrameRate, 15));
            var hiddenFrameRate = NormalizeFrameRate(request.HiddenFrameRate, fallback: Math.Min(passiveFrameRate, 5));
            return new BrowserRuntimePolicy(
                activeInteraction ? interactiveFrameRate : passiveFrameRate,
                hiddenFrameRate);
        }

        var basePolicy = request.PerformancePreset switch
        {
            BrowserViewPerformancePreset.Responsive => new BrowserRuntimePolicy(60, 10),
            BrowserViewPerformancePreset.Balanced => new BrowserRuntimePolicy(45, 8),
            BrowserViewPerformancePreset.Eco => new BrowserRuntimePolicy(30, 8),
            _ => new BrowserRuntimePolicy(60, 8),
        };

        if (!actOptimized)
        {
            return basePolicy;
        }

        return basePolicy;
    }

    private static int NormalizeFrameRate(int value, int fallback)
    {
        return value <= 0 ? fallback : Math.Clamp(value, 1, 60);
    }

    private static unsafe LUID GetAdapterLuid(ID3D11Device* device)
    {
        IDXGIDevice* dxgiDevice;
        var dxgiDeviceGuid = typeof(IDXGIDevice).GUID;
        var hr = device->QueryInterface(&dxgiDeviceGuid, (void**)&dxgiDevice);
        if (hr.FAILED)
        {
            throw new InvalidOperationException($"Failed to query IDXGIDevice: {hr}");
        }

        try
        {
            IDXGIAdapter* adapter;
            hr = dxgiDevice->GetAdapter(&adapter);
            if (hr.FAILED)
            {
                throw new InvalidOperationException($"Failed to get DXGI adapter: {hr}");
            }

            try
            {
                DXGI_ADAPTER_DESC description;
                hr = adapter->GetDesc(&description);
                if (hr.FAILED)
                {
                    throw new InvalidOperationException($"Failed to describe DXGI adapter: {hr}");
                }

                return description.AdapterLuid;
            }
            finally
            {
                adapter->Release();
            }
        }
        finally
        {
            dxgiDevice->Release();
        }
    }

    private sealed class RemoteViewState : IDisposable
    {
        private readonly Guid viewId;
        private readonly object syncRoot = new();

        private BrowserViewCommand? lastCommand;
        private SharedTextureSurface? surface;
        private IntPtr pendingTextureHandle;
        private bool hasPendingTexture;

        public RemoteViewState(Guid viewId)
        {
            this.viewId = viewId;
        }

        public void QueueTexture(long textureHandle, int width, int height)
        {
            _ = width;
            _ = height;

            lock (syncRoot)
            {
                pendingTextureHandle = new IntPtr(textureHandle);
                hasPendingTexture = pendingTextureHandle != IntPtr.Zero;
            }
        }

        public unsafe void ApplyPendingTexture(ID3D11Device* device, IPluginLog log)
        {
            IntPtr handle;
            lock (syncRoot)
            {
                if (!hasPendingTexture)
                {
                    return;
                }

                handle = pendingTextureHandle;
                hasPendingTexture = false;
            }

            surface?.Dispose();
            surface = null;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                surface = new SharedTextureSurface(device, handle);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to open shared texture for browser view {ViewId}", viewId);
            }
        }

        public void Sync(BrowserRendererProcessHost processHost, BrowserViewCommand command)
        {
            if (lastCommand == command)
            {
                return;
            }

            lastCommand = command;
            processHost.Send(RendererCommand.SyncView(command));
        }

        public void EnsureHidden(BrowserRendererProcessHost processHost)
        {
            if (lastCommand == null || lastCommand.Hidden)
            {
                return;
            }

            lastCommand = lastCommand with
            {
                Hidden = true,
                FrameRate = Math.Clamp(lastCommand.HiddenFrameRate, 1, 60),
            };
            processHost.Send(RendererCommand.SyncView(lastCommand));
        }

        public bool TryRender(Vector2 requestedSize, float opacityFactor)
        {
            if (surface == null)
            {
                return false;
            }

            surface.Render(requestedSize, opacityFactor);
            return true;
        }

        public void Remove(BrowserRendererProcessHost processHost)
        {
            processHost.Send(RendererCommand.RemoveView(viewId));
            lastCommand = null;
        }

        public void Dispose()
        {
            surface?.Dispose();
            surface = null;
        }
    }

    private sealed class BrowserRendererProcessHost : IDisposable
    {
        private readonly string executablePath;
        private readonly IPluginLog log;
        private readonly object syncRoot = new();

        private Process? process;
        private PipeJsonChannel? channel;
        private CancellationTokenSource? connectionTokenSource;
        private DateTimeOffset nextStartUtc = DateTimeOffset.MinValue;
        private DateTimeOffset lastHealthCheckUtc = DateTimeOffset.MinValue;
        private bool disposed;

        public BrowserRendererProcessHost(string executablePath, IPluginLog log)
        {
            this.executablePath = executablePath;
            this.log = log;
        }

        public event Action<RendererEvent>? EventReceived;
        public bool IsConnected { get; private set; }
        public string? LastError { get; private set; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                Send(RendererCommand.Shutdown());
            }
            catch
            {
            }

            StopInternal();
        }

        public void EnsureStarted(RendererLaunchOptions launchOptions)
        {
            if (disposed)
            {
                return;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        return;
                    }

                    LastError = $"Renderer process exited with code {process.ExitCode}.";
                    nextStartUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                    StopInternal();
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Failed to read browser renderer process status before restart.");
                    nextStartUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                    StopInternal();
                }
            }

            if (DateTimeOffset.UtcNow < nextStartUtc)
            {
                return;
            }

            StopInternal();

            var pipeName = $"DalamudBrowserRenderer_{Environment.ProcessId}_{Guid.NewGuid():N}";
            var payload = launchOptions with { PipeName = pipeName };
            connectionTokenSource = new CancellationTokenSource();
            _ = AcceptAsync(pipeName, connectionTokenSource.Token);

            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = payload.ToBase64Json(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                    EnableRaisingEvents = true,
                };

                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        log.Information("[Renderer] {Message}", args.Data);
                    }
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        log.Warning("[Renderer] {Message}", args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                nextStartUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                log.Warning(ex, "Failed to start browser renderer process.");
                StopInternal();
            }
        }

        public void Poll()
        {
            if (disposed || process == null)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - lastHealthCheckUtc < TimeSpan.FromSeconds(1))
            {
                return;
            }

            lastHealthCheckUtc = DateTimeOffset.UtcNow;

            try
            {
                if (!process.HasExited)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to read browser renderer process status.");
                return;
            }

            LastError = $"Renderer process exited with code {process.ExitCode}.";
            nextStartUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            StopInternal();
        }

        public void Send(RendererCommand command)
        {
            PipeJsonChannel? activeChannel;
            lock (syncRoot)
            {
                activeChannel = channel;
            }

            if (!IsConnected || activeChannel == null)
            {
                return;
            }

            _ = activeChannel.SendAsync(command).ContinueWith(task =>
            {
                if (task.IsCanceled || disposed || task.Exception == null)
                {
                    return;
                }

                var exception = task.Exception.Flatten().GetBaseException();
                if (exception is OperationCanceledException or ObjectDisposedException or IOException)
                {
                    return;
                }

                if (task.Exception != null)
                {
                    log.Warning(task.Exception.Flatten(), "Failed to send command to the browser renderer process.");
                }
            }, TaskScheduler.Default);
        }

        private async Task AcceptAsync(string pipeName, CancellationToken cancellationToken)
        {
            try
            {
                var acceptedChannel = await PipeJsonChannel.CreateServerAsync(pipeName, cancellationToken);
                acceptedChannel.LineReceived += HandleChannelLine;
                acceptedChannel.Faulted += ex =>
                {
                    LastError = ex.Message;
                    log.Warning(ex, "Renderer IPC channel faulted.");
                };
                acceptedChannel.Closed += () =>
                {
                    IsConnected = false;
                };

                lock (syncRoot)
                {
                    if (disposed || cancellationToken.IsCancellationRequested)
                    {
                        acceptedChannel.Dispose();
                        return;
                    }

                    channel = acceptedChannel;
                    IsConnected = true;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                nextStartUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                log.Warning(ex, "Failed to accept browser renderer pipe connection.");
            }
        }

        private void HandleChannelLine(string line)
        {
            RendererEvent? message;
            try
            {
                message = System.Text.Json.JsonSerializer.Deserialize<RendererEvent>(line, JsonProtocol.Options);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to deserialize browser renderer event.");
                return;
            }

            if (message == null)
            {
                return;
            }

            if (message.Kind == "fatal")
            {
                LastError = message.Message;
            }

            EventReceived?.Invoke(message);
        }

        private void StopInternal()
        {
            lock (syncRoot)
            {
                channel?.Dispose();
                channel = null;
                IsConnected = false;
            }

            connectionTokenSource?.Cancel();
            connectionTokenSource?.Dispose();
            connectionTokenSource = null;

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.WaitForExit(500);
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch
                {
                }

                process.Dispose();
                process = null;
            }
        }
    }

    private sealed unsafe class SharedTextureSurface : IDisposable
    {
        private readonly ID3D11Texture2D* texture;
        private readonly ID3D11ShaderResourceView* view;
        private readonly ImTextureID textureId;

        public SharedTextureSurface(ID3D11Device* device, IntPtr handle)
        {
            if (device == null)
            {
                throw new InvalidOperationException("The Dalamud D3D11 device handle is null.");
            }

            var textureGuid = typeof(ID3D11Texture2D).GUID;
            void* texturePointer;
            var hr = device->OpenSharedResource((HANDLE)handle, &textureGuid, &texturePointer);
            if (hr.FAILED)
            {
                throw new InvalidOperationException($"Failed to open shared browser texture: {hr}");
            }

            texture = (ID3D11Texture2D*)texturePointer;

            D3D11_TEXTURE2D_DESC textureDescription;
            texture->GetDesc(&textureDescription);

            var shaderResourceDescription = new D3D11_SHADER_RESOURCE_VIEW_DESC
            {
                Format = textureDescription.Format,
                ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
                Texture2D = new D3D11_TEX2D_SRV
                {
                    MostDetailedMip = 0,
                    MipLevels = textureDescription.MipLevels,
                },
            };

            ID3D11ShaderResourceView* shaderResourceView;
            hr = device->CreateShaderResourceView((ID3D11Resource*)texture, &shaderResourceDescription, &shaderResourceView);
            if (hr.FAILED)
            {
                texture->Release();
                throw new InvalidOperationException($"Failed to create browser texture shader resource view: {hr}");
            }

            view = shaderResourceView;
            textureId = new ImTextureID((nint)view);
        }

        public void Dispose()
        {
            view->Release();
            texture->Release();
        }

        public void Render(Vector2 requestedSize, float opacityFactor)
        {
            var size = new Vector2(
                MathF.Max(16f, requestedSize.X),
                MathF.Max(16f, requestedSize.Y));

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(opacityFactor, 0.01f, 1f));
            ImGui.Image(textureId, size);
            ImGui.PopStyleVar();
        }
    }
}
