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

    public void Draw(string url, BrowserViewStatusSnapshot status, Vector2 availableSize)
    {
        using var child = ImRaii.Child("BrowserSurface", availableSize, true);
        if (!child.Success)
        {
            return;
        }

        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(url) ? "No URL configured." : url);
        ImGui.TextColored(GetStatusColor(status.Availability), GetStatusLabel(status));

        if (status.LastAvailableUtc.HasValue)
        {
            ImGui.TextDisabled($"Last reachable: {status.LastAvailableUtc.Value.ToLocalTime():T}");
        }

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(status.LastError);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Collections, window layout, lock/click-through and URL retry logic are already active.");
        ImGui.TextWrapped("The actual HTML/JavaScript surface is not connected yet.");
        ImGui.TextWrapped("Recommended production backend: CEF off-screen rendering (OSR).");
    }

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
