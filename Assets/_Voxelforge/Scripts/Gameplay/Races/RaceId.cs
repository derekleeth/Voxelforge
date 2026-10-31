namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Permanent race IDs (Roadmap 4.7). They're saved in player profiles and sent over the network, so never change or
    /// reuse one; each must match the ID on its <see cref="RaceDefinition"/> asset. Sent as a byte, so only add to the end.
    /// </summary>
    public enum RaceId : byte
    {
        /// <summary>Hardy travelling folk of the roads and hearths: the hooded hero from Roadmap 2.5.</summary>
        Wayfarer = 0,
        /// <summary>Big, gentle bull-folk of the windswept plains, with a deep shaman tradition.</summary>
        Thunderhoof = 1,
    }
}
