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

        [Header("Ore (Roadmap 5.7)")]
        [Tooltip("The ore tiers, at most 4. Where two would grow in the same block, the first in the list wins, so list " +
                 "the rarest first. Written by Voxelforge > Build Professions.")]
        [SerializeField] List<OreVein> oreVeins = new List<OreVein>();

        [Header("Trees")]
        [Tooltip("Invisible solid block that fills tree trunks, so trees block building and collide.")]
        [SerializeField] BlockDefinition treeTrunkBlock;

        [Tooltip("Each square of this many blocks has one spot where a tree may grow. Smaller = denser forests. Trees never touch.")]
        [SerializeField, Range(3, 32)] int treeCellSize = 6;

        [Tooltip("Shortest and tallest trunk, in blocks.")]
        [SerializeField, Range(1, 16)] int treeMinHeight = 4;

        [SerializeField, Range(1, 16)] int treeMaxHeight = 6;

        /// <summary>The most ore veins the generator can carry (<see cref="TerrainColumn.OreRichness"/> is a float4).</summary>
        public const int MaxOreVeins = 4;

        public IReadOnlyList<BiomeSettings> Biomes => biomes;

        public IReadOnlyList<OreVein> OreVeins => oreVeins;

        /// <summary>Returns every problem with these settings. An empty list means they're valid.</summary>
        public List<string> Validate(BlockRegistry registry)
        {
            var errors = new List<string>();
            if (biomes.Count == 0)
                errors.Add("There are no biomes.");

            CheckBlock(registry, stoneBlock, "Stone Block", errors);
            CheckBlock(registry, snowBlock, "Snow Block", errors);
            if (oreVeins.Count > MaxOreVeins)
                errors.Add($"There are {oreVeins.Count} ore veins; the generator carries at most {MaxOreVeins}.");
            for (int i = 0; i < oreVeins.Count; i++)
            {
                OreVein vein = oreVeins[i];
                if (vein == null)
                {
                    errors.Add($"Ore vein {i} is empty.");
                    continue;
                }
                CheckBlock(registry, vein.Block, $"Ore vein {i} Block", errors);
                if (vein.MaxY < vein.MinY)
                    errors.Add($"Ore vein {i} has Max Y below Min Y.");
                foreach (OreBiomeRichness entry in vein.BiomeRichness)
                {
                    if (biomes.Find(b => b != null && b.Name == entry.biome) == null)
                        errors.Add($"Ore vein {i} names a biome that doesn't exist: '{entry.biome}'.");
                }
            }
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
                if (biome.HasMesas)
                {
                    CheckBlock(registry, biome.MesaBlock, $"'{biome.Name}' Mesa Block", errors);
                    CheckBlock(registry, biome.MesaBandBlock, $"'{biome.Name}' Mesa Band Block", errors);
                }
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
                OreVeins = OreVeinLookup(),
                TreeTrunkBlock = treeTrunkBlock != null ? treeTrunkBlock.Id : BlockIds.TreeTrunk,
                TreeCellSize = treeCellSize,
                TreeMinHeight = treeMinHeight,
                TreeMaxHeight = math.max(treeMaxHeight, treeMinHeight),
            };
        }

        /// <summary>
        /// Builds the biome list for jobs, in the same order as <see cref="Biomes"/>, with each ore vein's richness there
        /// (<see cref="BiomeInfo.VeinRichness"/>). The caller must Dispose it.
        /// </summary>
        public NativeArray<BiomeInfo> CreateBiomeLookup(Allocator allocator)
        {
            var lookup = new NativeArray<BiomeInfo>(biomes.Count, allocator);
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeInfo info = biomes[i].ToBiomeInfo();
                info.VeinRichness = VeinRichnessIn(biomes[i]);
                lookup[i] = info;
            }
            return lookup;
        }

        /// <summary>How rich each ore vein is in <paramref name="biome"/>: the vein's richness there times the biome's.</summary>
        public float4 VeinRichnessIn(BiomeSettings biome)
        {
            float4 richness = float4.zero;
            for (int v = 0; v < oreVeins.Count && v < MaxOreVeins; v++)
            {
                if (oreVeins[v] != null && oreVeins[v].Block != null)
                    richness[v] = oreVeins[v].RichnessIn(biome.Name) * biome.OreRichness;
            }
            return richness;
        }

        FixedList128Bytes<OreVeinInfo> OreVeinLookup()
        {
            var veins = new FixedList128Bytes<OreVeinInfo>();
            for (int v = 0; v < oreVeins.Count && v < MaxOreVeins; v++)
                veins.Add(oreVeins[v] != null && oreVeins[v].Block != null ? oreVeins[v].ToVeinInfo() : default);
            return veins;
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

        [Tooltip("How much ore of every kind grows underneath. 1 = normal. Each ore vein has its own richness per biome too.")]
        [SerializeField, Range(0f, 2f)] float oreRichness = 1f;

        [Header("Mesas (Roadmap 4.7)")]
        [Tooltip("How tall flat-topped mesas rise above the ground, in blocks. 0 = no mesas.")]
        [SerializeField, Min(0f)] float mesaHeight;

        [Tooltip("How big mesas are. Smaller numbers make bigger, further-apart mesas.")]
        [SerializeField, Range(0.002f, 0.08f)] float mesaFrequency = 0.012f;

        [Tooltip("Higher = fewer, smaller mesas (noise above this becomes mesa).")]
        [SerializeField, Range(0f, 1f)] float mesaThreshold = 0.55f;

        [Tooltip("How wide a mesa's cliff is, in noise units. Smaller = steeper sides.")]
        [SerializeField, Range(0.01f, 0.3f)] float mesaEdge = 0.06f;

        [Tooltip("The rock a mesa's banded cliffs are made of, under its grass cap.")]
        [SerializeField] BlockDefinition mesaBlock;

        [Tooltip("The rock of every other band in a mesa's cliffs.")]
        [SerializeField] BlockDefinition mesaBandBlock;

        [Tooltip("How many blocks thick each band of rock is.")]
        [SerializeField, Range(1, 8)] int mesaBandHeight = 3;

        [Header("Trees")]
        [Tooltip("Chance that each tree spot grows a tree. 0 = no trees, 1 = a tree in every spot.")]
        [SerializeField, Range(0f, 1f)] float treeChance;

        [Header("Colours")]
        [Tooltip("Multiplied into the green of grass blocks, grass tufts and flower leaves. White = unchanged.")]
        [SerializeField] Color grassTint = Color.white;

        [Tooltip("Multiplied into the green of leaves on bushes and trees. White = unchanged.")]
        [SerializeField] Color foliageTint = Color.white;

        public string Name => name;
        public float OreRichness => oreRichness;
        public BlockDefinition SurfaceBlock => surfaceBlock;
        public BlockDefinition SubsurfaceBlock => subsurfaceBlock;
        public float TreeChance => treeChance;
        public bool HasMesas => mesaHeight > 0f;
        public BlockDefinition MesaBlock => mesaBlock;
        public BlockDefinition MesaBandBlock => mesaBandBlock;

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
                MesaHeight = mesaHeight,
                MesaFrequency = mesaFrequency,
                MesaThreshold = mesaThreshold,
                MesaEdge = math.max(mesaEdge, 0.001f),
                MesaBlock = mesaBlock != null ? mesaBlock.Id : BlockIds.Stone,
                MesaBandBlock = mesaBandBlock != null ? mesaBandBlock.Id : (mesaBlock != null ? mesaBlock.Id : BlockIds.Stone),
                MesaBandHeight = math.max(mesaBandHeight, 1),
                GrassTint = new float3(grassTint.r, grassTint.g, grassTint.b),
                FoliageTint = new float3(foliageTint.r, foliageTint.g, foliageTint.b),
            };
        }
    }

    /// <summary>How rich one ore vein is in one biome (Roadmap 5.7). Edited inside an <see cref="OreVein"/>.</summary>
    [Serializable]
    public struct OreBiomeRichness
    {
        [Tooltip("The biome's name, as in the biome list.")]
        public string biome;

        [Range(0f, 3f)] public float richness;

        public OreBiomeRichness(string biome, float richness)
        {
            this.biome = biome;
            this.richness = richness;
        }
    }

    /// <summary>
    /// One tier of ore (Roadmap 5.7): which block, how clustered and how rare, between which heights, and how rich in
    /// each biome. Edited inside <see cref="TerrainSettings"/>.
    /// </summary>
    [Serializable]
    public sealed class OreVein
    {
        [SerializeField] BlockDefinition block;

        [Tooltip("Size of ore clusters. Bigger numbers make smaller, more scattered clusters.")]
        [SerializeField, Range(0.02f, 0.5f)] float frequency = 0.14f;

        [Tooltip("Higher = rarer ore.")]
        [SerializeField, Range(0f, 1f)] float threshold = 0.78f;

        [Tooltip("Ore only forms at or above this height...")]
        [SerializeField] int minY = 1;

        [Tooltip("...and at or below this one.")]
        [SerializeField] int maxY = 80;

        [Tooltip("Over this many blocks below Max Y the ore thins out to nothing, so it gets richer the deeper you dig.")]
        [SerializeField, Min(0)] int topFade = 12;

        [Tooltip("Richness in every biome not listed below. 0 = only in the listed biomes.")]
        [SerializeField, Range(0f, 3f)] float richness = 1f;

        [SerializeField] List<OreBiomeRichness> biomeRichness = new List<OreBiomeRichness>();

        public BlockDefinition Block => block;
        public int MinY => minY;
        public int MaxY => maxY;
        public IReadOnlyList<OreBiomeRichness> BiomeRichness => biomeRichness;

        /// <summary>The vein's richness in the biome called <paramref name="biomeName"/>.</summary>
        public float RichnessIn(string biomeName)
        {
            foreach (OreBiomeRichness entry in biomeRichness)
            {
                if (entry.biome == biomeName)
                    return entry.richness;
            }
            return richness;
        }

        public OreVeinInfo ToVeinInfo() => new OreVeinInfo
        {
            Block = block != null ? block.Id : BlockIds.Stone,
            Frequency = frequency,
            Threshold = threshold,
            MinY = minY,
            MaxY = maxY,
            TopFade = topFade,
        };

        /// <summary>Builds a vein in code, for tests and Build Professions.</summary>
        public static OreVein Create(BlockDefinition block, float frequency, float threshold, int minY, int maxY, int topFade,
            float richness, params OreBiomeRichness[] biomes) => new OreVein
        {
            block = block,
            frequency = frequency,
            threshold = threshold,
            minY = minY,
            maxY = maxY,
            topFade = topFade,
            richness = richness,
            biomeRichness = new List<OreBiomeRichness>(biomes),
        };
    }

    /// <summary>Job-friendly copy of an <see cref="OreVein"/> (Roadmap 5.7).</summary>
    public struct OreVeinInfo
    {
        public ushort Block;
        public float Frequency;
        public float Threshold;
        public int MinY;
        public int MaxY;
        public int TopFade;
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

        /// <summary>The ore tiers (Roadmap 5.7), the first that hits a block winning. At most <see cref="TerrainSettings.MaxOreVeins"/>.</summary>
        public FixedList128Bytes<OreVeinInfo> OreVeins;

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
        /// <summary>How much ore of every kind grows here. Already folded into <see cref="VeinRichness"/>.</summary>
        public float OreRichness;
        /// <summary>How rich each ore vein (<see cref="TerrainParams.OreVeins"/>, in order) is here (Roadmap 5.7).</summary>
        public float4 VeinRichness;
        public float TreeChance;
        /// <summary>0 = no mesas (Roadmap 4.7).</summary>
        public float MesaHeight;
        public float MesaFrequency;
        public float MesaThreshold;
        public float MesaEdge;
        public ushort MesaBlock;
        public ushort MesaBandBlock;
        public int MesaBandHeight;
        public float3 GrassTint;
        public float3 FoliageTint;
    }
}
