using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Foliage Decorations</i> (Roadmap 2.6 part B): makes the foliage materials, one
    /// decoration asset per plant and the <see cref="DecorationSet"/> from the Blender art that
    /// <c>Tools/Blender/build_foliage.py</c> exports. Re-running it updates the same assets in place.
    /// <para>
    /// Mesh contract (see build_foliage.py): origin at the bottom centre, UV0 = the shared atlas, vertex colour
    /// RGB = painted shading, vertex colour A = sway (0 at the root, 1 at the tips). Trees are modelled for a
    /// 5-block trunk. Each FBX holds "&lt;Name&gt;_LOD0" and "&lt;Name&gt;_LOD1".
    /// </para>
    /// </summary>
    public static class FoliageDecorationBuilder
    {
        const string ModelDir = "Assets/_Voxelforge/Art/Models/Foliage";
        const string AtlasPath = "Assets/_Voxelforge/Art/Textures/Foliage/Foliage_Atlas.png";
        const string MaterialDir = "Assets/_Voxelforge/Art/Materials/Foliage";
        const string DataDir = "Assets/_Voxelforge/Data/Decorations";
        const string RegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string TerrainPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";

        const string Meadows = "Meadows (Elwynn style)";
        const string Desert = "Desert (Tanaris style)";
        const string Crags = "Red Crags (Redridge style)";
        const string Snowy = "Snowy Peaks (Dun Morogh style)";

        /// <summary>Look settings for one foliage material.</summary>
        struct MaterialLook
        {
            public float NormalUp, BackFaceFlip, WindScale, Smoothness;
        }

        [MenuItem("Voxelforge/Build Foliage Decorations")]
        public static void Build()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null)
            {
                Debug.LogError($"[Voxelforge] Missing {AtlasPath}. Run build_all() in Tools/Blender/build_foliage.py first.");
                return;
            }
            EnsureFolder(MaterialDir);
            EnsureFolder(DataDir);

            // Cards light mostly from above; canopies keep their round crown normals on both sides.
            var cards = new MaterialLook { NormalUp = 0.8f, BackFaceFlip = 1f, WindScale = 1f, Smoothness = 0.05f };
            var flowers = new MaterialLook { NormalUp = 0.6f, BackFaceFlip = 1f, WindScale = 1f, Smoothness = 0.1f };
            var crowns = new MaterialLook { NormalUp = 0.15f, BackFaceFlip = 0f, WindScale = 0.8f, Smoothness = 0.05f };
            Material grassMat = MakeMaterial("Foliage_Grass", atlas, cards);
            Material flowerMat = MakeMaterial("Foliage_Flowers", atlas, flowers);
            Material shrubMat = MakeMaterial("Foliage_Shrub", atlas, new MaterialLook { NormalUp = 0.5f, BackFaceFlip = 1f, WindScale = 0.7f, Smoothness = 0.05f });
            Material bushMat = MakeMaterial("Foliage_Bush", atlas, new MaterialLook { NormalUp = 0.15f, BackFaceFlip = 0f, WindScale = 1f, Smoothness = 0.05f });
            Material treeMat = MakeMaterial("Foliage_Tree", atlas, crowns);

            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryPath);
            registry.TryGet(BlockIds.Grass, out BlockDefinition grassBlock);
            registry.TryGet(BlockIds.Sand, out BlockDefinition sandBlock);
            var onGrass = new[] { grassBlock };
            var onSand = new[] { sandBlock };
            var none = new BlockDefinition[0];

            // Ground densities are chances per open block top and add up (at most 1 per biome).
            var decorations = new List<DecorationDefinition>
            {
                MakeDecoration("GrassTuft", "GrassShort", DecorationKind.Ground, grassMat, onGrass, 0.4f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.6f) }, 1.1f, 1.6f, 30f, 70f, false, BiomeTint.Grass),
                MakeDecoration("GrassTall", "GrassTall", DecorationKind.Ground, grassMat, onGrass, 0.07f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.8f) }, 1.0f, 1.4f, 30f, 70f, false, BiomeTint.Grass),
                MakeDecoration("FlowerYellow", "FlowerYellow", DecorationKind.Ground, flowerMat, onGrass, 0.02f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.4f) }, 1.1f, 1.5f, 30f, 60f, false, BiomeTint.Grass),
                MakeDecoration("FlowerBlue", "FlowerBlue", DecorationKind.Ground, flowerMat, onGrass, 0.015f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.3f) }, 1.1f, 1.5f, 30f, 60f, false, BiomeTint.Grass),
                MakeDecoration("FlowerRed", "FlowerRed", DecorationKind.Ground, flowerMat, onGrass, 0.01f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.6f) }, 1.1f, 1.5f, 30f, 60f, false, BiomeTint.Grass),
                MakeDecoration("Bush", "Bush", DecorationKind.Ground, bushMat, onGrass, 0.02f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 1f) }, 0.8f, 1.4f, 60f, 120f, true, BiomeTint.Foliage),
                MakeDecoration("DryShrub", "DryShrub", DecorationKind.Ground, shrubMat, onSand, 0.012f,
                    new[] { Weight(Desert, 1f) }, 0.7f, 1.2f, 60f, 120f, true, BiomeTint.Foliage),
                MakeDecoration("OakTree", "OakTree", DecorationKind.Tree, treeMat, none, 0f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.6f) }, 1f, 1f, 90f, 240f, true, BiomeTint.Foliage),
                MakeDecoration("PineTree", "PineTree", DecorationKind.Tree, treeMat, none, 0f,
                    new[] { Weight(Snowy, 1f), Weight(Crags, 0.5f) }, 1f, 1f, 90f, 240f, true, BiomeTint.Foliage),
                MakeDecoration("ScrubTree", "ScrubTree", DecorationKind.Tree, treeMat, none, 0f,
                    new[] { Weight(Desert, 1f), Weight(Crags, 0.6f) }, 1f, 1f, 90f, 240f, true, BiomeTint.Foliage),
            };
            if (decorations.Contains(null))
                return;

            string setPath = $"{DataDir}/DecorationSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<DecorationSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }
            // Keep the torch and workbench models (Build Props & Items, Roadmap 3.5) at the end.
            foreach (DecorationDefinition existing in set.Decorations)
            {
                if (existing != null && existing.Kind == DecorationKind.Prop && !decorations.Contains(existing))
                    decorations.Add(existing);
            }
            var serializedSet = new SerializedObject(set);
            SerializedProperty list = serializedSet.FindProperty("decorations");
            list.arraySize = decorations.Count;
            for (int i = 0; i < decorations.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = decorations[i];
            serializedSet.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            List<string> errors = set.Validate(AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainPath), registry);
            if (errors.Count > 0)
                Debug.LogError("[Voxelforge] Foliage decoration set is invalid:\n" + string.Join("\n", errors), set);
            else
                Debug.Log($"[Voxelforge] Built {decorations.Count} foliage decorations.", set);
        }

        static BiomeWeight Weight(string biome, float weight) => new BiomeWeight { biome = biome, weight = weight };

        static Material MakeMaterial(string name, Texture2D atlas, MaterialLook look)
        {
            string path = $"{MaterialDir}/{name}.mat";
            Shader shader = Shader.Find("Voxelforge/Foliage");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Smoothness", look.Smoothness);
            material.SetFloat("_NormalUp", look.NormalUp);
            material.SetFloat("_BackFaceFlip", look.BackFaceFlip);
            material.SetFloat("_WindScale", look.WindScale);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The "&lt;model&gt;_LOD0" and "&lt;model&gt;_LOD1" meshes inside Art/Models/Foliage/&lt;model&gt;.fbx.</summary>
        static bool TryLoadMeshes(string model, out Mesh mesh, out Mesh lodMesh)
        {
            mesh = lodMesh = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{ModelDir}/{model}.fbx"))
            {
                if (asset is Mesh m && m.name == model + "_LOD0")
                    mesh = m;
                else if (asset is Mesh lod && lod.name == model + "_LOD1")
                    lodMesh = lod;
            }
            if (mesh == null || lodMesh == null)
                Debug.LogError($"[Voxelforge] {ModelDir}/{model}.fbx needs meshes '{model}_LOD0' and '{model}_LOD1'.");
            return mesh != null && lodMesh != null;
        }

        static DecorationDefinition MakeDecoration(string name, string model, DecorationKind kind, Material material,
            BlockDefinition[] surfaces, float density, BiomeWeight[] biomes, float minScale, float maxScale,
            float lodDistance, float maxDistance, bool castShadows, BiomeTint tint)
        {
            if (!TryLoadMeshes(model, out Mesh mesh, out Mesh lodMesh))
                return null;

            string path = $"{DataDir}/{name}.asset";
            var decoration = AssetDatabase.LoadAssetAtPath<DecorationDefinition>(path);
            if (decoration == null)
            {
                decoration = ScriptableObject.CreateInstance<DecorationDefinition>();
                AssetDatabase.CreateAsset(decoration, path);
            }

            var serialized = new SerializedObject(decoration);
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("mesh").objectReferenceValue = mesh;
            serialized.FindProperty("lodMesh").objectReferenceValue = lodMesh;
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.FindProperty("tint").enumValueIndex = (int)tint;
            serialized.FindProperty("castShadows").boolValue = castShadows;
            serialized.FindProperty("lodDistance").floatValue = lodDistance;
            serialized.FindProperty("maxDistance").floatValue = maxDistance;
            SerializedProperty surfaceList = serialized.FindProperty("surfaces");
            surfaceList.arraySize = surfaces.Length;
            for (int i = 0; i < surfaces.Length; i++)
                surfaceList.GetArrayElementAtIndex(i).objectReferenceValue = surfaces[i];
            serialized.FindProperty("density").floatValue = density;
            SerializedProperty biomeList = serialized.FindProperty("biomes");
            biomeList.arraySize = biomes.Length;
            for (int i = 0; i < biomes.Length; i++)
            {
                biomeList.GetArrayElementAtIndex(i).FindPropertyRelative("biome").stringValue = biomes[i].biome;
                biomeList.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = biomes[i].weight;
            }
            serialized.FindProperty("minScale").floatValue = minScale;
            serialized.FindProperty("maxScale").floatValue = maxScale;
            serialized.FindProperty("modelTrunkHeight").floatValue = 5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return decoration;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
