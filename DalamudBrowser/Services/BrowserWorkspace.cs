using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using DalamudBrowser.Models;
using DalamudBrowser.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace DalamudBrowser.Services;

public sealed class BrowserWorkspace : IDisposable
{
    private enum BrowserResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private readonly record struct BrowserProbeTarget(Guid ViewId, string Url, int TimeoutSeconds);
    private readonly record struct BrowserSurfaceLayout(Vector2 Position, Vector2 Size);
    private readonly record struct BrowserViewWindowSnapshot(
        Guid ViewId,
        string Title,
        string Url,
        bool Locked,
        bool ClickThrough,
        bool SoundEnabled,
        BrowserViewPerformancePreset PerformancePreset,
        float ZoomPercent,
        float PositionX,
        float PositionY,
        float Width,
        float Height);

    private const float MinViewWidth = 320f;
    private const float MinViewHeight = 200f;
    private const float SurfaceHandlePadding = 12f;
    private const float SurfaceHandleThickness = 10f;
    private const float HandleVisualThickness = 3f;
    private const float HandleCornerSize = 18f;

    private readonly IPluginLog log;
    private readonly IBrowserRenderBackend renderBackend;
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource disposeTokenSource = new();
    private readonly Task availabilityLoop;
    private readonly ConcurrentDictionary<Guid, BrowserViewRuntimeState> runtimeStates = new();
    private bool pendingLayoutSave;
    private Guid? activeDragViewId;
    private Guid? activeResizeViewId;
    private BrowserResizeHandle activeResizeHandle = BrowserResizeHandle.None;
    private Vector2 dragStartMousePosition;
    private Vector2 dragStartWindowPosition;
    private Vector2 resizeStartMousePosition;
    private Vector2 resizeStartWindowPosition;
    private Vector2 resizeStartWindowSize;

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
        List<Guid> knownViewIds;
        lock (SyncRoot)
        {
            Configuration.EnsureInitialized();
            SyncRuntimeStatesLocked();

            knownViewIds = Configuration.Collections
                .SelectMany(collection => collection.Views)
                .Select(view => view.Id)
                .ToList();

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
                    view.SoundEnabled,
                    view.PerformancePreset,
                    view.ZoomPercent,
                    view.PositionX,
                    view.PositionY,
                    view.Width,
                    view.Height))
                .ToList();
        }

        if (activeResizeViewId.HasValue
            && (!ImGui.IsMouseDown(0) || !knownViewIds.Contains(activeResizeViewId.Value)))
        {
            EndResizeInteraction();
        }

        if (activeDragViewId.HasValue
            && (!ImGui.IsMouseDown(0) || !knownViewIds.Contains(activeDragViewId.Value)))
        {
            EndDragInteraction();
        }

        if (!ImGui.IsMouseDown(0))
        {
            FlushPendingLayoutSave();
        }

        if (windows.Count == 0)
        {
            return;
        }

        renderBackend.BeginFrame(knownViewIds);
        try
        {
            foreach (var window in windows)
            {
                DrawView(window);
            }
        }
        finally
        {
            renderBackend.EndFrame();
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
        try
        {
            var normalizedUrl = BrowserUrlUtility.Normalize(target.Url);
            if (!BrowserUrlUtility.TryCreateAbsoluteUri(normalizedUrl, out var uri))
            {
                GetRuntimeState(target.ViewId).MarkUnavailable(DateTimeOffset.UtcNow, "Invalid URL.");
                return;
            }

            if (!BrowserUrlUtility.IsNavigableScheme(uri))
            {
                GetRuntimeState(target.ViewId).MarkUnavailable(DateTimeOffset.UtcNow, $"The '{uri.Scheme}' scheme is not supported.");
                return;
            }

            if (uri.IsFile)
            {
                var localPath = uri.LocalPath;
                if (File.Exists(localPath))
                {
                    GetRuntimeState(target.ViewId).MarkAvailable(DateTimeOffset.UtcNow);
                }
                else
                {
                    GetRuntimeState(target.ViewId).MarkUnavailable(DateTimeOffset.UtcNow, $"File not found: {localPath}");
                }

                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
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

        var flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        if (snapshot.Locked && snapshot.ClickThrough)
        {
            flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBringToFrontOnFocus;
        }

        var windowTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? "Browser View" : snapshot.Title;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (!ImGui.Begin($"##BrowserView-{snapshot.ViewId}", flags))
        {
            ImGui.End();
            ImGui.PopStyleVar();
            return;
        }

        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        if (snapshot.Locked && activeResizeViewId == snapshot.ViewId)
        {
            EndResizeInteraction();
        }

        if (snapshot.Locked && activeDragViewId == snapshot.ViewId)
        {
            EndDragInteraction();
        }

        if (!snapshot.Locked)
        {
            ApplyActiveResize(snapshot.ViewId, ref position, ref size);
            ApplyActiveDrag(snapshot.ViewId, ref position);
        }

        UpdateLayout(snapshot.ViewId, position, size, persistNow: !ImGui.IsMouseDown(0));
        var status = runtimeState.GetSnapshot();
        var surfaceLayout = snapshot.Locked
            ? new BrowserSurfaceLayout(ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail())
            : DrawUnlockedSurfaceChrome(snapshot.ViewId);
        var isWindowHovered = ImGui.IsWindowHovered();
        var isWindowFocused = ImGui.IsWindowFocused();

        renderBackend.Draw(new BrowserRenderRequest(
            snapshot.ViewId,
            windowTitle,
            snapshot.Url,
            snapshot.Locked,
            snapshot.ClickThrough,
            snapshot.SoundEnabled,
            snapshot.PerformancePreset,
            Math.Clamp(snapshot.ZoomPercent / 100f, 0.25f, 5f),
            isWindowHovered,
            isWindowFocused,
            status,
            surfaceLayout.Position,
            surfaceLayout.Size));
        ImGui.End();
        ImGui.PopStyleVar();
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

            var newWidth = Math.Max(MinViewWidth, size.X);
            if (HasMeaningfulDifference(view.Width, newWidth))
            {
                view.Width = newWidth;
                changed = true;
            }

            var newHeight = Math.Max(MinViewHeight, size.Y);
            if (HasMeaningfulDifference(view.Height, newHeight))
            {
                view.Height = newHeight;
                changed = true;
            }

            if (changed && persistNow)
            {
                Configuration.Save();
                pendingLayoutSave = false;
            }
            else if (changed)
            {
                pendingLayoutSave = true;
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

    private BrowserSurfaceLayout DrawUnlockedSurfaceChrome(Guid viewId)
    {
        var framePosition = ImGui.GetCursorScreenPos();
        var frameSize = ImGui.GetContentRegionAvail();
        frameSize.X = Math.Max(frameSize.X, SurfaceHandlePadding * 2f + 32f);
        frameSize.Y = Math.Max(frameSize.Y, SurfaceHandlePadding * 2f + 32f);

        var surfacePosition = framePosition + new Vector2(SurfaceHandlePadding, SurfaceHandlePadding);
        var surfaceSize = new Vector2(
            Math.Max(32f, frameSize.X - (SurfaceHandlePadding * 2f)),
            Math.Max(32f, frameSize.Y - (SurfaceHandlePadding * 2f)));

        var drawList = ImGui.GetWindowDrawList();
        var outerMin = framePosition;
        var outerMax = framePosition + frameSize;
        var innerMin = surfacePosition;
        var innerMax = surfacePosition + surfaceSize;

        var outerColor = ImGui.GetColorU32(new Vector4(0.16f, 0.2f, 0.25f, 0.9f));
        var innerColor = ImGui.GetColorU32(new Vector4(0.32f, 0.55f, 0.8f, 0.75f));
        var accentColor = ImGui.GetColorU32(new Vector4(0.6f, 0.82f, 1f, 0.95f));
        var fillColor = ImGui.GetColorU32(new Vector4(0.08f, 0.11f, 0.16f, 0.3f));

        drawList.AddRectFilled(outerMin, outerMax, fillColor, 10f);
        drawList.AddRect(outerMin, outerMax, outerColor, 10f, 0, 1.5f);
        drawList.AddRect(innerMin, innerMax, innerColor, 8f, 0, 1.5f);

        var hoveredHandle = GetHoveredResizeHandle(framePosition, frameSize);
        if (activeResizeViewId == viewId)
        {
            hoveredHandle = activeResizeHandle;
        }

        DrawHandleVisuals(drawList, framePosition, frameSize, hoveredHandle, accentColor, outerColor);

        if (activeResizeViewId == null
            && activeDragViewId == null
            && hoveredHandle != BrowserResizeHandle.None
            && ImGui.IsWindowHovered()
            && ImGui.IsMouseClicked(0))
        {
            BeginResizeInteraction(viewId, hoveredHandle);
        }
        else if (activeResizeViewId == null
            && activeDragViewId == null
            && hoveredHandle == BrowserResizeHandle.None
            && IsMouseInDragRegion(framePosition, frameSize, surfacePosition, surfaceSize)
            && ImGui.IsWindowHovered()
            && ImGui.IsMouseClicked(0))
        {
            BeginDragInteraction(viewId);
        }

        SetResizeCursor(hoveredHandle);
        ImGui.SetCursorScreenPos(surfacePosition);
        return new BrowserSurfaceLayout(surfacePosition, surfaceSize);
    }

    private void ApplyActiveResize(Guid viewId, ref Vector2 position, ref Vector2 size)
    {
        if (activeResizeViewId != viewId)
        {
            return;
        }

        if (!ImGui.IsMouseDown(0))
        {
            EndResizeInteraction();
            return;
        }

        var delta = ImGui.GetIO().MousePos - resizeStartMousePosition;
        var nextPosition = resizeStartWindowPosition;
        var nextSize = resizeStartWindowSize;

        ApplyResizeDelta(activeResizeHandle, delta, ref nextPosition, ref nextSize);
        ClampResizedWindow(activeResizeHandle, ref nextPosition, ref nextSize);

        position = nextPosition;
        size = nextSize;
        ImGui.SetWindowPos(nextPosition, ImGuiCond.Always);
        ImGui.SetWindowSize(nextSize, ImGuiCond.Always);
    }

    private void ApplyActiveDrag(Guid viewId, ref Vector2 position)
    {
        if (activeDragViewId != viewId)
        {
            return;
        }

        if (!ImGui.IsMouseDown(0))
        {
            EndDragInteraction();
            return;
        }

        var delta = ImGui.GetIO().MousePos - dragStartMousePosition;
        var nextPosition = dragStartWindowPosition + delta;
        position = nextPosition;
        ImGui.SetWindowPos(nextPosition, ImGuiCond.Always);
    }

    private void BeginDragInteraction(Guid viewId)
    {
        activeDragViewId = viewId;
        dragStartMousePosition = ImGui.GetIO().MousePos;
        dragStartWindowPosition = ImGui.GetWindowPos();
    }

    private void EndDragInteraction()
    {
        activeDragViewId = null;
        dragStartMousePosition = Vector2.Zero;
        dragStartWindowPosition = Vector2.Zero;
    }

    private void BeginResizeInteraction(Guid viewId, BrowserResizeHandle handle)
    {
        activeResizeViewId = viewId;
        activeResizeHandle = handle;
        resizeStartMousePosition = ImGui.GetIO().MousePos;
        resizeStartWindowPosition = ImGui.GetWindowPos();
        resizeStartWindowSize = ImGui.GetWindowSize();
    }

    private void EndResizeInteraction()
    {
        activeResizeViewId = null;
        activeResizeHandle = BrowserResizeHandle.None;
        resizeStartMousePosition = Vector2.Zero;
        resizeStartWindowPosition = Vector2.Zero;
        resizeStartWindowSize = Vector2.Zero;
    }

    private static BrowserResizeHandle GetHoveredResizeHandle(Vector2 framePosition, Vector2 frameSize)
    {
        var mousePosition = ImGui.GetIO().MousePos;
        var frameMax = framePosition + frameSize;
        if (mousePosition.X < framePosition.X
            || mousePosition.X > frameMax.X
            || mousePosition.Y < framePosition.Y
            || mousePosition.Y > frameMax.Y)
        {
            return BrowserResizeHandle.None;
        }

        var left = mousePosition.X <= framePosition.X + SurfaceHandleThickness;
        var right = mousePosition.X >= frameMax.X - SurfaceHandleThickness;
        var top = mousePosition.Y <= framePosition.Y + SurfaceHandleThickness;
        var bottom = mousePosition.Y >= frameMax.Y - SurfaceHandleThickness;

        if (left && top)
        {
            return BrowserResizeHandle.TopLeft;
        }

        if (right && top)
        {
            return BrowserResizeHandle.TopRight;
        }

        if (left && bottom)
        {
            return BrowserResizeHandle.BottomLeft;
        }

        if (right && bottom)
        {
            return BrowserResizeHandle.BottomRight;
        }

        return BrowserResizeHandle.None;
    }

    private static void DrawHandleVisuals(
        ImDrawListPtr drawList,
        Vector2 framePosition,
        Vector2 frameSize,
        BrowserResizeHandle hoveredHandle,
        uint accentColor,
        uint outerColor)
    {
        var frameMax = framePosition + frameSize;
        DrawHandleCorner(drawList, framePosition + new Vector2(SurfaceHandleThickness * 0.5f, SurfaceHandleThickness * 0.5f), hoveredHandle == BrowserResizeHandle.TopLeft, accentColor, outerColor);
        DrawHandleCorner(drawList, new Vector2(frameMax.X - (SurfaceHandleThickness * 0.5f), framePosition.Y + (SurfaceHandleThickness * 0.5f)), hoveredHandle == BrowserResizeHandle.TopRight, accentColor, outerColor);
        DrawHandleCorner(drawList, new Vector2(framePosition.X + (SurfaceHandleThickness * 0.5f), frameMax.Y - (SurfaceHandleThickness * 0.5f)), hoveredHandle == BrowserResizeHandle.BottomLeft, accentColor, outerColor);
        DrawHandleCorner(drawList, frameMax - new Vector2(SurfaceHandleThickness * 0.5f, SurfaceHandleThickness * 0.5f), hoveredHandle == BrowserResizeHandle.BottomRight, accentColor, outerColor);
    }

    private static void DrawHandleCorner(ImDrawListPtr drawList, Vector2 center, bool hovered, uint accentColor, uint outerColor)
    {
        var color = hovered ? accentColor : outerColor;
        var halfSize = new Vector2(HandleCornerSize * 0.5f, HandleCornerSize * 0.5f);
        drawList.AddRectFilled(center - halfSize, center + halfSize, color, 4f);
    }

    private static void SetResizeCursor(BrowserResizeHandle handle)
    {
        _ = handle;
    }

    private static void ApplyResizeDelta(BrowserResizeHandle handle, Vector2 delta, ref Vector2 position, ref Vector2 size)
    {
        switch (handle)
        {
            case BrowserResizeHandle.TopLeft:
                position.X += delta.X;
                size.X -= delta.X;
                position.Y += delta.Y;
                size.Y -= delta.Y;
                break;
            case BrowserResizeHandle.TopRight:
                size.X += delta.X;
                position.Y += delta.Y;
                size.Y -= delta.Y;
                break;
            case BrowserResizeHandle.BottomLeft:
                position.X += delta.X;
                size.X -= delta.X;
                size.Y += delta.Y;
                break;
            case BrowserResizeHandle.BottomRight:
                size.X += delta.X;
                size.Y += delta.Y;
                break;
        }
    }

    private void ClampResizedWindow(BrowserResizeHandle handle, ref Vector2 position, ref Vector2 size)
    {
        if (size.X < MinViewWidth)
        {
            if (AffectsLeftEdge(handle))
            {
                position.X = resizeStartWindowPosition.X + (resizeStartWindowSize.X - MinViewWidth);
            }

            size.X = MinViewWidth;
        }

        if (size.Y < MinViewHeight)
        {
            if (AffectsTopEdge(handle))
            {
                position.Y = resizeStartWindowPosition.Y + (resizeStartWindowSize.Y - MinViewHeight);
            }

            size.Y = MinViewHeight;
        }
    }

    private static bool AffectsLeftEdge(BrowserResizeHandle handle)
    {
        return handle is BrowserResizeHandle.TopLeft or BrowserResizeHandle.BottomLeft;
    }

    private static bool AffectsTopEdge(BrowserResizeHandle handle)
    {
        return handle is BrowserResizeHandle.TopLeft or BrowserResizeHandle.TopRight;
    }

    private static bool IsMouseInDragRegion(Vector2 framePosition, Vector2 frameSize, Vector2 surfacePosition, Vector2 surfaceSize)
    {
        var mousePosition = ImGui.GetIO().MousePos;
        var frameMax = framePosition + frameSize;
        if (mousePosition.X < framePosition.X
            || mousePosition.X > frameMax.X
            || mousePosition.Y < framePosition.Y
            || mousePosition.Y > frameMax.Y)
        {
            return false;
        }

        var surfaceMax = surfacePosition + surfaceSize;
        var insideSurface = mousePosition.X >= surfacePosition.X
            && mousePosition.X <= surfaceMax.X
            && mousePosition.Y >= surfacePosition.Y
            && mousePosition.Y <= surfaceMax.Y;

        return !insideSurface;
    }

    private static bool HasMeaningfulDifference(float current, float next)
    {
        return MathF.Abs(current - next) >= 0.5f;
    }

    private void FlushPendingLayoutSave()
    {
        lock (SyncRoot)
        {
            if (!pendingLayoutSave)
            {
                return;
            }

            Configuration.Save();
            pendingLayoutSave = false;
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
