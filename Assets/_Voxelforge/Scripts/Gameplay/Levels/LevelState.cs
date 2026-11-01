using System;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A hero's level and the XP they've earned towards the next one (Roadmap 5.6, D-037). Sent as raw bytes and saved in
    /// the player's profile. <see cref="Level"/> is 1 for a new hero, never 0 once they're in the game.
    /// </summary>
    public struct LevelState : INetworkSerializeByMemcpy, IEquatable<LevelState>
    {
        public byte Level;
        public int Xp;

        public LevelState(int level, int xp)
        {
            Level = (byte)Math.Clamp(level, 1, byte.MaxValue);
            Xp = Math.Max(0, xp);
        }

        /// <summary>A brand new hero: level 1, no XP.</summary>
        public static LevelState Start => new LevelState(1, 0);

        /// <summary>Level 1 for a default (zeroed) state, e.g. an old save's.</summary>
        public int EffectiveLevel => Level == 0 ? 1 : Level;

        public bool Equals(LevelState other) => Level == other.Level && Xp == other.Xp;
        public override bool Equals(object obj) => obj is LevelState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Level, Xp);
        public override string ToString() => $"Level {EffectiveLevel}, {Xp} XP";
    }
}
