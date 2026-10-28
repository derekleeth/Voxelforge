using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Placeholder Decorations</i> (Roadmap 2.6 part A): simple box and card meshes, materials,
    /// decoration assets and the <see cref="DecorationSet"/>, so the decoration layer can be seen and tested before
    /// the real Blender art arrives in part B. Re-running it updates the same assets in place.
    /// <para>
    /// Mesh contract (same as the real art): origin at the bottom centre, vertex colour RGB = shading,
    /// vertex colour A = sway (0 at the root, 1 at the tips). Trees are modelled for a 5-block trunk.
    /// </para>
    /// </summary>
    public static class PlaceholderDecorationBuilder
    {
        const string MeshDir = "Assets/_Voxelforge/Art/Models/Foliage/Placeholder";
        const string MaterialDir = "Assets/_Voxelforge/Art/Materials/Foliage";
        const string DataDir = "Assets/_Voxelforge/Data/Decorations";
        const string RegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string TerrainPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";

        const string Meadows = "Meadows (Elwynn style)";
        const string Desert = "Desert (Tanaris style)";
        const string Crags = "Red Crags (Redridge style)";
        const string Snowy = "Snowy Peaks (Dun Morogh style)";

        /// <summary>Collects one mesh's vertices before it's saved.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _positions = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<Color> _colors = new List<Color>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<int> _triangles = new List<int>();

            /// <summary>Corners a, b along the bottom edge, then c, d along the top edge.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float swayBottom, float swayTop, float shade)
            {
                int i = _positions.Count;
                _positions.AddRange(new[] { a, b, c, d });
                for (int k = 0; k < 4; k++)
                    _normals.Add(normal);
                _colors.Add(new Color(shade, shade, shade, swayBottom));
                _colors.Add(new Color(shade, shade, shade, swayBottom));
                _colors.Add(new Color(1f, 1f, 1f, swayTop));
                _colors.Add(new Color(1f, 1f, 1f, swayTop));
                _uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                _triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            public void Box(Vector3 min, Vector3 max, float swayBottom, float swayTop, float shade)
            {
                float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
                Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, swayBottom, swayTop, shade);
                Quad(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), Vector3.forward, swayBottom, swayTop, shade);
                Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), Vector3.left, swayBottom, swayTop, shade);
                Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, swayBottom, swayTop, shade);
                Quad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, swayTop, swayTop, 1f);
                Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), Vector3.down, swayBottom, swayBottom, shade);
            }

            /// <summary>Crossed upright cards, like a tuft of grass.</summary>
            public void Cards(float width, float height, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    float angle = k * Mathf.PI / count;
                    Vector3 half = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);
                    Vector3 normal = new Vector3(-half.z, 0f, half.x).normalized;
                    Quad(-half, half, half + Vector3.up * height, -half + Vector3.up * height, normal, 0f, 1f, 0.55f);
                }
            }

            public Mesh Save(string name)
            {
                string path = $"{MeshDir}/{name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool isNew = mesh == null;
                if (isNew)
                    mesh = new Mesh { name = name };
                mesh.Clear();
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                if (isNew)
                    AssetDatabase.CreateAsset(mesh, path);
                else
                    EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }

        [MenuItem("Voxelforge/Build Placeholder Decorations")]
        public static void Build()
        {
            foreach (string dir in new[] { "Assets/_Voxelforge/Art/Models/Foliage", MeshDir, MaterialDir, DataDir })
                EnsureFolder(dir);

            var grass = new MeshBuilder();
            grass.Cards(0.8f, 0.55f, 3);
            var flower = new MeshBuilder();
            flower.Cards(0.45f, 0.45f, 2);
            var bush = new MeshBuilder();
            bush.Box(new Vector3(-0.45f, 0f, -0.45f), new Vector3(0.45f, 0.75f, 0.45f), 0f, 0.35f, 0.6f);
            var oak = new MeshBuilder();
            oak.Box(new Vector3(-0.2f, 0f, -0.2f), new Vector3(0.2f, 4.6f, 0.2f), 0f, 0f, 0.7f);
            oak.Box(new Vector3(-1.6f, 3.4f, -1.6f), new Vector3(1.6f, 6f, 1.6f), 0.15f, 0.45f, 0.6f);
            oak.Box(new Vector3(-0.9f, 6f, -0.9f), new Vector3(0.9f, 7f, 0.9f), 0.45f, 0.6f, 0.75f);
            var pine = new MeshBuilder();
            pine.Box(new Vector3(-0.18f, 0f, -0.18f), new Vector3(0.18f, 5f, 0.18f), 0f, 0f, 0.7f);
            pine.Box(new Vector3(-1.5f, 2f, -1.5f), new Vector3(1.5f, 3.4f, 1.5f), 0.1f, 0.25f, 0.6f);
            pine.Box(new Vector3(-1.1f, 3.4f, -1.1f), new Vector3(1.1f, 4.8f, 1.1f), 0.25f, 0.4f, 0.65f);
            pine.Box(new Vector3(-0.7f, 4.8f, -0.7f), new Vector3(0.7f, 6.2f, 0.7f), 0.4f, 0.55f, 0.7f);
            pine.Box(new Vector3(-0.3f, 6.2f, -0.3f), new Vector3(0.3f, 7.2f, 0.3f), 0.55f, 0.65f, 0.75f);

            Mesh grassMesh = grass.Save("PH_GrassTuft");
            Mesh flowerMesh = flower.Save("PH_Flower");
            Mesh bushMesh = bush.Save("PH_Bush");
            Mesh oakMesh = oak.Save("PH_Oak");
            Mesh pineMesh = pine.Save("PH_Pine");

            Material grassMat = MakeMaterial("PH_Grass", new Color(0.42f, 0.66f, 0.25f), 0.8f, 1f);
            Material flowerMat = MakeMaterial("PH_Flower", new Color(0.95f, 0.8f, 0.25f), 0.6f, 1f);
            Material bushMat = MakeMaterial("PH_Bush", new Color(0.3f, 0.52f, 0.2f), 0.4f, 0.6f);
            Material shrubMat = MakeMaterial("PH_DryShrub", new Color(0.62f, 0.55f, 0.3f), 0.4f, 0.6f);
            Material oakMat = MakeMaterial("PH_Oak", new Color(0.33f, 0.55f, 0.22f), 0.3f, 0.35f);
            Material pineMat = MakeMaterial("PH_Pine", new Color(0.18f, 0.38f, 0.25f), 0.3f, 0.3f);

            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryPath);
            registry.TryGet(BlockIds.Grass, out BlockDefinition grassBlock);
            registry.TryGet(BlockIds.Sand, out BlockDefinition sandBlock);
            var onGrass = new[] { grassBlock };
            var none = new BlockDefinition[0];

            var decorations = new List<DecorationDefinition>
            {
                MakeDecoration("GrassTuft", DecorationKind.Ground, grassMesh, grassMat, onGrass, 0.35f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.6f) }, 0.8f, 1.3f, 60f, 70f, false, DecorationTint.Grass),
                MakeDecoration("FlowerYellow", DecorationKind.Ground, flowerMesh, flowerMat, onGrass, 0.04f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 0.4f) }, 0.8f, 1.2f, 50f, 60f, false, DecorationTint.None),
                MakeDecoration("Bush", DecorationKind.Ground, bushMesh, bushMat, onGrass, 0.02f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 1f) }, 0.8f, 1.4f, 80f, 120f, true, DecorationTint.Foliage),
                MakeDecoration("DryShrub", DecorationKind.Ground, bushMesh, shrubMat, new[] { sandBlock }, 0.012f,
                    new[] { Weight(Desert, 1f) }, 0.6f, 1f, 80f, 120f, true, DecorationTint.None),
                MakeDecoration("OakTree", DecorationKind.Tree, oakMesh, oakMat, none, 0f,
                    new[] { Weight(Meadows, 1f), Weight(Crags, 1f), Weight(Desert, 1f) }, 1f, 1f, 120f, 240f, true, DecorationTint.Foliage),
                MakeDecoration("PineTree", DecorationKind.Tree, pineMesh, pineMat, none, 0f,
                    new[] { Weight(Snowy, 1f), Weight(Crags, 0.5f) }, 1f, 1f, 120f, 240f, true, DecorationTint.Foliage),
            };

            string setPath = $"{DataDir}/DecorationSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<DecorationSet>();
                AssetDatabase.CreateAsset(set, setPath);
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
                Debug.LogError("[Voxelforge] Placeholder decoration set is invalid:\n" + string.Join("\n", errors), set);
            else
                Debug.Log($"[Voxelforge] Built {decorations.Count} placeholder decorations.", set);
        }

        static BiomeWeight Weight(string biome, float weight) => new BiomeWeight { biome = biome, weight = weight };

        static Material MakeMaterial(string name, Color color, float normalUp, float windScale)
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
            material.SetColor("_BaseColor", color);
            material.SetFloat("_NormalUp", normalUp);
            material.SetFloat("_WindScale", windScale);
            EditorUtility.SetDirty(material);
            return material;
        }

        static DecorationDefinition MakeDecoration(string name, DecorationKind kind, Mesh mesh, Material material,
            BlockDefinition[] surfaces, float density, BiomeWeight[] biomes, float minScale, float maxScale,
            float lodDistance, float maxDistance, bool castShadows, DecorationTint tint)
        {
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
            serialized.FindProperty("lodMesh").objectReferenceValue = null;
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
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
