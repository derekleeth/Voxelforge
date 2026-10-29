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
    }
}
