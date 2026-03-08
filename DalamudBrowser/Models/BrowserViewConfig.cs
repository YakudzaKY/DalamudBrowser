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
    public bool SoundEnabled { get; set; } = true;
    public bool AutoRetry { get; set; } = true;
    public BrowserViewPerformancePreset PerformancePreset { get; set; } = BrowserViewPerformancePreset.Balanced;
    public float ZoomPercent { get; set; } = 100f;
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
        if (!Enum.IsDefined(PerformancePreset))
        {
            PerformancePreset = BrowserViewPerformancePreset.Balanced;
        }

        ZoomPercent = Math.Clamp(ZoomPercent, 25f, 500f);
        Width = Math.Max(320f, Width);
        Height = Math.Max(200f, Height);
    }
}
