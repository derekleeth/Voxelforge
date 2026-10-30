using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>What happened to a hero's health, in a <see cref="HealthEvent"/>. Sent as a byte, so only add to the end.</summary>
    public enum HealthEventKind : byte
    {
        /// <summary>They lost <see cref="HealthEvent.Amount"/> health.</summary>
        Damaged,
        /// <summary>They got <see cref="HealthEvent.Amount"/> health back from someone's spell (not slow regeneration).</summary>
        Healed,
        /// <summary>They got a shield that soaks up <see cref="HealthEvent.Amount"/> damage.</summary>
        Shielded,
        /// <summary>Their shield soaked up <see cref="HealthEvent.Amount"/> damage and they lost nothing.</summary>
        Absorbed,
        /// <summary>
        /// Something was thrown at them from <see cref="HealthEvent.SourcePosition"/> (a dummy's hay bale) and lands in
        /// <see cref="HealthEvent.Amount"/> seconds. Looks only: the hit comes later as <see cref="Damaged"/>.
        /// </summary>
        Incoming,
    }

    /// <summary>
    /// One thing that happened to a hero's health (Roadmap 4.6), sent by the host to every PC by
    /// <see cref="PlayerHealth"/> for combat text and looks. It never changes the game: the health itself is already in
    /// <see cref="PlayerHealth"/>'s network variables.
    /// </summary>
    public struct HealthEvent : INetworkSerializable
    {
        public HealthEventKind Kind;
        public float Amount;
        /// <summary>The player whose spell did it (<see cref="Voxelforge.Core.PlayerId"/>), or 0 for anything else (a Training Dummy).</summary>
        public byte Source;
        /// <summary>The spell that did it (<see cref="AbilityIds"/>), or 0.</summary>
        public ushort AbilityId;
        /// <summary>Where it came from, e.g. the dummy that threw the hay bale.</summary>
        public Vector3 SourcePosition;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Amount);
            serializer.SerializeValue(ref Source);
            serializer.SerializeValue(ref AbilityId);
            serializer.SerializeValue(ref SourcePosition);
        }
    }
}
