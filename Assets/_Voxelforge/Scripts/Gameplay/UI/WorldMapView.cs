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
    /// One player's World Map window (Roadmap 4.8): a big sheet of parchment filling their own part of the screen above
    /// their action bar (CLAUDE.md Law #5). It shows the painted land under their own fog, every hero and Home Totem, and
    /// arrows on the edge for anything off the sheet, with the controls written underneath. <see cref="PlayerWorldMap"/>
    /// decides where it looks.
    /// </summary>
    sealed class WorldMapView : IDisposable
    {
        const float Margin = 20f;
        const float FrameInset = 12f;
        const float TitleHeight = 44f;
        const float HintHeight = 30f;
        const float CompassSize = 116f;
        const float CompassInset = 14f;
        /// <summary>How far the neatline sits outside the land, so it frames the edge rather than covering it.</summary>
        const float NeatlineOut = 4f;

        readonly RectTransform _root;
        readonly RectTransform _paper;
        readonly MapLandView _land;
        readonly MapMarkerLayer _markers;
        readonly TextMeshProUGUI _hints;
        string _scheme;
        bool _hasHome;

        public WorldMapView(RectTransform parent, HudSkin skin)
        {
            _root = HudWidgets.Place(HudWidgets.Rect("World Map", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(800f, 600f));

            // The painted sheet tiles its burnt edges rather than stretching them; the plain panel slices.
            Image frame = skin.mapSheet != null
                ? HudWidgets.Image("Frame", _root, skin.mapSheet, Color.white, Image.Type.Tiled)
                : HudWidgets.Image("Frame", _root, skin.panel, Color.white, Image.Type.Sliced);
            HudWidgets.Stretch(frame.rectTransform);

            TextMeshProUGUI title = HudWidgets.Label("Title", _root, skin.titleFont, 34f, skin.ink, TextAlignmentOptions.Center);
            HudWidgets.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f),
                new Vector2(400f, TitleHeight));
            title.text = "World Map";

            Image paper = HudWidgets.Image("Paper", _root, skin.white, FogTexture.Blank);
            _paper = paper.rectTransform;
            _paper.anchorMin = Vector2.zero;
            _paper.anchorMax = Vector2.one;
            _paper.offsetMin = new Vector2(FrameInset, FrameInset + HintHeight);
            _paper.offsetMax = new Vector2(-FrameInset, -TitleHeight);
            paper.gameObject.AddComponent<RectMask2D>();
            _land = new MapLandView(_paper);

            // A compass rose in the corner under the markers, with N over its north point.
            var northAt = new Vector2(-10f, -6f);
            if (skin.mapCompass != null)
            {
                Image compass = HudWidgets.Image("Compass", _paper, skin.mapCompass, new Color(1f, 1f, 1f, 0.85f));
                HudWidgets.Place(compass.rectTransform, Vector2.one, Vector2.one, new Vector2(-CompassInset, -CompassInset - 26f),
                    new Vector2(CompassSize, CompassSize));
                northAt = new Vector2(-CompassInset - CompassSize * 0.5f + 20f, -2f);
            }
            _markers = new MapMarkerLayer(_paper, skin) { HeroSize = 26f, SelfSize = 32f, HomeSize = 24f, EdgeSize = 26f };

            TextMeshProUGUI north = HudWidgets.SmallLabel("North", _paper, skin, 26f, skin.mapInk, TextAlignmentOptions.Center);
            HudWidgets.Place(north.rectTransform, Vector2.one, Vector2.one, northAt, new Vector2(40f, 30f));
            north.text = "N";

            if (skin.mapNeatline != null)
            {
                Image neatline = HudWidgets.Image("Neatline", _root, skin.mapNeatline, Color.white, Image.Type.Sliced);
                RectTransform line = neatline.rectTransform;
                line.anchorMin = Vector2.zero;
                line.anchorMax = Vector2.one;
                line.offsetMin = _paper.offsetMin - new Vector2(NeatlineOut, NeatlineOut);
                line.offsetMax = _paper.offsetMax + new Vector2(NeatlineOut, NeatlineOut);
            }

            _hints = HudWidgets.SmallLabel("Hints", _root, skin, 20f, skin.ink, TextAlignmentOptions.Center);
            HudWidgets.Place(_hints.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, FrameInset - 2f),
                new Vector2(1200f, HintHeight));
        }

        /// <param name="room">The space it may fill, in 1080p units: the player's own area less their action bar.</param>
        /// <param name="barRoom">Height kept clear for the action bar at the bottom, in 1080p units.</param>
        public void Tick(PlayerWorldMap map, MapTileCache tiles, ExploredMap explored, PlayerController self,
            List<MapMarker> markers, Vector2 room, float barRoom, string scheme)
        {
            var size = new Vector2(math.max(240f, room.x - Margin * 2f), math.max(200f, room.y - barRoom - Margin * 2f));
            if (_root.sizeDelta != size)
                _root.sizeDelta = size;
            var offset = new Vector2(0f, barRoom * 0.5f);
            if (_root.anchoredPosition != offset)
                _root.anchoredPosition = offset;

            WorldMapModel model = map.Model;
            Vector2 paper = _paper.rect.size;
            float2 half = new float2(paper.x, paper.y) * 0.5f;
            _land.Draw(tiles, explored, model.Center, half / model.UnitsPerBlock, model.UnitsPerBlock);
            _markers.Draw(markers, model.Center, model.UnitsPerBlock, round: false, half, self.PlayerId);

            if (scheme != _scheme || map.HasHome != _hasHome)
            {
                _scheme = scheme;
                _hasHome = map.HasHome;
                _hints.text = Hints(scheme, _hasHome);
            }
        }

        static string Hints(string scheme, bool hasHome)
        {
            (string pan, string zoom, string centre, string home, string close) = scheme switch
            {
                "Gamepad" => ("Stick", "LB / RB", "A", "LT", "Select"),
                "KeyboardLeft" => ("WASD", "Z / C", "Space", "E", "M"),
                "KeyboardRight" => ("Arrows", ". / /", "R-Ctrl", "Enter", "Num +"),
                _ => ("WASD", "Wheel", "Space", "Right-click", "M"),
            };
            string hearth = hasHome ? $"   <b>{home}</b> Hearth home" : "";
            return $"<b>{pan}</b> Pan   <b>{zoom}</b> Zoom   <b>{centre}</b> Back to me{hearth}   <b>{close}</b> Close";
        }

        public void Dispose() => _land.Dispose();
    }
}
