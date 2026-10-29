using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>What a line of floating combat text is about (Roadmap 4.4).</summary>
    public enum CombatTextKind : byte
    {
        /// <summary>A spell hit a block for <see cref="CombatTextEvent.Amount"/> damage.</summary>
        SpellHit,
        /// <summary>A spell blasted a crater that broke <see cref="CombatTextEvent.Amount"/> blocks.</summary>
        CraterHit,
        /// <summary>A spell went off and cost <see cref="CombatTextEvent.Amount"/> mana.</summary>
        ManaSpent,
        /// <summary>The host refused a cast (<see cref="CombatTextEvent.Reason"/>).</summary>
        Refused,
        /// <summary><see cref="CombatTextEvent.Amount"/> of <see cref="CombatTextEvent.ItemId"/> went into the bag.</summary>
        Pickup,
    }

    /// <summary>
    /// One thing worth a pop-up in a player's own view (Roadmap 4.4). Made on each PC from what the host already said
    /// (cues, refusals, pickups), so it's never sent anywhere and never changes the game.
    /// </summary>
    public struct CombatTextEvent
    {
        public CombatTextKind Kind;
        public int Amount;
        /// <summary>The spell, for <see cref="CombatTextKind.SpellHit"/>, <see cref="CombatTextKind.CraterHit"/> and <see cref="CombatTextKind.ManaSpent"/>.</summary>
        public ushort AbilityId;
        public AbilityElement Element;
        /// <summary>For <see cref="CombatTextKind.Pickup"/>.</summary>
        public ushort ItemId;
        /// <summary>For <see cref="CombatTextKind.Refused"/>.</summary>
        public CastCheck Reason;
        /// <summary>Where in the world it starts floating.</summary>
        public float3 Position;
    }
}
