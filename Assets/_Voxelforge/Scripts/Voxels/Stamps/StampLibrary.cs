using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Every stamp the world generator may use (Roadmap 5.4). <see cref="VoxelWorldManager"/> turns it into a
    /// <see cref="StampTable"/> for the chunk jobs. Built by <c>StampBuilder</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Stamp Library", fileName = "StampLibrary")]
    public sealed class StampLibrary : ScriptableObject
    {
        [SerializeField] StampDefinition[] stamps = new StampDefinition[0];

        public IReadOnlyList<StampDefinition> Stamps => stamps;

        /// <summary>The stamp with <paramref name="id"/>, if there is one.</summary>
        public bool TryGet(StampId id, out StampDefinition stamp)
        {
            foreach (StampDefinition candidate in stamps)
            {
                if (candidate != null && candidate.Id == id)
                {
                    stamp = candidate;
                    return true;
                }
            }
            stamp = null;
            return false;
        }

        /// <summary>
        /// The table the chunk jobs read: every valid stamp, with its biomes turned into a mask over
        /// <paramref name="biomeNames"/> (in the terrain settings' order). The caller owns and disposes it.
        /// </summary>
        public StampTable CreateTable(IReadOnlyList<string> biomeNames, Allocator allocator, int2 keepClearCenter = default,
            int keepClearRadius = 0)
        {
            var valid = new List<StampDefinition>();
            var errors = new List<string>();
            foreach (StampDefinition stamp in stamps)
            {
                if (stamp == null)
                    continue;
                errors.Clear();
                stamp.Validate(errors);
                if (errors.Count == 0)
                    valid.Add(stamp);
                else
                    Debug.LogError($"[Voxelforge] {string.Join(" ", errors)} It's left out of the world.", stamp);
            }
            return StampTable.Create(valid, biomeNames, allocator, keepClearCenter, keepClearRadius);
        }

        /// <summary>Builds a library in code, for tests and tools.</summary>
        internal static StampLibrary Create(params StampDefinition[] stamps)
        {
            var library = CreateInstance<StampLibrary>();
            library.stamps = stamps;
            return library;
        }

        /// <summary>For <c>StampBuilder</c>.</summary>
        public void Assign(StampDefinition[] all) => stamps = all ?? new StampDefinition[0];
    }

    /// <summary>One stamp as the chunk jobs see it (Roadmap 5.4).</summary>
    public struct StampInfo
    {
        public StampId Id;
        public StampKind Kind;
        public int3 Size;
        public int Sink;
        public ushort Foundation;
        public bool Rotate;
        /// <summary>Bit i set: found in biome i.</summary>
        public uint BiomeMask;
        public float Chance;
        /// <summary>Where its blocks start in <see cref="StampTable.Blocks"/>.</summary>
        public int Offset;
    }

    /// <summary>
    /// Every stamp, flattened for Burst (Roadmap 5.4): their <see cref="Infos"/> and one long list of their blocks.
    /// Read-only once made; the <see cref="ChunkGenerationScheduler"/> owns the world's and disposes it.
    /// Also carries the one spot no stamp may come near: where new players arrive.
    /// </summary>
    public struct StampTable : IDisposable
    {
        [ReadOnly] public NativeArray<StampInfo> Infos;
        [ReadOnly] public NativeArray<ushort> Blocks;

        /// <summary>No stamp's centre comes within <see cref="KeepClearRadius"/> blocks of this column (the world spawn).</summary>
        public int2 KeepClearCenter;
        public int KeepClearRadius;

        public bool IsCreated => Infos.IsCreated && Blocks.IsCreated;

        public int Count => Infos.IsCreated ? Infos.Length : 0;

        /// <summary>No stamps at all: the world is all natural terrain, as before Roadmap 5.4.</summary>
        public static StampTable CreateEmpty(Allocator allocator) => new StampTable
        {
            Infos = new NativeArray<StampInfo>(0, allocator),
            Blocks = new NativeArray<ushort>(0, allocator),
        };

        public static StampTable Create(IReadOnlyList<StampDefinition> stamps, IReadOnlyList<string> biomeNames,
            Allocator allocator, int2 keepClearCenter = default, int keepClearRadius = 0)
        {
            int total = 0;
            foreach (StampDefinition stamp in stamps)
                total += stamp.Blocks.Count;
            var table = new StampTable
            {
                Infos = new NativeArray<StampInfo>(stamps.Count, allocator),
                Blocks = new NativeArray<ushort>(total, allocator),
                KeepClearCenter = keepClearCenter,
                KeepClearRadius = keepClearRadius,
            };
            int offset = 0;
            for (int i = 0; i < stamps.Count; i++)
            {
                StampDefinition stamp = stamps[i];
                table.Infos[i] = new StampInfo
                {
                    Id = stamp.Id, Kind = stamp.Kind, Size = stamp.Size, Sink = stamp.Sink, Foundation = stamp.Foundation,
                    Rotate = stamp.Rotate, BiomeMask = BiomeMask(stamp.Biomes, biomeNames), Chance = stamp.Chance,
                    Offset = offset,
                };
                for (int b = 0; b < stamp.Blocks.Count; b++)
                    table.Blocks[offset + b] = stamp.Blocks[b];
                offset += stamp.Blocks.Count;
            }
            return table;
        }

        /// <summary>Bit i set for each biome whose name starts with one of <paramref name="wanted"/>.</summary>
        public static uint BiomeMask(IReadOnlyList<string> wanted, IReadOnlyList<string> biomeNames)
        {
            uint mask = 0;
            if (wanted == null || biomeNames == null)
                return mask;
            for (int i = 0; i < biomeNames.Count && i < 32; i++)
            {
                foreach (string name in wanted)
                {
                    if (!string.IsNullOrEmpty(name) && biomeNames[i] != null &&
                        biomeNames[i].StartsWith(name, StringComparison.OrdinalIgnoreCase))
                        mask |= 1u << i;
                }
            }
            return mask;
        }

        public void Dispose()
        {
            if (Infos.IsCreated)
                Infos.Dispose();
            if (Blocks.IsCreated)
                Blocks.Dispose();
        }
    }
}
