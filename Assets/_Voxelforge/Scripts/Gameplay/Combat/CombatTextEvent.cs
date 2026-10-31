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
        /// <summary>The host said no to something in the bag (<see cref="CombatTextEvent.BagReason"/>, Roadmap 4.5).</summary>
        BagRefused,
        /// <summary><see cref="CombatTextEvent.Amount"/> of <see cref="CombatTextEvent.ItemId"/> went to a partner (Roadmap 4.5).</summary>
        Sent,
        /// <summary>A spell hurt a target (a Training Dummy, Roadmap 4.6) for <see cref="CombatTextEvent.Amount"/>.</summary>
        TargetHit,
        /// <summary>A spell's hit (<see cref="CombatTextEvent.Amount"/>) emptied a target: the dummy topples (Roadmap 4.6).</summary>
        Toppled,
        /// <summary>The hero lost <see cref="CombatTextEvent.Amount"/> health (Roadmap 4.6).</summary>
        Hurt,
        /// <summary>A hero was healed for <see cref="CombatTextEvent.Amount"/> (Roadmap 4.6).</summary>
        Healed,
        /// <summary>A hero got a shield of <see cref="CombatTextEvent.Amount"/> (Roadmap 4.6).</summary>
        Shielded,
        /// <summary>The hero's shield soaked up <see cref="CombatTextEvent.Amount"/> damage (Roadmap 4.6).</summary>
        Absorbed,
        /// <summary>A spell stunned a target (Roadmap 4.7, Hoofquake).</summary>
        Stunned,
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
        /// <summary>For <see cref="CombatTextKind.Pickup"/> and <see cref="CombatTextKind.Sent"/>.</summary>
        public ushort ItemId;
        /// <summary>For <see cref="CombatTextKind.Refused"/>.</summary>
        public CastCheck Reason;
        /// <summary>For <see cref="CombatTextKind.BagRefused"/>.</summary>
        public BagRefusal BagReason;
        /// <summary>Where in the world it starts floating.</summary>
        public float3 Position;
    }
}
