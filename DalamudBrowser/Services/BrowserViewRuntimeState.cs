using System;
using DalamudBrowser.Models;

namespace DalamudBrowser.Services;

public sealed class BrowserViewRuntimeState
{
    private readonly object syncRoot = new();
    private BrowserAvailabilityState availability = BrowserAvailabilityState.Unknown;
    private bool isChecking;
    private DateTimeOffset? lastCheckedUtc;
    private DateTimeOffset? lastAvailableUtc;
    private string? lastError;
    private DateTimeOffset nextProbeUtc = DateTimeOffset.MinValue;
    private bool forceLayoutApply = true;
    private int reloadGeneration;

    public BrowserViewStatusSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new BrowserViewStatusSnapshot(availability, isChecking, lastCheckedUtc, lastAvailableUtc, lastError, reloadGeneration);
        }
    }

    public bool TryBeginProbe(DateTimeOffset nowUtc, TimeSpan interval)
    {
        lock (syncRoot)
        {
            if (isChecking || nowUtc < nextProbeUtc)
            {
                return false;
            }

            isChecking = true;
            if (availability == BrowserAvailabilityState.Unknown)
            {
                availability = BrowserAvailabilityState.Checking;
            }

            lastError = null;
            nextProbeUtc = nowUtc + interval;
            return true;
        }
    }

    public void ForceProbe(DateTimeOffset nowUtc)
    {
        lock (syncRoot)
        {
            nextProbeUtc = nowUtc;
        }
    }

    public void MarkAvailable(DateTimeOffset nowUtc)
    {
        lock (syncRoot)
        {
            if (availability != BrowserAvailabilityState.Available)
            {
                reloadGeneration = reloadGeneration == int.MaxValue ? 1 : reloadGeneration + 1;
            }

            availability = BrowserAvailabilityState.Available;
            isChecking = false;
            lastCheckedUtc = nowUtc;
            lastAvailableUtc = nowUtc;
            lastError = null;
        }
    }

    public void MarkUnavailable(DateTimeOffset nowUtc, string reason)
    {
        lock (syncRoot)
        {
            availability = BrowserAvailabilityState.Unavailable;
            isChecking = false;
            lastCheckedUtc = nowUtc;
            lastError = reason;
        }
    }

    public bool ConsumeForceLayoutApply()
    {
        lock (syncRoot)
        {
            if (!forceLayoutApply)
            {
                return false;
            }

            forceLayoutApply = false;
            return true;
        }
    }

    public void RequestLayoutApply()
    {
        lock (syncRoot)
        {
            forceLayoutApply = true;
        }
    }
}
