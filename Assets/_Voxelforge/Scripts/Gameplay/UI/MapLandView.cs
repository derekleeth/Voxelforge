using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The land part of a map (Roadmap 4.8): the painted tiles from the shared <see cref="MapTileCache"/> under one
    /// player's own fog (<see cref="FogTexture"/>). Places everything from its parent's centre, so the parent decides the
    /// map's size and clips it. The minimap and the World Map each have one.
    /// </summary>
    sealed class MapLandView : IDisposable
    {
        readonly RectTransform _root;
        readonly List<RawImage> _tiles = new List<RawImage>();
        readonly RawImage _fog;
        readonly FogTexture _fogTexture = new FogTexture();
        int _usedTiles;

        public MapLandView(RectTransform parent)
        {
            _root = HudWidgets.Place(HudWidgets.Rect("Land", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            _fog = NewImage("Fog");
        }

        /// <summary>
        /// Draws the land within <paramref name="halfSize"/> blocks of <paramref name="center"/> (world x, z), at
        /// <paramref name="unitsPerBlock"/>. Tiles still being painted show as blank parchment until they're ready.
        /// </summary>
        public void Draw(MapTileCache tiles, ExploredMap explored, float2 center, float2 halfSize, float unitsPerBlock)
        {
            _usedTiles = 0;
            if (tiles != null)
            {
                int2 first = MapTile.Of(center - halfSize);
                int2 last = MapTile.Of(center + halfSize);
                for (int z = first.y; z <= last.y; z++)
                for (int x = first.x; x <= last.x; x++)
                {
                    var tile = new int2(x, z);
                    if (!tiles.TryGetTile(tile, out Texture2D texture))
                        continue;
                    RawImage image = NextTile();
                    if (image.texture != texture)
                        image.texture = texture;
                    Place(image.rectTransform, MapTile.Origin(tile), MapTile.Size, center, unitsPerBlock);
                }
            }
            for (int i = _usedTiles; i < _tiles.Count; i++)
                HudWidgets.SetActive(_tiles[i], false);

            // The fog covers a few cells more than the view, so its soft edge never shows at the map's edge.
            int2 cells = (int2)math.ceil(halfSize * 2f / ExploredMap.CellSize) + 3;
            int2 minCell = ExploredMap.CellOf(center.x, center.y) - cells / 2;
            _fogTexture.Update(explored, minCell, cells);
            if (_fog.texture != _fogTexture.Texture)
                _fog.texture = _fogTexture.Texture;
            if (_fog.transform.GetSiblingIndex() != _root.childCount - 1)
                _fog.transform.SetAsLastSibling();
            Place(_fog.rectTransform, minCell * ExploredMap.CellSize, cells * ExploredMap.CellSize, center, unitsPerBlock);
        }

        static void Place(RectTransform rect, int2 originBlock, int2 sizeBlocks, float2 center, float unitsPerBlock)
        {
            var position = (Vector2)MapMath.ToMap(originBlock, center, unitsPerBlock);
            var size = (Vector2)((float2)sizeBlocks * unitsPerBlock);
            if (rect.anchoredPosition != position)
                rect.anchoredPosition = position;
            if (rect.sizeDelta != size)
                rect.sizeDelta = size;
        }

        static void Place(RectTransform rect, int2 originBlock, int sizeBlocks, float2 center, float unitsPerBlock) =>
            Place(rect, originBlock, new int2(sizeBlocks), center, unitsPerBlock);

        RawImage NextTile()
        {
            if (_usedTiles == _tiles.Count)
            {
                RawImage image = NewImage("Tile");
                _tiles.Add(image);
            }
            RawImage next = _tiles[_usedTiles++];
            HudWidgets.SetActive(next, true);
            return next;
        }

        RawImage NewImage(string name)
        {
            RawImage image = HudWidgets.Rect(name, _root).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = Vector2.zero;
            return image;
        }

        public void Dispose() => _fogTexture.Dispose();
    }
}
