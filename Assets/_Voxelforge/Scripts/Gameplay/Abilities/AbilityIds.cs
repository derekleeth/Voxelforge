namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Permanent ability IDs (Roadmap 4.1). They're sent over the network (and later saved on action bars), so never change
    /// or reuse one. Each must match the ID on its <see cref="AbilityDefinition"/> asset.
    /// </summary>
    public static class AbilityIds
    {
        /// <summary>No ability.</summary>
        public const ushort None = 0;

        /// <summary>What Kian's M.G.R.S. shoots (Roadmap 3.6): an instant bolt that breaks blocks.</summary>
        public const ushort ManaBolt = 1;

        /// <summary>A slow, heavy test spell with a cast time, on the temporary Ability 1 button until 4.4.</summary>
        public const ushort ArcaneOrb = 2;

        /// <summary>A slow fire orb with a long cast that blasts a deep, round crater (Roadmap 4.3).</summary>
        public const ushort Pyroblast = 3;

        /// <summary>An instant slam on the ground you aim at that blasts a wide, shallow crater (Roadmap 4.3).</summary>
        public const ushort EarthShatter = 4;

        /// <summary>Runeforged Paladin (Roadmap 4.6): a glowing hammer thrown end over end.</summary>
        public const ushort RuneHammer = 5;

        /// <summary>Runeforged Paladin (Roadmap 4.6): a shield on the partner you aim at, or on yourself.</summary>
        public const ushort BulwarkRune = 6;

        /// <summary>Runeforged Paladin (Roadmap 4.6): a golden circle at your feet that heals heroes standing in it.</summary>
        public const ushort HallowedForge = 7;
    }
}
