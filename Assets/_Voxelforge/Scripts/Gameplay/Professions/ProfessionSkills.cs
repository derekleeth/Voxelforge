using System;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A hero's skill in each gathering profession (Roadmap 5.7, D-038), 1 for a new hero. Sent as raw bytes and saved in
    /// the player's profile. A zeroed skill (e.g. from an old save) counts as 1.
    /// </summary>
    public struct ProfessionSkills : INetworkSerializeByMemcpy, IEquatable<ProfessionSkills>
    {
        /// <summary>How many professions there are (<see cref="ProfessionId"/> 0 to Count - 1).</summary>
        public const int Count = 3;

        public byte Mining;
        public byte Herbalism;
        public byte Skinning;

        /// <summary>A brand new hero: 1 in everything.</summary>
        public static ProfessionSkills Start => new ProfessionSkills { Mining = 1, Herbalism = 1, Skinning = 1 };

        /// <summary>The skill in <paramref name="profession"/>, 1 or more.</summary>
        public int Get(ProfessionId profession)
        {
            byte raw = profession switch
            {
                ProfessionId.Mining => Mining,
                ProfessionId.Herbalism => Herbalism,
                ProfessionId.Skinning => Skinning,
                _ => 1,
            };
            return raw == 0 ? 1 : raw;
        }

        /// <summary>A copy with <paramref name="profession"/> set to <paramref name="skill"/> (kept within 1-255).</summary>
        public ProfessionSkills With(ProfessionId profession, int skill)
        {
            byte value = (byte)Math.Clamp(skill, 1, byte.MaxValue);
            ProfessionSkills copy = this;
            switch (profession)
            {
                case ProfessionId.Mining: copy.Mining = value; break;
                case ProfessionId.Herbalism: copy.Herbalism = value; break;
                case ProfessionId.Skinning: copy.Skinning = value; break;
            }
            return copy;
        }

        public bool Equals(ProfessionSkills other) =>
            Get(ProfessionId.Mining) == other.Get(ProfessionId.Mining) &&
            Get(ProfessionId.Herbalism) == other.Get(ProfessionId.Herbalism) &&
            Get(ProfessionId.Skinning) == other.Get(ProfessionId.Skinning);

        public override bool Equals(object obj) => obj is ProfessionSkills other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Get(ProfessionId.Mining), Get(ProfessionId.Herbalism), Get(ProfessionId.Skinning));

        public override string ToString() =>
            $"Mining {Get(ProfessionId.Mining)}, Herbalism {Get(ProfessionId.Herbalism)}, Skinning {Get(ProfessionId.Skinning)}";
    }
}
