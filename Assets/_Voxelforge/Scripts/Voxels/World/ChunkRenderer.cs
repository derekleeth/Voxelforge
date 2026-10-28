using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Draws one chunk. Lives on its own GameObject at the chunk's world origin and keeps a single
    /// <see cref="Mesh"/> that is overwritten every time the chunk is remeshed.
    /// <para>
    /// Pooled: when its chunk unloads, <see cref="ChunkMeshScheduler"/> deactivates it and later moves it to another
    /// chunk with <see cref="Reuse"/>, so streaming doesn't create and destroy GameObjects and meshes all the time.
    /// </para>
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
            var go = new GameObject();
            go.transform.SetParent(parent, false);

            var chunkRenderer = go.AddComponent<ChunkRenderer>();
            chunkRenderer._mesh = new Mesh();
            go.GetComponent<MeshFilter>().sharedMesh = chunkRenderer._mesh;
            chunkRenderer._renderer = go.GetComponent<MeshRenderer>();
            chunkRenderer._renderer.sharedMaterial = material;
            chunkRenderer.Reuse(coord);
            return chunkRenderer;
        }

        /// <summary>Moves this renderer to <paramref name="coord"/> with an empty mesh, and activates it.</summary>
        internal void Reuse(ChunkCoord coord)
        {
            Coord = coord;
            name = $"Chunk {coord.X},{coord.Y},{coord.Z}";
            _mesh.name = name;
            transform.localPosition = new Vector3(coord.WorldOrigin.x, coord.WorldOrigin.y, coord.WorldOrigin.z);
            Clear();
            gameObject.SetActive(true);
        }

        /// <summary>Empties and hides this renderer so it can wait in the pool.</summary>
        internal void Release()
        {
            Clear();
            gameObject.SetActive(false);
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
