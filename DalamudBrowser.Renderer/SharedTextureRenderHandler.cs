using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Range = CefSharp.Structs.Range;
using Size = System.Drawing.Size;

namespace DalamudBrowser.Renderer;

internal unsafe sealed class SharedTextureRenderHandler : IRenderHandler, IDisposable
{
    private readonly object renderLock = new();
    private readonly List<nint> obsoleteTextures = new();

    private ID3D11Texture2D* sharedTexture;
    private ID3D11Texture2D* viewTexture;
    private ID3D11Texture2D* popupTexture;
    private Rect popupRect;
    private bool popupVisible;
    private IntPtr sharedTextureHandle;

    public SharedTextureRenderHandler(Size size)
    {
        sharedTexture = BuildTexture(size, isShared: true);
        viewTexture = BuildTexture(size, isShared: false);
    }

    public IntPtr SharedTextureHandle
    {
        get
        {
            if (sharedTextureHandle != IntPtr.Zero)
            {
                return sharedTextureHandle;
            }

            IDXGIResource* resource;
            var resourceGuid = typeof(IDXGIResource).GUID;
            var hr = ((IUnknown*)sharedTexture)->QueryInterface(&resourceGuid, (void**)&resource);
            if (hr.FAILED)
            {
                throw new InvalidOperationException($"Failed to query IDXGIResource for shared texture: {hr}");
            }

            HANDLE handle;
            resource->GetSharedHandle(&handle);
            resource->Release();
            sharedTextureHandle = (IntPtr)handle.Value;
            return sharedTextureHandle;
        }
    }

    public void Dispose()
    {
        if (popupTexture != null)
        {
            popupTexture->Release();
            popupTexture = null;
        }

        if (viewTexture != null)
        {
            viewTexture->Release();
            viewTexture = null;
        }

        if (sharedTexture != null)
        {
            sharedTexture->Release();
            sharedTexture = null;
        }

        foreach (var texture in obsoleteTextures)
        {
            ((ID3D11Texture2D*)texture)->Release();
        }

        obsoleteTextures.Clear();
    }

    public void Resize(Size size)
    {
        lock (renderLock)
        {
            obsoleteTextures.Add((nint)sharedTexture);
            obsoleteTextures.Add((nint)viewTexture);

            sharedTexture = BuildTexture(size, isShared: true);
            viewTexture = BuildTexture(size, isShared: false);
            sharedTextureHandle = IntPtr.Zero;
        }
    }

    public Rect GetViewRect()
    {
        D3D11_TEXTURE2D_DESC desc;
        sharedTexture->GetDesc(&desc);
        return new Rect(0, 0, (int)desc.Width, (int)desc.Height);
    }

    public ScreenInfo? GetScreenInfo()
    {
        return new ScreenInfo
        {
            DeviceScaleFactor = 1f,
        };
    }

    public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
    {
        screenX = viewX;
        screenY = viewY;
        return false;
    }

    public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, AcceleratedPaintInfo acceleratedPaintInfo)
    {
        throw new NotSupportedException("Accelerated paint is not used by the shared texture renderer.");
    }

    public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
    {
        lock (renderLock)
        {
            var targetTexture = type == PaintElementType.Popup ? popupTexture : viewTexture;
            if (targetTexture == null)
            {
                return;
            }

            var rowPitch = width * 4;
            var depthPitch = rowPitch * height;
            var sourceRegionPtr = buffer + (dirtyRect.X * 4) + (dirtyRect.Y * rowPitch);

            D3D11_TEXTURE2D_DESC textureDescription;
            targetTexture->GetDesc(&textureDescription);

            var destinationBox = new D3D11_BOX
            {
                top = (uint)Math.Min(dirtyRect.Y, (int)textureDescription.Height),
                bottom = (uint)Math.Min(dirtyRect.Y + dirtyRect.Height, (int)textureDescription.Height),
                left = (uint)Math.Min(dirtyRect.X, (int)textureDescription.Width),
                right = (uint)Math.Min(dirtyRect.X + dirtyRect.Width, (int)textureDescription.Width),
                front = 0,
                back = 1,
            };

            ID3D11DeviceContext* context;
            DxHandler.Device->GetImmediateContext(&context);

            context->UpdateSubresource(
                (ID3D11Resource*)targetTexture,
                0,
                &destinationBox,
                sourceRegionPtr.ToPointer(),
                (uint)rowPitch,
                (uint)depthPitch);

            context->CopySubresourceRegion(
                (ID3D11Resource*)sharedTexture,
                0,
                0,
                0,
                0,
                (ID3D11Resource*)viewTexture,
                0,
                null);

            if (popupVisible && popupTexture != null)
            {
                context->CopySubresourceRegion(
                    (ID3D11Resource*)sharedTexture,
                    0,
                    (uint)Math.Max(0, popupRect.X),
                    (uint)Math.Max(0, popupRect.Y),
                    0,
                    (ID3D11Resource*)popupTexture,
                    0,
                    null);
            }

            context->Flush();
            context->Release();

            foreach (var texture in obsoleteTextures)
            {
                ((ID3D11Texture2D*)texture)->Release();
            }

            obsoleteTextures.Clear();
        }
    }

    public void OnPopupShow(bool show)
    {
        popupVisible = show;
    }

    public void OnPopupSize(Rect rect)
    {
        popupRect = rect;
        if (popupTexture != null)
        {
            popupTexture->Release();
            popupTexture = null;
        }

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        popupTexture = BuildTexture(new Size(rect.Width, rect.Height), isShared: false);
    }

    public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
    {
    }

    public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds)
    {
    }

    public void OnCursorChange(IntPtr cursorPtr, CursorType type, CursorInfo customCursorInfo)
    {
    }

    public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y)
    {
        return false;
    }

    public void UpdateDragCursor(DragOperationsMask operation)
    {
    }

    private static ID3D11Texture2D* BuildTexture(Size size, bool isShared)
    {
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)Math.Max(1, size.Width),
            Height = (uint)Math.Max(1, size.Height),
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            CPUAccessFlags = 0,
            MiscFlags = isShared ? (uint)D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_SHARED : 0,
        };

        ID3D11Texture2D* texture;
        var hr = DxHandler.Device->CreateTexture2D(&description, null, &texture);
        if (hr.FAILED)
        {
            throw new InvalidOperationException($"Failed to create renderer texture: {hr}");
        }

        return texture;
    }
}
