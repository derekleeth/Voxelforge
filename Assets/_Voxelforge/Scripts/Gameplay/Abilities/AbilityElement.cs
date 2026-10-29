namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What kind of magic (or not) an ability is (Roadmap 4.1). Resistances, talents and class flavour key off it later
    /// (4.6, 5.x). Sent as a byte, so only add to the end.
    /// </summary>
    public enum AbilityElement : byte
    {
        Physical,
        Arcane,
        Fire,
        Frost,
        Earth,
        Nature,
        Lightning,
        Holy,
    }

    /// <summary>How an ability reaches what it hits. Sent as a byte, so only add to the end.</summary>
    public enum AbilityDelivery : byte
    {
        /// <summary>A bolt flies from the hero's eyes along their aim (<see cref="PlayerStaff"/>).</summary>
        Projectile,
        /// <summary>
        /// Goes off at once on the block the hero aims at, within range, with nothing flying (Roadmap 4.3, EarthShatter).
        /// </summary>
        GroundSlam,
    }

    /// <summary>Why an ability can't be cast right now, or why a cast stopped before it went off.</summary>
    public enum CastCheck : byte
    {
        Ok,
        UnknownAbility,
        /// <summary>Already casting something.</summary>
        Busy,
        OnCooldown,
        NotEnoughMana,
        /// <summary>The hero moved or jumped during a cast that needs them to stand still.</summary>
        Interrupted,
        /// <summary>A <see cref="AbilityDelivery.GroundSlam"/> with no block in range to slam.</summary>
        NoTarget,
    }
}
