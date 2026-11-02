namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The professions: gathering (Roadmap 5.7, D-038), then crafting (5.8). Saved and sent as a byte, so never renumber
    /// one. Salvaging is Skinning done to slimes and constructs: the mob says which word to show.
    /// </summary>
    public enum ProfessionId : byte
    {
        Mining = 0,
        Herbalism = 1,
        Skinning = 2,
        Alchemy = 3,
        Blacksmithing = 4,
        Leatherworking = 5,
    }

    /// <summary>What a <see cref="GatherNode"/> is gathered from (Roadmap 5.7).</summary>
    public enum GatherNodeKind : byte
    {
        /// <summary>An ore block, by block ID. Mined with Mine.</summary>
        Ore = 0,
        /// <summary>A herb in the foliage layer, by the herb's item ID. Picked with Interact.</summary>
        Herb = 1,
        /// <summary>A defeated mob's body, by <see cref="MobId"/>. Skinned (or salvaged) with Interact.</summary>
        Mob = 2,
    }

    /// <summary>
    /// How a node compares with a gatherer's skill (Roadmap 5.7), WoW-style: the colour of its name and how likely
    /// gathering it is to raise the skill (<see cref="ProfessionRules.GainChance"/>).
    /// </summary>
    public enum GatherDifficulty : byte
    {
        /// <summary>The skill is below the node's: it can't be gathered yet.</summary>
        TooHard = 0,
        /// <summary>Just unlocked: every gather raises the skill.</summary>
        Orange = 1,
        /// <summary>Usually raises the skill.</summary>
        Yellow = 2,
        /// <summary>Now and then raises the skill.</summary>
        Green = 3,
        /// <summary>Too easy to teach anything more.</summary>
        Grey = 4,
    }
}
