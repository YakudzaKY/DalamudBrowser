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
            ImGui.TextWrapped("Current step uses a real WebView2 child host inside the game process, one instance per browser view.");
            ImGui.TextWrapped("This is window-hosted browser content synced to the ImGui layout area. It is not texture compositing yet.");
            ImGui.TextWrapped("Unlocked views expose resize handles around the browser surface so the child browser window does not block layout edits.");
            ImGui.TextWrapped("If a later step needs a true texture-backed surface, that will require a different composition path.");

            if (changed)
            {
                workspace.Save();
            }
        }
    }
}
