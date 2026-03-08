using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using DalamudBrowser.Models;

namespace DalamudBrowser.Rendering;

public sealed class PlaceholderBrowserRenderBackend : IBrowserRenderBackend
{
    public string Name => "Placeholder workspace";
    public bool SupportsJavaScript => false;

    public void Dispose() { }

    public void BeginFrame(IReadOnlyCollection<Guid> knownViewIds) { }

    public void Draw(BrowserRenderRequest request)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(request.OpacityFactor, 0.01f, 1f));
        using var child = ImRaii.Child($"BrowserSurface-{request.ViewId}", request.SurfaceSize, true);
        if (!child.Success)
        {
            ImGui.PopStyleVar();
            return;
        }

        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(request.Url) ? "No URL configured." : request.Url);
        ImGui.TextColored(GetStatusColor(request.Status.Availability), GetStatusLabel(request.Status));

        if (request.Status.LastAvailableUtc.HasValue)
        {
            ImGui.TextDisabled($"Last reachable: {request.Status.LastAvailableUtc.Value.ToLocalTime():T}");
        }

        if (!string.IsNullOrWhiteSpace(request.Status.LastError))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(request.Status.LastError);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Collections, window layout, lock/click-through and URL retry logic are already active.");
        ImGui.TextWrapped("The actual HTML/JavaScript surface is not connected yet.");
        ImGui.TextWrapped("Recommended production backend: CEF off-screen rendering (OSR).");
        ImGui.PopStyleVar();
    }

    public void EndFrame() { }

    private static string GetStatusLabel(BrowserViewStatusSnapshot status)
    {
        return status.Availability switch
        {
            BrowserAvailabilityState.Available => "Reachable",
            BrowserAvailabilityState.Unavailable => "Unavailable",
            BrowserAvailabilityState.Checking => "Checking",
            _ => "Not checked yet",
        };
    }

    private static Vector4 GetStatusColor(BrowserAvailabilityState state)
    {
        return state switch
        {
            BrowserAvailabilityState.Available => new Vector4(0.35f, 0.9f, 0.45f, 1f),
            BrowserAvailabilityState.Unavailable => new Vector4(0.95f, 0.4f, 0.35f, 1f),
            BrowserAvailabilityState.Checking => new Vector4(0.4f, 0.75f, 1f, 1f),
            _ => new Vector4(0.85f, 0.85f, 0.4f, 1f),
        };
    }
}
