using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a world remembers about one player between sessions (Roadmap 3.4, D-017): where they stood, which way they
    /// faced, their block bag, and their Home Totem (3.8). Kept under their <see cref="PlayerUuid"/>, so it survives players joining in a
    /// different order. Phase 4 adds class, race and stats here.
    /// </summary>
    public sealed class PlayerProfile
    {
        public PlayerUuid Uuid;

        /// <summary>A readable name for logs and the overlay, e.g. "Player 1".</summary>
        public string Name = "";

        /// <summary>Where their feet were.</summary>
        public float3 Feet;

        /// <summary>The way they faced, in degrees.</summary>
        public float Yaw;

        /// <summary>The bag's slots in order, including empty ones, so the block picker order stays the same.</summary>
        public List<ItemStack> Bag = new List<ItemStack>();

        /// <summary>True if they've placed a Home Totem (Roadmap 3.8) at <see cref="HomeBlock"/>.</summary>
        public bool HasHome;

        /// <summary>Where their Home Totem stands. Only meaningful with <see cref="HasHome"/>.</summary>
        public int3 HomeBlock;

        public PlayerProfile Clone() => new PlayerProfile
        {
            Uuid = Uuid,
            Name = Name,
            Feet = Feet,
            Yaw = Yaw,
            Bag = new List<ItemStack>(Bag),
            HasHome = HasHome,
            HomeBlock = HomeBlock,
        };
    }
}
