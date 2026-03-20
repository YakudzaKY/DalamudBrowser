using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DalamudBrowser.Bench
{
    public class BrowserWorkspace_Bench
    {
        private readonly Configuration _config;

        public BrowserWorkspace_Bench()
        {
            _config = new Configuration();
            _config.Collections = new List<BrowserCollectionConfig>();

            for (int c = 0; c < 5; c++)
            {
                var col = new BrowserCollectionConfig { Id = Guid.NewGuid(), Name = $"Col {c}", IsEnabled = true, Views = new List<BrowserViewConfig>() };
                for (int v = 0; v < 10; v++)
                {
                    col.Views.Add(new BrowserViewConfig { Id = Guid.NewGuid(), Title = $"View {c}-{v}", Url = "http://example.com", IsVisible = true });
                }
                _config.Collections.Add(col);
            }
        }

        public void RunBaseline(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                var knownViewIds = _config.Collections
                    .SelectMany(collection => collection.Views)
                    .Select(view => view.Id)
                    .ToList();

                var windows = _config.Collections
                    .Where(collection => collection.IsEnabled)
                    .SelectMany(collection => collection.Views)
                    .Where(view => view.IsVisible)
                    .Select(view =>
                    {
                        // Simplified ResolveWindowLayout for benchmark
                        var layoutPosition = new Vector2(0, 0);
                        var layoutSize = new Vector2(800, 600);
                        return new BrowserViewWindowSnapshot_Mock(
                            view.Id, view.Title, view.Url, false, false, false, false, false, default, 60, 30, 10, 100, 100, layoutPosition, layoutSize);
                    })
                    .ToList();
            }
        }

        public void RunOptimized(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                int viewCapacity = 0;
                int windowCapacity = 0;

                foreach (var collection in _config.Collections)
                {
                    var viewsCount = collection.Views.Count;
                    viewCapacity += viewsCount;
                    if (collection.IsEnabled)
                    {
                        windowCapacity += viewsCount; // Rough estimate to prevent reallocations
                    }
                }

                var knownViewIds = new List<Guid>(viewCapacity);
                var windows = new List<BrowserViewWindowSnapshot_Mock>(windowCapacity);

                foreach (var collection in _config.Collections)
                {
                    foreach (var view in collection.Views)
                    {
                        knownViewIds.Add(view.Id);
                    }

                    if (!collection.IsEnabled) continue;

                    foreach (var view in collection.Views)
                    {
                        if (!view.IsVisible) continue;

                        // Simplified ResolveWindowLayout for benchmark
                        var layoutPosition = new Vector2(0, 0);
                        var layoutSize = new Vector2(800, 600);
                        windows.Add(new BrowserViewWindowSnapshot_Mock(
                            view.Id, view.Title, view.Url, false, false, false, false, false, default, 60, 30, 10, 100, 100, layoutPosition, layoutSize));
                    }
                }
            }
        }

        public readonly record struct BrowserViewWindowSnapshot_Mock(
            Guid ViewId, string Title, string Url, bool Locked, bool ClickThrough, bool ActOptimizations, bool UseCustomFrameRates, bool SoundEnabled,
            int PerformancePreset, int InteractiveFrameRate, int PassiveFrameRate, int HiddenFrameRate,
            float ZoomPercent, float OpacityPercent, Vector2 Position, Vector2 Size);

        public class Configuration
        {
            public List<BrowserCollectionConfig> Collections { get; set; } = new();
        }

        public class BrowserCollectionConfig
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsEnabled { get; set; }
            public List<BrowserViewConfig> Views { get; set; } = new();
        }

        public class BrowserViewConfig
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public bool IsVisible { get; set; }
        }
    }
}
