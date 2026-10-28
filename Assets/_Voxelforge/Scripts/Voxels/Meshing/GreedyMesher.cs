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
    /// visible faces of the <i>same block</i> into the biggest rectangles possible.</item>
    /// <item><b>Emit:</b> write one quad (4 vertices, 2 triangles) per rectangle.</item>
    /// </list>
    /// A flat 32x32 floor becomes 1 quad instead of 1,024.
    /// </summary>
    public static class GreedyMesher
    {
        const int Rows = ChunkSize * ChunkSize;

        /// <summary>Appends 4 vertices per visible quad. Burst-safe; tests call it directly.</summary>
        public static void Build(NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, NativeList<ChunkVertex> vertices)
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
                MergeAndEmit(facing, faceRows, padded, blocks, vertices);
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
            NativeArray<BlockInfo> blocks, NativeList<ChunkVertex> vertices)
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

                    // Grow right along u while the faces are visible and the same block.
                    int width = 1;
                    while (u + width < ChunkSize && (bits & (1u << (u + width))) != 0 && BlockAt(facing, slice, u + width, v, padded) == id)
                        width++;

                    // Grow up along v while the whole strip matches.
                    uint mask = (width == ChunkSize ? uint.MaxValue : (1u << width) - 1) << u;
                    int height = 1;
                    while (v + height < ChunkSize && (faceRows[row + height] & mask) == mask
                           && StripMatches(facing, slice, u, width, v + height, id, padded))
                        height++;

                    for (int k = 0; k < height; k++)
                        faceRows[row + k] &= ~mask;

                    EmitQuad(facing, slice, u, v, width, height, blocks[id], vertices);
                }
            }
        }

        static bool StripMatches(BlockFacing facing, int slice, int u, int width, int v, ushort id, NativeArray<ushort> padded)
        {
            for (int i = 0; i < width; i++)
            {
                if (BlockAt(facing, slice, u + i, v, padded) != id)
                    return false;
            }
            return true;
        }

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

        static void EmitQuad(BlockFacing facing, int slice, int u, int v, int width, int height, BlockInfo block,
            NativeList<ChunkVertex> vertices)
        {
            int3 normal = Normal(facing);
            int3 uAxis, vAxis;
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

            // Positive faces sit on the far side of the block.
            int3 origin = ToLocal(facing, slice, u, v) + math.max(normal, 0);
            float3 o = origin;
            float3 du = uAxis * width;
            float3 dv = vAxis * height;

            // Unity draws triangles whose corners go clockwise as seen from the front.
            // That holds for (o, o+du, o+du+dv) when cross(u, v) points along the normal; otherwise swap u and v.
            float3 n = normal;
            bool swap = math.dot(math.cross((float3)uAxis, (float3)vAxis), n) < 0;
            float id = block.Id;

            if (!swap)
            {
                vertices.Add(Vertex(o, n, block.MapColor, new float2(0, 0), id));
                vertices.Add(Vertex(o + du, n, block.MapColor, new float2(width, 0), id));
                vertices.Add(Vertex(o + du + dv, n, block.MapColor, new float2(width, height), id));
                vertices.Add(Vertex(o + dv, n, block.MapColor, new float2(0, height), id));
            }
            else
            {
                vertices.Add(Vertex(o, n, block.MapColor, new float2(0, 0), id));
                vertices.Add(Vertex(o + dv, n, block.MapColor, new float2(0, height), id));
                vertices.Add(Vertex(o + du + dv, n, block.MapColor, new float2(width, height), id));
                vertices.Add(Vertex(o + du, n, block.MapColor, new float2(width, 0), id));
            }
        }

        static ChunkVertex Vertex(float3 position, float3 normal, Color32 color, float2 uv, float blockId)
        {
            return new ChunkVertex { Position = position, Normal = normal, Color = color, UV = uv, BlockId = blockId };
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

        public void Execute()
        {
            var vertices = new NativeList<ChunkVertex>(4096, Allocator.Temp);
            GreedyMesher.Build(Padded, Blocks, vertices);
            GreedyMesher.WriteMesh(vertices, MeshData);
        }
    }
}
