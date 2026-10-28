using System;

namespace Voxelforge.Core
{
    /// <summary>
    /// Who a player is in this session, handed out by the host when the player joins. This is the
    /// <c>InstigatorPlayerID</c> from CLAUDE.md Law #1: voxel edits, damage and the delta log all record it.
    /// <para>
    /// It is <b>not</b> the network connection. One connection (a couch) can bring several players, so the
    /// host's player registry keeps track of which connection and local slot each <see cref="PlayerId"/> belongs to.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        /// <summary>1 and up for real players. 0 is <see cref="None"/>.</summary>
        public readonly byte Value;

        public PlayerId(byte value) => Value = value;

        /// <summary>No player, e.g. a block the world generator placed.</summary>
        public static PlayerId None => default;

        public bool IsValid => Value != 0;

        public bool Equals(PlayerId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);

        public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);

        public override string ToString() => IsValid ? $"P{Value}" : "None";
    }
}
