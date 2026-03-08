using System;

namespace DalamudBrowser.Models;

[Serializable]
public sealed class BrowserViewConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Browser View";
    public string Url { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool AutoRetry { get; set; } = true;
    public float PositionX { get; set; } = 220f;
    public float PositionY { get; set; } = 220f;
    public float Width { get; set; } = 640f;
    public float Height { get; set; } = 420f;

    public void EnsureInitialized()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Title = string.IsNullOrWhiteSpace(Title) ? "Browser View" : Title.Trim();
        Url ??= string.Empty;
        Width = Math.Max(320f, Width);
        Height = Math.Max(200f, Height);
    }
}
