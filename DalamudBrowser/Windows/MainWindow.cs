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
            ImGui.TextDisabled("Unlocked views can be moved and resized using the ImGui frame. Click-through is applied when a view is locked.");
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

        var autoRetry = view.AutoRetry;
        if (ImGui.Checkbox("Auto Retry", ref autoRetry))
        {
            view.AutoRetry = autoRetry;
            changed = true;
        }

        var status = workspace.GetStatusSnapshot(view.Id);
        ImGui.TextColored(GetStatusColor(status.Availability), workspace.GetStatusText(status));
        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            ImGui.TextWrapped(status.LastError);
        }

        ImGui.TextDisabled($"Layout: {view.Width:0}x{view.Height:0} @ {view.PositionX:0},{view.PositionY:0}");
        ImGui.TextDisabled("If the view is unlocked, use the ImGui frame to drag and resize it. Click-through only applies while locked.");

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
}
