using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DalamudBrowser.Services;

namespace DalamudBrowser.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly BrowserWorkspace workspace;

    public ConfigWindow(BrowserWorkspace workspace) : base("Dalamud Browser Settings###DalamudBrowserConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(520f, 320f);
        SizeCondition = ImGuiCond.FirstUseEver;
        this.workspace = workspace;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

        lock (workspace.SyncRoot)
        {
            var configuration = workspace.Configuration;

            var openManagerOnStartup = configuration.OpenManagerOnStartup;
            if (ImGui.Checkbox("Open workspace on startup", ref openManagerOnStartup))
            {
                configuration.OpenManagerOnStartup = openManagerOnStartup;
                changed = true;
            }

            var linkCheckIntervalSeconds = configuration.LinkCheckIntervalSeconds;
            if (ImGui.SliderInt("Link check interval (seconds)", ref linkCheckIntervalSeconds, 2, 60))
            {
                configuration.LinkCheckIntervalSeconds = linkCheckIntervalSeconds;
                changed = true;
            }

            var linkRequestTimeoutSeconds = configuration.LinkRequestTimeoutSeconds;
            if (ImGui.SliderInt("Link request timeout (seconds)", ref linkRequestTimeoutSeconds, 2, 30))
            {
                configuration.LinkRequestTimeoutSeconds = linkRequestTimeoutSeconds;
                changed = true;
            }

            ImGui.Separator();
            ImGui.TextUnformatted($"Current renderer backend: {workspace.BackendName}");
            ImGui.TextDisabled($"JavaScript support active: {(workspace.SupportsJavaScript ? "yes" : "no")}");
            ImGui.Spacing();
            ImGui.TextWrapped("Current step uses an external CEF renderer process with offscreen rendering and shared D3D11 textures.");
            ImGui.TextWrapped("Browser pages are composited into the game UI as textures, so the browser no longer owns an HWND inside the game process.");
            ImGui.TextWrapped("Unlocked views expose only corner resize handles; the rest of the frame around the browser surface is used to drag the window.");
            ImGui.TextWrapped("Keyboard focus is intentionally kept away from the browser surface so game keybinds stay with FFXIV.");

            if (changed)
            {
                workspace.Save();
            }
        }
    }
}
