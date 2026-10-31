using System;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Plain stand-in shapes for the map (Roadmap 4.8), drawn in code the first time they're needed: a round mask, the
    /// rim, the hero arrow, the edge arrow and the Home Totem. White with a dark ink edge, so a team colour tints the
    /// fill and leaves an outline. <see cref="HudSkin"/>'s painted map art replaces any of them when it's set.
    /// </summary>
    static class MapSprites
    {
        const int Size = 64;

        static Sprite _disc, _ring, _hero, _edge, _home;

        public static Sprite Disc => _disc ??= Make("Map Disc", p => math.length(p) - 0.96f, edge: 0f);

        public static Sprite Ring => _ring ??= Make("Map Ring", p => math.abs(math.length(p) - 0.9f) - 0.07f, edge: 0f);

        /// <summary>A hero, pointing up (north) before it's turned to their heading.</summary>
        public static Sprite Hero => _hero ??= Make("Map Hero", p => Polygon(p,
            new float2(0f, 0.95f), new float2(0.72f, -0.78f), new float2(0f, -0.38f), new float2(-0.72f, -0.78f)));

        /// <summary>A partner or totem off the edge of the map, pointing up before it's turned their way.</summary>
        public static Sprite EdgeArrow => _edge ??= Make("Map Edge Arrow", p => Polygon(p,
            new float2(0f, 0.92f), new float2(0.78f, -0.62f), new float2(-0.78f, -0.62f)));

        /// <summary>A little house for a Home Totem.</summary>
        public static Sprite Home => _home ??= Make("Map Home", p => Polygon(p,
            new float2(-0.62f, -0.82f), new float2(0.62f, -0.82f), new float2(0.62f, 0.12f), new float2(0.9f, 0.12f),
            new float2(0f, 0.92f), new float2(-0.9f, 0.12f), new float2(-0.62f, 0.12f)));

        /// <summary>
        /// A sprite from a signed distance (negative inside) over -1..1. The outer <paramref name="edge"/> of the shape is
        /// dark ink, the rest white.
        /// </summary>
        static Sprite Make(string name, Func<float2, float> distance, float edge = 0.2f)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            float pixel = 2f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float2 p = new float2(x + 0.5f, y + 0.5f) * pixel - 1f;
                float d = distance(p);
                float alpha = math.saturate(0.5f - d / pixel);
                float ink = edge > 0f ? math.saturate((-d - edge) / pixel + 0.5f) : 1f;
                byte grey = (byte)math.lerp(48f, 255f, ink);
                pixels[y * Size + x] = new Color32(grey, grey, grey, (byte)(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Signed distance to a polygon (any winding): negative inside.</summary>
        static float Polygon(float2 p, params float2[] corners)
        {
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
            {
                float2 a = corners[j], b = corners[i];
                float2 ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
                best = math.min(best, math.distance(p, a + ab * t));
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * ab.x)
                    inside = !inside;
            }
            return inside ? -best : best;
        }
    }
}
