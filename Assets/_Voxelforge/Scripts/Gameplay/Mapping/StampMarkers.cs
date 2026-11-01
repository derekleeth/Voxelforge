using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Ruins and arenas for the maps (Roadmap 5.4), on each PC: a marker for every site in the parts of the world this PC's
    /// players have explored. Each map then shows only the ones in its own player's explored cells
    /// (<see cref="MapMarkerLayer"/>), so a ruin appears on your map once you've been near it. Sites come from the world's
    /// <see cref="StampSiteCache"/>, a couple of regions a frame, so a long saved map loads without a hitch.
    /// </summary>
    public sealed class StampMarkers : IMapMarkerSource, IDisposable
    {
        const int RegionsPerFrame = 2;

        readonly StampSiteCache _sites;
        readonly HashSet<ExploredMap> _watched = new HashSet<ExploredMap>();
        readonly HashSet<int2> _wanted = new HashSet<int2>();
        readonly Queue<int2> _pending = new Queue<int2>();
        readonly List<MapMarker> _markers = new List<MapMarker>();

        public StampMarkers(StampSiteCache sites) => _sites = sites;

        /// <summary>Shows sites wherever <paramref name="map"/>'s player has explored, now and from now on.</summary>
        public void Watch(ExploredMap map)
        {
            if (map == null || !_watched.Add(map))
                return;
            map.Explored += OnExplored;
            foreach (int2 cell in map.Cells)
                Want(cell);
        }

        void OnExplored(IReadOnlyList<int2> cells)
        {
            foreach (int2 cell in cells)
                Want(cell);
        }

        void Want(int2 cell)
        {
            int2 region = StampSiteCache.RegionOf((cell.x + 0.5f) * ExploredMap.CellSize, (cell.y + 0.5f) * ExploredMap.CellSize);
            if (_wanted.Add(region))
                _pending.Enqueue(region);
        }

        public void CollectMarkers(List<MapMarker> markers)
        {
            for (int i = 0; i < RegionsPerFrame && _pending.Count > 0; i++)
            {
                foreach (StampSite site in _sites.SitesIn(_pending.Dequeue()))
                {
                    _markers.Add(new MapMarker
                    {
                        Kind = _sites.InfoOf(site).Kind == StampKind.Arena ? MapMarkerKind.Arena : MapMarkerKind.Ruin,
                        WorldXZ = (float2)site.Centre + 0.5f,
                    });
                }
            }
            markers.AddRange(_markers);
        }

        public void Dispose()
        {
            foreach (ExploredMap map in _watched)
                map.Explored -= OnExplored;
            _watched.Clear();
        }
    }
}
