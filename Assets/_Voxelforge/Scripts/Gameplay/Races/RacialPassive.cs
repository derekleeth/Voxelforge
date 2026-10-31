namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a race is always good at (Roadmap 4.7). Each kind is read by the system it changes, with the race's
    /// <see cref="RaceDefinition.PassiveValue"/>. Saved nowhere, so new ones can go anywhere, but keep the order tidy.
    /// </summary>
    public enum RacialPassive : byte
    {
        None = 0,
        /// <summary>The Home hearth cast takes <see cref="RaceDefinition.PassiveValue"/> of its usual time (<see cref="PlayerHearth"/>).</summary>
        Hearthbound = 1,
        /// <summary>
        /// Herbs gathered give <see cref="RaceDefinition.PassiveValue"/> more (0.5 = half as many again). Herbalism comes in
        /// Roadmap 5.5, which reads it; until then it's only shown.
        /// </summary>
        HerbSense = 2,
    }
}
