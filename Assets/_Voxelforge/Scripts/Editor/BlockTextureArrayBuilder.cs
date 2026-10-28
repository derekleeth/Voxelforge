using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Packs the baked block PNGs into the three texture arrays the block shader reads, and plugs them into the
    /// block material. Run <i>Voxelforge &gt; Rebuild Block Textures</i> after re-baking in Blender or after giving a
    /// block a new texture.
    /// <list type="bullet">
    /// <item><b>AlbedoHeight</b>: painted color (RGB, sRGB) + height (A). BC7.</item>
    /// <item><b>Normal</b>: tangent-space normal X/Y (RG). BC5; the shader rebuilds Z.</item>
    /// <item><b>Mask</b>: ambient occlusion (R), smoothness (G), emission (B). BC7.</item>
    /// </list>
    /// Layer order comes from <see cref="BlockRegistry.GetTextureLayers"/>, the same order the mesher uses.
    /// </summary>
    public static class BlockTextureArrayBuilder
    {
        /// <summary>Pixels per block face in game. Sources are baked larger and shrunk with a box filter.</summary>
        public const int Size = 128;

        public const string RegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        public const string SourceFolder = "Assets/_Voxelforge/Art/Textures/Blocks";
        public const string OutputFolder = "Assets/_Voxelforge/Art/Textures/Blocks/Arrays";
        public const string MaterialPath = "Assets/_Voxelforge/Materials/VoxelBlocks.mat";
        public const string ShaderName = "Voxelforge/Voxel Block";

        public const string AlbedoHeightProperty = "_AlbedoHeightArray";
        public const string NormalProperty = "_NormalArray";
        public const string MaskProperty = "_MaskArray";

        static readonly int MipCount = (int)Math.Log(Size, 2) + 1;

        [MenuItem("Voxelforge/Rebuild Block Textures")]
        public static void Rebuild()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryPath);
            if (registry == null)
                throw new FileNotFoundException($"Missing {RegistryPath}");

            List<BlockTexture> layers = registry.GetTextureLayers();
            if (layers.Count == 0)
                throw new InvalidOperationException("No block uses a Block Texture, so there's nothing to build.");

            var albedoHeight = new Texture2DArray(Size, Size, layers.Count, TextureFormat.BC7, MipCount, false);
            var normal = new Texture2DArray(Size, Size, layers.Count, TextureFormat.BC5, MipCount, true);
            var mask = new Texture2DArray(Size, Size, layers.Count, TextureFormat.BC7, MipCount, true);

            for (int layer = 0; layer < layers.Count; layer++)
            {
                string name = layers[layer].name;
                EditorUtility.DisplayProgressBar("Rebuild Block Textures", name, (float)layer / layers.Count);

                Color[] albedo = Load(name, "Albedo");
                Color[] height = Load(name, "Height");
                for (int i = 0; i < albedo.Length; i++)
                    albedo[i].a = height[i].r;

                CopyLayer(albedoHeight, layer, BuildMips(albedo, MipKind.Albedo), TextureFormat.BC7, false);
                CopyLayer(normal, layer, BuildMips(Load(name, "Normal"), MipKind.Normal), TextureFormat.BC5, true);
                CopyLayer(mask, layer, BuildMips(Load(name, "Mask"), MipKind.Plain), TextureFormat.BC7, true);
            }
            EditorUtility.ClearProgressBar();

            Directory.CreateDirectory(OutputFolder);
            albedoHeight = SaveArray(albedoHeight, "BlockAlbedoHeight");
            normal = SaveArray(normal, "BlockNormal");
            mask = SaveArray(mask, "BlockMask");

            Material material = LoadOrCreateMaterial();
            material.SetTexture(AlbedoHeightProperty, albedoHeight);
            material.SetTexture(NormalProperty, normal);
            material.SetTexture(MaskProperty, mask);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Voxelforge] Built block texture arrays: {layers.Count} layers ({string.Join(", ", layers.ConvertAll(l => l.name))}) " +
                      $"at {Size}x{Size}.", material);
        }

        enum MipKind
        {
            /// <summary>sRGB color in RGB, averaged in linear space so mips don't darken; linear height in A.</summary>
            Albedo,
            /// <summary>Unit vectors in RGB: averaged, then re-normalized.</summary>
            Normal,
            Plain,
        }

        /// <summary>Reads <c>&lt;layer&gt;_&lt;channel&gt;.png</c> as raw (un-converted) 0-1 values, shrunk to <see cref="Size"/>.</summary>
        static Color[] Load(string layer, string channel)
        {
            string path = $"{SourceFolder}/{layer}_{channel}.png";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing {path}. Bake it with Tools/Blender/bake_block_textures.py.", path);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                texture.LoadImage(File.ReadAllBytes(path));
                int size = texture.width;
                if (size != texture.height || size < Size || (size & (size - 1)) != 0)
                    throw new InvalidDataException($"{path} must be square, a power of two, and at least {Size} px.");

                Color[] pixels = texture.GetPixels();
                MipKind kind = channel == "Albedo" ? MipKind.Albedo : channel == "Normal" ? MipKind.Normal : MipKind.Plain;
                while (size > Size)
                {
                    pixels = Downsample(pixels, size, kind);
                    size /= 2;
                }
                return pixels;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static List<Color[]> BuildMips(Color[] top, MipKind kind)
        {
            var mips = new List<Color[]> { top };
            for (int size = Size; size > 1; size /= 2)
                mips.Add(Downsample(mips[mips.Count - 1], size, kind));
            return mips;
        }

        /// <summary>Halves a square image with a 2x2 box filter.</summary>
        static Color[] Downsample(Color[] source, int size, MipKind kind)
        {
            int half = size / 2;
            var result = new Color[half * half];
            for (int y = 0; y < half; y++)
            for (int x = 0; x < half; x++)
            {
                Color sum = default;
                for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    Color c = source[(y * 2 + dy) * size + x * 2 + dx];
                    if (kind == MipKind.Albedo)
                        c = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
                    sum += c;
                }
                Color average = sum * 0.25f;

                if (kind == MipKind.Albedo)
                {
                    average = new Color(Mathf.LinearToGammaSpace(average.r), Mathf.LinearToGammaSpace(average.g),
                        Mathf.LinearToGammaSpace(average.b), average.a);
                }
                else if (kind == MipKind.Normal)
                {
                    Vector3 n = new Vector3(average.r * 2f - 1f, average.g * 2f - 1f, average.b * 2f - 1f).normalized;
                    average = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
                result[y * half + x] = average;
            }
            return result;
        }

        /// <summary>Compresses one layer's mip chain and copies the compressed bytes into the array.</summary>
        static void CopyLayer(Texture2DArray array, int layer, List<Color[]> mips, TextureFormat format, bool linear)
        {
            var staging = new Texture2D(Size, Size, TextureFormat.RGBA32, mips.Count, linear);
            try
            {
                for (int mip = 0; mip < mips.Count; mip++)
                    staging.SetPixels(mips[mip], mip);
                staging.Apply(false);
                EditorUtility.CompressTexture(staging, format, TextureCompressionQuality.Best);
                for (int mip = 0; mip < mips.Count; mip++)
                    array.SetPixelData(staging.GetPixelData<byte>(mip), mip, layer);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        static Texture2DArray SaveArray(Texture2DArray array, string name)
        {
            array.name = name;
            array.wrapMode = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Trilinear;
            array.anisoLevel = 8;
            array.Apply(false, false);

            string path = $"{OutputFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(array, path);
                return array;
            }

            // Overwrite in place so the asset keeps its GUID and anything pointing at it stays connected.
            EditorUtility.CopySerialized(array, existing);
            UnityEngine.Object.DestroyImmediate(array);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{ShaderName}' not found.");
            material = new Material(shader) { name = "VoxelBlocks" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
