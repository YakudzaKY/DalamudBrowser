using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using DalamudBrowser.Models;
using DalamudBrowser.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace DalamudBrowser.Services;

public sealed class BrowserWorkspace : IDisposable
{
    private readonly record struct BrowserProbeTarget(Guid ViewId, string Url, int TimeoutSeconds);
    private readonly record struct BrowserViewWindowSnapshot(
        Guid ViewId,
        string Title,
        string Url,
        bool Locked,
        bool ClickThrough,
        float PositionX,
        float PositionY,
        float Width,
        float Height);

    private readonly IPluginLog log;
    private readonly IBrowserRenderBackend renderBackend;
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource disposeTokenSource = new();
    private readonly Task availabilityLoop;
    private readonly ConcurrentDictionary<Guid, BrowserViewRuntimeState> runtimeStates = new();

    public BrowserWorkspace(Configuration configuration, IPluginLog log, IBrowserRenderBackend renderBackend)
    {
        Configuration = configuration;
        this.log = log;
        this.renderBackend = renderBackend;
        httpClient = new HttpClient();

        lock (SyncRoot)
        {
            Configuration.EnsureInitialized();
            SyncRuntimeStatesLocked();
        }

        availabilityLoop = Task.Run(() => MonitorAvailabilityAsync(disposeTokenSource.Token));
    }

    public object SyncRoot { get; } = new();
    public Configuration Configuration { get; }
    public string BackendName => renderBackend.Name;
    public bool SupportsJavaScript => renderBackend.SupportsJavaScript;

    public void Dispose()
    {
        disposeTokenSource.Cancel();
        httpClient.Dispose();
        renderBackend.Dispose();

        try
        {
            availabilityLoop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }

    public BrowserCollectionConfig AddCollection()
    {
        lock (SyncRoot)
        {
            var collection = BrowserCollectionConfig.CreateDefault($"Collection {Configuration.Collections.Count + 1}");
            Configuration.Collections.Add(collection);
            Configuration.SelectedCollectionId = collection.Id;
            SyncRuntimeStatesLocked();
            Configuration.Save();
            return collection;
        }
    }

    public BrowserViewConfig AddView(Guid collectionId)
    {
        lock (SyncRoot)
        {
            var collection = Configuration.Collections.Find(candidate => candidate.Id == collectionId)
                ?? throw new InvalidOperationException("Collection not found.");

            var view = new BrowserViewConfig
            {
                Title = $"Browser View {collection.Views.Count + 1}",
                PositionX = 220f + (collection.Views.Count * 24f),
                PositionY = 180f + (collection.Views.Count * 24f),
            };

            collection.Views.Add(view);
            GetRuntimeState(view.Id).RequestLayoutApply();
            Configuration.Save();
            return view;
        }
    }

    public void RemoveCollection(Guid collectionId)
    {
        lock (SyncRoot)
        {
            Configuration.Collections.RemoveAll(collection => collection.Id == collectionId);
            Configuration.EnsureInitialized();
            SyncRuntimeStatesLocked();
            Configuration.Save();
        }
    }

    public void RemoveView(Guid collectionId, Guid viewId)
    {
        lock (SyncRoot)
        {
            var collection = Configuration.Collections.Find(candidate => candidate.Id == collectionId);
            if (collection == null)
            {
                return;
            }

            collection.Views.RemoveAll(view => view.Id == viewId);
            runtimeStates.TryRemove(viewId, out _);
            Configuration.Save();
        }
    }

    public void ResetLayout(Guid viewId)
    {
        lock (SyncRoot)
        {
            if (!TryFindViewLocked(viewId, out var view))
            {
                return;
            }

            view.PositionX = 220f;
            view.PositionY = 180f;
            view.Width = 640f;
            view.Height = 420f;
            GetRuntimeState(viewId).RequestLayoutApply();
            Configuration.Save();
        }
    }

    public void ForceProbe(Guid viewId)
    {
        GetRuntimeState(viewId).ForceProbe(DateTimeOffset.UtcNow);
    }

    public BrowserViewStatusSnapshot GetStatusSnapshot(Guid viewId)
    {
        return GetRuntimeState(viewId).GetSnapshot();
    }

    public string GetStatusText(BrowserViewStatusSnapshot status)
    {
        if (status.IsChecking)
        {
            return "Checking link availability...";
        }

        return status.Availability switch
        {
            BrowserAvailabilityState.Available => "Link reachable",
            BrowserAvailabilityState.Unavailable => "Link unavailable",
            _ => "Link not checked yet",
        };
    }

    public void Save()
    {
        lock (SyncRoot)
        {
            Configuration.EnsureInitialized();
            SyncRuntimeStatesLocked();
            Configuration.Save();
        }
    }

    public void DrawViews()
    {
        List<BrowserViewWindowSnapshot> windows;
        lock (SyncRoot)
        {
            Configuration.EnsureInitialized();
            SyncRuntimeStatesLocked();

            windows = Configuration.Collections
                .Where(collection => collection.IsEnabled)
                .SelectMany(collection => collection.Views)
                .Where(view => view.IsVisible)
                .Select(view => new BrowserViewWindowSnapshot(
                    view.Id,
                    view.Title,
                    view.Url,
                    view.Locked,
                    view.ClickThrough,
                    view.PositionX,
                    view.PositionY,
                    view.Width,
                    view.Height))
                .ToList();
        }

        foreach (var window in windows)
        {
            DrawView(window);
        }
    }

    private async Task MonitorAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var targets = GetDueProbeTargets(DateTimeOffset.UtcNow);
                if (targets.Count == 0)
                {
                    continue;
                }

                var probeTasks = targets.Select(target => ProbeAsync(target, cancellationToken)).ToArray();
                await Task.WhenAll(probeTasks);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProbeAsync(BrowserProbeTarget target, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            var nowUtc = DateTimeOffset.UtcNow;
            if ((int)response.StatusCode is >= 200 and < 400)
            {
                GetRuntimeState(target.ViewId).MarkAvailable(nowUtc);
            }
            else
            {
                GetRuntimeState(target.ViewId).MarkUnavailable(nowUtc, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            GetRuntimeState(target.ViewId).MarkUnavailable(DateTimeOffset.UtcNow, $"Timeout after {target.TimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to probe browser view URL {Url}", target.Url);
            GetRuntimeState(target.ViewId).MarkUnavailable(DateTimeOffset.UtcNow, ex.Message);
        }
    }

    private List<BrowserProbeTarget> GetDueProbeTargets(DateTimeOffset nowUtc)
    {
        lock (SyncRoot)
        {
            var interval = TimeSpan.FromSeconds(Configuration.LinkCheckIntervalSeconds);
            var targets = new List<BrowserProbeTarget>();

            foreach (var collection in Configuration.Collections.Where(collection => collection.IsEnabled))
            {
                foreach (var view in collection.Views.Where(view => view.AutoRetry && !string.IsNullOrWhiteSpace(view.Url)))
                {
                    var runtimeState = GetRuntimeState(view.Id);
                    if (runtimeState.TryBeginProbe(nowUtc, interval))
                    {
                        targets.Add(new BrowserProbeTarget(view.Id, view.Url, Configuration.LinkRequestTimeoutSeconds));
                    }
                }
            }

            return targets;
        }
    }

    private void DrawView(BrowserViewWindowSnapshot snapshot)
    {
        var runtimeState = GetRuntimeState(snapshot.ViewId);
        var applyCondition = runtimeState.ConsumeForceLayoutApply() ? ImGuiCond.Always : ImGuiCond.Appearing;

        ImGui.SetNextWindowPos(new Vector2(snapshot.PositionX, snapshot.PositionY), applyCondition);
        ImGui.SetNextWindowSize(new Vector2(snapshot.Width, snapshot.Height), applyCondition);

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (snapshot.Locked)
        {
            flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        }

        if (snapshot.Locked && snapshot.ClickThrough)
        {
            flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBringToFrontOnFocus;
        }

        var windowTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? "Browser View" : snapshot.Title;
        var open = true;
        if (!ImGui.Begin($"{windowTitle}##BrowserView-{snapshot.ViewId}", ref open, flags))
        {
            ImGui.End();
            if (!open)
            {
                SetViewVisibility(snapshot.ViewId, false);
            }

            return;
        }

        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        UpdateLayout(snapshot.ViewId, position, size, persistNow: !ImGui.IsMouseDown(0));

        var status = runtimeState.GetSnapshot();
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(snapshot.Url) ? "No URL configured." : snapshot.Url);
        ImGui.SameLine();
        ImGui.TextColored(GetStatusColor(status.Availability), GetStatusText(status));
        ImGui.Separator();

        renderBackend.Draw(snapshot.Url, status, ImGui.GetContentRegionAvail());
        ImGui.End();

        if (!open)
        {
            SetViewVisibility(snapshot.ViewId, false);
        }
    }

    private void UpdateLayout(Guid viewId, Vector2 position, Vector2 size, bool persistNow)
    {
        lock (SyncRoot)
        {
            if (!TryFindViewLocked(viewId, out var view))
            {
                return;
            }

            var changed = false;
            var newPositionX = position.X;
            if (HasMeaningfulDifference(view.PositionX, newPositionX))
            {
                view.PositionX = newPositionX;
                changed = true;
            }

            var newPositionY = position.Y;
            if (HasMeaningfulDifference(view.PositionY, newPositionY))
            {
                view.PositionY = newPositionY;
                changed = true;
            }

            var newWidth = Math.Max(320f, size.X);
            if (HasMeaningfulDifference(view.Width, newWidth))
            {
                view.Width = newWidth;
                changed = true;
            }

            var newHeight = Math.Max(200f, size.Y);
            if (HasMeaningfulDifference(view.Height, newHeight))
            {
                view.Height = newHeight;
                changed = true;
            }

            if (changed && persistNow)
            {
                Configuration.Save();
            }
        }
    }

    private void SetViewVisibility(Guid viewId, bool isVisible)
    {
        lock (SyncRoot)
        {
            if (!TryFindViewLocked(viewId, out var view))
            {
                return;
            }

            if (view.IsVisible == isVisible)
            {
                return;
            }

            view.IsVisible = isVisible;
            Configuration.Save();
        }
    }

    private bool TryFindViewLocked(Guid viewId, out BrowserViewConfig view)
    {
        foreach (var collection in Configuration.Collections)
        {
            var found = collection.Views.Find(candidate => candidate.Id == viewId);
            if (found != null)
            {
                view = found;
                return true;
            }
        }

        view = null!;
        return false;
    }

    private void SyncRuntimeStatesLocked()
    {
        var activeIds = Configuration.Collections
            .SelectMany(collection => collection.Views)
            .Select(view => view.Id)
            .ToHashSet();

        foreach (var viewId in activeIds)
        {
            runtimeStates.TryAdd(viewId, new BrowserViewRuntimeState());
        }

        foreach (var runtimeState in runtimeStates.Keys)
        {
            if (!activeIds.Contains(runtimeState))
            {
                runtimeStates.TryRemove(runtimeState, out _);
            }
        }
    }

    private BrowserViewRuntimeState GetRuntimeState(Guid viewId)
    {
        return runtimeStates.GetOrAdd(viewId, _ => new BrowserViewRuntimeState());
    }

    private static bool HasMeaningfulDifference(float current, float next)
    {
        return MathF.Abs(current - next) >= 0.5f;
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
