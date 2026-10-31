using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's round parchment minimap (Roadmap 4.8), in the top-right corner of their own part of the screen
    /// (CLAUDE.md Law #5). North is always up and their arrow turns. It shows the land around them under their own fog,
    /// every partner as a team-coloured arrow, and every Home Totem; anyone past the edge gets an arrow on the rim.
    /// </summary>
    sealed class MinimapView : IDisposable
    {
        /// <summary>Blocks from the centre to the edge of the map.</summary>
        const float RadiusBlocks = 96f;

        /// <summary>Room inside the edge for the rim, in 1080p pixels.</summary>
        const float RimInset = 7f;

        readonly RectTransform _root;
        readonly MapLandView _land;
        readonly MapMarkerLayer _markers;
        readonly float _size;

        public MinimapView(RectTransform parent, HudSkin skin)
        {
            _size = skin.minimapSize;
            _root = HudWidgets.Place(HudWidgets.Rect("Minimap", parent), Vector2.one, Vector2.one, new Vector2(-16f, -16f),
                new Vector2(_size, _size));

            // Blank parchment under the tiles, and the mask that makes the map round.
            Image paper = HudWidgets.Image("Paper", _root, MapSprites.Disc, FogTexture.Blank);
            HudWidgets.Stretch(paper.rectTransform, RimInset * 0.5f);
            paper.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _land = new MapLandView(paper.rectTransform);

            Image rim = HudWidgets.Image("Rim", _root, skin.MapRim, skin.mapInk);
            HudWidgets.Stretch(rim.rectTransform);

            TextMeshProUGUI north = HudWidgets.SmallLabel("North", _root, skin, 22f, skin.mapInk, TextAlignmentOptions.Center);
            HudWidgets.Place(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f),
                new Vector2(30f, 26f));
            north.text = "N";

            _markers = new MapMarkerLayer(_root, skin);
        }

        public RectTransform Root => _root;

        /// <param name="scale">How much to shrink it to fit a small view (1 = full size).</param>
        public void Tick(MapTileCache tiles, ExploredMap explored, PlayerController self, List<MapMarker> markers, float scale)
        {
            if (!Mathf.Approximately(_root.localScale.x, scale))
                _root.localScale = new Vector3(scale, scale, 1f);

            float radius = _size * 0.5f - RimInset;
            float unitsPerBlock = radius / RadiusBlocks;
            Vector3 hero = self.VisualRoot.position;
            var center = new float2(hero.x, hero.z);
            _land.Draw(tiles, explored, center, new float2(RadiusBlocks), unitsPerBlock);
            _markers.Draw(markers, center, unitsPerBlock, round: true, new float2(radius), self.PlayerId);
        }

        public void Dispose() => _land.Dispose();
    }
}
