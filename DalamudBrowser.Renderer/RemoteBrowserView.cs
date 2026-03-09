using CefSharp;
using CefSharp.OffScreen;
using CefSharp.Structs;
using DalamudBrowser.Common;
using BrowserSettings = CefSharp.BrowserSettings;
using RequestContext = CefSharp.RequestContext;
using RequestContextSettings = CefSharp.RequestContextSettings;
using Size = System.Drawing.Size;
using WindowInfo = CefSharp.WindowInfo;

namespace DalamudBrowser.Renderer;

internal sealed class RemoteBrowserView : IDisposable
{
    private readonly Guid viewId;
    private readonly string cacheRootPath;

    private SharedTextureRenderHandler? renderHandler;
    private ChromiumWebBrowser? browser;
    private RequestContext? requestContext;
    private BrowserViewCommand? command;
    private Size currentViewSize = new(1, 1);
    private Size currentPixelSize = new(1, 1);
    private float deviceScaleFactor = 1f;
    private string liveUrl = string.Empty;
    private bool? muted;
    private double? zoomLevel;
    private bool hidden = true;
    private int frameRate;
    private int reloadGeneration = -1;

    public RemoteBrowserView(Guid viewId, string cacheRootPath)
    {
        this.viewId = viewId;
        this.cacheRootPath = cacheRootPath;
    }

    public IntPtr SharedTextureHandle => renderHandler?.SharedTextureHandle ?? IntPtr.Zero;
    public int PixelWidth => currentPixelSize.Width;
    public int PixelHeight => currentPixelSize.Height;

    public bool Apply(BrowserViewCommand nextCommand)
    {
        command = nextCommand;
        var textureChanged = EnsureSurface(
            nextCommand.ViewWidth,
            nextCommand.ViewHeight,
            nextCommand.PixelWidth,
            nextCommand.PixelHeight,
            nextCommand.DeviceScaleFactor);
        EnsureBrowser(nextCommand.FrameRate);

        if (browser is { IsBrowserInitialized: true })
        {
            ApplyState();
        }

        return textureChanged;
    }

    public void Dispose()
    {
        DisposeBrowser();
        renderHandler?.Dispose();
        renderHandler = null;
    }

    private bool EnsureSurface(int viewWidth, int viewHeight, int pixelWidth, int pixelHeight, float nextDeviceScaleFactor)
    {
        var nextViewSize = new Size(Math.Max(1, viewWidth), Math.Max(1, viewHeight));
        var nextPixelSize = new Size(Math.Max(1, pixelWidth), Math.Max(1, pixelHeight));
        var clampedScaleFactor = Math.Clamp(nextDeviceScaleFactor, 0.5f, 4f);
        if (renderHandler == null)
        {
            renderHandler = new SharedTextureRenderHandler(nextViewSize, nextPixelSize, clampedScaleFactor);
            currentViewSize = nextViewSize;
            currentPixelSize = nextPixelSize;
            deviceScaleFactor = clampedScaleFactor;
            return true;
        }

        if (currentViewSize == nextViewSize
            && currentPixelSize == nextPixelSize
            && Math.Abs(deviceScaleFactor - clampedScaleFactor) < 0.001f)
        {
            return false;
        }

        currentViewSize = nextViewSize;
        currentPixelSize = nextPixelSize;
        deviceScaleFactor = clampedScaleFactor;
        renderHandler.Resize(nextViewSize, nextPixelSize, clampedScaleFactor);
        if (browser is { IsBrowserInitialized: true })
        {
            browser.Size = nextViewSize;
        }

        return true;
    }

    private void EnsureBrowser(int nextFrameRate)
    {
        if (browser != null)
        {
            ApplyFrameRate(nextFrameRate);
            return;
        }

        if (renderHandler == null)
        {
            return;
        }

        frameRate = nextFrameRate;
        var requestContextSettings = new RequestContextSettings
        {
            CachePath = Path.Combine(cacheRootPath, viewId.ToString("N")),
            PersistSessionCookies = true,
        };

        requestContext = new RequestContext(requestContextSettings);
        browser = new ChromiumWebBrowser("about:blank", automaticallyCreateBrowser: false, requestContext: requestContext)
        {
            RenderHandler = renderHandler,
        };

        browser.BrowserInitialized += OnBrowserInitialized;
        browser.LoadingStateChanged += OnLoadingStateChanged;

        var windowInfo = new WindowInfo
        {
            Width = currentViewSize.Width,
            Height = currentViewSize.Height,
        };
        windowInfo.SetAsWindowless(IntPtr.Zero);

        using var browserSettings = new BrowserSettings
        {
            WindowlessFrameRate = frameRate,
        };

        browser.CreateBrowser(windowInfo, browserSettings);
        windowInfo.Dispose();
    }

    private void OnBrowserInitialized(object? sender, EventArgs e)
    {
        if (browser == null)
        {
            return;
        }

        browser.Size = currentViewSize;
        ApplyState();
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading || browser == null || command == null)
        {
            return;
        }

        ApplyZoom(command.ZoomFactor);
        ApplyMute(command.Muted);
    }

    private void ApplyState()
    {
        if (browser == null || command == null || !browser.IsBrowserInitialized)
        {
            return;
        }

        ApplyFrameRate(command.FrameRate);
        ApplyHidden(command.Hidden);
        ApplyMute(command.Muted);
        ApplyZoom(command.ZoomFactor);
        ApplyUrl(command.Url, command.ReloadGeneration);
    }

    private void ApplyFrameRate(int nextFrameRate)
    {
        var clampedFrameRate = Math.Clamp(nextFrameRate, 1, 60);
        if (frameRate == clampedFrameRate)
        {
            return;
        }

        frameRate = clampedFrameRate;
        if (browser == null || !browser.IsBrowserInitialized)
        {
            return;
        }

        browser.GetBrowserHost().WindowlessFrameRate = clampedFrameRate;
    }

    private void ApplyHidden(bool nextHidden)
    {
        if (hidden == nextHidden || browser == null || !browser.IsBrowserInitialized)
        {
            hidden = nextHidden;
            return;
        }

        hidden = nextHidden;
        browser.GetBrowserHost().WindowlessFrameRate = nextHidden
            ? Math.Clamp(command?.HiddenFrameRate ?? frameRate, 1, 60)
            : Math.Clamp(frameRate, 1, 60);
        browser.GetBrowserHost().WasHidden(nextHidden);
    }

    private void ApplyMute(bool nextMuted)
    {
        if (muted == nextMuted || browser == null || !browser.IsBrowserInitialized)
        {
            muted = nextMuted;
            return;
        }

        muted = nextMuted;
        browser.GetBrowserHost().SetAudioMuted(nextMuted);
    }

    private void ApplyZoom(float zoomFactor)
    {
        if (browser == null || !browser.IsBrowserInitialized)
        {
            return;
        }

        var clampedFactor = Math.Clamp((double)zoomFactor, 0.25d, 5d);
        var nextLevel = Math.Abs(clampedFactor - 1d) < 0.001d
            ? 0d
            : Math.Log(clampedFactor, 1.2d);

        if (zoomLevel.HasValue && Math.Abs(zoomLevel.Value - nextLevel) < 0.001d)
        {
            return;
        }

        zoomLevel = nextLevel;
        browser.SetZoomLevel(nextLevel);
    }

    private void ApplyUrl(string url, int nextReloadGeneration)
    {
        if (browser == null || !browser.IsBrowserInitialized || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (string.Equals(liveUrl, url, StringComparison.Ordinal)
            && reloadGeneration == nextReloadGeneration)
        {
            return;
        }

        liveUrl = url;
        reloadGeneration = nextReloadGeneration;
        browser.Load(url);
    }

    private void DisposeBrowser()
    {
        if (browser == null)
        {
            return;
        }

        browser.BrowserInitialized -= OnBrowserInitialized;
        browser.LoadingStateChanged -= OnLoadingStateChanged;
        browser.RenderHandler = null;
        browser.Dispose();
        browser = null;

        requestContext?.Dispose();
        requestContext = null;

        liveUrl = string.Empty;
        muted = null;
        zoomLevel = null;
        hidden = true;
        reloadGeneration = -1;
        currentViewSize = new Size(1, 1);
        currentPixelSize = new Size(1, 1);
        deviceScaleFactor = 1f;
    }
}
