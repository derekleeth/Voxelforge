using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>What happened, in an <see cref="AbilityCue"/>.</summary>
    public enum AbilityCueKind : byte
    {
        /// <summary>A cast with a cast time started (at <see cref="AbilityCue.Position"/>, the hero's feet).</summary>
        CastBegan,
        /// <summary>A cast stopped before it went off (<see cref="AbilityCue.Reason"/>).</summary>
        CastStopped,
        /// <summary>An ability went off: instantly, or at the end of its cast.</summary>
        Released,
        /// <summary>A projectile left the hero's eyes (<see cref="AbilityCue.Position"/>) along <see cref="AbilityCue.Direction"/>.</summary>
        Launched,
        /// <summary>A projectile hit <see cref="AbilityCue.BlockId"/> at <see cref="AbilityCue.Position"/>, on the face <see cref="AbilityCue.Direction"/>.</summary>
        Impact,
        /// <summary>A projectile flew its whole range and fizzled at <see cref="AbilityCue.Position"/>.</summary>
        Fizzled,
    }

    /// <summary>
    /// One thing an ability did, sent by the host to every PC (Roadmap 4.2). The host only runs the rules
    /// (<see cref="AbilityCaster"/>, <see cref="ManaBoltFlight"/>, <see cref="AbilityImpact"/>) and says what happened
    /// with a cue; each PC turns the cue into effects and sounds from the ability's <see cref="AbilityLook"/>. A cue
    /// never changes the game, so a PC that draws nothing (a headless host) plays the same game.
    /// </summary>
    public struct AbilityCue : INetworkSerializable
    {
        public AbilityCueKind Kind;
        /// <summary><see cref="AbilityIds"/>.</summary>
        public ushort AbilityId;
        /// <summary>Pairs a projectile's <see cref="AbilityCueKind.Launched"/> with how it ended. 0 for a ground slam's impact.</summary>
        public ushort ShotId;
        public Vector3 Position;
        /// <summary>Which way a projectile left, or the face it hit (minus its direction if it fizzled).</summary>
        public Vector3 Direction;
        /// <summary>The block a projectile hit, as it was before the hit, to tint the burst.</summary>
        public ushort BlockId;
        /// <summary>How many blocks an <see cref="AbilityCueKind.Impact"/> broke, for the combat text (Roadmap 4.4).</summary>
        public ushort BlocksBroken;
        /// <summary>How much damage an <see cref="AbilityCueKind.Impact"/> did to targets (Training Dummies, Roadmap 4.6), for the combat text.</summary>
        public ushort TargetDamage;
        /// <summary>Why a cast stopped (<see cref="AbilityCueKind.CastStopped"/>).</summary>
        public CastCheck Reason;

        public static AbilityCue Cast(AbilityCueKind kind, ushort abilityId, Vector3 feet, CastCheck reason = CastCheck.Ok) =>
            new AbilityCue { Kind = kind, AbilityId = abilityId, Position = feet, Reason = reason };

        public static AbilityCue Launch(in ManaBolt bolt) =>
            new AbilityCue
            {
                Kind = AbilityCueKind.Launched, AbilityId = bolt.AbilityId, ShotId = bolt.Id, Position = bolt.Position,
                Direction = bolt.Direction,
            };

        /// <summary>A <see cref="AbilityDelivery.GroundSlam"/> hit <paramref name="blockId"/> at <paramref name="point"/>, on the face <paramref name="normal"/>.</summary>
        public static AbilityCue Blast(ushort abilityId, Vector3 point, Vector3 normal, ushort blockId, int blocksBroken = 0,
            float targetDamage = 0f) =>
            new AbilityCue
            {
                Kind = AbilityCueKind.Impact, AbilityId = abilityId, Position = point, Direction = normal, BlockId = blockId,
                BlocksBroken = ClampCount(blocksBroken), TargetDamage = ClampCount(Mathf.RoundToInt(targetDamage)),
            };

        public static AbilityCue End(in ManaBoltEnd end, ushort blockId, int blocksBroken = 0, float targetDamage = 0f) =>
            new AbilityCue
            {
                Kind = end.HitBlock ? AbilityCueKind.Impact : AbilityCueKind.Fizzled, AbilityId = end.AbilityId, ShotId = end.Id,
                Position = end.Point, Direction = end.Normal, BlockId = blockId, BlocksBroken = ClampCount(blocksBroken),
                TargetDamage = ClampCount(Mathf.RoundToInt(targetDamage)),
            };

        static ushort ClampCount(int count) => (ushort)Mathf.Clamp(count, 0, ushort.MaxValue);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref AbilityId);
            serializer.SerializeValue(ref ShotId);
            serializer.SerializeValue(ref Position);
            // Only what each kind uses goes over the wire.
            switch (Kind)
            {
                case AbilityCueKind.CastStopped:
                    serializer.SerializeValue(ref Reason);
                    break;
                case AbilityCueKind.Launched:
                case AbilityCueKind.Fizzled:
                    serializer.SerializeValue(ref Direction);
                    break;
                case AbilityCueKind.Impact:
                    serializer.SerializeValue(ref Direction);
                    serializer.SerializeValue(ref BlockId);
                    serializer.SerializeValue(ref BlocksBroken);
                    serializer.SerializeValue(ref TargetDamage);
                    break;
            }
        }
    }
}
