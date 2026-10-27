using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// A 32x32x32 block of the world.
    /// <para>
    /// <b>Uniform storage:</b> a new chunk holds a single <see cref="Voxel"/> (e.g. all Air or all Stone)
    /// and allocates no array. The 128 KB array is allocated only when a different block is written.
    /// Use <see cref="Compact"/> to collapse a chunk that became uniform again.
    /// </para>
    /// <para>
    /// <b>Edits are internal:</b> only the voxel assembly (the world manager) may change blocks.
    /// Gameplay code must go through <c>VoxelWorldManager.RequestModifyVoxel</c> (Roadmap 3.1).
    /// </para>
    /// Not thread-safe: mutate on the main thread only, and don't edit while a job is reading
    /// <see cref="GetReadOnlyVoxels"/>.
    /// </summary>
    public sealed class VoxelChunk : IDisposable
    {
        NativeArray<Voxel> _voxels;
        Voxel _uniformVoxel;
        bool _isDisposed;

        public VoxelChunk(ChunkCoord coord, Voxel fill = default)
        {
            Coord = coord;
            _uniformVoxel = fill;
        }

        public ChunkCoord Coord { get; }

        /// <summary>Goes up by one on every change. Meshing and networking compare it to spot stale data.</summary>
        public int Version { get; private set; }

        /// <summary>True when every block in the chunk is <see cref="UniformVoxel"/> and no array is allocated.</summary>
        public bool IsUniform => !_voxels.IsCreated;

        /// <summary>The block filling the whole chunk. Only meaningful while <see cref="IsUniform"/>.</summary>
        public Voxel UniformVoxel => _uniformVoxel;

        public bool IsDisposed => _isDisposed;

        public Voxel GetVoxel(int3 local)
        {
            CheckLocal(local);
            return GetVoxel(VoxelMath.ToIndex(local));
        }

        public Voxel GetVoxel(int index)
        {
            CheckNotDisposed();
            CheckIndex(index);
            return IsUniform ? _uniformVoxel : _voxels[index];
        }

        /// <summary>
        /// Read-only view of every block, for jobs. Throws for uniform chunks, which have no array.
        /// Check <see cref="IsUniform"/> first and use <see cref="UniformVoxel"/> instead.
        /// </summary>
        public NativeArray<Voxel>.ReadOnly GetReadOnlyVoxels()
        {
            CheckNotDisposed();
            if (IsUniform)
                throw new InvalidOperationException($"{Coord} is uniform and has no voxel array. Check IsUniform first.");
            return _voxels.AsReadOnly();
        }

        /// <summary>Sets one block. Returns false (and leaves <see cref="Version"/> alone) if nothing changed.</summary>
        internal bool SetVoxel(int3 local, Voxel voxel)
        {
            CheckLocal(local);
            return SetVoxel(VoxelMath.ToIndex(local), voxel);
        }

        internal bool SetVoxel(int index, Voxel voxel)
        {
            CheckNotDisposed();
            CheckIndex(index);

            if (IsUniform)
            {
                if (voxel == _uniformVoxel)
                    return false;
                Expand();
            }
            else if (_voxels[index] == voxel)
            {
                return false;
            }

            _voxels[index] = voxel;
            Version++;
            return true;
        }

        /// <summary>Replaces every block with one voxel and frees the array.</summary>
        internal void Fill(Voxel voxel)
        {
            CheckNotDisposed();
            if (IsUniform && voxel == _uniformVoxel)
                return;

            if (_voxels.IsCreated)
                _voxels.Dispose();
            _uniformVoxel = voxel;
            Version++;
        }

        /// <summary>
        /// Frees the array if every block is the same. Returns true if the chunk is uniform afterwards.
        /// Doesn't change <see cref="Version"/>, because the blocks themselves don't change.
        /// </summary>
        internal bool Compact()
        {
            CheckNotDisposed();
            if (IsUniform)
                return true;

            Voxel first = _voxels[0];
            for (int i = 1; i < ChunkVolume; i++)
            {
                if (_voxels[i] != first)
                    return false;
            }

            _voxels.Dispose();
            _uniformVoxel = first;
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            if (_voxels.IsCreated)
                _voxels.Dispose();
            _isDisposed = true;
        }

        void Expand()
        {
            _voxels = new NativeArray<Voxel>(ChunkVolume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < ChunkVolume; i++)
                _voxels[i] = _uniformVoxel;
        }

        void CheckNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(Coord.ToString());
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckLocal(int3 local)
        {
            if (!VoxelMath.IsInsideChunk(local))
                throw new ArgumentOutOfRangeException(nameof(local), local, $"Local coordinates must be 0-{ChunkMask}.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckIndex(int index)
        {
            if ((uint)index >= ChunkVolume)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be 0-{ChunkVolume - 1}.");
        }
    }
}
