using System;
using System.Collections.Generic;

namespace DalamudBrowser.Rendering;

public interface IBrowserRenderBackend : IDisposable
{
    string Name { get; }
    bool SupportsJavaScript { get; }
    void BeginFrame(IReadOnlyCollection<Guid> knownViewIds);
    void Draw(BrowserRenderRequest request);
    void EndFrame();
}
