using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DalamudBrowser.Rendering;

public sealed class SafeBrowserRenderBackend : IBrowserRenderBackend
{
    private readonly IPluginLog log;
    private readonly IBrowserRenderBackend inner;
    private readonly IBrowserRenderBackend fallback = new PlaceholderBrowserRenderBackend();

    private bool failed;
    private string? failureMessage;

    public SafeBrowserRenderBackend(IPluginLog log, IBrowserRenderBackend inner)
    {
        this.log = log;
        this.inner = inner;
    }

    public string Name => failed ? $"{inner.Name} (disabled)" : inner.Name;
    public bool SupportsJavaScript => !failed && inner.SupportsJavaScript;

    public void Dispose()
    {
        try
        {
            inner.Dispose();
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "dispose");
        }

        fallback.Dispose();
    }

    public void BeginFrame(IReadOnlyCollection<Guid> knownViewIds)
    {
        if (failed)
        {
            fallback.BeginFrame(knownViewIds);
            return;
        }

        try
        {
            inner.BeginFrame(knownViewIds);
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "begin frame");
            fallback.BeginFrame(knownViewIds);
        }
    }

    public void Draw(BrowserRenderRequest request)
    {
        if (failed)
        {
            DrawFailureSurface(request);
            return;
        }

        try
        {
            inner.Draw(request);
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "draw");
            DrawFailureSurface(request);
        }
    }

    public void EndFrame()
    {
        if (failed)
        {
            fallback.EndFrame();
            return;
        }

        try
        {
            inner.EndFrame();
        }
        catch (Exception ex)
        {
            MarkFailed(ex, "end frame");
            fallback.EndFrame();
        }
    }

    private void MarkFailed(Exception ex, string stage)
    {
        if (!failed)
        {
            log.Error(ex, "Browser backend failed during {Stage}. Falling back to safe placeholder backend.", stage);
        }

        failed = true;
        failureMessage = ex.GetBaseException().Message;
    }

    private void DrawFailureSurface(BrowserRenderRequest request)
    {
        var size = new Vector2(
            MathF.Max(16f, request.SurfaceSize.X),
            MathF.Max(16f, request.SurfaceSize.Y));

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.09f, 0.08f, 0.08f, 1f)), 8f);
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.62f, 0.24f, 0.24f, 1f)), 8f, 0, 1.4f);
        drawList.AddText(min + new Vector2(14f, 12f), ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.82f, 1f)), "Browser backend disabled");
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            drawList.AddText(min + new Vector2(14f, 34f), ImGui.GetColorU32(new Vector4(0.92f, 0.86f, 0.86f, 1f)), failureMessage);
        }

        ImGui.Dummy(size);
    }
}
