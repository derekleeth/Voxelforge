using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Which pre-built structure a stamp is (Roadmap 5.4): permanent IDs, named later by map markers, loot and the 5.9 boss.
    /// Never change or reuse one; add new stamps to the end.
    /// </summary>
    public enum StampId : byte
    {
        None = 0,
        /// <summary>Meadows: a mossy tower stump with its top fallen beside it.</summary>
        BrokenWatchtower = 1,
        /// <summary>Snowy Peaks: a ring of cracked pillars round a rune slab.</summary>
        FrostShrine = 2,
        /// <summary>Thunderhoof Plains: red standing stones round a fire pit.</summary>
        StandingStones = 3,
        /// <summary>Meadows and Plains: the sunken boss arena (5.9's boss lives here).</summary>
        SunkenArena = 4,
    }

    /// <summary>What sort of place a stamp is. Each kind has its own placement grid (<see cref="StampPlacement"/>).</summary>
    public enum StampKind : byte
    {
        /// <summary>A small ruin: common, one candidate every <see cref="StampPlacement.RuinCell"/> blocks.</summary>
        Ruin,
        /// <summary>A boss arena: rare, one candidate every <see cref="StampPlacement.ArenaCell"/> blocks.</summary>
        Arena,
    }

    /// <summary>
    /// One pre-built structure the world generator stamps into the terrain (Roadmap 5.4): a box of blocks, how deep it
    /// sits, which biomes it's found in and how often. Built in code by <c>StampBuilder</c> (Voxelforge &gt; Build Stamps),
    /// so keep the shapes there, not here. The blocks are read by Burst jobs through a <see cref="StampTable"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Stamp", fileName = "NewStamp")]
    public sealed class StampDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID (StampId). Never change or reuse one.")]
        [SerializeField] StampId id;

        [SerializeField] string displayName;

        [SerializeField] StampKind kind;

        [Tooltip("Blocks across (x), up (y) and deep (z).")]
        [SerializeField] Vector3Int size;

        [Tooltip("Block IDs, x fastest, then z, then y (layer by layer, bottom up). StampBlocks.Keep leaves the terrain; Air carves it.")]
        [SerializeField, HideInInspector] ushort[] blocks;

        [Tooltip("How far below the ground its bottom layer sits. 1 = the bottom layer replaces the top block of ground.")]
        [SerializeField, Min(0)] int sink = 1;

        [Tooltip("Block that fills under its bottom layer down to the ground, so it never floats on a slope. 0 = none.")]
        [SerializeField] ushort foundation;

        [Tooltip("Biomes it's found in, matched against the start of the biome's name (\"Meadows\" matches \"Meadows (Elwynn style)\").")]
        [SerializeField] string[] biomes;

        [Tooltip("The chance a candidate site in one of its biomes gets it (0-1).")]
        [SerializeField, Range(0f, 1f)] float chance = 0.5f;

        [Tooltip("Turned a random quarter-turn at each site, so no two look alike.")]
        [SerializeField] bool rotate = true;

        public StampId Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public StampKind Kind => kind;
        public int3 Size => new int3(size.x, size.y, size.z);
        public IReadOnlyList<ushort> Blocks => blocks;
        public int Sink => sink;
        public ushort Foundation => foundation;
        public IReadOnlyList<string> Biomes => biomes;
        public float Chance => chance;
        public bool Rotate => rotate;

        /// <summary>Index into <see cref="Blocks"/> of local block <paramref name="p"/>.</summary>
        public static int IndexOf(int3 p, int3 size) => p.x + size.x * (p.z + size.z * p.y);

        /// <summary>Everything at once, for <c>StampBuilder</c> and tests.</summary>
        public void Assign(StampId stampId, string stampName, StampKind stampKind, int3 stampSize, ushort[] stampBlocks,
            int stampSink, ushort stampFoundation, string[] stampBiomes, float stampChance, bool stampRotate)
        {
            id = stampId;
            displayName = stampName;
            kind = stampKind;
            size = new Vector3Int(stampSize.x, stampSize.y, stampSize.z);
            blocks = stampBlocks;
            sink = math.max(0, stampSink);
            foundation = stampFoundation;
            biomes = stampBiomes;
            chance = math.saturate(stampChance);
            rotate = stampRotate;
        }

        /// <summary>Builds one in code, for tests.</summary>
        internal static StampDefinition Create(StampId id, StampKind kind, int3 size, ushort[] blocks, int sink = 1,
            ushort foundation = 0, string[] biomes = null, float chance = 1f, bool rotate = false)
        {
            var stamp = CreateInstance<StampDefinition>();
            stamp.name = id.ToString();
            stamp.Assign(id, id.ToString(), kind, size, blocks, sink, foundation, biomes ?? new string[0], chance, rotate);
            return stamp;
        }

        /// <summary>What's wrong with it, if anything.</summary>
        public void Validate(List<string> errors)
        {
            if (id == StampId.None)
                errors.Add($"Stamp '{name}' has no ID.");
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                errors.Add($"Stamp '{name}' has no size.");
            else if (blocks == null || blocks.Length != size.x * size.y * size.z)
                errors.Add($"Stamp '{name}' has {blocks?.Length ?? 0} blocks; its {size} needs {size.x * size.y * size.z}.");
            if (biomes == null || biomes.Length == 0)
                errors.Add($"Stamp '{name}' isn't found in any biome.");
        }
    }

    /// <summary>Special block values inside a stamp (Roadmap 5.4).</summary>
    public static class StampBlocks
    {
        /// <summary>Leave whatever the terrain has here (round a ruin's broken edges, say).</summary>
        public const ushort Keep = ushort.MaxValue;
    }
}
