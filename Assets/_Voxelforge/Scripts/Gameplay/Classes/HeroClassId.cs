namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Permanent class IDs (Roadmap 4.6). They're saved in player profiles and sent over the network, so never change or
    /// reuse one; each must match the ID on its <see cref="ClassDefinition"/> asset. Sent as a byte, so only add to the end.
    /// </summary>
    public enum HeroClassId : byte
    {
        /// <summary>No calling yet: every new hero, with the starter spells.</summary>
        Adventurer = 0,
        /// <summary>A holy knight of forge-runes: tough, shields partners, heals on hallowed ground.</summary>
        RuneforgedPaladin = 1,
        /// <summary>A mage of stone and earth: stone spears, shattered ground, pillars of rock.</summary>
        GeomancerMage = 2,
        /// <summary>A caller of the elements: totems that heal and strike, and walls of living earth.</summary>
        Shaman = 3,
    }
}
