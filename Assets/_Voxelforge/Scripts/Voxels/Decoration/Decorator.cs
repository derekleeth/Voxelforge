using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Voxelforge.Voxels.ChunkGatherJob;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Places one chunk's decorations (Roadmap 2.6) from the same padded block buffer the mesher reads, so it runs
    /// alongside meshing and is rebuilt every time the chunk is. Everything depends only on the seed, the blocks
    /// and the position, so every PC places the same plants and nothing needs saving or syncing.
    /// <list type="bullet">
    /// <item><b>Ground</b> decorations: every block top with Air above rolls once per block. The roll picks at most
    /// one decoration, each with chance <c>density × biome weight</c>.</item>
    /// <item><b>Trees</b>: every <see cref="BlockIds.TreeTrunk"/> block with no trunk under it is the bottom of a
    /// tree. The tree look is picked by the biome's tree weights and scaled to the trunk height.</item>
    /// <item><b>Props</b> (Roadmap 3.5): every prop block (torch, workbench) gets its model, stood on the floor or hung
    /// on a wall by <see cref="VoxelSupport"/>'s rules. Props are blocks players placed, so they come back from the
    /// delta log like any other block.</item>
    /// </list>
    /// A decoration belongs to the chunk holding the block it stands on, so it disappears when that block changes.
    /// Each one is tinted with its biome's grass or foliage colour where it stands (Roadmap 2.6).
    /// </summary>
    public static class Decorator
    {
        const uint DecorationSalt = 9;

        public static void Build(NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, int3 origin, WorldSeed seed, in TerrainSampler sampler,
            in TerrainParams terrain, NativeArray<DecorationInfo> decorations, NativeArray<float> biomeWeights,
            NativeList<DecorationInstance> output)
        {
            if (decorations.Length == 0)
                return;

            uint seedHash = seed.Hash(DecorationSalt);
            uint treeHash = TreePlacement.SeedHash(seed);
            int biomeCount = sampler.BiomeCount;

            for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
            {
                int biome = -1; // Worked out only for columns that need it.
                int wx = origin.x + x, wz = origin.z + z;
                for (int y = 0; y < ChunkSize; y++)
                {
                    ushort block = padded[PaddedIndex(x + 1, y + 1, z + 1)];
                    if (block == BlockIds.Air)
                        continue;
                    int wy = origin.y + y;

                    if (block < blocks.Length && blocks[block].IsProp)
                    {
                        AddProp(new int3(wx, wy, wz), block, SupportMask(padded, blocks, x + 1, y + 1, z + 1), seedHash,
                            decorations, output);
                        continue;
                    }

                    if (block == terrain.TreeTrunkBlock)
                    {
                        if (padded[PaddedIndex(x + 1, y, z + 1)] == terrain.TreeTrunkBlock)
                            continue; // Not the bottom of the trunk.
                        if (biome < 0)
                            biome = sampler.SurfaceBiome(wx, wz);
                        AddTree(new int3(wx, wy, wz), biome, biomeCount, treeHash, sampler, terrain, decorations, biomeWeights, output);
                        continue;
                    }

                    if (padded[PaddedIndex(x + 1, y + 2, z + 1)] != BlockIds.Air)
                        continue; // Only block tops open to the sky (or a cave) grow plants.
                    if (biome < 0)
                        biome = sampler.SurfaceBiome(wx, wz);
                    AddGround(new int3(wx, wy, wz), block, biome, biomeCount, seedHash, sampler, decorations, biomeWeights, output);
                }
            }
        }

        static void AddGround(int3 w, ushort block, int biome, int biomeCount, uint seedHash, in TerrainSampler sampler,
            NativeArray<DecorationInfo> decorations, NativeArray<float> biomeWeights, NativeList<DecorationInstance> output)
        {
            uint h = math.hash(new uint4((uint)w.x, (uint)w.y, (uint)w.z, seedHash));
            float roll = Unit(h);
            float total = 0f;
            for (int type = 0; type < decorations.Length; type++)
            {
                DecorationInfo info = decorations[type];
                if (info.Kind != DecorationKind.Ground || !info.GrowsOn(block))
                    continue;

                total += info.Density * biomeWeights[type * biomeCount + biome];
                if (roll >= total)
                    continue;

                // Fresh hashes for the look, so they don't depend on the roll that picked the type.
                uint a = math.hash(new uint2(h, 1));
                uint b = math.hash(new uint2(h, 2));
                var position = new float3(w.x + 0.2f + 0.6f * Unit(a), w.y + 1f, w.z + 0.2f + 0.6f * Unit(a >> 12));
                output.Add(new DecorationInstance
                {
                    Position = position,
                    Yaw = Unit(b) * 2f * math.PI,
                    Scale = math.lerp(info.MinScale, info.MaxScale, Unit(b >> 12)),
                    Type = (uint)type,
                    Tint = TintAt(sampler, position, info.Tint),
                    Random = math.hash(new uint2(h, 3)),
                });
                return;
            }
        }

        static void AddTree(int3 w, int biome, int biomeCount, uint treeHash, in TerrainSampler sampler, in TerrainParams terrain,
            NativeArray<DecorationInfo> decorations, NativeArray<float> biomeWeights, NativeList<DecorationInstance> output)
        {
            float total = 0f;
            for (int type = 0; type < decorations.Length; type++)
            {
                if (decorations[type].Kind == DecorationKind.Tree)
                    total += biomeWeights[type * biomeCount + biome];
            }

            uint variant = TreePlacement.Variant(treeHash, w.x, w.z);
            int chosen = -1;
            float pick = Unit(variant) * total;
            for (int type = 0; type < decorations.Length && chosen < 0; type++)
            {
                if (decorations[type].Kind != DecorationKind.Tree)
                    continue;
                pick -= biomeWeights[type * biomeCount + biome];
                if (pick < 0f)
                    chosen = type;
            }
            // A tree where its biome lists none (e.g. a border column): use any tree rather than leave an invisible trunk.
            for (int type = 0; type < decorations.Length && chosen < 0; type++)
            {
                if (decorations[type].Kind == DecorationKind.Tree)
                    chosen = type;
            }
            if (chosen < 0)
                return;

            DecorationInfo info = decorations[chosen];
            int trunk = TreePlacement.TrunkHeight(treeHash, w.x, w.z, terrain.TreeMinHeight, terrain.TreeMaxHeight);
            var position = new float3(w.x + 0.5f, w.y, w.z + 0.5f);
            output.Add(new DecorationInstance
            {
                Position = position,
                Yaw = Unit(variant >> 8) * 2f * math.PI,
                Scale = trunk / info.ModelTrunkHeight,
                Type = (uint)chosen,
                Tint = TintAt(sampler, position, info.Tint),
                Random = math.hash(new uint2(variant, 7)),
            });
        }

        /// <summary><see cref="VoxelSupport.SupportMask"/> for padded cell (px, py, pz), from the padded copy.</summary>
        static int SupportMask(NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, int px, int py, int pz)
        {
            int mask = 0;
            if (IsSolid(blocks, padded[PaddedIndex(px, py - 1, pz)])) mask |= 1 << 0;
            if (IsSolid(blocks, padded[PaddedIndex(px, py, pz + 1)])) mask |= 1 << 1;
            if (IsSolid(blocks, padded[PaddedIndex(px + 1, py, pz)])) mask |= 1 << 2;
            if (IsSolid(blocks, padded[PaddedIndex(px, py, pz - 1)])) mask |= 1 << 3;
            if (IsSolid(blocks, padded[PaddedIndex(px - 1, py, pz)])) mask |= 1 << 4;
            return mask;
        }

        static bool IsSolid(NativeArray<BlockInfo> blocks, ushort id) => id < blocks.Length && blocks[id].IsSolid;

        static void AddProp(int3 w, ushort block, int supportMask, uint seedHash, NativeArray<DecorationInfo> decorations,
            NativeList<DecorationInstance> output)
        {
            int support = VoxelSupport.Choose(supportMask);
            int chosen = -1, fallback = -1;
            for (int type = 0; type < decorations.Length && chosen < 0; type++)
            {
                DecorationInfo info = decorations[type];
                if (info.Kind != DecorationKind.Prop || info.PropBlock != block)
                    continue;
                if (fallback < 0)
                    fallback = type;
                if (info.Attach == PropAttach.Any || (info.Attach == PropAttach.Floor && support == 0) ||
                    (info.Attach == PropAttach.Wall && support > 0))
                    chosen = type;
            }
            if (chosen < 0)
                chosen = fallback;
            if (chosen < 0)
                return;

            uint h = math.hash(new uint4((uint)w.x, (uint)w.y, (uint)w.z, seedHash ^ 0x9E3779B9u));
            // Walls: the mesh's +Z side faces the wall (north, east, south, west = support 1-4). Otherwise a random quarter.
            float yaw = decorations[chosen].Attach == PropAttach.Wall && support > 0
                ? (support - 1) * 0.5f * math.PI
                : (h & 3u) * 0.5f * math.PI;
            output.Add(new DecorationInstance
            {
                Position = new float3(w.x + 0.5f, w.y, w.z + 0.5f),
                Yaw = yaw,
                Scale = 1f,
                Type = (uint)chosen,
                Tint = DecorationInstance.White,
                Random = math.hash(new uint2(h, 5)),
            });
        }

        static uint TintAt(in TerrainSampler sampler, float3 position, BiomeTint tint)
        {
            return tint == BiomeTint.None ? DecorationInstance.White : BiomeTints.Pack(sampler.TintAt(position.xz, tint));
        }

        /// <summary>The top 24 bits of <paramref name="h"/> as a number from 0 (inclusive) to 1 (exclusive).</summary>
        static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
    }

    /// <summary>
    /// Runs <see cref="Decorator"/> on a worker thread, alongside <see cref="GreedyMeshJob"/>. Strict floats like
    /// <see cref="GenerateChunkJob"/>, so every PC agrees on each column's biome and places the same plants.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct DecorateChunkJob : IJob
    {
        [ReadOnly] public NativeArray<ushort> Padded;
        [ReadOnly] public NativeArray<BlockInfo> Blocks;
        public int3 Origin;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;
        [ReadOnly] public NativeArray<DecorationInfo> Decorations;
        [ReadOnly] public NativeArray<float> BiomeWeights;
        public NativeList<DecorationInstance> Output;

        public void Execute()
        {
            Output.Clear();
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            Decorator.Build(Padded, Blocks, Origin, Seed, sampler, Terrain, Decorations, BiomeWeights, Output);
            sampler.Dispose();
        }
    }
}
