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

    /// <summary>How an ability reaches what it hits. Only <see cref="Projectile"/> exists so far; 4.3 and 4.6 add more.</summary>
    public enum AbilityDelivery : byte
    {
        /// <summary>A bolt flies from the hero's eyes along their aim (<see cref="PlayerStaff"/>).</summary>
        Projectile,
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
    }
}
