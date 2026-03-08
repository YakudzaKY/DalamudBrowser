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
    private Size currentSize = new(1, 1);
    private string liveUrl = string.Empty;
    private bool? muted;
    private double? zoomLevel;
    private bool hidden = true;
    private int frameRate;

    public RemoteBrowserView(Guid viewId, string cacheRootPath)
    {
        this.viewId = viewId;
        this.cacheRootPath = cacheRootPath;
    }

    public IntPtr SharedTextureHandle => renderHandler?.SharedTextureHandle ?? IntPtr.Zero;
    public int PixelWidth => currentSize.Width;
    public int PixelHeight => currentSize.Height;

    public bool Apply(BrowserViewCommand nextCommand)
    {
        command = nextCommand;
        var textureChanged = EnsureSurface(nextCommand.Width, nextCommand.Height);
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

    private bool EnsureSurface(int width, int height)
    {
        var nextSize = new Size(Math.Max(1, width), Math.Max(1, height));
        if (renderHandler == null)
        {
            renderHandler = new SharedTextureRenderHandler(nextSize);
            currentSize = nextSize;
            return true;
        }

        if (currentSize == nextSize)
        {
            return false;
        }

        currentSize = nextSize;
        renderHandler.Resize(nextSize);
        if (browser is { IsBrowserInitialized: true })
        {
            browser.Size = nextSize;
        }

        return true;
    }

    private void EnsureBrowser(int nextFrameRate)
    {
        if (browser != null && frameRate == nextFrameRate)
        {
            return;
        }

        if (browser != null && frameRate != nextFrameRate)
        {
            DisposeBrowser();
        }

        if (browser != null || renderHandler == null)
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
            Width = currentSize.Width,
            Height = currentSize.Height,
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

        browser.Size = currentSize;
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

        ApplyHidden(command.Hidden);
        ApplyMute(command.Muted);
        ApplyZoom(command.ZoomFactor);
        ApplyUrl(command.Url);
    }

    private void ApplyHidden(bool nextHidden)
    {
        if (hidden == nextHidden || browser == null || !browser.IsBrowserInitialized)
        {
            hidden = nextHidden;
            return;
        }

        hidden = nextHidden;
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

    private void ApplyUrl(string url)
    {
        if (browser == null || !browser.IsBrowserInitialized || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (string.Equals(liveUrl, url, StringComparison.Ordinal))
        {
            return;
        }

        liveUrl = url;
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
    }
}
