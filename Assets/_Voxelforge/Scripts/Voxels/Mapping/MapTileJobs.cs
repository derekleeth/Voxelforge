using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>What the map needs to know about one column of terrain (Roadmap 4.8).</summary>
    public struct MapColumn
    {
        public int Height;
        /// <summary>The block on top: grass, sand, snow, a mesa's banded rock...</summary>
        public ushort Surface;
        /// <summary>A tree grows out of this column.</summary>
        public bool Tree;
        public float3 GrassTint;
        public float3 FoliageTint;
    }

    /// <summary>
    /// Samples the terrain the seed makes for one map tile, plus a one-column border so the painter can shade slopes and
    /// draw contour lines across tile edges. One row per index, so each worker builds its own sampler once per row.
    /// Uses the same strict float mode as <see cref="GenerateChunkJob"/>, so the map always matches the generated ground.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct SampleMapColumnsJob : IJobParallelFor
    {
        /// <summary>World x, z of the tile's south-west column.</summary>
        public int2 Origin;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;

        /// <summary><see cref="MapTile.BorderedSize"/> squared, row by row from the south-west corner of the border.</summary>
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<MapColumn> Columns;

        public void Execute(int row)
        {
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            int z = Origin.y - 1 + row;
            for (int i = 0; i < MapTile.BorderedSize; i++)
            {
                int x = Origin.x - 1 + i;
                TerrainColumn column = sampler.SampleColumn(x, z);
                var position = new float2(x, z);
                Columns[row * MapTile.BorderedSize + i] = new MapColumn
                {
                    Height = column.Height,
                    Surface = sampler.SurfaceBlock(column),
                    Tree = column.TreeHeight > 0,
                    GrassTint = sampler.TintAt(position, BiomeTint.Grass),
                    FoliageTint = sampler.TintAt(position, BiomeTint.Foliage),
                };
            }
            sampler.Dispose();
        }
    }

    /// <summary>
    /// Paints one map tile from <see cref="SampleMapColumnsJob"/>'s columns in the parchment style (Roadmap 4.8): each
    /// surface block's map colour, hillshade from the north-west, ink contour lines, dark tree canopies, then a wash of
    /// old paper with grain and a faint stain. One pixel per block, row 0 to the south, so north is up on the texture.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct PaintMapTileJob : IJobParallelFor
    {
        public int2 Origin;
        [ReadOnly] public NativeArray<MapColumn> Columns;
        [ReadOnly] public NativeArray<BlockInfo> Blocks;
        [WriteOnly] public NativeArray<Color32> Pixels;

        public void Execute(int index)
        {
            int px = index % MapTile.Size;
            int pz = index / MapTile.Size;
            Pixels[index] = MapInk.Paint(Columns, Blocks, px, pz, Origin + new int2(px, pz));
        }
    }

    /// <summary>The parchment map style, one pixel at a time (Roadmap 4.8). Burst-safe.</summary>
    public static class MapInk
    {
        /// <summary>Contour lines are drawn every this many blocks of height.</summary>
        public const int ContourStep = 8;

        static readonly float3 Paper = new float3(0.94f, 0.87f, 0.70f);
        static readonly float3 ContourInk = new float3(0.36f, 0.24f, 0.14f);

        /// <summary>
        /// The colour of the tile pixel (<paramref name="px"/>, <paramref name="pz"/>) at world column
        /// <paramref name="world"/>. <paramref name="columns"/> is the bordered tile (see <see cref="MapTile.BorderedSize"/>).
        /// </summary>
        public static Color32 Paint(NativeArray<MapColumn> columns, NativeArray<BlockInfo> blocks, int px, int pz, int2 world)
        {
            const int s = MapTile.BorderedSize;
            int i = (pz + 1) * s + px + 1;
            MapColumn c = columns[i];
            MapColumn west = columns[i - 1], east = columns[i + 1], south = columns[i - s], north = columns[i + s];

            BlockInfo block = c.Surface < blocks.Length ? blocks[c.Surface] : default;
            Color32 map = block.MapColor;
            float3 ink = new float3(map.r, map.g, map.b) * (1f / 255f);
            if (block.Tint == BiomeTint.Grass)
                ink *= c.GrassTint;
            else if (block.Tint == BiomeTint.Foliage)
                ink *= c.FoliageTint;

            // Trees: a dark blot of leaves round each trunk.
            if (c.Tree)
                ink = c.FoliageTint * 0.32f;
            else if (west.Tree || east.Tree || south.Tree || north.Tree)
                ink = math.lerp(ink, c.FoliageTint * 0.45f, 0.75f);

            // Hillshade, lit from the north-west: slopes facing west or north are brighter.
            float facing = (east.Height - west.Height) - (north.Height - south.Height);
            ink *= math.clamp(1f + 0.07f * facing, 0.55f, 1.3f);

            // Contour lines, drawn on the high side of each step.
            int band = FloorDiv(c.Height, ContourStep);
            if (FloorDiv(west.Height, ContourStep) < band || FloorDiv(east.Height, ContourStep) < band ||
                FloorDiv(south.Height, ContourStep) < band || FloorDiv(north.Height, ContourStep) < band)
                ink = math.lerp(ink, ContourInk, 0.6f);

            // Old paper: wash the ink into it, then add grain and a faint stain. Both come from the world position,
            // so neighbouring tiles join up without a seam.
            float3 colour = math.lerp(Paper, ink * Paper * 1.2f, 0.7f);
            float grain = (math.hash(world) & 0xFF) * (1f / 255f) - 0.5f;
            float stain = noise.snoise((float2)world * 0.013f) * 0.5f + noise.snoise((float2)world * 0.05f) * 0.25f;
            colour *= 1f + grain * 0.06f - math.max(0f, stain) * 0.12f;

            // A touch of sepia.
            float luma = math.dot(colour, new float3(0.299f, 0.587f, 0.114f));
            colour = math.lerp(colour, luma * new float3(1.07f, 0.98f, 0.82f), 0.2f);

            float3 bytes = math.saturate(colour) * 255f + 0.5f;
            return new Color32((byte)bytes.x, (byte)bytes.y, (byte)bytes.z, 255);
        }

        static int FloorDiv(int a, int b) => (int)math.floor((float)a / b);
    }
}
