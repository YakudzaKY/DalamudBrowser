using System;
using System.Numerics;
using DalamudBrowser.Models;

namespace DalamudBrowser.Rendering;

public readonly record struct BrowserRenderRequest(
    Guid ViewId,
    string Title,
    string Url,
    bool Locked,
    bool ClickThrough,
    BrowserViewStatusSnapshot Status,
    Vector2 SurfacePosition,
    Vector2 SurfaceSize);
