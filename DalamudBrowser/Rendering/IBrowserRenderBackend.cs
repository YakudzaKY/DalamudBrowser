using System;
using System.Numerics;
using DalamudBrowser.Models;

namespace DalamudBrowser.Rendering;

public interface IBrowserRenderBackend : IDisposable
{
    string Name { get; }
    bool SupportsJavaScript { get; }
    void Draw(string url, BrowserViewStatusSnapshot status, Vector2 availableSize);
}
