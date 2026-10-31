using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a world remembers about one player between sessions (Roadmap 3.4, D-017): where they stood, which way they
    /// faced, their block bag, their Home Totem (3.8), their class (4.6), their race (4.7) and the map cells they've
    /// explored (4.8). Kept under their
    /// <see cref="PlayerUuid"/>, so it survives players joining in a different order.
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

        /// <summary>The bag's slots in order, including empty ones, so everything stays where the player put it.</summary>
        public List<ItemStack> Bag = new List<ItemStack>();

        /// <summary>The bags they wear (Roadmap 4.5), which give the extra slots in <see cref="Bag"/>.</summary>
        public EquippedBags Bags;

        /// <summary>True if they've placed a Home Totem (Roadmap 3.8) at <see cref="HomeBlock"/>.</summary>
        public bool HasHome;

        /// <summary>Where their Home Totem stands. Only meaningful with <see cref="HasHome"/>.</summary>
        public int3 HomeBlock;

        /// <summary>Their class (Roadmap 4.6). Saves from before 4.6 load as Adventurers.</summary>
        public HeroClassId Class;

        /// <summary>Their race (Roadmap 4.7). Only final once <see cref="RaceChosen"/>.</summary>
        public RaceId Race;

        /// <summary>
        /// They've chosen their race. Saves from before 4.7 load without, so those heroes get to choose once on their next
        /// join.
        /// </summary>
        public bool RaceChosen;

        /// <summary>The map cells they've explored (Roadmap 4.8, <see cref="ExploredMap"/>). Saves from before 4.8 have none.</summary>
        public HashSet<int2> Explored = new HashSet<int2>();

        public PlayerProfile Clone() => new PlayerProfile
        {
            Uuid = Uuid,
            Name = Name,
            Feet = Feet,
            Yaw = Yaw,
            Bag = new List<ItemStack>(Bag),
            Bags = Bags,
            HasHome = HasHome,
            HomeBlock = HomeBlock,
            Class = Class,
            Race = Race,
            RaceChosen = RaceChosen,
            Explored = new HashSet<int2>(Explored),
        };
    }
}
