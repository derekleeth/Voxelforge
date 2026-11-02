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

        [Tooltip("Earthen Rock crumbling back to air when its spell's time is up (Roadmap 4.6).")]
        [SerializeField] AudioClip earthCrumble;

        [Tooltip("A hero's shield soaking up a hit (Roadmap 4.6, Bulwark Rune).")]
        [SerializeField] AudioClip shieldBlock;

        [Tooltip("A Training Dummy's hay sack landing on a hero (Roadmap 4.6).")]
        [SerializeField] AudioClip hayThwack;

        [Tooltip("Air rushing past a sprinting hero (Roadmap 4.8). Loops, and gets louder the faster they run.")]
        [SerializeField] AudioClip sprintWind;

        [Tooltip("Paper rustling open as a hero unfolds their World Map (Roadmap 4.8).")]
        [SerializeField] AudioClip mapOpen;

        [Tooltip("The World Map folding shut (Roadmap 4.8).")]
        [SerializeField] AudioClip mapClose;

        [Tooltip("A Rare loot sack going into the bag (Roadmap 5.5). Common loot uses the pickup pop.")]
        [SerializeField] AudioClip lootRare;

        [SerializeField] AudioClip lootEpic;

        [Tooltip("A Legendary: a little fanfare.")]
        [SerializeField] AudioClip lootLegendary;

        [Tooltip("A hero putting on or taking off a piece of gear (Roadmap 5.5).")]
        [SerializeField] AudioClip gearEquip;

        [Tooltip("A hero reaching a new level (Roadmap 5.6).")]
        [SerializeField] AudioClip levelUp;

        [Tooltip("Roadmap 5.7: picking a herb, on every PC.")]
        [SerializeField] AudioClip herbPick;

        [Tooltip("Roadmap 5.7: skinning or salvaging a body, on every PC.")]
        [SerializeField] AudioClip skinSlice;

        [Tooltip("Roadmap 5.7: a profession going up a point, for the gatherer.")]
        [SerializeField] AudioClip skillUp;

        [Header("Crafting (Roadmap 5.8), on every PC")]
        [SerializeField] AudioClip craftWorkbench;
        [SerializeField] AudioClip craftAlchemy;
        [SerializeField] AudioClip craftForge;
        [SerializeField] AudioClip craftAnvil;
        [SerializeField] AudioClip craftTanning;

        [Header("Loops")]
        [SerializeField] AmbientLoop[] loops = Array.Empty<AmbientLoop>();

        [NonSerialized] SurfaceSound[] _byId;

        public SurfaceSound DefaultSurface => defaultSurface;
        public AudioClip Jump => jump;
        public AudioClip PickupPop => pickupPop;
        public AudioClip TreeCreak => treeCreak;
        public AudioClip LeafRustle => leafRustle;
        public AudioClip EarthCrumble => earthCrumble;
        public AudioClip ShieldBlock => shieldBlock;
        public AudioClip HayThwack => hayThwack;
        public AudioClip SprintWind => sprintWind;
        public AudioClip MapOpen => mapOpen;
        public AudioClip MapClose => mapClose;
        public AudioClip GearEquip => gearEquip;
        public AudioClip LevelUp => levelUp;
        public AudioClip HerbPick => herbPick;
        public AudioClip SkinSlice => skinSlice;
        public AudioClip SkillUp => skillUp;

        /// <summary>The sound of something being made at <paramref name="station"/> (Roadmap 5.8).</summary>
        public AudioClip CraftSound(CraftStation station) => station switch
        {
            CraftStation.AlchemyTable => craftAlchemy,
            CraftStation.Forge => craftForge,
            CraftStation.Anvil => craftAnvil,
            CraftStation.TanningRack => craftTanning,
            _ => craftWorkbench,
        };

        /// <summary>The sound of loot of <paramref name="rarity"/> going into a bag (Roadmap 5.5): the plain pop for Common.</summary>
        public AudioClip LootPickup(ItemRarity rarity)
        {
            AudioClip clip = rarity switch
            {
                ItemRarity.Legendary => lootLegendary,
                ItemRarity.Epic => lootEpic,
                ItemRarity.Rare => lootRare,
                _ => null,
            };
            return clip != null ? clip : pickupPop;
        }
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
