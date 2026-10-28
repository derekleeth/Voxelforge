using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a world remembers about one player between sessions (Roadmap 3.4, D-017): where they stood, which way they
    /// faced, and their block bag. Kept under their <see cref="PlayerUuid"/>, so it survives players joining in a
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
        public List<BlockStack> Bag = new List<BlockStack>();

        public PlayerProfile Clone() => new PlayerProfile
        {
            Uuid = Uuid,
            Name = Name,
            Feet = Feet,
            Yaw = Yaw,
            Bag = new List<BlockStack>(Bag),
        };
    }
}
