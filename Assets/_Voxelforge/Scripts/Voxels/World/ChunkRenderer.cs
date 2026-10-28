using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Draws one chunk. Lives on its own GameObject at the chunk's world origin and keeps a single
    /// <see cref="Mesh"/> that is overwritten every time the chunk is remeshed.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ChunkRenderer : MonoBehaviour
    {
        const MeshUpdateFlags ApplyFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        Mesh _mesh;
        MeshRenderer _renderer;

        public ChunkCoord Coord { get; private set; }

        /// <summary>The current mesh. Empty (0 vertices) when the chunk has nothing visible.</summary>
        public Mesh Mesh => _mesh;

        internal static ChunkRenderer Create(ChunkCoord coord, Transform parent, Material material)
        {
            var go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(coord.WorldOrigin.x, coord.WorldOrigin.y, coord.WorldOrigin.z);

            var chunkRenderer = go.AddComponent<ChunkRenderer>();
            chunkRenderer.Coord = coord;
            chunkRenderer._mesh = new Mesh { name = go.name };
            go.GetComponent<MeshFilter>().sharedMesh = chunkRenderer._mesh;
            chunkRenderer._renderer = go.GetComponent<MeshRenderer>();
            chunkRenderer._renderer.sharedMaterial = material;
            chunkRenderer._renderer.enabled = false;
            return chunkRenderer;
        }

        /// <summary>Replaces the mesh with a finished job's output and disposes <paramref name="data"/>.</summary>
        internal void ApplyMesh(Mesh.MeshDataArray data)
        {
            Mesh.ApplyAndDisposeWritableMeshData(data, _mesh, ApplyFlags);
            _mesh.bounds = _mesh.GetSubMesh(0).bounds;
            _renderer.enabled = _mesh.vertexCount > 0;
        }

        internal void Clear()
        {
            _mesh.Clear();
            _renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }
    }
}
