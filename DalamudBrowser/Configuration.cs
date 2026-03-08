using Dalamud.Configuration;
using DalamudBrowser.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DalamudBrowser;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OpenManagerOnStartup { get; set; } = true;
    public int LinkCheckIntervalSeconds { get; set; } = 5;
    public int LinkRequestTimeoutSeconds { get; set; } = 3;
    public Guid SelectedCollectionId { get; set; } = Guid.Empty;
    public List<BrowserCollectionConfig> Collections { get; set; } = [];

    public void EnsureInitialized()
    {
        LinkCheckIntervalSeconds = Math.Clamp(LinkCheckIntervalSeconds, 2, 60);
        LinkRequestTimeoutSeconds = Math.Clamp(LinkRequestTimeoutSeconds, 2, 30);

        if (Collections.Count == 0)
        {
            var collection = BrowserCollectionConfig.CreateDefault("Default");
            Collections.Add(collection);
            SelectedCollectionId = collection.Id;
        }

        foreach (var collection in Collections)
        {
            collection.EnsureInitialized();
        }

        if (SelectedCollectionId == Guid.Empty || Collections.All(collection => collection.Id != SelectedCollectionId))
        {
            SelectedCollectionId = Collections[0].Id;
        }
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
