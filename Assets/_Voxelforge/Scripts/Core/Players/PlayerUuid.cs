using System;

namespace Voxelforge.Core
{
    /// <summary>
    /// Who a player is <b>across sessions</b> (Roadmap 3.4, D-017). Their saved profile (position, block bag) is kept
    /// under it. Unlike <see cref="PlayerId"/>, which the host hands out in join order each session, this never changes:
    /// each PC remembers one per local slot, so Player 1 on a couch is always the same profile.
    /// <para>
    /// Sent as two <c>ulong</c>s, so it fits in an RPC without strings.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct PlayerUuid : IEquatable<PlayerUuid>
    {
        public readonly ulong High;
        public readonly ulong Low;

        public PlayerUuid(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public PlayerUuid(Guid guid)
        {
            byte[] bytes = guid.ToByteArray();
            High = BitConverter.ToUInt64(bytes, 0);
            Low = BitConverter.ToUInt64(bytes, 8);
        }

        /// <summary>A fresh, random identity for a new player.</summary>
        public static PlayerUuid NewUuid() => new PlayerUuid(Guid.NewGuid());

        public static PlayerUuid None => default;

        public bool IsValid => High != 0 || Low != 0;

        public Guid ToGuid()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(High).CopyTo(bytes, 0);
            BitConverter.GetBytes(Low).CopyTo(bytes, 8);
            return new Guid(bytes);
        }

        /// <summary>Reads the form <see cref="ToString"/> writes. False (and <see cref="None"/>) for anything else.</summary>
        public static bool TryParse(string text, out PlayerUuid uuid)
        {
            uuid = Guid.TryParse(text, out Guid guid) ? new PlayerUuid(guid) : None;
            return uuid.IsValid;
        }

        public bool Equals(PlayerUuid other) => High == other.High && Low == other.Low;

        public override bool Equals(object obj) => obj is PlayerUuid other && Equals(other);

        public override int GetHashCode() => (High ^ Low).GetHashCode();

        public static bool operator ==(PlayerUuid a, PlayerUuid b) => a.Equals(b);

        public static bool operator !=(PlayerUuid a, PlayerUuid b) => !a.Equals(b);

        public override string ToString() => ToGuid().ToString("D");
    }
}
