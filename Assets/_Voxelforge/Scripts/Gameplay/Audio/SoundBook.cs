using System;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Which <see cref="SurfaceSound"/> one block uses.</summary>
    [Serializable]
    public struct BlockSound
    {
        public BlockDefinition block;
        public SurfaceSound surface;
    }

    /// <summary>A sound that loops beside every placed copy of a prop, e.g. a torch's crackle.</summary>
    [Serializable]
    public struct AmbientLoop
    {
        public DecorationDefinition decoration;
        public AudioClip clip;
        [Range(0f, 2f)] public float volume;
        [Tooltip("Silent beyond this many metres from every player.")]
        [Min(1f)] public float maxDistance;
    }

    /// <summary>
    /// Every sound the game plays (Roadmap 3.9), in one asset: a <see cref="SurfaceSound"/> per block, the one-off
    /// sounds, and the loops props make. Spells keep theirs in their <see cref="AbilityLook"/> (4.2). Kept out of <see cref="BlockDefinition"/> so the voxel engine knows nothing
    /// about audio. Built by <c>SfxBuilder</c> (Voxelforge &gt; Build Sound Effects).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Sound Book", fileName = "SoundBook")]
    public sealed class SoundBook : ScriptableObject
    {
        [Tooltip("For blocks that aren't listed below.")]
        [SerializeField] SurfaceSound defaultSurface;

        [SerializeField] BlockSound[] blocks = Array.Empty<BlockSound>();

        [Header("One-off sounds")]
        [SerializeField] AudioClip jump;
        [Tooltip("A drop flying into a bag.")]
        [SerializeField] AudioClip pickupPop;
        [Tooltip("A chopped tree falling.")]
        [SerializeField] AudioClip treeCreak;
        [SerializeField] AudioClip leafRustle;

        [Header("Loops")]
        [SerializeField] AmbientLoop[] loops = Array.Empty<AmbientLoop>();

        [NonSerialized] SurfaceSound[] _byId;

        public SurfaceSound DefaultSurface => defaultSurface;
        public AudioClip Jump => jump;
        public AudioClip PickupPop => pickupPop;
        public AudioClip TreeCreak => treeCreak;
        public AudioClip LeafRustle => leafRustle;
        public AmbientLoop[] Loops => loops;

        /// <summary>How the block with this ID sounds. Never null if there's a default surface.</summary>
        public SurfaceSound SurfaceFor(ushort blockId)
        {
            if (_byId == null)
                BuildLookup();
            SurfaceSound surface = blockId < _byId.Length ? _byId[blockId] : null;
            return surface != null ? surface : defaultSurface;
        }

        void OnValidate() => _byId = null;

        void BuildLookup()
        {
            int length = 0;
            foreach (BlockSound entry in blocks)
            {
                if (entry.block != null)
                    length = Math.Max(length, entry.block.Id + 1);
            }
            _byId = new SurfaceSound[length];
            foreach (BlockSound entry in blocks)
            {
                if (entry.block != null)
                    _byId[entry.block.Id] = entry.surface;
            }
        }

        /// <summary>Builds a book in code, for tests.</summary>
        internal static SoundBook Create(SurfaceSound defaultSurface, params BlockSound[] blocks)
        {
            var book = CreateInstance<SoundBook>();
            book.defaultSurface = defaultSurface;
            book.blocks = blocks;
            return book;
        }
    }
}
