namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which kind of creature a mob is (Roadmap 5.2): permanent IDs sent over the network and, from 5.5, named by loot
    /// tables. Never change or reuse one; add new kinds to the end.
    /// </summary>
    public enum MobId : ushort
    {
        None = 0,
        /// <summary>Meadows: a tusked boar that charges (5.2 step B).</summary>
        BristlebackBoar = 1,
        /// <summary>Snowy Peaks: a pack hunter (5.2 step C).</summary>
        FrostfangWolf = 2,
        /// <summary>Caves: a glowing slime that splits (5.2 step C).</summary>
        CrystalSlime = 3,
        /// <summary>Thunderhoof Plains: a neutral grazing herd beast (5.2 step C).</summary>
        ThunderRhino = 4,
    }
}
