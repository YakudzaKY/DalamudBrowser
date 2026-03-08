using System;
using System.Collections.Generic;

namespace DalamudBrowser.Models;

[Serializable]
public sealed class BrowserCollectionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Collection";
    public bool IsEnabled { get; set; } = true;
    public List<BrowserViewConfig> Views { get; set; } = [];

    public static BrowserCollectionConfig CreateDefault(string name)
    {
        return new BrowserCollectionConfig
        {
            Name = name,
        };
    }

    public void EnsureInitialized()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Collection" : Name.Trim();

        foreach (var view in Views)
        {
            view.EnsureInitialized();
        }
    }
}
