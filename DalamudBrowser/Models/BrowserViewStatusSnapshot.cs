using System;

namespace DalamudBrowser.Models;

public readonly record struct BrowserViewStatusSnapshot(
    BrowserAvailabilityState Availability,
    bool IsChecking,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? LastAvailableUtc,
    string? LastError,
    int ReloadGeneration);
