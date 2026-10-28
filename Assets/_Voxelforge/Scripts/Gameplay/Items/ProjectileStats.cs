using System;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a ranged item shoots (Roadmap 3.6): Kian's M.G.R.S. fires Mana Bolts that break blocks from afar. The host
    /// moves each bolt (<see cref="PlayerStaff"/>), so these numbers are only ever trusted on the host. In Phase 4 they
    /// move into the 4.1 ability templates (mana cost, damage to creatures).
    /// </summary>
    [Serializable]
    public struct ProjectileStats
    {
        [Tooltip("Block damage per hit, like mining damage (Stone has 60 durability). 0 = not a ranged item.")]
        [Min(0f)] public float damage;

        [Tooltip("Seconds between shots. Holding fire shoots this often.")]
        [Min(0f)] public float cooldown;

        [Tooltip("How fast a bolt flies, in metres per second.")]
        [Min(0f)] public float speed;

        [Tooltip("How far a bolt flies before it fizzles out, in metres.")]
        [Min(0f)] public float range;

        [Tooltip("Where the bolt leaves the model, in the model's own space (the top of the barrel). Looks only.")]
        public Vector3 muzzle;

        [Tooltip("Colour of the bolt, its light, the muzzle flash and the burst.")]
        public Color glow;

        /// <summary>True if it shoots anything.</summary>
        public bool IsRanged => damage > 0f;
    }
}
