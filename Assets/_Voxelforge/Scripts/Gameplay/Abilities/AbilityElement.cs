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
        /// <summary>
        /// A shield on the partner nearest the aim, or on the caster if no partner is in range (Roadmap 4.6, Bulwark Rune;
        /// <see cref="PlayerSupport"/>).
        /// </summary>
        Shield,
        /// <summary>
        /// A circle on the ground at the caster's feet that pulses while it lasts (Roadmap 4.6, Hallowed Forge;
        /// <see cref="AbilityZone"/>).
        /// </summary>
        Zone,
        /// <summary>
        /// A column of Earthen Rock rises under the caster one block at a time, lifting them (Roadmap 4.6, Rising Pillar;
        /// <see cref="EarthWorks"/>).
        /// </summary>
        Pillar,
        /// <summary>
        /// A wall of Earthen Rock rises row by row on the block the caster aims at, across their aim (Roadmap 4.6,
        /// Earthen Wall; <see cref="EarthWorks"/>).
        /// </summary>
        Wall,
        /// <summary>
        /// A totem stands on the block the caster aims at and pulses while it lasts, as long as its block stands
        /// (Roadmap 4.6, Shaman totems; <see cref="AbilityZone"/>). One of each per caster.
        /// </summary>
        Totem,
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
