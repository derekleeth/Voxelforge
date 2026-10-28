using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>How far one block has been mined.</summary>
    public struct BlockDamage
    {
        /// <summary>Mining damage so far. The block breaks when it reaches <see cref="BlockInfo.MaxDurability"/>.</summary>
        public float Damage;
        /// <summary>When the block was last hit, in game seconds. Blocks left alone heal (<see cref="BlockDamageMap.HealIdle"/>).</summary>
        public float LastHitTime;
    }

    /// <summary>
    /// In-progress mining damage, by world block position (Roadmap 3.2, D-003). Sparse: only blocks someone is mining
    /// right now are in it, and blocks nobody has hit for a few seconds heal and leave, so it stays tiny. Damage from
    /// several players adds up. <b>Host only</b>; it isn't saved or logged, because a half-mined block is still the
    /// same block.
    /// </summary>
    public sealed class BlockDamageMap
    {
        readonly Dictionary<int3, BlockDamage> _damage = new Dictionary<int3, BlockDamage>();
        readonly List<int3> _healed = new List<int3>();

        public int Count => _damage.Count;

        /// <summary>Every damaged block, e.g. for drawing cracks.</summary>
        public IReadOnlyDictionary<int3, BlockDamage> Entries => _damage;

        /// <summary>Adds <paramref name="amount"/> damage to the block at <paramref name="coord"/> and returns its new total.</summary>
        public float Add(int3 coord, float amount, float time)
        {
            _damage.TryGetValue(coord, out BlockDamage damage);
            damage.Damage += amount;
            damage.LastHitTime = time;
            _damage[coord] = damage;
            return damage.Damage;
        }

        public bool TryGet(int3 coord, out BlockDamage damage) => _damage.TryGetValue(coord, out damage);

        /// <summary>Forgets a block's damage, e.g. because it broke or changed.</summary>
        public bool Remove(int3 coord) => _damage.Remove(coord);

        /// <summary>Fully heals every block nobody has hit for <paramref name="delay"/> seconds. Returns how many healed.</summary>
        public int HealIdle(float time, float delay)
        {
            _healed.Clear();
            foreach (var pair in _damage)
            {
                if (time - pair.Value.LastHitTime >= delay)
                    _healed.Add(pair.Key);
            }
            foreach (int3 coord in _healed)
                _damage.Remove(coord);
            return _healed.Count;
        }

        public void Clear() => _damage.Clear();
    }
}
