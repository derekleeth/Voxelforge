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
    /// The player's own hero is drawn last and a little bigger, so it's always on top. Ruins and arenas (Roadmap 5.4)
    /// only show where this player has explored, and only on the map itself: no edge arrows, or old finds would crowd it.
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
        public float HeroSize = 20f, SelfSize = 24f, HomeSize = 18f, EdgeSize = 18f, SiteSize = 18f;

        static readonly Color RuinInk = new Color(0.42f, 0.3f, 0.18f);
        static readonly Color ArenaInk = new Color(0.72f, 0.16f, 0.12f);

        /// <summary>
        /// Draws <paramref name="markers"/> on a map centred on <paramref name="center"/>. The map's edge is a circle of
        /// <paramref name="edge"/>.x when <paramref name="round"/>, otherwise a rectangle of half-size <paramref name="edge"/>.
        /// Ruins and arenas only show in <paramref name="explored"/> cells.
        /// </summary>
        public void Draw(List<MapMarker> markers, float2 center, float unitsPerBlock, bool round, float2 edge, PlayerId self,
            ExploredMap explored)
        {
            _used = 0;
            int selfIndex = -1;
            for (int i = 0; i < markers.Count; i++)
            {
                MapMarker marker = markers[i];
                if (marker.Kind == MapMarkerKind.Hero && marker.Owner == self)
                    selfIndex = i;
                else if (IsSite(marker.Kind))
                    DrawSite(marker, center, unitsPerBlock, round, edge, explored);
                else
                    DrawOne(marker, center, unitsPerBlock, round, edge, false);
            }
            if (selfIndex >= 0)
                DrawOne(markers[selfIndex], center, unitsPerBlock, round, edge, true);
            for (int i = _used; i < _pool.Count; i++)
                HudWidgets.SetActive(_pool[i], false);
        }

        static bool IsSite(MapMarkerKind kind) => kind == MapMarkerKind.Ruin || kind == MapMarkerKind.Arena;

        /// <summary>Whether a player whose map is <paramref name="explored"/> may see <paramref name="marker"/>: a ruin or arena only once they've been there.</summary>
        internal static bool Shows(in MapMarker marker, ExploredMap explored) =>
            !IsSite(marker.Kind) || (explored != null && explored.IsExplored(marker.WorldXZ.x, marker.WorldXZ.y));

        void DrawSite(in MapMarker marker, float2 center, float unitsPerBlock, bool round, float2 edge, ExploredMap explored)
        {
            if (!Shows(marker, explored))
                return;
            float2 p = MapMath.ToMap(marker.WorldXZ, center, unitsPerBlock);
            float inset = SiteSize * 0.5f;
            if (round ? MapMath.ClampToCircle(ref p, edge.x - inset) : MapMath.ClampToRect(ref p, edge - inset))
                return;
            bool arena = marker.Kind == MapMarkerKind.Arena;
            Image image = Next();
            Sprite sprite = arena ? MapSprites.Arena : MapSprites.Ruin;
            if (image.sprite != sprite)
                image.sprite = sprite;
            HudWidgets.SetColor(image, arena ? ArenaInk : RuinInk);
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = (Vector2)p;
            float size = arena ? SiteSize * 1.3f : SiteSize;
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.identity;
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
