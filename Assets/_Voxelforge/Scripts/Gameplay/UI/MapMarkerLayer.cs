using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws map markers for the minimap and the World Map (Roadmap 4.8): heroes as team-coloured arrows turned to their
    /// heading, Home Totems as little houses, and anything past the map's edge as an arrow on the edge pointing the way.
    /// The player's own hero is drawn last and a little bigger, so it's always on top.
    /// </summary>
    sealed class MapMarkerLayer
    {
        readonly RectTransform _root;
        readonly HudSkin _skin;
        readonly List<Image> _pool = new List<Image>();
        int _used;

        /// <param name="parent">Markers are placed from its centre, in its units.</param>
        public MapMarkerLayer(RectTransform parent, HudSkin skin)
        {
            _skin = skin;
            _root = HudWidgets.Place(HudWidgets.Rect("Markers", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        /// <summary>Marker sizes, in the parent's units.</summary>
        public float HeroSize = 20f, SelfSize = 24f, HomeSize = 18f, EdgeSize = 18f;

        /// <summary>
        /// Draws <paramref name="markers"/> on a map centred on <paramref name="center"/>. The map's edge is a circle of
        /// <paramref name="edge"/>.x when <paramref name="round"/>, otherwise a rectangle of half-size <paramref name="edge"/>.
        /// </summary>
        public void Draw(List<MapMarker> markers, float2 center, float unitsPerBlock, bool round, float2 edge, PlayerId self)
        {
            _used = 0;
            int selfIndex = -1;
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].Kind == MapMarkerKind.Hero && markers[i].Owner == self)
                    selfIndex = i;
                else
                    DrawOne(markers[i], center, unitsPerBlock, round, edge, false);
            }
            if (selfIndex >= 0)
                DrawOne(markers[selfIndex], center, unitsPerBlock, round, edge, true);
            for (int i = _used; i < _pool.Count; i++)
                HudWidgets.SetActive(_pool[i], false);
        }

        void DrawOne(in MapMarker marker, float2 center, float unitsPerBlock, bool round, float2 edge, bool isSelf)
        {
            float2 p = MapMath.ToMap(marker.WorldXZ, center, unitsPerBlock);
            float inset = EdgeSize * 0.5f;
            bool offEdge = round ? MapMath.ClampToCircle(ref p, edge.x - inset) : MapMath.ClampToRect(ref p, edge - inset);

            Sprite sprite;
            float size, rotation;
            Color tint = marker.Tint;
            if (offEdge)
            {
                sprite = _skin.MapEdgeArrow;
                rotation = MapMath.ArrowRotation(p);
                size = marker.Kind == MapMarkerKind.Home ? EdgeSize * 0.75f : EdgeSize;
                if (marker.Kind == MapMarkerKind.Home)
                    tint = Color.Lerp(tint, Color.white, 0.35f);
            }
            else if (marker.Kind == MapMarkerKind.Home)
            {
                sprite = _skin.MapHome;
                rotation = 0f;
                size = HomeSize;
            }
            else
            {
                sprite = _skin.MapHero;
                rotation = MapMath.HeadingRotation(marker.Heading);
                size = isSelf ? SelfSize : HeroSize;
            }

            Image image = Next();
            if (image.sprite != sprite)
                image.sprite = sprite;
            HudWidgets.SetColor(image, tint);
            RectTransform rect = image.rectTransform;
            var position = (Vector2)p;
            if (rect.anchoredPosition != position)
                rect.anchoredPosition = position;
            var sizeDelta = new Vector2(size, size);
            if (rect.sizeDelta != sizeDelta)
                rect.sizeDelta = sizeDelta;
            Quaternion turn = Quaternion.Euler(0f, 0f, rotation);
            if (rect.localRotation != turn)
                rect.localRotation = turn;
        }

        Image Next()
        {
            if (_used == _pool.Count)
            {
                Image image = HudWidgets.Image("Marker", _root, null, Color.white);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _pool.Add(image);
            }
            Image next = _pool[_used++];
            HudWidgets.SetActive(next, true);
            return next;
        }
    }
}
