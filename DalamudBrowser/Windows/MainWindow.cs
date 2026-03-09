using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DalamudBrowser.Models;
using DalamudBrowser.Services;

namespace DalamudBrowser.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly BrowserWorkspace workspace;

    public MainWindow(BrowserWorkspace workspace)
        : base("Dalamud Browser Workspace###DalamudBrowserMainWindow")
    {
        Size = new Vector2(980f, 680f);
        SizeCondition = ImGuiCond.FirstUseEver;
        this.workspace = workspace;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var configuration = workspace.Configuration;
        var changed = false;

        lock (workspace.SyncRoot)
        {
            configuration.EnsureInitialized();

            ImGui.TextUnformatted("Collections contain zero or more browser views.");
            ImGui.TextDisabled($"Renderer backend: {workspace.BackendName} | JavaScript: {(workspace.SupportsJavaScript ? "yes" : "not yet")}");
            ImGui.TextDisabled("Unlocked views can be moved by dragging the frame around the page and resized only from the corner handles.");
            ImGui.TextDisabled("The render window itself now stays page-only: no title bar, URL text or status text on top of the page.");
            ImGui.TextDisabled("Layout is now tracked in viewport percentages so windowed/fullscreen transitions keep views aligned.");
            ImGui.Separator();

            if (ImGui.Button("Add Collection"))
            {
                workspace.AddCollection();
                changed = true;
            }

            var selectedCollection = configuration.Collections.Find(collection => collection.Id == configuration.SelectedCollectionId);
            if (selectedCollection != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove Collection"))
                {
                    workspace.RemoveCollection(selectedCollection.Id);
                    changed = true;
                    selectedCollection = configuration.Collections.Find(collection => collection.Id == configuration.SelectedCollectionId);
                }

                if (selectedCollection != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Add View"))
                    {
                        workspace.AddView(selectedCollection.Id);
                        changed = true;
                    }
                }
            }

            var collectionPaneWidth = MathF.Min(280f, ImGui.GetContentRegionAvail().X * 0.3f);
            if (ImGui.BeginChild("CollectionsPane", new Vector2(collectionPaneWidth, 0f), true))
            {
                foreach (var collection in configuration.Collections)
                {
                    ImGui.PushID(collection.Id.ToString());

                    var enabled = collection.IsEnabled;
                    if (ImGui.Checkbox("##Enabled", ref enabled))
                    {
                        collection.IsEnabled = enabled;
                        changed = true;
                    }

                    ImGui.SameLine();
                    var isSelected = collection.Id == configuration.SelectedCollectionId;
                    if (ImGui.Selectable(collection.Name, isSelected))
                    {
                        configuration.SelectedCollectionId = collection.Id;
                        changed = true;
                    }

                    ImGui.PopID();
                }
            }

            ImGui.EndChild();
            ImGui.SameLine();

            if (ImGui.BeginChild("CollectionEditorPane", Vector2.Zero, true))
            {
                selectedCollection ??= configuration.Collections.Find(collection => collection.Id == configuration.SelectedCollectionId);
                if (selectedCollection == null)
                {
                    ImGui.TextDisabled("No collection selected.");
                }
                else
                {
                    DrawCollectionEditor(selectedCollection, ref changed);
                }
            }

            ImGui.EndChild();

            if (changed)
            {
                workspace.Save();
            }
        }
    }

    private void DrawCollectionEditor(BrowserCollectionConfig collection, ref bool changed)
    {
        var collectionName = collection.Name;
        if (ImGui.InputText("Collection Name", ref collectionName, 128))
        {
            collection.Name = string.IsNullOrWhiteSpace(collectionName) ? "Collection" : collectionName.Trim();
            changed = true;
        }

        var isEnabled = collection.IsEnabled;
        if (ImGui.Checkbox("Collection Enabled", ref isEnabled))
        {
            collection.IsEnabled = isEnabled;
            changed = true;
        }

        ImGui.Separator();

        if (collection.Views.Count == 0)
        {
            ImGui.TextDisabled("This collection has no browser views yet.");
            return;
        }

        BrowserViewConfig? viewToRemove = null;
        foreach (var view in collection.Views)
        {
            ImGui.PushID(view.Id.ToString());
            ImGui.Separator();
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(view.Title) ? "Browser View" : view.Title);
            DrawViewEditor(collection, view, ref changed, ref viewToRemove);
            ImGui.PopID();
        }

        if (viewToRemove != null)
        {
            workspace.RemoveView(collection.Id, viewToRemove.Id);
            changed = true;
        }
    }

    private void DrawViewEditor(BrowserCollectionConfig collection, BrowserViewConfig view, ref bool changed, ref BrowserViewConfig? viewToRemove)
    {
        var title = view.Title;
        if (ImGui.InputText("Title", ref title, 128))
        {
            view.Title = string.IsNullOrWhiteSpace(title) ? "Browser View" : title.Trim();
            changed = true;
        }

        var url = view.Url;
        if (ImGui.InputText("URL", ref url, 512))
        {
            view.Url = url.Trim();
            changed = true;
        }

        var visible = view.IsVisible;
        if (ImGui.Checkbox("Visible", ref visible))
        {
            view.IsVisible = visible;
            changed = true;
        }

        ImGui.SameLine();
        var locked = view.Locked;
        if (ImGui.Checkbox("Lock", ref locked))
        {
            view.Locked = locked;
            changed = true;
        }

        ImGui.SameLine();
        var clickThrough = view.ClickThrough;
        if (ImGui.Checkbox("Click Through", ref clickThrough))
        {
            view.ClickThrough = clickThrough;
            changed = true;
        }

        ImGui.SameLine();
        var soundEnabled = view.SoundEnabled;
        if (ImGui.Checkbox("Sound", ref soundEnabled))
        {
            view.SoundEnabled = soundEnabled;
            changed = true;
        }

        var autoRetry = view.AutoRetry;
        if (ImGui.Checkbox("Auto Retry", ref autoRetry))
        {
            view.AutoRetry = autoRetry;
            changed = true;
        }

        ImGui.SameLine();
        var actOptimizations = view.ActOptimizations;
        if (ImGui.Checkbox("ACT Recovery", ref actOptimizations))
        {
            view.ActOptimizations = actOptimizations;
            changed = true;
        }

        var zoomPercent = view.ZoomPercent;
        if (ImGui.SliderFloat("Zoom", ref zoomPercent, 25f, 500f, "%.0f%%"))
        {
            view.ZoomPercent = zoomPercent;
            changed = true;
        }

        var opacityPercent = view.OpacityPercent;
        if (ImGui.SliderFloat("Opacity", ref opacityPercent, 5f, 100f, "%.0f%%"))
        {
            view.OpacityPercent = opacityPercent;
            changed = true;
        }

        var performancePreset = view.PerformancePreset;
        var performancePreview = GetPerformancePresetLabel(performancePreset);
        if (ImGui.BeginCombo("Performance", performancePreview))
        {
            foreach (var preset in Enum.GetValues<BrowserViewPerformancePreset>())
            {
                var isSelected = preset == performancePreset;
                if (ImGui.Selectable(GetPerformancePresetLabel(preset), isSelected))
                {
                    view.PerformancePreset = preset;
                    changed = true;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        var useCustomFrameRates = view.UseCustomFrameRates;
        if (ImGui.Checkbox("Custom FPS", ref useCustomFrameRates))
        {
            view.UseCustomFrameRates = useCustomFrameRates;
            changed = true;
        }

        if (view.UseCustomFrameRates)
        {
            var interactiveFrameRate = view.InteractiveFrameRate <= 0 ? 30 : view.InteractiveFrameRate;
            if (ImGui.SliderInt("Interactive FPS", ref interactiveFrameRate, 1, 60))
            {
                view.InteractiveFrameRate = interactiveFrameRate;
                changed = true;
            }

            var passiveFrameRate = view.PassiveFrameRate <= 0 ? 15 : view.PassiveFrameRate;
            if (ImGui.SliderInt("Passive FPS", ref passiveFrameRate, 1, 60))
            {
                view.PassiveFrameRate = passiveFrameRate;
                changed = true;
            }

            var hiddenFrameRate = view.HiddenFrameRate <= 0 ? 5 : view.HiddenFrameRate;
            if (ImGui.SliderInt("Hidden FPS", ref hiddenFrameRate, 1, 30))
            {
                view.HiddenFrameRate = hiddenFrameRate;
                changed = true;
            }
        }

        ImGui.TextDisabled(GetPerformancePresetDescription(view.PerformancePreset, view.ActOptimizations && BrowserUrlUtility.IsLikelyActOverlay(view.Url)));
        if (view.UseCustomFrameRates)
        {
            ImGui.TextDisabled($"Custom FPS: active {Math.Max(1, view.InteractiveFrameRate <= 0 ? 30 : view.InteractiveFrameRate)}, passive {Math.Max(1, view.PassiveFrameRate <= 0 ? 15 : view.PassiveFrameRate)}, hidden {Math.Max(1, view.HiddenFrameRate <= 0 ? 5 : view.HiddenFrameRate)}");
        }

        var status = workspace.GetStatusSnapshot(view.Id);
        ImGui.TextColored(GetStatusColor(status.Availability), workspace.GetStatusText(status));
        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            ImGui.TextWrapped(status.LastError);
        }

        ImGui.TextDisabled($"Layout(px): {view.Width:0}x{view.Height:0} @ {view.PositionX:0},{view.PositionY:0} | Zoom: {view.ZoomPercent:0}% | Opacity: {view.OpacityPercent:0}%");
        if (HasRelativeLayout(view))
        {
            ImGui.TextDisabled($"Layout(%): {view.WidthPercent:0.0}% x {view.HeightPercent:0.0}% @ {view.PositionXPercent:0.0}%, {view.PositionYPercent:0.0}%");
        }
        else
        {
            ImGui.TextDisabled("Layout(%): will be captured automatically after the first rendered frame.");
        }

        if (view.ActOptimizations && BrowserUrlUtility.IsLikelyActOverlay(view.Url))
        {
            ImGui.TextDisabled("ACT recovery is enabled for this view: the plugin watches the ACT process, probes OVERLAY_WS and reloads the page after ACT comes back.");
        }

        ImGui.TextDisabled("If the view is unlocked, drag the frame around the page to move it and use the corner handles to resize it.");
        ImGui.TextDisabled("Click-through only applies while the view is locked.");
        if (view.Locked && view.ClickThrough && view.OpacityPercent < 100f)
        {
            ImGui.TextDisabled("Semi-transparent click-through views are treated as passive overlays and throttled more aggressively.");
        }

        if (ImGui.Button("Check Now"))
        {
            workspace.ForceProbe(view.Id);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Layout"))
        {
            workspace.ResetLayout(view.Id);
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Remove View"))
        {
            viewToRemove = view;
        }
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

    private static string GetPerformancePresetLabel(BrowserViewPerformancePreset preset)
    {
        return preset switch
        {
            BrowserViewPerformancePreset.Responsive => "Responsive",
            BrowserViewPerformancePreset.Balanced => "Balanced",
            BrowserViewPerformancePreset.Eco => "Eco",
            _ => "Balanced",
        };
    }

    private static string GetPerformancePresetDescription(BrowserViewPerformancePreset preset, bool actOptimized)
    {
        if (actOptimized)
        {
            return preset switch
            {
                BrowserViewPerformancePreset.Responsive => "ACT mode: keeps active overlays responsive, but throttles passive locked overlays to reduce game-side cost.",
                BrowserViewPerformancePreset.Balanced => "ACT mode: favors passive overlay efficiency and lowers the frame rate further when the view is click-through.",
                BrowserViewPerformancePreset.Eco => "ACT mode: aggressive low-FPS policy for passive overlays while keeping active interaction usable.",
                _ => string.Empty,
            };
        }

        return preset switch
        {
            BrowserViewPerformancePreset.Responsive => "Keeps the browser fully active for the lowest interaction latency.",
            BrowserViewPerformancePreset.Balanced => "Keeps active views fast and lowers memory pressure when the view is not being used.",
            BrowserViewPerformancePreset.Eco => "Suspends hidden views and restores them when shown again to cut background cost.",
            _ => string.Empty,
        };
    }

    private static bool HasRelativeLayout(BrowserViewConfig view)
    {
        return view.PositionXPercent >= 0f
            && view.PositionYPercent >= 0f
            && view.WidthPercent >= 0f
            && view.HeightPercent >= 0f;
    }
}
