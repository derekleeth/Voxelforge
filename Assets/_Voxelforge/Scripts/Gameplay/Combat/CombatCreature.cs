using System;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A creature spells can hurt (Roadmap 5.2): a networked body that walks the world, unlike a Training Dummy's block.
    /// It joins <see cref="CombatTargets"/> on every PC through <see cref="CombatCreatures"/>; the host hurts and stuns it
    /// there, and every PC reads where it stands so its health bar follows it.
    /// </summary>
    public interface ICombatCreature
    {
        /// <summary>Its <c>NetworkObjectId</c>, the same on every PC.</summary>
        ulong CreatureId { get; }

        /// <summary>Alive and in the world: spells can hit it.</summary>
        bool IsTargetable { get; }

        /// <summary>Where its feet are: the host's own body, or the synced transform elsewhere.</summary>
        float3 Feet { get; }

        /// <summary>Half its body's size on each axis, like <c>MotorSettings.HalfExtents</c>.</summary>
        float3 HalfExtents { get; }

        /// <summary>
        /// Host only: takes up to <paramref name="amount"/> damage in <paramref name="instigator"/>'s name, who gains its
        /// threat times <paramref name="threatScale"/> (Roadmap 5.3).
        /// </summary>
        TargetDamage ServerDamage(float amount, PlayerId instigator, float threatScale = 1f);

        /// <summary>Host only: stunned for <paramref name="seconds"/> in <paramref name="instigator"/>'s name.</summary>
        bool ServerStun(float seconds, PlayerId instigator, float threatScale = 1f);

        /// <summary>
        /// Host only: taunted by <paramref name="taunter"/> for <paramref name="seconds"/> (Roadmap 5.3). False if it can't
        /// be (defeated, or a creature that keeps no threat).
        /// </summary>
        bool ServerTaunt(float seconds, PlayerId taunter);

        /// <summary>Host only: who it's angry at (Roadmap 5.3), or null for a creature that doesn't keep a table.</summary>
        ThreatTable ServerThreat { get; }
    }

    /// <summary>
    /// Raised on every PC when a creature (<see cref="ICombatCreature"/>) comes into or leaves the world, so
    /// <see cref="CombatTargets"/> can list it without anyone finding it by name. Events, not a global list
    /// (CLAUDE.md Law #1), like <see cref="PlayerController.AnySpawned"/>.
    /// </summary>
    public static class CombatCreatures
    {
        public static event Action<ICombatCreature> AnySpawned;
        public static event Action<ICombatCreature> AnyDespawned;

        public static void RaiseSpawned(ICombatCreature creature) => AnySpawned?.Invoke(creature);
        public static void RaiseDespawned(ICombatCreature creature) => AnyDespawned?.Invoke(creature);
    }

    /// <summary>
    /// Which target something happened to, the same on every PC (Roadmap 5.2): a Training Dummy by its block, or a
    /// creature by its <see cref="ICombatCreature.CreatureId"/>. Sent in cues, so health bars find the same target again
    /// even after it has walked off.
    /// </summary>
    public readonly struct TargetKey : IEquatable<TargetKey>
    {
        const ulong CreatureBit = 1UL << 63;
        const ulong DummyBit = 1UL << 62;
        const int Bits = 20;
        const ulong Mask = (1UL << Bits) - 1;
        const int Offset = 1 << (Bits - 1);

        public readonly ulong Value;

        public TargetKey(ulong value) => Value = value;

        public static TargetKey None => default;

        /// <summary>The Training Dummy in <paramref name="block"/> (within ±524,288 blocks of the origin).</summary>
        public static TargetKey Dummy(int3 block) =>
            new TargetKey(DummyBit | Pack(block.x) | (Pack(block.y) << Bits) | (Pack(block.z) << (2 * Bits)));

        public static TargetKey Creature(ulong creatureId) => new TargetKey(CreatureBit | creatureId);

        public bool IsValid => Value != 0;

        public bool IsCreature => (Value & CreatureBit) != 0;

        public bool IsDummy => (Value & DummyBit) != 0 && !IsCreature;

        /// <summary>The creature's <see cref="ICombatCreature.CreatureId"/>, if <see cref="IsCreature"/>.</summary>
        public ulong CreatureId => Value & ~CreatureBit;

        static ulong Pack(int coordinate) => (ulong)(coordinate + Offset) & Mask;

        public bool Equals(TargetKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TargetKey other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(TargetKey a, TargetKey b) => a.Value == b.Value;
        public static bool operator !=(TargetKey a, TargetKey b) => a.Value != b.Value;
        public override string ToString() => IsCreature ? $"Creature {CreatureId}" : IsDummy ? $"Dummy {Value & ~DummyBit:X}" : "None";
    }
}
