using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Draws one chunk. Lives on its own GameObject at the chunk's world origin and keeps a single
    /// <see cref="Mesh"/> that is overwritten every time the chunk is remeshed.
    /// <para>
    /// Pooled: when its chunk unloads, <see cref="ChunkMeshScheduler"/> deactivates it and later moves it to another
    /// chunk with <see cref="Reuse"/>, so streaming doesn't create and destroy GameObjects all the time.
    /// </para>
    /// <para>
    /// Each reuse gets a <b>new</b> <see cref="Mesh"/>, though. URP's GPU Resident Drawer doesn't see a mesh change while
    /// its renderer is off, so a refilled old mesh kept the old chunk's bounds for GPU occlusion culling, and whole
    /// chunks of ground stayed hidden after a reload until the renderer was toggled (found in 4.8 play-testing).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ChunkRenderer : MonoBehaviour
    {
        const MeshUpdateFlags ApplyFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;

        public ChunkCoord Coord { get; private set; }

        /// <summary>The current mesh. Empty (0 vertices) when the chunk has nothing visible.</summary>
        public Mesh Mesh => _mesh;

        internal static ChunkRenderer Create(ChunkCoord coord, Transform parent, Material material)
        {
            var go = new GameObject();
            go.transform.SetParent(parent, false);

            var chunkRenderer = go.AddComponent<ChunkRenderer>();
            chunkRenderer._filter = go.GetComponent<MeshFilter>();
            chunkRenderer._renderer = go.GetComponent<MeshRenderer>();
            chunkRenderer._renderer.sharedMaterial = material;
            chunkRenderer.Reuse(coord);
            return chunkRenderer;
        }

        /// <summary>Moves this renderer to <paramref name="coord"/> with a new, empty mesh, and activates it.</summary>
        internal void Reuse(ChunkCoord coord)
        {
            Coord = coord;
            name = $"Chunk {coord.X},{coord.Y},{coord.Z}";
            DestroyMesh();
            _mesh = new Mesh { name = name };
            _filter.sharedMesh = _mesh;
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

        void DestroyMesh()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }

        void OnDestroy() => DestroyMesh();
    }
}
