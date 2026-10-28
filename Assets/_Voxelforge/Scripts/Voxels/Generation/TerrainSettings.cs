using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The terrain recipe: the biomes, caves and ore, all tweakable in the Inspector.
    /// <see cref="ToTerrainParams"/> and <see cref="CreateBiomeLookup"/> turn it into job-friendly data.
    /// <para>
    /// <b>Changing a value changes every existing world</b> made from it. Once saves exist (Roadmap 3.4),
    /// bump <see cref="TerrainSampler.GeneratorVersion"/> when you do.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Terrain Settings", fileName = "TerrainSettings")]
    public sealed class TerrainSettings : ScriptableObject
    {
        [Header("Biomes")]
        [Tooltip("How big biomes are. Smaller numbers make bigger biomes.")]
        [SerializeField, Range(0.0005f, 0.02f)] float climateFrequency = 0.004f;

        [Tooltip("How wide the smooth blend between two biomes is, in climate units. Bigger = gentler borders.")]
        [SerializeField, Range(0.02f, 1f)] float biomeBlend = 0.25f;

        [Tooltip("How ragged the line between two biomes' surface blocks looks, in climate units.")]
        [SerializeField, Range(0f, 0.3f)] float surfaceJitter = 0.06f;

        [SerializeField] List<BiomeSettings> biomes = new List<BiomeSettings>();

        [Header("Blocks")]
        [Tooltip("Fills the ground under every biome's surface layers.")]
        [SerializeField] BlockDefinition stoneBlock;

        [Tooltip("Caps any ground taller than the snow line, in every biome.")]
        [SerializeField] BlockDefinition snowBlock;

        [SerializeField] int snowLine = 165;

        [Header("Caves")]
        [Tooltip("Size of the winding tunnels. Smaller numbers make longer, straighter tunnels.")]
        [SerializeField, Range(0.002f, 0.05f)] float tunnelFrequency = 0.012f;

        [Tooltip("How thick tunnels are.")]
        [SerializeField, Range(0f, 0.2f)] float tunnelWidth = 0.06f;

        [Tooltip("Size of the big open caverns deep underground.")]
        [SerializeField, Range(0.002f, 0.1f)] float cavernFrequency = 0.02f;

        [Tooltip("Higher = fewer, smaller caverns. 1 turns them off.")]
        [SerializeField, Range(0f, 1f)] float cavernThreshold = 0.62f;

        [Tooltip("Caverns only form below this height.")]
        [SerializeField] int cavernMaxY = 56;

        [Tooltip("Tunnels shrink within this many blocks of the surface, so only some break through as cave entrances.")]
        [SerializeField, Min(1f)] float caveEntranceDepth = 8f;

        [Tooltip("Caves never carve below this height, so the world always has a floor.")]
        [SerializeField, Min(0)] int caveFloorY = 1;

        [Header("Mana Crystal Ore")]
        [SerializeField] BlockDefinition oreBlock;

        [Tooltip("Size of ore clusters. Bigger numbers make smaller, more scattered clusters.")]
        [SerializeField, Range(0.02f, 0.5f)] float oreFrequency = 0.14f;

        [Tooltip("Higher = rarer ore.")]
        [SerializeField, Range(0f, 1f)] float oreThreshold = 0.78f;

        [Tooltip("Ore only forms below this height.")]
        [SerializeField] int oreMaxY = 80;

        [Header("Trees")]
        [Tooltip("Invisible solid block that fills tree trunks, so trees block building and collide.")]
        [SerializeField] BlockDefinition treeTrunkBlock;

        [Tooltip("Each square of this many blocks has one spot where a tree may grow. Smaller = denser forests. Trees never touch.")]
        [SerializeField, Range(3, 32)] int treeCellSize = 6;

        [Tooltip("Shortest and tallest trunk, in blocks.")]
        [SerializeField, Range(1, 16)] int treeMinHeight = 4;

        [SerializeField, Range(1, 16)] int treeMaxHeight = 6;

        public IReadOnlyList<BiomeSettings> Biomes => biomes;

        /// <summary>Returns every problem with these settings. An empty list means they're valid.</summary>
        public List<string> Validate(BlockRegistry registry)
        {
            var errors = new List<string>();
            if (biomes.Count == 0)
                errors.Add("There are no biomes.");

            CheckBlock(registry, stoneBlock, "Stone Block", errors);
            CheckBlock(registry, snowBlock, "Snow Block", errors);
            CheckBlock(registry, oreBlock, "Ore Block", errors);
            CheckBlock(registry, treeTrunkBlock, "Tree Trunk Block", errors);
            if (treeMaxHeight < treeMinHeight)
                errors.Add("Tree Max Height is lower than Tree Min Height.");
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeSettings biome = biomes[i];
                if (biome == null)
                {
                    errors.Add($"Biome {i} is empty.");
                    continue;
                }
                CheckBlock(registry, biome.SurfaceBlock, $"'{biome.Name}' Surface Block", errors);
                CheckBlock(registry, biome.SubsurfaceBlock, $"'{biome.Name}' Subsurface Block", errors);
            }
            return errors;
        }

        public TerrainParams ToTerrainParams()
        {
            return new TerrainParams
            {
                ClimateFrequency = climateFrequency,
                BiomeBlend = math.max(biomeBlend, 0.001f),
                SurfaceJitter = surfaceJitter,
                StoneBlock = stoneBlock != null ? stoneBlock.Id : BlockIds.Stone,
                SnowBlock = snowBlock != null ? snowBlock.Id : BlockIds.Snow,
                SnowLine = snowLine,
                TunnelFrequency = tunnelFrequency,
                TunnelWidth = tunnelWidth,
                CavernFrequency = cavernFrequency,
                CavernThreshold = cavernThreshold,
                CavernMaxY = cavernMaxY,
                CaveEntranceDepth = math.max(caveEntranceDepth, 1f),
                CaveFloorY = caveFloorY,
                OreBlock = oreBlock != null ? oreBlock.Id : BlockIds.ManaCrystalOre,
                OreFrequency = oreFrequency,
                OreThreshold = oreThreshold,
                OreMaxY = oreMaxY,
                TreeTrunkBlock = treeTrunkBlock != null ? treeTrunkBlock.Id : BlockIds.TreeTrunk,
                TreeCellSize = treeCellSize,
                TreeMinHeight = treeMinHeight,
                TreeMaxHeight = math.max(treeMaxHeight, treeMinHeight),
            };
        }

        /// <summary>Builds the biome list for jobs, in the same order as <see cref="Biomes"/>. The caller must Dispose it.</summary>
        public NativeArray<BiomeInfo> CreateBiomeLookup(Allocator allocator)
        {
            var lookup = new NativeArray<BiomeInfo>(biomes.Count, allocator);
            for (int i = 0; i < biomes.Count; i++)
                lookup[i] = biomes[i].ToBiomeInfo();
            return lookup;
        }

        static void CheckBlock(BlockRegistry registry, BlockDefinition block, string label, List<string> errors)
        {
            if (block == null)
                errors.Add($"{label} is not set.");
            else if (registry != null && !registry.TryGet(block.Id, out _))
                errors.Add($"{label} '{block.DisplayName}' (ID {block.Id}) is not in the block registry.");
        }
    }

    /// <summary>One biome's shape and blocks. Edited inside <see cref="TerrainSettings"/>.</summary>
    [Serializable]
    public sealed class BiomeSettings
    {
        [SerializeField] string name = "New Biome";

        [Tooltip("Where this biome lives on the climate map. X: -1 cold to +1 hot. Y: -1 flat to +1 mountainous.")]
        [SerializeField] Vector2 climate;

        [Header("Shape")]
        [Tooltip("Average ground height.")]
        [SerializeField] float baseHeight = 72f;

        [Tooltip("How far hills rise above and dip below the base height.")]
        [SerializeField, Min(0f)] float heightAmplitude = 10f;

        [Tooltip("How wide hills are. Smaller numbers make wider hills.")]
        [SerializeField, Range(0.001f, 0.1f)] float frequency = 0.01f;

        [Tooltip("Layers of detail. More layers = bumpier ground.")]
        [SerializeField, Range(1, 8)] int octaves = 3;

        [Tooltip("How strong each finer layer of detail is.")]
        [SerializeField, Range(0f, 1f)] float roughness = 0.5f;

        [Tooltip("Sharp crests instead of round hills: dunes and jagged peaks.")]
        [SerializeField] bool ridged;

        [Tooltip("Height of terrace steps, for cliffs and plateaus. 0 = smooth.")]
        [SerializeField, Min(0f)] float terraceStep;

        [Header("Blocks")]
        [SerializeField] BlockDefinition surfaceBlock;
        [SerializeField] BlockDefinition subsurfaceBlock;

        [Tooltip("How many layers of subsurface block sit under the surface before stone starts.")]
        [SerializeField, Range(0, 16)] int subsurfaceDepth = 3;

        [Tooltip("How much Mana Crystal Ore grows underneath. 1 = normal.")]
        [SerializeField, Range(0f, 2f)] float oreRichness = 1f;

        [Header("Trees")]
        [Tooltip("Chance that each tree spot grows a tree. 0 = no trees, 1 = a tree in every spot.")]
        [SerializeField, Range(0f, 1f)] float treeChance;

        public string Name => name;
        public BlockDefinition SurfaceBlock => surfaceBlock;
        public BlockDefinition SubsurfaceBlock => subsurfaceBlock;
        public float TreeChance => treeChance;

        public BiomeInfo ToBiomeInfo()
        {
            return new BiomeInfo
            {
                Climate = climate,
                BaseHeight = baseHeight,
                HeightAmplitude = heightAmplitude,
                Frequency = frequency,
                Octaves = math.max(octaves, 1),
                Roughness = roughness,
                Ridged = ridged,
                TerraceStep = terraceStep,
                SurfaceBlock = surfaceBlock != null ? surfaceBlock.Id : BlockIds.Stone,
                SubsurfaceBlock = subsurfaceBlock != null ? subsurfaceBlock.Id : BlockIds.Stone,
                SubsurfaceDepth = subsurfaceDepth,
                OreRichness = oreRichness,
                TreeChance = treeChance,
            };
        }
    }

    /// <summary>Job-friendly copy of the global values in <see cref="TerrainSettings"/>.</summary>
    public struct TerrainParams
    {
        public float ClimateFrequency;
        public float BiomeBlend;
        public float SurfaceJitter;

        public ushort StoneBlock;
        public ushort SnowBlock;
        public int SnowLine;

        public float TunnelFrequency;
        public float TunnelWidth;
        public float CavernFrequency;
        public float CavernThreshold;
        public int CavernMaxY;
        public float CaveEntranceDepth;
        public int CaveFloorY;

        public ushort OreBlock;
        public float OreFrequency;
        public float OreThreshold;
        public int OreMaxY;

        public ushort TreeTrunkBlock;
        public int TreeCellSize;
        public int TreeMinHeight;
        public int TreeMaxHeight;
    }

    /// <summary>Job-friendly copy of a <see cref="BiomeSettings"/>.</summary>
    public struct BiomeInfo
    {
        public float2 Climate;
        public float BaseHeight;
        public float HeightAmplitude;
        public float Frequency;
        public int Octaves;
        public float Roughness;
        public bool Ridged;
        public float TerraceStep;
        public ushort SurfaceBlock;
        public ushort SubsurfaceBlock;
        public int SubsurfaceDepth;
        public float OreRichness;
        public float TreeChance;
    }
}
