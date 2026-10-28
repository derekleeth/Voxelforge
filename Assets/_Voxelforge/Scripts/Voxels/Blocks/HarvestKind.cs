namespace Voxelforge.Voxels
{
    /// <summary>
    /// Which kind of tool mines a block fastest (Roadmap 3.5). A tool multiplies mining power for the kinds it's good at;
    /// bare hands mine everything at the base rate.
    /// </summary>
    public enum HarvestKind : byte
    {
        /// <summary>Soft blocks (dirt, sand, snow): no tool needed, every tool's "any" power applies.</summary>
        Any = 0,
        /// <summary>Stone, ore and rock: pickaxes.</summary>
        Pick = 1,
        /// <summary>Wood (trees, logs, workbenches): axes.</summary>
        Axe = 2,
    }
}
