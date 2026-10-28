using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// A mesh of one whole block, for block drops and anything else that shows a block on its own (Roadmap 3.2).
    /// It runs the real <see cref="GreedyMesher"/> on a single block, so it gets the same vertex layout, texture
    /// layers and look as the terrain and draws with the same block material. It's a unit cube centred on the origin;
    /// scale it to taste.
    /// </summary>
    public static class BlockItemMesh
    {
        public static Mesh Create(NativeArray<BlockInfo> blocks, ushort blockId)
        {
            var padded = new NativeArray<ushort>(ChunkGatherJob.PaddedVolume, Allocator.Temp);
            var vertices = new NativeList<ChunkVertex>(24, Allocator.Temp);
            try
            {
                padded[ChunkGatherJob.PaddedIndex(1, 1, 1)] = blockId; // Chunk-local block (0, 0, 0).
                GreedyMesher.Build(padded, blocks, vertices);
                for (int i = 0; i < vertices.Length; i++)
                {
                    ChunkVertex vertex = vertices[i];
                    vertex.Position -= 0.5f; // Centre the cube on the origin.
                    vertices[i] = vertex;
                }

                Mesh.MeshDataArray meshData = Mesh.AllocateWritableMeshData(1);
                GreedyMesher.WriteMesh(vertices, meshData[0]);
                var mesh = new Mesh { name = $"Block Item {blockId}" };
                Mesh.ApplyAndDisposeWritableMeshData(meshData, mesh);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                return mesh;
            }
            finally
            {
                padded.Dispose();
                vertices.Dispose();
            }
        }
    }
}
