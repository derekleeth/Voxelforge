using System;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's fog of war over a window of the map (Roadmap 4.8): a texel per <see cref="ExploredMap"/> cell, blank
    /// parchment where they haven't been and clear where they have. Bilinear filtering softens the edges. Only rebuilt
    /// when the window moves or more of the map is explored.
    /// </summary>
    public sealed class FogTexture : IDisposable
    {
        /// <summary>Unexplored parchment: the paper with nothing drawn on it yet.</summary>
        public static readonly Color32 Blank = new Color32(222, 204, 165, 255);

        Texture2D _texture;
        Color32[] _pixels;
        int2 _minCell;
        int2 _size;
        ExploredMap _map;
        int _version = -1;

        public Texture Texture => _texture;

        /// <summary>The cell at the south-west corner of the window.</summary>
        public int2 MinCell => _minCell;

        public int2 Size => _size;

        /// <summary>Shows <paramref name="map"/>'s fog over <paramref name="size"/> cells from <paramref name="minCell"/>.</summary>
        public void Update(ExploredMap map, int2 minCell, int2 size)
        {
            size = math.max(size, 1);
            if (_texture == null || !_size.Equals(size))
            {
                Destroy();
                _texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "Map Fog",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                _pixels = new Color32[size.x * size.y];
                _size = size;
                _map = null;
            }
            if (map != null && map == _map && map.Version == _version && minCell.Equals(_minCell))
                return;
            _map = map;
            _version = map?.Version ?? -1;
            _minCell = minCell;

            Color32 clear = Blank;
            clear.a = 0;
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                _pixels[y * size.x + x] = map != null && map.IsExplored(minCell + new int2(x, y)) ? clear : Blank;
            _texture.SetPixels32(_pixels);
            _texture.Apply(updateMipmaps: false);
        }

        void Destroy()
        {
            if (_texture == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(_texture);
            else
                Object.DestroyImmediate(_texture);
            _texture = null;
        }

        public void Dispose() => Destroy();
    }
}
