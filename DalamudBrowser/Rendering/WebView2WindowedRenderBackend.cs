using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using DalamudBrowser.Interop;
using DalamudBrowser.Models;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;

namespace DalamudBrowser.Rendering;

public sealed class WebView2WindowedRenderBackend : IBrowserRenderBackend
{
    private sealed record PendingFrame(Guid[] KnownViewIds, BrowserRenderRequest[] Requests);

    private readonly IPluginLog log;
    private readonly BrowserThreadDispatcher dispatcher;
    private readonly object frameSync = new();
    private readonly List<BrowserRenderRequest> frameRequests = [];

    private Guid[] frameKnownViewIds = [];
    private PendingFrame? pendingFrame;
    private bool applyPosted;
    private bool disposed;

    private readonly Dictionary<Guid, BrowserViewHost> hosts = new();
    private CoreWebView2Environment? environment;
    private Task? environmentInitializationTask;
    private DateTimeOffset nextEnvironmentRetryUtc = DateTimeOffset.MinValue;
    private volatile string? environmentError;

    public WebView2WindowedRenderBackend(IPluginLog log)
    {
        this.log = log;
        dispatcher = new BrowserThreadDispatcher("DalamudBrowser.WebView2");
    }

    public string Name => "WebView2 windowed host";
    public bool SupportsJavaScript => true;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        dispatcher.Post(DisposeBrowserState);
        dispatcher.Dispose();
    }

    public void BeginFrame(IReadOnlyCollection<Guid> knownViewIds)
    {
        frameKnownViewIds = knownViewIds.Count == 0 ? [] : knownViewIds.ToArray();
        frameRequests.Clear();
    }

    public void Draw(BrowserRenderRequest request)
    {
        frameRequests.Add(request);
        DrawHostPlaceholder(request);
    }

    public void EndFrame()
    {
        lock (frameSync)
        {
            pendingFrame = new PendingFrame(frameKnownViewIds.ToArray(), frameRequests.ToArray());
            if (applyPosted)
            {
                return;
            }

            applyPosted = true;
        }

        dispatcher.Post(ProcessPendingFrame);
    }

    private void DrawHostPlaceholder(BrowserRenderRequest request)
    {
        var size = new Vector2(
            MathF.Max(16f, request.SurfaceSize.X),
            MathF.Max(16f, request.SurfaceSize.Y));

        using var child = ImRaii.Child($"WebView2Surface-{request.ViewId}", size, true);
        if (!child.Success)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(environmentError))
        {
            ImGui.TextWrapped("WebView2 runtime is not ready.");
            ImGui.Spacing();
            ImGui.TextWrapped(environmentError);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            ImGui.TextUnformatted("No URL configured.");
            return;
        }

        if (request.Status.Availability == BrowserAvailabilityState.Unavailable)
        {
            ImGui.TextUnformatted("Waiting for the link to become reachable...");
            if (!string.IsNullOrWhiteSpace(request.Status.LastError))
            {
                ImGui.Spacing();
                ImGui.TextWrapped(request.Status.LastError);
            }

            return;
        }

        if (request.Status.Availability == BrowserAvailabilityState.Checking
            || (request.Status.IsChecking && request.Status.Availability != BrowserAvailabilityState.Available))
        {
            ImGui.TextUnformatted("Checking link availability...");
            return;
        }

        ImGui.TextDisabled("WebView2 surface syncing...");
    }

    private void ProcessPendingFrame()
    {
        while (true)
        {
            PendingFrame? frame;
            lock (frameSync)
            {
                frame = pendingFrame;
                pendingFrame = null;
                if (frame == null)
                {
                    applyPosted = false;
                    return;
                }
            }

            try
            {
                ApplyFrame(frame);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to apply WebView2 frame state.");
            }
        }
    }

    private void ApplyFrame(PendingFrame frame)
    {
        if (disposed)
        {
            return;
        }

        EnsureEnvironmentStarted();
        TrimUnknownHosts(frame.KnownViewIds);

        var activeViewIds = frame.Requests.Select(request => request.ViewId).ToHashSet();
        foreach (var pair in hosts)
        {
            if (!activeViewIds.Contains(pair.Key))
            {
                pair.Value.Hide();
            }
        }

        var parentWindow = ResolveGameWindowHandle();
        foreach (var request in frame.Requests)
        {
            if (!hosts.TryGetValue(request.ViewId, out var host))
            {
                host = new BrowserViewHost(request.ViewId, log, HandleEnvironmentFailure);
                hosts.Add(request.ViewId, host);
            }

            host.Apply(parentWindow, environment, request);
        }
    }

    private void EnsureEnvironmentStarted()
    {
        if (environment != null || environmentInitializationTask != null || DateTimeOffset.UtcNow < nextEnvironmentRetryUtc)
        {
            return;
        }

        environmentInitializationTask = InitializeEnvironmentAsync();
    }

    private async Task InitializeEnvironmentAsync()
    {
        try
        {
            var userDataFolder = GetUserDataFolder();
            Directory.CreateDirectory(userDataFolder);
            environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
            environmentError = null;
        }
        catch (Exception ex)
        {
            environment = null;
            nextEnvironmentRetryUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            environmentError = ex.Message;
            log.Warning(ex, "Failed to initialize WebView2 environment.");
        }
        finally
        {
            environmentInitializationTask = null;
        }
    }

    private void TrimUnknownHosts(IReadOnlyCollection<Guid> knownViewIds)
    {
        HashSet<Guid> known = knownViewIds.Count == 0 ? [] : knownViewIds.ToHashSet();
        var removedIds = hosts.Keys.Where(viewId => !known.Contains(viewId)).ToArray();
        foreach (var removedId in removedIds)
        {
            hosts[removedId].Dispose();
            hosts.Remove(removedId);
        }
    }

    private void DisposeBrowserState()
    {
        foreach (var host in hosts.Values)
        {
            host.Dispose();
        }

        hosts.Clear();
        environment = null;
        environmentInitializationTask = null;
    }

    private void HandleEnvironmentFailure(Guid viewId, Exception ex)
    {
        environment = null;
        environmentInitializationTask = null;
        nextEnvironmentRetryUtc = DateTimeOffset.UtcNow.AddSeconds(5);
        environmentError = ex.Message;
        log.Warning(ex, "WebView2 controller failed for browser view {ViewId}. Environment will be recreated.", viewId);
    }

    private static string GetUserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DalamudBrowser",
            "WebView2");
    }

    private static nint ResolveGameWindowHandle()
    {
        var process = Process.GetCurrentProcess();
        if (process.MainWindowHandle != 0 && NativeMethods.IsWindow(process.MainWindowHandle))
        {
            return process.MainWindowHandle;
        }

        nint resolvedWindow = 0;
        NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId != process.Id)
            {
                return true;
            }

            resolvedWindow = windowHandle;
            return false;
        }, 0);

        return resolvedWindow;
    }

    private sealed class BrowserViewHost(
        Guid viewId,
        IPluginLog log,
        Action<Guid, Exception> environmentFailureHandler) : IDisposable
    {
        private readonly Guid viewId = viewId;
        private readonly IPluginLog log = log;
        private readonly Action<Guid, Exception> environmentFailureHandler = environmentFailureHandler;

        private nint parentWindow;
        private nint hostWindow;
        private CoreWebView2Controller? controller;
        private CoreWebView2? webView;
        private Task? controllerInitializationTask;
        private DateTimeOffset nextControllerRetryUtc = DateTimeOffset.MinValue;
        private int controllerGeneration;

        private BrowserRenderRequest? lastRequest;
        private string liveUrl = string.Empty;
        private string statusSignature = string.Empty;
        private bool showingLiveContent;

        public void Dispose()
        {
            DisposeController();
            DestroyHostWindow();
        }

        public void Apply(nint requestedParentWindow, CoreWebView2Environment? environment, BrowserRenderRequest request)
        {
            lastRequest = request;

            if (requestedParentWindow == 0)
            {
                Hide();
                return;
            }

            EnsureHostWindow(requestedParentWindow);
            UpdateHostBounds(request);
            ApplyClickThrough(request.Locked && request.ClickThrough);

            if (environment == null && controller == null)
            {
                Hide();
                return;
            }

            if (environment != null)
            {
                EnsureControllerStarted(environment);
            }

            if (controller == null)
            {
                Hide();
                return;
            }

            if (!ShouldDisplay(request))
            {
                Hide();
                return;
            }

            NativeMethods.ShowWindow(hostWindow, NativeMethods.SwShow);
            controller.IsVisible = true;
            controller.Bounds = new Rectangle(0, 0, Math.Max(1, (int)MathF.Round(request.SurfaceSize.X)), Math.Max(1, (int)MathF.Round(request.SurfaceSize.Y)));
            controller.NotifyParentWindowPositionChanged();

            UpdateContent(request);
        }

        public void Hide()
        {
            if (controller != null)
            {
                controller.IsVisible = false;
            }

            if (hostWindow != 0)
            {
                NativeMethods.ShowWindow(hostWindow, NativeMethods.SwHide);
            }
        }

        private void EnsureHostWindow(nint requestedParentWindow)
        {
            if (hostWindow != 0 && parentWindow == requestedParentWindow && NativeMethods.IsWindow(hostWindow))
            {
                return;
            }

            DisposeController();
            DestroyHostWindow();

            parentWindow = requestedParentWindow;
            hostWindow = NativeMethods.CreateWindowEx(
                0,
                "Static",
                $"DalamudBrowserHost-{viewId}",
                NativeMethods.WsChild | NativeMethods.WsClipChildren | NativeMethods.WsClipSiblings,
                0,
                0,
                16,
                16,
                requestedParentWindow,
                0,
                0,
                0);

            if (hostWindow == 0)
            {
                throw new InvalidOperationException($"Failed to create browser host window for view {viewId}.");
            }

            NativeMethods.ShowWindow(hostWindow, NativeMethods.SwHide);
        }

        private void UpdateHostBounds(BrowserRenderRequest request)
        {
            var x = (int)MathF.Round(request.SurfacePosition.X);
            var y = (int)MathF.Round(request.SurfacePosition.Y);
            var width = Math.Max(1, (int)MathF.Round(request.SurfaceSize.X));
            var height = Math.Max(1, (int)MathF.Round(request.SurfaceSize.Y));
            NativeMethods.MoveWindow(hostWindow, x, y, width, height, true);
        }

        private void EnsureControllerStarted(CoreWebView2Environment environment)
        {
            if (controller != null || controllerInitializationTask != null || hostWindow == 0 || DateTimeOffset.UtcNow < nextControllerRetryUtc)
            {
                return;
            }

            controllerInitializationTask = InitializeControllerAsync(environment, controllerGeneration);
        }

        private async Task InitializeControllerAsync(CoreWebView2Environment environment, int generation)
        {
            try
            {
                var createdController = await environment.CreateCoreWebView2ControllerAsync(hostWindow);
                if (generation != controllerGeneration || hostWindow == 0)
                {
                    createdController.Close();
                    return;
                }

                controller = createdController;
                webView = createdController.CoreWebView2;

                controller.DefaultBackgroundColor = Color.Transparent;
                controller.Bounds = new Rectangle(0, 0, 1, 1);
                controller.IsVisible = false;

                webView.Settings.AreDefaultContextMenusEnabled = false;
                webView.Settings.AreDevToolsEnabled = false;
                webView.Settings.IsStatusBarEnabled = false;
                webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
                webView.NewWindowRequested += OnNewWindowRequested;
                webView.NavigationCompleted += OnNavigationCompleted;

                if (lastRequest.HasValue)
                {
                    UpdateContent(lastRequest.Value);
                }
            }
            catch (Exception ex)
            {
                nextControllerRetryUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                DisposeController();
                environmentFailureHandler(viewId, ex);
                log.Warning(ex, "Failed to create WebView2 controller for browser view {ViewId}", viewId);
            }
            finally
            {
                controllerInitializationTask = null;
            }
        }

        private void UpdateContent(BrowserRenderRequest request)
        {
            if (webView == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                ShowStatusPage("No URL configured.", request.Url, request);
                return;
            }

            var shouldKeepLiveContent = showingLiveContent
                && string.Equals(liveUrl, request.Url, StringComparison.Ordinal)
                && request.Status.IsChecking;

            if (request.Status.Availability == BrowserAvailabilityState.Available || shouldKeepLiveContent)
            {
                NavigateLive(request.Url);
                return;
            }

            var message = request.Status.Availability switch
            {
                BrowserAvailabilityState.Unavailable => string.IsNullOrWhiteSpace(request.Status.LastError)
                    ? "The target is unavailable right now. Automatic retry is still active."
                    : request.Status.LastError,
                BrowserAvailabilityState.Checking => "Checking link availability...",
                _ => "Waiting for the first successful availability check..."
            };

            ShowStatusPage(message, request.Url, request);
        }

        private void NavigateLive(string url)
        {
            if (webView == null)
            {
                return;
            }

            if (showingLiveContent && string.Equals(liveUrl, url, StringComparison.Ordinal))
            {
                return;
            }

            liveUrl = url;
            statusSignature = string.Empty;
            showingLiveContent = true;
            webView.Navigate(url);
        }

        private void ShowStatusPage(string message, string url, BrowserRenderRequest request)
        {
            if (webView == null)
            {
                return;
            }

            var signature = string.Join("|",
                url,
                request.Status.Availability,
                request.Status.IsChecking,
                request.Status.LastError ?? string.Empty,
                message);

            if (!showingLiveContent && string.Equals(statusSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            liveUrl = url;
            statusSignature = signature;
            showingLiveContent = false;
            webView.NavigateToString(BuildStatusHtml(request.Title, url, message));
        }

        private void ApplyClickThrough(bool clickThrough)
        {
            if (hostWindow == 0)
            {
                return;
            }

            ApplyWindowClickThrough(hostWindow, clickThrough);
            NativeMethods.EnumChildWindows(hostWindow, (windowHandle, lParam) =>
            {
                ApplyWindowClickThrough(windowHandle, lParam != 0);
                return true;
            }, clickThrough ? 1 : 0);
        }

        private static void ApplyWindowClickThrough(nint windowHandle, bool clickThrough)
        {
            if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle))
            {
                return;
            }

            var currentStyle = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64();
            var nextStyle = clickThrough
                ? currentStyle | NativeMethods.WsExTransparent
                : currentStyle & ~NativeMethods.WsExTransparent;

            if (nextStyle != currentStyle)
            {
                NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle, new nint(nextStyle));
            }

            NativeMethods.EnableWindow(windowHandle, !clickThrough);
            NativeMethods.SetWindowPos(
                windowHandle,
                0,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove
                | NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpFrameChanged);
        }

        private static bool ShouldDisplay(BrowserRenderRequest request)
        {
            return request.SurfaceSize.X >= 8f && request.SurfaceSize.Y >= 8f;
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (!string.IsNullOrWhiteSpace(args.Uri))
            {
                NavigateLive(args.Uri);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess || webView == null || !lastRequest.HasValue)
            {
                return;
            }

            showingLiveContent = false;
            statusSignature = string.Empty;
            ShowStatusPage($"Navigation failed: {args.WebErrorStatus}", lastRequest.Value.Url, lastRequest.Value);
        }

        private void DisposeController()
        {
            controllerGeneration++;

            if (webView != null)
            {
                webView.NewWindowRequested -= OnNewWindowRequested;
                webView.NavigationCompleted -= OnNavigationCompleted;
                webView = null;
            }

            if (controller != null)
            {
                controller.IsVisible = false;
                controller.Close();
                controller = null;
            }

            controllerInitializationTask = null;
            showingLiveContent = false;
            liveUrl = string.Empty;
            statusSignature = string.Empty;
        }

        private void DestroyHostWindow()
        {
            if (hostWindow == 0)
            {
                return;
            }

            NativeMethods.DestroyWindow(hostWindow);
            hostWindow = 0;
            parentWindow = 0;
        }

        private static string BuildStatusHtml(string title, string url, string message)
        {
            var escapedTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Browser View" : title);
            var escapedUrl = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(url) ? "No URL configured." : url);
            var escapedMessage = WebUtility.HtmlEncode(message);

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""color-scheme"" content=""dark"">
  <title>{escapedTitle}</title>
  <style>
    :root {{
      color-scheme: dark;
      font-family: ""Segoe UI"", sans-serif;
      background: #11151b;
      color: #f3f6fb;
    }}

    body {{
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      background:
        radial-gradient(circle at top, #203246 0%, #11151b 58%),
        linear-gradient(160deg, #101820, #141a22);
    }}

    main {{
      width: min(680px, calc(100vw - 48px));
      border: 1px solid rgba(255, 255, 255, 0.14);
      border-radius: 18px;
      padding: 28px;
      background: rgba(8, 12, 18, 0.84);
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.38);
      backdrop-filter: blur(10px);
    }}

    h1 {{
      margin: 0 0 12px;
      font-size: 24px;
      font-weight: 700;
    }}

    p {{
      margin: 0;
      line-height: 1.5;
      color: rgba(243, 246, 251, 0.84);
    }}

    code {{
      display: block;
      margin-top: 18px;
      padding: 14px 16px;
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.06);
      color: #9fd0ff;
      word-break: break-word;
      white-space: pre-wrap;
      font-family: ""Cascadia Code"", ""Consolas"", monospace;
    }}
  </style>
</head>
<body>
  <main>
    <h1>{escapedTitle}</h1>
    <p>{escapedMessage}</p>
    <code>{escapedUrl}</code>
  </main>
</body>
</html>";
        }
    }
}
