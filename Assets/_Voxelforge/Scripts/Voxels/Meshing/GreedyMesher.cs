using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Voxelforge.Voxels.ChunkGatherJob;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Turns a padded block buffer (from <see cref="ChunkGatherJob"/>) into a chunk mesh in 3 steps:
    /// <list type="number">
    /// <item><b>Cull:</b> pack each row of opaque blocks into the bits of a <c>ulong</c>. One shift and
    /// mask then finds every visible face in that row at once (a face is visible when the block next to it isn't opaque).</item>
    /// <item><b>Greedy merge:</b> for each of the 6 directions and 32 slices, join neighbouring
    /// visible faces of the <i>same block with the same corner shading</i> into the biggest rectangles possible.</item>
    /// <item><b>Emit:</b> write one quad (4 vertices, 2 triangles) per rectangle.</item>
    /// </list>
    /// A flat 32x32 floor becomes 1 quad instead of 1,024.
    /// <para>
    /// <b>Corner ambient occlusion</b> (Roadmap 2.6): each face corner is darkened by the blocks touching it in front
    /// of the face, from 0 (tucked into a corner) to 3 (open). It goes in vertex colour alpha. Faces only merge when
    /// all 4 corners match, and each quad is split along the diagonal that keeps the shading symmetric.
    /// </para>
    /// <para>
    /// <b>Biome tint</b>: textured faces carry the biome colour of their block's <see cref="BiomeTint"/> at each
    /// corner in vertex colour RGB (white for untinted blocks, see <see cref="ChunkTints"/>). Untextured faces carry
    /// their flat map colour instead.
    /// </para>
    /// </summary>
    public static class GreedyMesher
    {
        const int Rows = ChunkSize * ChunkSize;

        /// <summary>Corner AO of a fully open corner. 0 is a corner tucked between two blocks.</summary>
        public const int OpenCorner = 3;

        /// <summary>Appends 4 vertices per visible quad, with no biome tint. Burst-safe; tests call it directly.</summary>
        public static void Build(NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, NativeList<ChunkVertex> vertices)
        {
            ChunkTints tints = ChunkTints.None;
            Build(padded, blocks, vertices, ref tints);
        }

        /// <summary>Appends 4 vertices per visible quad, colouring tinted blocks from <paramref name="tints"/>.</summary>
        public static void Build(NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, NativeList<ChunkVertex> vertices,
            ref ChunkTints tints)
        {
            // Opaque bits along each axis. Bit p = padded coordinate p (0 and 33 are the neighbour border).
            var columnsX = new NativeArray<ulong>(Rows, Allocator.Temp); // [y * 32 + z], bits over x
            var columnsY = new NativeArray<ulong>(Rows, Allocator.Temp); // [z * 32 + x], bits over y
            var columnsZ = new NativeArray<ulong>(Rows, Allocator.Temp); // [y * 32 + x], bits over z
            var faceRows = new NativeArray<uint>(Rows, Allocator.Temp);  // [slice * 32 + v], bits over u

            for (int py = 0; py < PaddedSize; py++)
            for (int pz = 0; pz < PaddedSize; pz++)
            for (int px = 0; px < PaddedSize; px++)
            {
                if (!IsOpaque(blocks, padded[PaddedIndex(px, py, pz)]))
                    continue;

                int x = px - 1, y = py - 1, z = pz - 1;
                bool inX = (uint)x < ChunkSize, inY = (uint)y < ChunkSize, inZ = (uint)z < ChunkSize;
                if (inY && inZ)
                    columnsX[y * ChunkSize + z] |= 1ul << px;
                if (inX && inZ)
                    columnsY[z * ChunkSize + x] |= 1ul << py;
                if (inX && inY)
                    columnsZ[y * ChunkSize + x] |= 1ul << pz;
            }

            for (int face = 0; face < 6; face++)
            {
                var facing = (BlockFacing)face;
                FindVisibleFaces(facing, columnsX, columnsY, columnsZ, faceRows);
                MergeAndEmit(facing, faceRows, padded, blocks, vertices, ref tints);
            }
        }

        /// <summary>True if <paramref name="id"/> hides the faces next to it. Unknown IDs count as see-through.</summary>
        public static bool IsOpaque(NativeArray<BlockInfo> blocks, ushort id) => id < blocks.Length && blocks[id].IsOpaque;

        /// <summary>
        /// Fills <paramref name="faceRows"/> with one bit per visible face facing <paramref name="facing"/>.
        /// Rows are grouped by slice (position along the face normal), then by the slice's v axis.
        /// </summary>
        static void FindVisibleFaces(BlockFacing facing, NativeArray<ulong> columnsX, NativeArray<ulong> columnsY,
            NativeArray<ulong> columnsZ, NativeArray<uint> faceRows)
        {
            for (int i = 0; i < Rows; i++)
                faceRows[i] = 0;

            bool positive = IsPositive(facing);
            switch (facing)
            {
                case BlockFacing.East:
                case BlockFacing.West:
                    // Slice = x, u = z, v = y.
                    for (int y = 0; y < ChunkSize; y++)
                    for (int z = 0; z < ChunkSize; z++)
                    {
                        uint bits = VisibleBits(columnsX[y * ChunkSize + z], positive);
                        while (bits != 0)
                        {
                            int x = math.tzcnt(bits);
                            bits &= bits - 1;
                            faceRows[x * ChunkSize + y] |= 1u << z;
                        }
                    }
                    break;

                case BlockFacing.Up:
                case BlockFacing.Down:
                    // Slice = y, u = x, v = z.
                    for (int z = 0; z < ChunkSize; z++)
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        uint bits = VisibleBits(columnsY[z * ChunkSize + x], positive);
                        while (bits != 0)
                        {
                            int y = math.tzcnt(bits);
                            bits &= bits - 1;
                            faceRows[y * ChunkSize + z] |= 1u << x;
                        }
                    }
                    break;

                default:
                    // North / South. Slice = z, u = x, v = y.
                    for (int y = 0; y < ChunkSize; y++)
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        uint bits = VisibleBits(columnsZ[y * ChunkSize + x], positive);
                        while (bits != 0)
                        {
                            int z = math.tzcnt(bits);
                            bits &= bits - 1;
                            faceRows[z * ChunkSize + y] |= 1u << x;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Given a padded column of opaque bits, returns a 32-bit mask of blocks whose face in that
        /// direction is visible: the block is opaque and its neighbour on that side isn't.
        /// </summary>
        static uint VisibleBits(ulong column, bool positive)
        {
            ulong visible = positive ? column & ~(column >> 1) : column & ~(column << 1);
            return (uint)(visible >> 1); // Drop the padding bit so bit 0 = local block 0.
        }

        static void MergeAndEmit(BlockFacing facing, NativeArray<uint> faceRows, NativeArray<ushort> padded,
            NativeArray<BlockInfo> blocks, NativeList<ChunkVertex> vertices, ref ChunkTints tints)
        {
            for (int slice = 0; slice < ChunkSize; slice++)
            for (int v = 0; v < ChunkSize; v++)
            {
                int row = slice * ChunkSize + v;
                while (faceRows[row] != 0)
                {
                    uint bits = faceRows[row];
                    int u = math.tzcnt(bits);
                    ushort id = BlockAt(facing, slice, u, v, padded);
                    int ao = CornerOcclusion(facing, slice, u, v, padded, blocks);

                    // Grow right along u while the faces are visible, the same block and shaded the same.
                    int width = 1;
                    while (u + width < ChunkSize && (bits & (1u << (u + width))) != 0
                           && Matches(facing, slice, u + width, v, id, ao, padded, blocks))
                        width++;

                    // Grow up along v while the whole strip matches.
                    uint mask = (width == ChunkSize ? uint.MaxValue : (1u << width) - 1) << u;
                    int height = 1;
                    while (v + height < ChunkSize && (faceRows[row + height] & mask) == mask
                           && StripMatches(facing, slice, u, width, v + height, id, ao, padded, blocks))
                        height++;

                    for (int k = 0; k < height; k++)
                        faceRows[row + k] &= ~mask;

                    EmitQuad(facing, slice, u, v, width, height, blocks[id], ao, vertices, ref tints);
                }
            }
        }

        static bool Matches(BlockFacing facing, int slice, int u, int v, ushort id, int ao, NativeArray<ushort> padded,
            NativeArray<BlockInfo> blocks)
        {
            return BlockAt(facing, slice, u, v, padded) == id && CornerOcclusion(facing, slice, u, v, padded, blocks) == ao;
        }

        static bool StripMatches(BlockFacing facing, int slice, int u, int width, int v, ushort id, int ao,
            NativeArray<ushort> padded, NativeArray<BlockInfo> blocks)
        {
            for (int i = 0; i < width; i++)
            {
                if (!Matches(facing, slice, u + i, v, id, ao, padded, blocks))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The corner AO of one face, packed 2 bits per corner in (u, v) order (0, 0), (1, 0), (1, 1), (0, 1),
        /// lowest bits first. Each corner is 0 to <see cref="OpenCorner"/>, from the 3 blocks that touch it in the
        /// layer just in front of the face: two along the sides and one diagonal. Two sides blocked count as fully
        /// tucked in, whatever the diagonal is.
        /// </summary>
        public static int CornerOcclusion(BlockFacing facing, int slice, int u, int v, NativeArray<ushort> padded,
            NativeArray<BlockInfo> blocks)
        {
            int3 front = ToLocal(facing, slice, u, v) + Normal(facing) + 1; // Padded coordinates.
            Axes(facing, out int3 uAxis, out int3 vAxis);
            int packed = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                int3 du = corner == 1 || corner == 2 ? uAxis : -uAxis;
                int3 dv = corner >= 2 ? vAxis : -vAxis;
                bool side1 = IsOpaque(blocks, padded[PaddedIndexOf(front + du)]);
                bool side2 = IsOpaque(blocks, padded[PaddedIndexOf(front + dv)]);
                bool diagonal = IsOpaque(blocks, padded[PaddedIndexOf(front + du + dv)]);
                int ao = side1 && side2 ? 0 : OpenCorner - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (diagonal ? 1 : 0));
                packed |= ao << (corner * 2);
            }
            return packed;
        }

        static int PaddedIndexOf(int3 p) => PaddedIndex(p.x, p.y, p.z);

        static ushort BlockAt(BlockFacing facing, int slice, int u, int v, NativeArray<ushort> padded)
        {
            int3 local = ToLocal(facing, slice, u, v);
            return padded[PaddedIndex(local.x + 1, local.y + 1, local.z + 1)];
        }

        /// <summary>Converts face-plane coordinates (slice, u, v) back to a local block position.</summary>
        static int3 ToLocal(BlockFacing facing, int slice, int u, int v)
        {
            switch (facing)
            {
                case BlockFacing.East:
                case BlockFacing.West:
                    return new int3(slice, v, u);
                case BlockFacing.Up:
                case BlockFacing.Down:
                    return new int3(u, slice, v);
                default:
                    return new int3(u, v, slice);
            }
        }

        /// <summary>The face plane's u and v directions, matching <see cref="ToLocal"/>.</summary>
        static void Axes(BlockFacing facing, out int3 uAxis, out int3 vAxis)
        {
            switch (facing)
            {
                case BlockFacing.East:
                case BlockFacing.West:
                    uAxis = new int3(0, 0, 1);
                    vAxis = new int3(0, 1, 0);
                    break;
                case BlockFacing.Up:
                case BlockFacing.Down:
                    uAxis = new int3(1, 0, 0);
                    vAxis = new int3(0, 0, 1);
                    break;
                default:
                    uAxis = new int3(1, 0, 0);
                    vAxis = new int3(0, 1, 0);
                    break;
            }
        }

        static void EmitQuad(BlockFacing facing, int slice, int u, int v, int width, int height, BlockInfo block, int ao,
            NativeList<ChunkVertex> vertices, ref ChunkTints tints)
        {
            int3 normal = Normal(facing);
            Axes(facing, out int3 uAxis, out int3 vAxis);

            // Positive faces sit on the far side of the block.
            int3 origin = ToLocal(facing, slice, u, v) + math.max(normal, 0);
            float3 o = origin;
            float3 du = uAxis * width;
            float3 dv = vAxis * height;

            // Unity draws triangles whose corners go clockwise as seen from the front.
            // That holds for (o, o+du, o+du+dv) when cross(u, v) points along the normal; otherwise swap u and v,
            // which visits the (u, v) corners in the order 0, 3, 2, 1.
            float3 n = normal;
            bool swap = math.dot(math.cross((float3)uAxis, (float3)vAxis), n) < 0;
            int4 corners = swap ? new int4(0, 3, 2, 1) : new int4(0, 1, 2, 3);

            // The index buffer splits each quad along its 1st and 3rd vertex. Splitting through two bright corners
            // smears a dark corner into a triangle, so in that case start one corner later to split the other way.
            int ao0 = ao & 3, ao1 = (ao >> 2) & 3, ao2 = (ao >> 4) & 3, ao3 = (ao >> 6) & 3;
            if (ao0 + ao2 > ao1 + ao3)
                corners = corners.yzwx;

            ushort layer = block.LayerFor(facing);
            float textureLayer = layer == BlockInfo.NoTexture ? -1f : layer;

            for (int i = 0; i < 4; i++)
            {
                int corner = corners[i];
                float cu = corner == 1 || corner == 2 ? 1f : 0f;
                float cv = corner >= 2 ? 1f : 0f;
                float3 position = o + du * cu + dv * cv;
                byte occlusion = (byte)(((ao >> (corner * 2)) & 3) * 85); // 0, 85, 170 or 255.
                Color32 colour = textureLayer < 0f
                    ? new Color32(block.MapColor.r, block.MapColor.g, block.MapColor.b, occlusion)
                    : BiomeTints.ToColor32(tints.At((int)position.x, (int)position.z, block.Tint), occlusion);
                vertices.Add(new ChunkVertex
                {
                    Position = position,
                    Normal = n,
                    Color = colour,
                    UV = new float2(width * cu, height * cv),
                    TextureLayer = textureLayer,
                });
            }
        }

        public static bool IsPositive(BlockFacing facing)
        {
            return facing == BlockFacing.North || facing == BlockFacing.East || facing == BlockFacing.Up;
        }

        public static int3 Normal(BlockFacing facing)
        {
            switch (facing)
            {
                case BlockFacing.North: return new int3(0, 0, 1);
                case BlockFacing.South: return new int3(0, 0, -1);
                case BlockFacing.East: return new int3(1, 0, 0);
                case BlockFacing.West: return new int3(-1, 0, 0);
                case BlockFacing.Up: return new int3(0, 1, 0);
                default: return new int3(0, -1, 0);
            }
        }

        /// <summary>
        /// Copies quads into <paramref name="meshData"/>: one submesh, 32-bit indices, two triangles
        /// per quad. Bounds are computed here so the main thread doesn't have to.
        /// </summary>
        public static void WriteMesh(NativeList<ChunkVertex> vertices, Mesh.MeshData meshData)
        {
            int vertexCount = vertices.Length;
            int indexCount = vertexCount / 4 * 6;

            var layout = ChunkVertex.CreateLayout(Allocator.Temp);
            meshData.SetVertexBufferParams(vertexCount, layout);
            meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

            NativeArray<ChunkVertex> vertexData = meshData.GetVertexData<ChunkVertex>();
            float3 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                ChunkVertex vertex = vertices[i];
                vertexData[i] = vertex;
                min = math.min(min, vertex.Position);
                max = math.max(max, vertex.Position);
            }

            NativeArray<uint> indexData = meshData.GetIndexData<uint>();
            for (int quad = 0, i = 0; i < indexCount; quad += 4, i += 6)
            {
                indexData[i] = (uint)quad;
                indexData[i + 1] = (uint)quad + 1;
                indexData[i + 2] = (uint)quad + 2;
                indexData[i + 3] = (uint)quad;
                indexData[i + 4] = (uint)quad + 2;
                indexData[i + 5] = (uint)quad + 3;
            }

            var bounds = vertexCount > 0 ? new Bounds((min + max) * 0.5f, max - min) : default;
            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount) { bounds = bounds, vertexCount = vertexCount },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }
    }

    /// <summary>Runs <see cref="GreedyMesher"/> on a worker thread and writes straight into a <see cref="Mesh.MeshData"/>.</summary>
    [BurstCompile]
    public struct GreedyMeshJob : IJob
    {
        [ReadOnly] public NativeArray<ushort> Padded;
        [ReadOnly] public NativeArray<BlockInfo> Blocks;
        public Mesh.MeshData MeshData;

        /// <summary>For biome tint: the chunk's world origin and the terrain it grew from. Empty biomes = no tint.</summary>
        public int3 Origin;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;

        public void Execute()
        {
            var vertices = new NativeList<ChunkVertex>(4096, Allocator.Temp);
            if (Biomes.Length == 0)
            {
                GreedyMesher.Build(Padded, Blocks, vertices);
            }
            else
            {
                var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
                var tints = new ChunkTints(sampler, Origin, Allocator.Temp);
                GreedyMesher.Build(Padded, Blocks, vertices, ref tints);
                tints.Dispose();
                sampler.Dispose();
            }
            GreedyMesher.WriteMesh(vertices, MeshData);
        }
    }
}
