using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build HUD Art</i> (Roadmap 4.4): paints the comic-book fantasy HUD in code and writes the
    /// <see cref="HudSkin"/> that uses it. Re-running it repaints the same files in place.
    /// <list type="bullet">
    /// <item>Sprites in <c>Art/Textures/UI/</c>: a parchment panel and wooden, ink-outlined frames with brass rivets,
    /// halftone-dot shading like an old comic print, a glow flash and a starburst for craters.</item>
    /// <item>TextMeshPro font assets in <c>Art/Fonts/</c> with a thick ink outline and a hard drop shadow built in:
    /// Bangers (OFL) for numbers and pop-ups, MedievalSharp (OFL) for spell names.</item>
    /// </list>
    /// Needs TMP Essential Resources (Window &gt; TextMeshPro &gt; Import TMP Essential Resources).
    /// Spell icons are rendered in Blender into <c>Art/Textures/UI/Icons/</c>; <i>Build Abilities</i> hooks them up.
    /// </summary>
    public static class HudArtBuilder
    {
        const string UiDir = "Assets/_Voxelforge/Art/Textures/UI";
        const string FontDir = "Assets/_Voxelforge/Art/Fonts";
        const string SkinDir = "Assets/_Voxelforge/Data/UI";
        public const string SkinPath = SkinDir + "/HudSkin.asset";

        static readonly Color Ink = new Color(0.07f, 0.04f, 0.05f);
        static readonly Color Parchment = new Color(0.95f, 0.87f, 0.68f);
        static readonly Color Burnt = new Color(0.7f, 0.5f, 0.28f);
        static readonly Color Wood = new Color(0.55f, 0.34f, 0.16f);
        static readonly Color WoodDark = new Color(0.36f, 0.2f, 0.09f);
        static readonly Color Brass = new Color(1f, 0.8f, 0.35f);
        static readonly Color Night = new Color(0.13f, 0.1f, 0.22f);
        static readonly Color Dusk = new Color(0.3f, 0.23f, 0.45f);

        [MenuItem("Voxelforge/Build HUD Art")]
        public static void BuildMenu()
        {
            Build();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voxelforge] HUD art built: {UiDir}, {FontDir}, {SkinPath}.");
        }

        public static HudSkin Build()
        {
            EnsureFolder(UiDir);
            EnsureFolder(SkinDir);

            Sprite panel = Save("HudPanel", 128, 128, PaintPanel, border: new Vector4(30, 30, 30, 30));
            Sprite slotFrame = Save("HudSlotFrame", 128, 128, PaintSlotFrame);
            Sprite slotBack = Save("HudSlotBack", 128, 128, PaintSlotBack);
            Sprite slotFlash = Save("HudSlotFlash", 128, 128, PaintFlash);
            Sprite white = Save("HudWhite", 32, 32, (x, y, w, h) => Color.white);
            Sprite barFrame = Save("HudBarFrame", 128, 48, PaintBarFrame, border: new Vector4(22, 22, 22, 22));
            Sprite barFill = Save("HudBarFill", 64, 32, PaintBarFill);
            Sprite barBack = Save("HudBarBack", 64, 32, PaintBarBack);
            Sprite starburst = Save("HudStarburst", 256, 256, PaintStarburst);
            Sprite fallback = Save("HudFallbackIcon", 128, 128, PaintFallbackIcon);

            TMP_FontAsset comic = MakeFont($"{FontDir}/Bangers/Bangers-Regular.ttf", $"{FontDir}/Bangers/Bangers SDF.asset",
                outline: 0.28f, dilate: 0.2f, shadow: 1f);
            TMP_FontAsset title = MakeFont($"{FontDir}/MedievalSharp/MedievalSharp.ttf",
                $"{FontDir}/MedievalSharp/MedievalSharp SDF.asset", outline: 0.22f, dilate: 0.1f, shadow: 0.8f);

            var skin = AssetDatabase.LoadAssetAtPath<HudSkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<HudSkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }
            skin.comicFont = comic;
            skin.comicSmall = MakeCrispPreset(comic, $"{FontDir}/Bangers/Bangers SDF - Crisp.mat");
            skin.titleFont = title;
            skin.manaBarHeight = 22f;
            skin.panel = panel;
            skin.slotFrame = slotFrame;
            skin.slotBack = slotBack;
            skin.slotFlash = slotFlash;
            skin.white = white;
            skin.barFrame = barFrame;
            skin.barFill = barFill;
            skin.barBack = barBack;
            skin.starburst = starburst;
            skin.fallbackIcon = fallback;
            skin.looks = AssetDatabase.LoadAssetAtPath<AbilityLookBook>(AbilityBuilder.LookBookPath);
            EditorUtility.SetDirty(skin);
            return skin;
        }

        // ---- Painters: (x, y, width, height) → colour, with y up. ----

        static Color PaintPanel(int x, int y, int w, int h)
        {
            float d = RoundRect(x, y, w, h, 3f, 16f);
            if (d > 0.5f)
                return Clear;
            // Parchment, burnt toward the edge, with halftone shading low down and a little grain.
            float edge = Mathf.Clamp01(1f + d / 14f);
            Color paper = Color.Lerp(Parchment, Burnt, edge * edge * 0.8f);
            float shade = Mathf.Clamp01(0.55f - y / (float)h) * 0.9f;
            paper = Color.Lerp(paper, Burnt, Halftone(x, y, 6f, shade) * 0.55f);
            paper *= 0.96f + 0.04f * Noise(x, y);
            paper.a = 1f;
            return Over(paper, InkLine(d, 5f));
        }

        static Color PaintSlotFrame(int x, int y, int w, int h)
        {
            float d = RoundRect(x, y, w, h, 2f, 14f);
            if (d > 0.5f || d < -18f)
                return Clear;
            // A wooden band between an outer and an inner ink line, grain running around it.
            bool horizontal = Mathf.Abs(y - h * 0.5f) > Mathf.Abs(x - w * 0.5f);
            float along = horizontal ? x : y;
            float grain = 0.5f + 0.5f * Mathf.Sin(along * 0.35f + Mathf.Sin(along * 0.07f + (horizontal ? y : x)) * 2.5f);
            Color wood = Color.Lerp(WoodDark, Wood, grain * 0.7f + 0.3f * Noise(x, y));
            // Lit from the top left.
            if (y > h * 0.5f && x < w * 0.5f)
                wood = Color.Lerp(wood, Brass, 0.12f);
            Color c = Over(wood, InkLine(d, 4.5f));
            c = Over(c, InkLine(d + 15f, 3f));
            // Brass rivets in the corners.
            foreach (Vector2 corner in new[] { new Vector2(11, 11), new Vector2(w - 11, 11), new Vector2(11, h - 11), new Vector2(w - 11, h - 11) })
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), corner);
                if (r < 5.5f)
                {
                    Color rivet = Color.Lerp(Brass, new Color(0.6f, 0.4f, 0.12f), Mathf.Clamp01((r - 1f) / 4f));
                    c = Over(rivet, InkLine(r - 5.5f, 1.6f));
                }
            }
            return c;
        }

        static Color PaintSlotBack(int x, int y, int w, int h)
        {
            float d = RoundRect(x, y, w, h, 2f, 14f);
            if (d > 0.5f)
                return Clear;
            float centre = Vector2.Distance(new Vector2(x, y), new Vector2(w * 0.4f, h * 0.65f)) / w;
            Color c = Color.Lerp(Dusk, Night, Mathf.Clamp01(centre * 1.6f));
            // Comic halftone highlight from the top left.
            float light = Mathf.Clamp01(0.9f - centre * 1.8f);
            c = Color.Lerp(c, Dusk * 1.5f, Halftone(x, y, 7f, light) * 0.6f);
            c.a = Mathf.Clamp01(0.5f - d);
            return c;
        }

        static Color PaintFlash(int x, int y, int w, int h)
        {
            var p = new Vector2(x + 0.5f - w * 0.5f, y + 0.5f - h * 0.5f) / (w * 0.5f);
            float r = p.magnitude;
            float angle = Mathf.Atan2(p.y, p.x);
            float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f)), 8f) * Mathf.Clamp01(1f - r);
            float glow = Mathf.Clamp01(1f - r);
            float a = Mathf.Clamp01(glow * glow * 0.9f + rays * 0.7f);
            return new Color(1f, 1f, 0.92f, a);
        }

        static Color PaintBarFrame(int x, int y, int w, int h)
        {
            float d = RoundRect(x, y, w, h, 2f, 10f);
            if (d > 0.5f || d < -12f)
                return Clear;
            float grain = 0.5f + 0.5f * Mathf.Sin(x * 0.3f + Mathf.Sin(x * 0.05f + y) * 2f);
            Color wood = Color.Lerp(WoodDark, Wood, grain * 0.7f + 0.3f * Noise(x, y));
            Color c = Over(wood, InkLine(d, 4f));
            return Over(c, InkLine(d + 10f, 2.5f));
        }

        static Color PaintBarFill(int x, int y, int w, int h)
        {
            float v = y / (float)(h - 1);
            // White, tinted at runtime: a bright stripe on top and halftone shade along the bottom.
            float shade = Mathf.Clamp01(0.55f - v) * 1.4f;
            float c = Mathf.Lerp(0.78f, 1f, v);
            c = Mathf.Lerp(c, 0.5f, Halftone(x, y, 5f, shade) * 0.7f);
            if (v > 0.68f && v < 0.84f)
                c = 1f;
            return new Color(c, c, c, 1f);
        }

        static Color PaintBarBack(int x, int y, int w, int h)
        {
            Color c = Color.Lerp(Night * 0.7f, Night, y / (float)h);
            c = Color.Lerp(c, Dusk, Halftone(x, y, 5f, 0.35f) * 0.4f);
            c.a = 1f;
            return c;
        }

        static Color PaintStarburst(int x, int y, int w, int h)
        {
            var p = new Vector2(x + 0.5f - w * 0.5f, y + 0.5f - h * 0.5f);
            float r = p.magnitude / (w * 0.5f);
            float angle = Mathf.Atan2(p.y, p.x);
            // 13 jagged spikes, each a little different, like a hand-inked "KA-BOOM!" balloon.
            const int Spikes = 13;
            float t = (angle / (Mathf.PI * 2f) + 1f) * Spikes;
            int spike = Mathf.FloorToInt(t) % Spikes;
            float f = t - Mathf.Floor(t);
            float outer = 0.86f + 0.12f * Hash(spike);
            float inner = 0.58f + 0.06f * Hash(spike + 31);
            float edge = Mathf.Lerp(outer, inner, Mathf.Abs(f - 0.5f) * 2f);
            float d = (r - edge) * w * 0.5f; // Roughly in pixels.
            if (d > 0.5f)
                return Clear;
            // White, tinted at runtime, with halftone dots toward the rim.
            float rim = Mathf.Clamp01((r / edge - 0.45f) * 1.6f);
            float c = Mathf.Lerp(1f, 0.72f, Halftone(x, y, 9f, rim * rim));
            return Over(new Color(c, c, c, 1f), InkLine(d, 7f));
        }

        static Color PaintFallbackIcon(int x, int y, int w, int h)
        {
            Color c = PaintSlotBack(x, y, w, h);
            // A faceted mana gem: a diamond with an ink outline and a lit face.
            float px = Mathf.Abs(x + 0.5f - w * 0.5f) / (w * 0.3f);
            float py = Mathf.Abs(y + 0.5f - h * 0.5f) / (h * 0.38f);
            float d = (px + py - 1f) * w * 0.25f;
            if (d < 0.5f)
            {
                bool lit = x < w * 0.5f == y > h * 0.5f;
                Color gem = lit ? new Color(0.75f, 0.9f, 1f) : new Color(0.35f, 0.55f, 0.95f);
                c = Over(gem, InkLine(d, 4f));
            }
            return c;
        }

        // ---- Painting helpers ----

        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>Signed distance (pixels, negative inside) to a rounded rectangle inset from the image edge.</summary>
        static float RoundRect(int x, int y, int w, int h, float inset, float radius)
        {
            float px = Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - inset - radius);
            float py = Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - inset - radius);
            float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
        }

        /// <summary>Ink for a line <paramref name="width"/> pixels wide just inside the edge at distance <paramref name="d"/>.</summary>
        static Color InkLine(float d, float width)
        {
            float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + width + 0.5f);
            return new Color(Ink.r, Ink.g, Ink.b, a);
        }

        /// <summary>How much of a halftone dot covers this pixel: dots on a 45° grid, bigger where <paramref name="shade"/> is.</summary>
        static float Halftone(int x, int y, float spacing, float shade)
        {
            if (shade <= 0f)
                return 0f;
            float u = (x + y) * 0.7071f / spacing;
            float v = (x - y) * 0.7071f / spacing;
            float du = u - Mathf.Round(u);
            float dv = v - Mathf.Round(v);
            float dist = Mathf.Sqrt(du * du + dv * dv) * spacing;
            float radius = Mathf.Sqrt(Mathf.Clamp01(shade)) * spacing * 0.55f;
            return Mathf.Clamp01(radius - dist + 0.5f);
        }

        static float Noise(int x, int y) => Mathf.PerlinNoise(x * 0.21f + 3.7f, y * 0.21f + 9.1f);

        static float Hash(int n)
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 13;
            h *= 1274126177u;
            return (h & 0xFFFF) / 65535f;
        }

        /// <summary><paramref name="top"/> painted over <paramref name="bottom"/>.</summary>
        static Color Over(Color bottom, Color top)
        {
            float a = top.a + bottom.a * (1f - top.a);
            if (a <= 0f)
                return Clear;
            Color c = (top * top.a + bottom * bottom.a * (1f - top.a)) / a;
            c.a = a;
            return c;
        }

        // ---- Assets ----

        static Sprite Save(string name, int width, int height, Func<int, int, int, int, Color> paint, Vector4 border = default)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = paint(x, y, width, height);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            string path = $"{UiDir}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// A dynamic SDF font asset with an ink outline, a little extra weight and a hard drop shadow (underlay) baked
        /// into its material, so every label using it looks inked without per-label setup.
        /// </summary>
        static TMP_FontAsset MakeFont(string fontPath, string assetPath, float outline, float dilate, float shadow)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError($"[Voxelforge] Missing font {fontPath}.");
                return null;
            }
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (asset == null)
            {
                asset = TMP_FontAsset.CreateFontAsset(font, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024);
                asset.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                asset.atlasTextures[0].name = asset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
                // Fill the atlas with what the HUD writes, so nothing pops in on first use.
                asset.TryAddCharacters(" 0123456789+-/×!?.,:'%ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", out _);
            }

            Material material = asset.material;
            material.SetColor("_OutlineColor", Ink);
            material.SetFloat("_OutlineWidth", outline);
            material.SetFloat("_FaceDilate", dilate);
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(Ink.r, Ink.g, Ink.b, 0.95f));
            material.SetFloat("_UnderlayOffsetX", shadow);
            material.SetFloat("_UnderlayOffsetY", -shadow);
            material.SetFloat("_UnderlayDilate", outline * 2f);
            material.SetFloat("_UnderlaySoftness", 0f);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// A material preset of <paramref name="font"/> for small lettering: a thin ink outline and no drop shadow. At
        /// 15-20 pixels the big pop-ups' thick outline and shadow smear the numbers together.
        /// </summary>
        static Material MakeCrispPreset(TMP_FontAsset font, string path)
        {
            if (font == null)
                return null;
            var preset = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (preset == null)
            {
                preset = new Material(font.material);
                AssetDatabase.CreateAsset(preset, path);
            }
            else
            {
                preset.CopyPropertiesFromMaterial(font.material);
            }
            preset.SetColor("_OutlineColor", Ink);
            preset.SetFloat("_OutlineWidth", 0.16f);
            preset.SetFloat("_FaceDilate", 0.08f);
            preset.DisableKeyword("UNDERLAY_ON");
            preset.SetFloat("_UnderlayDilate", 0f);
            EditorUtility.SetDirty(preset);
            return preset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
