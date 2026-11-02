using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Stamps</i> (Roadmap 5.4): the ruin blocks and every stamp the world generator places, drawn
    /// in code so they rebuild the same every time. Re-running it updates the same assets in place, so keep the shapes
    /// here.
    /// <list type="bullet">
    /// <item><b>Ruin Brick</b>, <b>Mossy Ruin Brick</b> and unbreakable, blast-proof <b>Warded Stone</b>, with textures
    /// from <c>Tools/Blender/bake_block_textures.py</c> (bake those first; this rebuilds the texture arrays).</item>
    /// <item><b>Broken Watchtower</b> (Meadows): a mossy tower stump with its top fallen beside it, a torch and a
    /// chest.</item>
    /// <item><b>Frost Shrine</b> (Snowy Peaks): a ring of cracked, snow-capped pillars round a rune slab.</item>
    /// <item><b>Standing Stones</b> (Thunderhoof Plains): a henge of red rock and sandstone round a fire pit.</item>
    /// <item><b>Sunken Arena</b> (Meadows, Plains): the 5.9 boss's pit, walled and floored in Warded Stone, with two
    /// stairways down, a ring of pillars with torches, a broken rim and a two-step dais in the middle.</item>
    /// </list>
    /// It also puts the <see cref="StampLibrary"/> on the open scene's <see cref="VoxelWorldManager"/>.
    /// </summary>
    public static class StampBuilder
    {
        const string StampDir = "Assets/_Voxelforge/Data/Stamps";
        public const string LibraryPath = StampDir + "/StampLibrary.asset";
        const string BlockDir = "Assets/_Voxelforge/Data/Blocks";
        const string TextureDir = "Assets/_Voxelforge/Data/BlockTextures";

        const ushort Keep = StampBlocks.Keep;
        const ushort Air = BlockIds.Air;
        const ushort Brick = BlockIds.RuinBrick;
        const ushort Mossy = BlockIds.MossyRuinBrick;
        const ushort Warded = BlockIds.WardedStone;

        [MenuItem("Voxelforge/Build Stamps")]
        public static void BuildMenu()
        {
            Build();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voxelforge] Stamps built in {StampDir}.");
        }

        public static void Build()
        {
            BuildBlocks();
            if (!AssetDatabase.IsValidFolder(StampDir))
                AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Stamps");

            StampDefinition[] stamps =
            {
                Save("BrokenWatchtower", StampId.BrokenWatchtower, "Broken Watchtower", StampKind.Ruin, Watchtower(),
                    sink: 1, foundation: Brick, biomes: new[] { "Meadows" }, chance: 0.55f),
                Save("FrostShrine", StampId.FrostShrine, "Frost Shrine", StampKind.Ruin, FrostShrine(),
                    sink: 1, foundation: BlockIds.Stone, biomes: new[] { "Snowy" }, chance: 0.55f),
                Save("StandingStones", StampId.StandingStones, "Standing Stones", StampKind.Ruin, StandingStones(),
                    sink: 1, foundation: BlockIds.Dirt, biomes: new[] { "Thunderhoof" }, chance: 0.55f),
                Save("SunkenArena", StampId.SunkenArena, "Sunken Arena", StampKind.Arena, SunkenArena(),
                    sink: ArenaSink, foundation: 0, biomes: new[] { "Meadows", "Thunderhoof" }, chance: 0.8f),
            };
            var library = AssetDatabase.LoadAssetAtPath<StampLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<StampLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Assign(stamps);
            EditorUtility.SetDirty(library);

            // The world needs the library to stamp anything.
            foreach (VoxelWorldManager world in Object.FindObjectsByType<VoxelWorldManager>())
            {
                var serialized = new SerializedObject(world);
                SerializedProperty property = serialized.FindProperty("stampLibrary");
                if (property.objectReferenceValue == library)
                    continue;
                property.objectReferenceValue = library;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                Debug.Log($"[Voxelforge] Put the stamp library on {world.name}: save the scene to keep it.", world);
            }
        }

        // ---- Blocks ----

        static void BuildBlocks()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>($"{BlockDir}/BlockRegistry.asset");
            BlockDefinition brick = MakeCubeBlock("RuinBrick", BlockIds.RuinBrick, "Ruin Brick", durability: 50,
                blastProof: false, light: 0, new Color32(150, 140, 125, 255));
            BlockDefinition mossy = MakeCubeBlock("MossyRuinBrick", BlockIds.MossyRuinBrick, "Mossy Ruin Brick", durability: 50,
                blastProof: false, light: 0, new Color32(110, 130, 85, 255));
            // A faint glow from its runes, so the arena's pit is never pitch black.
            BlockDefinition warded = MakeCubeBlock("WardedStone", BlockIds.WardedStone, "Warded Stone", durability: 0,
                blastProof: true, light: 3, new Color32(60, 64, 82, 255));

            var serialized = new SerializedObject(registry);
            SerializedProperty list = serialized.FindProperty("blocks");
            foreach (BlockDefinition block in new[] { brick, mossy, warded })
            {
                bool listed = false;
                for (int i = 0; i < list.arraySize; i++)
                    listed |= list.GetArrayElementAtIndex(i).objectReferenceValue == block;
                if (listed)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = block;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            BlockTextureArrayBuilder.Rebuild();
        }

        /// <summary>A plain textured cube block, one texture on every face, mined with a pick. Ore uses it too (5.7).</summary>
        internal static BlockDefinition MakeCubeBlock(string name, ushort id, string displayName, int durability, bool blastProof,
            int light, Color32 mapColor)
        {
            string texturePath = $"{TextureDir}/{name}.asset";
            var texture = AssetDatabase.LoadAssetAtPath<BlockTexture>(texturePath);
            if (texture == null)
            {
                texture = ScriptableObject.CreateInstance<BlockTexture>();
                AssetDatabase.CreateAsset(texture, texturePath);
            }
            string path = $"{BlockDir}/{name}.asset";
            var block = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
            if (block == null)
            {
                block = ScriptableObject.CreateInstance<BlockDefinition>();
                AssetDatabase.CreateAsset(block, path);
            }
            var serialized = new SerializedObject(block);
            serialized.FindProperty("id").intValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("maxDurability").intValue = durability;
            serialized.FindProperty("drop").objectReferenceValue = null; // Drops itself.
            serialized.FindProperty("dropCount").intValue = durability > 0 ? 1 : 0;
            serialized.FindProperty("harvestKind").enumValueIndex = (int)HarvestKind.Pick;
            serialized.FindProperty("isSolid").boolValue = true;
            serialized.FindProperty("isTransparent").boolValue = false;
            serialized.FindProperty("isProp").boolValue = false;
            serialized.FindProperty("needsSupport").boolValue = false;
            serialized.FindProperty("blastProof").boolValue = blastProof;
            serialized.FindProperty("lightEmission").intValue = light;
            serialized.FindProperty("mapColor").colorValue = (Color)mapColor;
            serialized.FindProperty("topTexture").objectReferenceValue = texture;
            serialized.FindProperty("sideTexture").objectReferenceValue = null;
            serialized.FindProperty("bottomTexture").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(block);
            return block;
        }

        // ---- Drawing ----

        /// <summary>A box of stamp blocks to draw in, everything <see cref="StampBlocks.Keep"/> to start with.</summary>
        sealed class Canvas
        {
            public readonly int3 Size;
            public readonly ushort[] Blocks;

            public Canvas(int x, int y, int z)
            {
                Size = new int3(x, y, z);
                Blocks = new ushort[x * y * z];
                for (int i = 0; i < Blocks.Length; i++)
                    Blocks[i] = Keep;
            }

            public float2 Centre => new float2(Size.x - 1, Size.z - 1) * 0.5f;

            public bool Inside(int3 p) => math.all(p >= 0 & p < Size);

            public void Set(int x, int y, int z, ushort block)
            {
                var p = new int3(x, y, z);
                if (Inside(p))
                    Blocks[StampDefinition.IndexOf(p, Size)] = block;
            }

            public ushort Get(int x, int y, int z) => Blocks[StampDefinition.IndexOf(new int3(x, y, z), Size)];

            /// <summary>How far column (x, z) is from the middle, across the ground.</summary>
            public float Radius(int x, int z) => math.distance(new float2(x, z), Centre);

            /// <summary>Its angle round the middle, 0-360 degrees, 0 toward +x, 90 toward +z.</summary>
            public float Angle(int x, int z)
            {
                float2 d = new float2(x, z) - Centre;
                return (math.degrees(math.atan2(d.y, d.x)) + 360f) % 360f;
            }

            /// <summary>Every column within <paramref name="radius"/> of the middle, layers lo to hi, set to the block.</summary>
            public void Disc(float radius, int lo, int hi, ushort block)
            {
                for (int y = lo; y <= hi; y++)
                for (int z = 0; z < Size.z; z++)
                for (int x = 0; x < Size.x; x++)
                {
                    if (Radius(x, z) <= radius)
                        Set(x, y, z, block);
                }
            }
        }

        /// <summary>A number 0-1 for anything, the same every time the stamps are built.</summary>
        static float Noise(int a, int b, uint salt) => (math.hash(new uint3((uint)a, (uint)b, salt)) >> 8) * (1f / 16777216f);

        /// <summary>Old bricks: mostly plain, mossier near the ground.</summary>
        static ushort Weathered(int x, int y, int z, float mossLow = 0.55f, float mossHigh = 0.15f, int fade = 6)
        {
            float moss = math.lerp(mossLow, mossHigh, math.saturate(y / (float)fade));
            return Noise(x * 31 + y, z * 17 - y, 7) < moss ? Mossy : Brick;
        }

        // ---- The stamps ----

        static Canvas Watchtower()
        {
            // Wider to the east, where the tower's top fell.
            var c = new Canvas(21, 12, 15);
            // Clear a space so hills don't fill the tower, then lay a floor where the ground's top block was.
            c.Disc(6.6f, 1, 11, Air);
            c.Disc(6.6f, 0, 0, Brick);
            for (int z = 0; z < c.Size.z; z++)
            for (int x = 0; x < c.Size.x; x++)
            {
                float r = c.Radius(x, z);
                if (r > 6.6f)
                    continue;
                if (Noise(x, z, 3) < 0.35f)
                    c.Set(x, 0, z, Mossy);
                // The tower wall: a ring broken off at a different height all round, with a doorway on the south side.
                if (r < 3.4f || r > 4.9f)
                    continue;
                float angle = c.Angle(x, z);
                int height = 3 + (int)(Noise((int)(angle / 25f), 0, 11) * 7f);
                bool door = angle > 255f && angle < 285f;
                for (int y = 1; y <= height && y < c.Size.y; y++)
                {
                    if (door && y <= 3)
                        continue;
                    c.Set(x, y, z, Weathered(x, y, z));
                }
            }
            // The fallen top: a line of tumbled bricks running away east from the wall, lying on the grass.
            for (int i = 0; i < 6; i++)
            {
                int x = 15 + i, z = 7 + (i % 3 == 0 ? 1 : 0);
                c.Set(x, 1, z, Weathered(x, 1, z, 0.6f, 0.6f));
                if (i % 2 == 0)
                    c.Set(x, 1, z - 1, Weathered(x, 1, z - 1, 0.6f, 0.6f));
                if (i < 3)
                    c.Set(x, 2, z, Brick);
            }
            // Inside: a torch on the floor and a chest (5.5 will fill it).
            c.Set(8, 1, 7, BlockIds.Torch);
            c.Set(12, 1, 8, BlockIds.Chest);
            return c;
        }

        static Canvas FrostShrine()
        {
            var c = new Canvas(13, 9, 13);
            c.Disc(5.9f, 1, 8, Air);
            c.Disc(5.9f, 0, 0, Brick);
            for (int z = 0; z < c.Size.z; z++)
            for (int x = 0; x < c.Size.x; x++)
            {
                if (c.Radius(x, z) <= 5.9f && Noise(x, z, 5) < 0.3f)
                    c.Set(x, 0, z, BlockIds.Snow);
            }
            // The rune slab in the middle, glowing faintly.
            for (int z = 5; z <= 7; z++)
            for (int x = 5; x <= 7; x++)
                c.Set(x, 1, z, Warded);
            // Six cracked pillars, every 60 degrees, the tall ones capped with snow.
            for (int i = 0; i < 6; i++)
            {
                float a = math.radians(i * 60f + 15f);
                int x = (int)math.round(c.Centre.x + math.cos(a) * 4.6f);
                int z = (int)math.round(c.Centre.y + math.sin(a) * 4.6f);
                int height = 2 + (int)(Noise(i, 1, 13) * 5f);
                for (int y = 1; y <= height; y++)
                    c.Set(x, y, z, Noise(i, y, 17) < 0.25f ? Mossy : Brick);
                if (height >= 4)
                    c.Set(x, height + 1, z, BlockIds.Snow);
            }
            return c;
        }

        static Canvas StandingStones()
        {
            var c = new Canvas(17, 8, 17);
            c.Disc(7.9f, 1, 7, Air);
            // A fire pit in the middle: a ring of stone round a log with a torch burning on it.
            for (int z = 7; z <= 9; z++)
            for (int x = 7; x <= 9; x++)
                c.Set(x, 0, z, BlockIds.Stone);
            c.Set(8, 0, 8, BlockIds.Log);
            c.Set(8, 1, 8, BlockIds.Torch);
            // Seven standing stones, two blocks deep, red rock banded with sandstone; two of them carry a lintel.
            for (int i = 0; i < 7; i++)
            {
                float a = math.radians(i * (360f / 7f));
                int x = (int)math.round(c.Centre.x + math.cos(a) * 6.3f);
                int z = (int)math.round(c.Centre.y + math.sin(a) * 6.3f);
                int height = 3 + (int)(Noise(i, 2, 19) * 3f);
                for (int y = 0; y <= height; y++)
                {
                    ushort rock = y % 3 == 2 ? BlockIds.Sandstone : BlockIds.RedRock;
                    c.Set(x, y, z, rock);
                    // Two blocks deep, toward the middle.
                    int dx = (int)math.sign(c.Centre.x - x), dz = (int)math.sign(c.Centre.y - z);
                    c.Set(x + (math.abs(x - c.Centre.x) > math.abs(z - c.Centre.y) ? dx : 0), y,
                        z + (math.abs(x - c.Centre.x) > math.abs(z - c.Centre.y) ? 0 : dz), rock);
                }
            }
            // A sandstone lintel across the top of the east pair.
            for (int z = 8; z <= 13; z++)
                c.Set(14, 5, z, BlockIds.Sandstone);
            for (int y = 1; y <= 4; y++)
            {
                c.Set(14, y, 8, BlockIds.RedRock);
                c.Set(14, y, 13, BlockIds.RedRock);
            }
            return c;
        }

        /// <summary>How deep the arena's floor sits below the ground: its walls rise this many blocks to the rim.</summary>
        const int ArenaSink = 10;

        static Canvas SunkenArena()
        {
            const int size = 35;
            const float wallIn = 15f, wallOut = 16.6f;
            int ground = ArenaSink - 1; // The layer the ground's top block was in.
            var c = new Canvas(size, 17, size);

            // The pit: open to the sky, so clear it all the way up; a Warded Stone floor and walls up to the ground.
            c.Disc(wallIn, 1, c.Size.y - 1, Air);
            c.Disc(wallOut, 0, 0, Warded);
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float r = c.Radius(x, z);
                if (r <= wallIn || r > wallOut)
                    continue;
                for (int y = 1; y <= ground; y++)
                    c.Set(x, y, z, Warded);
                // The rim above the ground: broken old brickwork, higher in some places than others.
                int rim = (int)(Noise((int)(c.Angle(x, z) / 18f), 0, 23) * 4.5f);
                for (int y = ground + 1; y <= ground + rim; y++)
                    c.Set(x, y, z, Weathered(x, y, z, 0.4f, 0.2f, 4));
            }

            // Two stairways down, on the north and south sides, cut through the wall to the floor.
            int mid = size / 2;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k <= ground; k++)
                {
                    int z = side > 0 ? size - 1 - k : k;
                    int top = ground - k;
                    for (int x = mid - 1; x <= mid + 1; x++)
                    {
                        for (int y = 0; y <= top; y++)
                            c.Set(x, y, z, Warded);
                        for (int y = top + 1; y < c.Size.y; y++)
                            c.Set(x, y, z, Air);
                    }
                }
            }

            // Eight pillars round the floor, some broken short, with torches burning on the tall ones.
            for (int i = 0; i < 8; i++)
            {
                float a = math.radians(i * 45f + 22.5f);
                int px = (int)math.round(c.Centre.x + math.cos(a) * 11.5f);
                int pz = (int)math.round(c.Centre.y + math.sin(a) * 11.5f);
                int height = 5 + (int)(Noise(i, 3, 29) * 4f);
                for (int y = 1; y <= height; y++)
                for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                    c.Set(px + dx, y, pz + dz, y == height && Noise(i, dx + dz * 2, 31) < 0.5f ? Brick : Warded);
                if (height >= 7)
                    c.Set(px, height + 1, pz, BlockIds.Torch);
            }

            // The boss's dais in the middle: two steps up.
            for (int z = mid - 3; z <= mid + 3; z++)
            for (int x = mid - 3; x <= mid + 3; x++)
            {
                c.Set(x, 1, z, Warded);
                if (math.abs(x - mid) <= 2 && math.abs(z - mid) <= 2)
                    c.Set(x, 2, z, Warded);
            }
            return c;
        }

        static StampDefinition Save(string file, StampId id, string displayName, StampKind kind, Canvas canvas, int sink,
            ushort foundation, string[] biomes, float chance)
        {
            string path = $"{StampDir}/{file}.asset";
            var stamp = AssetDatabase.LoadAssetAtPath<StampDefinition>(path);
            if (stamp == null)
            {
                stamp = ScriptableObject.CreateInstance<StampDefinition>();
                AssetDatabase.CreateAsset(stamp, path);
            }
            stamp.Assign(id, displayName, kind, canvas.Size, canvas.Blocks, sink, foundation, biomes, chance, stampRotate: true);
            var errors = new List<string>();
            stamp.Validate(errors);
            foreach (string error in errors)
                Debug.LogError($"[Voxelforge] {error}", stamp);
            EditorUtility.SetDirty(stamp);
            return stamp;
        }
    }
}
