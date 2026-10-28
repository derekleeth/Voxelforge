using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Draws the crack stages a block shows while it's being mined (Roadmap 3.2, D-015) and sets up the crack material.
    /// Run <i>Voxelforge &gt; Rebuild Crack Textures</i>. Cracks grow out from a few points near the middle of the face,
    /// branching as they go; each stage draws more of the same cracks, a little bolder, so they visibly spread.
    /// The stages sit side by side in one strip (white = untouched), which <c>Voxelforge/Block Crack</c> multiplies
    /// over the block.
    /// </summary>
    public static class CrackTextureBuilder
    {
        public const int Size = 128;
        public const string TexturePath = "Assets/_Voxelforge/Art/Textures/Cracks/BlockCracks.png";
        public const string MaterialPath = "Assets/_Voxelforge/Materials/BlockCrack.mat";
        public const string ShaderName = "Voxelforge/Block Crack";

        const int Seed = 3_2_2026;
        const float SegmentLength = 5f;
        const float Ink = 0.12f; // How dark the middle of a crack is.

        struct Segment
        {
            public Vector2 A, B;
            public int Generation;
        }

        [MenuItem("Voxelforge/Rebuild Crack Textures")]
        public static void Rebuild()
        {
            int stages = BlockCrackRenderer.StageCount;
            List<Segment> segments = GrowCracks(new System.Random(Seed));
            var pixels = new Color32[Size * stages * Size];
            for (int stage = 0; stage < stages; stage++)
                DrawStage(segments, stage, stages, pixels);

            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            var texture = new Texture2D(Size * stages, Size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            ConfigureImport();
            SetUpMaterial();
            Debug.Log($"[Voxelforge] Drew {stages} crack stages ({segments.Count} crack pieces) into {TexturePath}.");
        }

        /// <summary>Random walks out from a few starting points near the middle, in the order they grow.</summary>
        static List<Segment> GrowCracks(System.Random random)
        {
            var segments = new List<Segment>();
            var tips = new Queue<(Vector2 position, float angle, int generation, int life)>();
            int roots = 4;
            float centre = Size * 0.5f;
            for (int i = 0; i < roots; i++)
            {
                var start = new Vector2(centre + Range(random, -18f, 18f), centre + Range(random, -18f, 18f));
                float angle = (i + Range(random, -0.3f, 0.3f)) / roots * Mathf.PI * 2f;
                tips.Enqueue((start, angle, 0, 13 + random.Next(6)));
                tips.Enqueue((start, angle + Mathf.PI + Range(random, -0.5f, 0.5f), 0, 9 + random.Next(5)));
            }

            // Breadth first, so a stage that shows the first N pieces shows every crack grown evenly.
            while (tips.Count > 0 && segments.Count < 600)
            {
                var (position, angle, generation, life) = tips.Dequeue();
                if (life <= 0)
                    continue;
                angle += Range(random, -0.35f, 0.35f);
                Vector2 next = position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SegmentLength * Range(random, 0.7f, 1.3f);
                segments.Add(new Segment { A = position, B = next, Generation = generation });
                if (next.x < -2f || next.y < -2f || next.x > Size + 2f || next.y > Size + 2f)
                    continue; // Reached the block's edge.

                tips.Enqueue((next, angle, generation + 1, life - 1));
                if (random.NextDouble() < 0.22)
                    tips.Enqueue((next, angle + Mathf.Sign(Range(random, -1f, 1f)) * Range(random, 0.6f, 1.2f), generation + 1, life / 2 + 1));
            }
            return segments;
        }

        static void DrawStage(List<Segment> segments, int stage, int stages, Color32[] pixels)
        {
            float progress = (stage + 1f) / stages;
            int shown = Mathf.CeilToInt(segments.Count * progress);
            float width = Mathf.Lerp(0.7f, 1.5f, progress); // Half-width of the darkest part, in pixels.

            var shade = new float[Size * Size];
            for (int i = 0; i < shade.Length; i++)
                shade[i] = 1f;
            for (int i = 0; i < shown; i++)
            {
                Segment segment = segments[i];
                // Older (earlier) cracks are wider than the fresh tips.
                float w = width * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(segment.Generation / 12f));
                DrawSegment(shade, segment.A, segment.B, w);
            }

            int stride = Size * stages;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade[x + y * Size]) * 255f);
                pixels[stage * Size + x + y * stride] = new Color32(v, v, v, 255);
            }
        }

        /// <summary>A soft-edged line: <see cref="Ink"/> within <paramref name="halfWidth"/>, fading out over one pixel.</summary>
        static void DrawSegment(float[] shade, Vector2 a, Vector2 b, float halfWidth)
        {
            float reach = halfWidth + 1.5f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach));
            Vector2 ab = b - a;
            float lengthSq = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                float distance = Vector2.Distance(p, a + ab * t);
                float coverage = 1f - Mathf.Clamp01(distance - halfWidth);
                float value = Mathf.Lerp(1f, Ink, coverage);
                int index = x + y * Size;
                if (value < shade[index])
                    shade[index] = value;
            }
        }

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        static void ConfigureImport()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false; // Mips would blend neighbouring stages in the strip together.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static void SetUpMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{ShaderName}' not found.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetTexture("_CrackStrip", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetFloat("_StageCount", BlockCrackRenderer.StageCount);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }
    }
}
