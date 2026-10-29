using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>One Mana Bolt in flight (Roadmap 3.6).</summary>
    public struct ManaBolt
    {
        /// <summary>Counts up per shooter, so every PC can match the end of a bolt to its start.</summary>
        public ushort Id;
        /// <summary>The ability that launched it (<see cref="AbilityIds"/>).</summary>
        public ushort AbilityId;
        public float3 Position;
        /// <summary>Normalized.</summary>
        public float3 Direction;
        public float Speed;
        /// <summary>Metres left before it fizzles.</summary>
        public float Remaining;
        /// <summary>Block damage on a hit, fixed when it leaves, so switching items mid-flight doesn't change it.</summary>
        public float Damage;
    }

    /// <summary>How a bolt's flight ended.</summary>
    public struct ManaBoltEnd
    {
        public ushort Id;
        /// <summary>True if it hit a block (<see cref="Hit"/>); false if it flew its whole range and fizzled.</summary>
        public bool HitBlock;
        public VoxelHit Hit;
        /// <summary>Where it ended: the hit point, or where it fizzled.</summary>
        public float3 Point;
        /// <summary>The face it hit, or minus its direction if it fizzled.</summary>
        public float3 Normal;
        public float Damage;
    }

    /// <summary>
    /// The host's Mana Bolts for one shooter (Roadmap 3.6). Plain C#, so it's tested without a scene. Each step sweeps
    /// a voxel ray over the distance the bolt moves (<see cref="VoxelQueries.Raycast"/>), so a fast bolt can't skip
    /// through a block between steps. It stops at the same blocks aiming does: solid ones and props such as torches.
    /// </summary>
    public sealed class ManaBoltFlight
    {
        readonly List<ManaBolt> _bolts = new List<ManaBolt>();
        ushort _nextId;

        public int Count => _bolts.Count;

        /// <summary>
        /// Launches a bolt of <paramref name="ability"/> from <paramref name="origin"/>. Cooldowns and mana are the
        /// caster's job (<see cref="AbilityCaster"/>), so this only refuses a missing ability or direction.
        /// </summary>
        public bool TryLaunch(float3 origin, float3 direction, AbilityDefinition ability, out ManaBolt bolt)
        {
            bolt = default;
            float length = math.length(direction);
            if (ability == null || ability.ProjectileSpeed <= 0f || ability.Range <= 0f || length < 1e-6f)
                return false;

            bolt = new ManaBolt
            {
                Id = _nextId++,
                AbilityId = ability.Id,
                Position = origin,
                Direction = direction / length,
                Speed = ability.ProjectileSpeed,
                Remaining = ability.Range,
                Damage = ability.BlockDamage,
            };
            _bolts.Add(bolt);
            return true;
        }

        /// <summary>Moves every bolt <paramref name="dt"/> seconds on, adding the ones that hit or fizzled to <paramref name="ended"/>.</summary>
        public void Step(VoxelWorld world, float dt, List<ManaBoltEnd> ended)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                ManaBolt bolt = _bolts[i];
                float move = math.min(bolt.Speed * dt, bolt.Remaining);
                if (world != null && VoxelQueries.Raycast(world, bolt.Position, bolt.Direction, move, out VoxelHit hit))
                {
                    ended.Add(new ManaBoltEnd
                        { Id = bolt.Id, HitBlock = true, Hit = hit, Point = hit.Point, Normal = (float3)hit.Normal, Damage = bolt.Damage });
                    _bolts.RemoveAt(i);
                    continue;
                }

                bolt.Position += bolt.Direction * move;
                bolt.Remaining -= move;
                if (bolt.Remaining <= 0f)
                {
                    ended.Add(new ManaBoltEnd { Id = bolt.Id, Point = bolt.Position, Normal = -bolt.Direction });
                    _bolts.RemoveAt(i);
                    continue;
                }
                _bolts[i] = bolt;
            }
        }

        /// <summary>Drops every bolt without ending it, when the shooter leaves.</summary>
        public void Clear() => _bolts.Clear();
    }
}
