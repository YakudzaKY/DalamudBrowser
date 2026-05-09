using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace DalamudBrowser.Bench
{
    public class TryFindView_Bench
    {
        private readonly Configuration _config;
        private readonly List<Guid> _allIds;

        public TryFindView_Bench(int collectionCount, int viewsPerCollection)
        {
            _config = new Configuration();
            _config.Collections = new List<BrowserCollectionConfig>();
            _allIds = new List<Guid>();

            for (int c = 0; c < collectionCount; c++)
            {
                var col = new BrowserCollectionConfig { Id = Guid.NewGuid(), Name = $"Col {c}", Views = new List<BrowserViewConfig>() };
                for (int v = 0; v < viewsPerCollection; v++)
                {
                    var view = new BrowserViewConfig { Id = Guid.NewGuid(), Title = $"View {c}-{v}" };
                    col.Views.Add(view);
                    _allIds.Add(view.Id);
                }
                _config.Collections.Add(col);
            }
        }

        public bool TryFindView_Baseline(Guid viewId, out BrowserViewConfig view)
        {
            foreach (var collection in _config.Collections)
            {
                var found = collection.Views.Find(candidate => candidate.Id == viewId);
                if (found != null)
                {
                    view = found;
                    return true;
                }
            }

            view = null;
            return false;
        }

        private Dictionary<Guid, BrowserViewConfig> _cache;
        public void BuildCache()
        {
            _cache = new Dictionary<Guid, BrowserViewConfig>();
            foreach (var collection in _config.Collections)
            {
                foreach (var view in collection.Views)
                {
                    _cache[view.Id] = view;
                }
            }
        }

        public bool TryFindView_Optimized(Guid viewId, out BrowserViewConfig view)
        {
            return _cache.TryGetValue(viewId, out view);
        }

        public void RunBaseline(int iterations)
        {
            int foundCount = 0;
            for (int i = 0; i < iterations; i++)
            {
                Guid idToFind = _allIds[i % _allIds.Count];
                if (TryFindView_Baseline(idToFind, out _))
                {
                    foundCount++;
                }
            }
        }

        public void RunOptimized(int iterations)
        {
            int foundCount = 0;
            for (int i = 0; i < iterations; i++)
            {
                Guid idToFind = _allIds[i % _allIds.Count];
                if (TryFindView_Optimized(idToFind, out _))
                {
                    foundCount++;
                }
            }
        }

        public class Configuration
        {
            public List<BrowserCollectionConfig> Collections { get; set; } = new();
        }

        public class BrowserCollectionConfig
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public List<BrowserViewConfig> Views { get; set; } = new();
        }

        public class BrowserViewConfig
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = "";
        }
    }
}
