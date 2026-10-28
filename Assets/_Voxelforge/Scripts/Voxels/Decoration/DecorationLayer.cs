using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The decorations of every loaded chunk (Roadmap 2.6). <see cref="ChunkMeshScheduler"/> runs a
    /// <see cref="DecorateChunkJob"/> next to each mesh job and hands the result to <see cref="SetChunk"/>; unloading a
    /// chunk calls <see cref="RemoveChunk"/>. So decorations stream with the terrain (2.4) and are rebuilt whenever
    /// their chunk's blocks change.
    /// <para>
    /// Instances are kept per decoration type in one array each, where every chunk owns a range of slots
    /// (<see cref="InstanceRangeAllocator"/>). Empty slots have scale 0. <see cref="DecorationRenderer"/> copies the
    /// changed parts of these arrays to the GPU. Plain C# with no GPU work, so tests can use it.
    /// </para>
    /// </summary>
    public sealed class DecorationLayer : IDisposable
    {
        const int InitialCapacity = 1024;

        /// <summary>All instances of one decoration type, and which slots changed since the last upload.</summary>
        public sealed class TypeData
        {
            public DecorationDefinition Definition;
            public InstanceRangeAllocator Allocator;
            public NativeArray<DecorationInstance> Instances;
            /// <summary>Changed slots [start, end) since the renderer last uploaded. Empty when start >= end.</summary>
            public int DirtyStart = int.MaxValue;
            public int DirtyEnd;
            /// <summary>Set when <see cref="Instances"/> grew, so the GPU buffer must be rebuilt.</summary>
            public bool Resized = true;

            internal void MarkDirty(int start, int count)
            {
                DirtyStart = math.min(DirtyStart, start);
                DirtyEnd = math.max(DirtyEnd, start + count);
            }

            public void ClearDirty()
            {
                DirtyStart = int.MaxValue;
                DirtyEnd = 0;
                Resized = false;
            }
        }

        readonly WorldSeed _seed;
        readonly TerrainParams _terrain;
        readonly NativeArray<BiomeInfo> _biomes; // Borrowed from the generator.
        NativeArray<DecorationInfo> _infos;
        NativeArray<float> _biomeWeights;
        readonly TypeData[] _types;
        readonly Dictionary<ChunkCoord, int2[]> _chunks = new Dictionary<ChunkCoord, int2[]>(); // Per type: (start, count).
        readonly Stack<int2[]> _rangePool = new Stack<int2[]>();
        readonly Stack<NativeList<DecorationInstance>> _listPool = new Stack<NativeList<DecorationInstance>>();
        readonly int[] _typeCounts;
        bool _isDisposed;

        /// <param name="biomes">Read, not owned: the generator's biome list, which must outlive this layer.</param>
        public DecorationLayer(DecorationSet set, TerrainSettings terrainSettings, BlockRegistry registry,
            WorldSeed seed, TerrainParams terrain, NativeArray<BiomeInfo> biomes)
        {
            if (set == null)
                throw new ArgumentNullException(nameof(set));
            _seed = seed;
            _terrain = terrain;
            _biomes = biomes;
            set.CreateLookups(terrainSettings, registry, Allocator.Persistent, out _infos, out _biomeWeights);

            _types = new TypeData[set.Decorations.Count];
            for (int i = 0; i < _types.Length; i++)
            {
                _types[i] = new TypeData
                {
                    Definition = set.Decorations[i],
                    Allocator = new InstanceRangeAllocator(InitialCapacity),
                    Instances = new NativeArray<DecorationInstance>(InitialCapacity, Allocator.Persistent),
                };
            }
            _typeCounts = new int[_types.Length];
        }

        public IReadOnlyList<TypeData> Types => _types;

        /// <summary>Chunks that have at least one decoration.</summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>Decorations placed in every loaded chunk.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>
        /// Schedules a <see cref="DecorateChunkJob"/> for <paramref name="coord"/> after <paramref name="dependsOn"/>
        /// (the gather job filling <paramref name="padded"/>). Pass <paramref name="output"/> to <see cref="SetChunk"/>
        /// or <see cref="Discard"/> once the job is done.
        /// </summary>
        public JobHandle Schedule(ChunkCoord coord, NativeArray<ushort> padded, NativeArray<BlockInfo> blocks, JobHandle dependsOn,
            out NativeList<DecorationInstance> output)
        {
            output = _listPool.Count > 0 ? _listPool.Pop() : new NativeList<DecorationInstance>(256, Allocator.Persistent);
            return new DecorateChunkJob
            {
                Padded = padded,
                Blocks = blocks,
                Origin = coord.WorldOrigin,
                Seed = _seed,
                Terrain = _terrain,
                Biomes = _biomes,
                Decorations = _infos,
                BiomeWeights = _biomeWeights,
                Output = output,
            }.Schedule(dependsOn);
        }

        /// <summary>Replaces a chunk's decorations with a finished job's output, and takes the list back.</summary>
        public void SetChunk(ChunkCoord coord, NativeList<DecorationInstance> instances)
        {
            RemoveChunk(coord);
            if (instances.Length > 0)
                AddChunk(coord, instances);
            Discard(instances);
        }

        /// <summary>Takes back a job's output without using it (e.g. the result was stale).</summary>
        public void Discard(NativeList<DecorationInstance> instances)
        {
            instances.Clear();
            _listPool.Push(instances);
        }

        /// <summary>Removes every decoration of an unloaded or emptied chunk. Does nothing if it has none.</summary>
        public void RemoveChunk(ChunkCoord coord)
        {
            if (!_chunks.Remove(coord, out int2[] ranges))
                return;

            for (int type = 0; type < _types.Length; type++)
            {
                int2 range = ranges[type];
                if (range.y == 0)
                    continue;
                TypeData data = _types[type];
                for (int i = range.x; i < range.x + range.y; i++)
                    data.Instances[i] = default; // Scale 0 = empty slot.
                data.Allocator.Free(range.x, range.y);
                data.MarkDirty(range.x, range.y);
                InstanceCount -= range.y;
            }
            _rangePool.Push(ranges);
        }

        /// <summary>How many decorations a chunk has. For tests and the debug overlay.</summary>
        public int CountIn(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out int2[] ranges))
                return 0;
            int count = 0;
            foreach (int2 range in ranges)
                count += range.y;
            return count;
        }

        void AddChunk(ChunkCoord coord, NativeList<DecorationInstance> instances)
        {
            Array.Clear(_typeCounts, 0, _typeCounts.Length);
            for (int i = 0; i < instances.Length; i++)
                _typeCounts[instances[i].Type]++;

            int2[] ranges = _rangePool.Count > 0 ? _rangePool.Pop() : new int2[_types.Length];
            for (int type = 0; type < _types.Length; type++)
            {
                int count = _typeCounts[type];
                ranges[type] = new int2(count > 0 ? Reserve(_types[type], count) : 0, count);
                _typeCounts[type] = 0; // Reused below as the write cursor.
            }

            for (int i = 0; i < instances.Length; i++)
            {
                DecorationInstance instance = instances[i];
                int type = (int)instance.Type;
                _types[type].Instances[ranges[type].x + _typeCounts[type]++] = instance;
            }

            for (int type = 0; type < _types.Length; type++)
            {
                if (ranges[type].y > 0)
                    _types[type].MarkDirty(ranges[type].x, ranges[type].y);
            }
            _chunks.Add(coord, ranges);
            InstanceCount += instances.Length;
        }

        static int Reserve(TypeData data, int count)
        {
            int start = data.Allocator.Allocate(count, out bool grew);
            if (grew)
            {
                var bigger = new NativeArray<DecorationInstance>(data.Allocator.Capacity, Allocator.Persistent);
                NativeArray<DecorationInstance>.Copy(data.Instances, bigger, data.Instances.Length);
                data.Instances.Dispose();
                data.Instances = bigger;
                data.Resized = true;
            }
            return start;
        }

        /// <summary>Frees all memory. Jobs from <see cref="Schedule"/> must be finished first.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            foreach (TypeData data in _types)
                data.Instances.Dispose();
            while (_listPool.Count > 0)
                _listPool.Pop().Dispose();
            _infos.Dispose();
            _biomeWeights.Dispose();
            _chunks.Clear();
        }
    }
}
