using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// When each block of Earthen Rock crumbles (Roadmap 4.6). Plain C#, so it's tested without a scene;
    /// <see cref="EarthWorks"/> keeps one on the host and saves it as seconds left, so earth saved mid-way still crumbles
    /// on time after loading.
    /// </summary>
    public sealed class EarthTimers
    {
        readonly Dictionary<int3, float> _crumbleAt = new Dictionary<int3, float>();

        public int Count => _crumbleAt.Count;

        public bool Contains(int3 cell) => _crumbleAt.ContainsKey(cell);

        /// <summary>The earth at <paramref name="cell"/> crumbles at <paramref name="crumbleAt"/> (game seconds).</summary>
        public void Add(int3 cell, float crumbleAt) => _crumbleAt[cell] = crumbleAt;

        /// <summary>Forgets <paramref name="cell"/>, e.g. it was mined or crumbled.</summary>
        public bool Remove(int3 cell) => _crumbleAt.Remove(cell);

        /// <summary>Adds every cell whose time is up at <paramref name="now"/> to <paramref name="expired"/> (they stay until removed).</summary>
        public void CollectExpired(float now, List<int3> expired)
        {
            foreach (KeyValuePair<int3, float> timer in _crumbleAt)
            {
                if (now >= timer.Value)
                    expired.Add(timer.Key);
            }
        }

        /// <summary>Every cell and the seconds it has left at <paramref name="now"/> (never below 0), for saving.</summary>
        public Dictionary<int3, float> Snapshot(float now)
        {
            var saved = new Dictionary<int3, float>(_crumbleAt.Count);
            foreach (KeyValuePair<int3, float> timer in _crumbleAt)
                saved[timer.Key] = math.max(0f, timer.Value - now);
            return saved;
        }

        /// <summary>Replaces every timer with saved seconds-left, counting from <paramref name="now"/>.</summary>
        public void Restore(IReadOnlyDictionary<int3, float> secondsLeft, float now)
        {
            _crumbleAt.Clear();
            if (secondsLeft == null)
                return;
            foreach (KeyValuePair<int3, float> timer in secondsLeft)
                _crumbleAt[timer.Key] = now + math.max(0f, timer.Value);
        }
    }

    /// <summary>Where earth spells may raise rock (Roadmap 4.6). Plain C#, tested.</summary>
    public static class EarthRules
    {
        /// <summary>
        /// Whether a hero whose feet are in <paramref name="feet"/> can be lifted one block on a pillar: their feet's cell
        /// is empty air for the rock to fill, and the cell over their head (two up) is free to rise into.
        /// </summary>
        public static bool CanRise(VoxelWorld world, int3 feet) =>
            world.GetVoxel(feet).BlockId == BlockIds.Air && !VoxelBodyMotor.IsSolid(feet + new int3(0, 2, 0), world);
    }
}
