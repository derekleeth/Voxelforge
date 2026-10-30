using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Earth raised by spells (Roadmap 4.6), on <b>the host</b>. Rising Pillar lifts its caster on a column of Earthen
    /// Rock one block at a time, and Earthen Wall raises a wall row by row where its caster aims; every block goes in through <see cref="VoxelWorldManager.RequestModifyVoxel"/> in the
    /// caster's name (CLAUDE.md Law #3), so it's logged, saved, remeshed and heard like any other block. Each block
    /// crumbles back to air after its spell's duration (in <see cref="PlayerId.World"/>'s name, since its caster may have
    /// left), and its timer is saved with the world (<see cref="WorldSaveManager"/>). Mined or broken early, it's simply
    /// forgotten; it drops nothing either way.
    /// <b>Not a singleton:</b> heroes reach it through <see cref="PlayerRegistry.Earth"/> (CLAUDE.md Law #1).
    /// </summary>
    public sealed class EarthWorks : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Seconds between checks for earth whose time is up.")]
        [SerializeField, Range(0.05f, 2f)] float crumbleCheck = 0.25f;

        // A pillar rising under its caster, one block per step.
        sealed class Pillar
        {
            public PlayerController Hero;
            public int StepsLeft;
            public float Step;
            public float NextAt;
            public float Lifetime;
        }

        // A wall rising one row per step.
        sealed class Wall
        {
            public PlayerController Caster;
            public PlayerId CasterId;
            public readonly List<int3> Cells = new List<int3>();
            public int Width;
            public int NextCell;
            public float Step;
            public float NextAt;
            public float Lifetime;
        }

        readonly EarthTimers _timers = new EarthTimers();
        readonly List<Pillar> _pillars = new List<Pillar>();
        readonly List<Wall> _walls = new List<Wall>();
        readonly List<int3> _expired = new List<int3>();
        float _nextCrumbleCheck;

        bool IsServer => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        /// <summary>Host only: how many blocks of earth are waiting to crumble.</summary>
        public int Count => _timers.Count;

        void OnEnable()
        {
            if (world != null)
                world.EditApplied += OnEdit;
        }

        void OnDisable()
        {
            if (world != null)
                world.EditApplied -= OnEdit;
        }

        /// <summary>
        /// Host only: <paramref name="ability"/> (a <see cref="AbilityDelivery.Pillar"/>) starts lifting
        /// <paramref name="hero"/>. The first block rises at once; it stops early when something is over their head.
        /// </summary>
        public void ServerRaisePillar(PlayerController hero, AbilityDefinition ability)
        {
            if (!IsServer || hero == null || ability == null || ability.EarthHeight <= 0)
                return;
            _pillars.Add(new Pillar
            {
                Hero = hero, StepsLeft = ability.EarthHeight, Step = ability.EarthStep, NextAt = Time.time,
                Lifetime = ability.Duration,
            });
        }

        /// <summary>
        /// Host only: <paramref name="ability"/> (a <see cref="AbilityDelivery.Wall"/>) raises a wall standing on
        /// <paramref name="anchor"/>, across <paramref name="aim"/>, one row per step. Cells that are full, or that a hero
        /// stands in, are left as they are.
        /// </summary>
        public void ServerRaiseWall(PlayerController caster, AbilityDefinition ability, int3 anchor, float3 aim)
        {
            if (!IsServer || caster == null || ability == null || ability.EarthHeight <= 0 || ability.EarthWidth <= 0)
                return;
            var wall = new Wall
            {
                Caster = caster, CasterId = caster.PlayerId, Width = ability.EarthWidth, Step = ability.EarthStep,
                NextAt = Time.time, Lifetime = ability.Duration,
            };
            EarthRules.WallCells(anchor, aim, ability.EarthWidth, ability.EarthHeight, wall.Cells);
            _walls.Add(wall);
        }

        /// <summary>Host only, at load (Roadmap 3.4): every saved block of earth and the seconds it had left.</summary>
        public void ServerRestore(IReadOnlyDictionary<int3, float> secondsLeft) => _timers.Restore(secondsLeft, Time.time);

        /// <summary>Host only: every block of earth and the seconds it has left, for saving.</summary>
        public Dictionary<int3, float> ServerSnapshot() => _timers.Snapshot(Time.time);

        void FixedUpdate()
        {
            if (!IsServer || world == null || world.World == null)
                return;
            float now = Time.time;
            for (int i = _pillars.Count - 1; i >= 0; i--)
            {
                Pillar pillar = _pillars[i];
                while (pillar.StepsLeft > 0 && now >= pillar.NextAt)
                {
                    pillar.NextAt += pillar.Step;
                    pillar.StepsLeft = Rise(pillar) ? pillar.StepsLeft - 1 : 0;
                }
                if (pillar.StepsLeft <= 0)
                    _pillars.RemoveAt(i);
            }
            for (int i = _walls.Count - 1; i >= 0; i--)
            {
                Wall wall = _walls[i];
                while (wall.NextCell < wall.Cells.Count && now >= wall.NextAt)
                {
                    wall.NextAt += wall.Step;
                    RaiseRow(wall);
                }
                if (wall.NextCell >= wall.Cells.Count)
                    _walls.RemoveAt(i);
            }

            if (_timers.Count > 0 && now >= _nextCrumbleCheck)
            {
                _nextCrumbleCheck = now + crumbleCheck;
                Crumble(now);
            }
        }

        /// <summary>Lifts the hero one block and fills the cell under them with Earthen Rock. False if it can't.</summary>
        bool Rise(Pillar pillar)
        {
            PlayerController hero = pillar.Hero;
            if (hero == null || !hero.IsSpawned || !hero.TryGetSaveState(out float3 feet, out float yaw))
                return false;
            var cell = (int3)math.floor(feet + new float3(0f, 0.01f, 0f));
            VoxelWorld blocks = world.World;
            if (!EarthRules.CanRise(blocks, cell) || SomeoneElseIn(cell, hero, hero))
                return false;
            // Up first, so the rock never fills the cell the hero is standing in.
            hero.ServerTeleport(new float3(feet.x, cell.y + 1f, feet.z), yaw);
            return Place(cell, hero.PlayerId, pillar.Lifetime);
        }

        void RaiseRow(Wall wall)
        {
            VoxelWorld blocks = world.World;
            int end = math.min(wall.NextCell + wall.Width, wall.Cells.Count);
            for (; wall.NextCell < end; wall.NextCell++)
            {
                int3 cell = wall.Cells[wall.NextCell];
                if (blocks.GetVoxel(cell).BlockId == BlockIds.Air && !SomeoneElseIn(cell, wall.Caster, null))
                    Place(cell, wall.CasterId, wall.Lifetime);
            }
        }

        /// <summary>A hero other than <paramref name="except"/> stands in <paramref name="cell"/>. <paramref name="from"/> says whose registry to ask.</summary>
        static bool SomeoneElseIn(int3 cell, PlayerController from, PlayerController except)
        {
            PlayerRegistry players = from != null ? from.Registry : null;
            if (players == null)
                return false;
            foreach (PlayerController other in players.Players)
            {
                if (other != null && other != except && other.Bounds.Overlaps(cell))
                    return true;
            }
            return false;
        }

        bool Place(int3 cell, PlayerId caster, float lifetime)
        {
            if (world.RequestModifyVoxel(cell, BlockIds.EarthenRock, caster) != VoxelEditResult.Applied)
                return false;
            _timers.Add(cell, Time.time + lifetime);
            return true;
        }

        void Crumble(float now)
        {
            _expired.Clear();
            _timers.CollectExpired(now, _expired);
            VoxelWorld blocks = world.World;
            foreach (int3 cell in _expired)
            {
                if (blocks.GetVoxel(cell).BlockId != BlockIds.EarthenRock)
                {
                    _timers.Remove(cell);
                    continue;
                }
                // A chunk that isn't loaded keeps its earth until a hero comes near again.
                if (world.RequestModifyVoxel(cell, BlockIds.Air, PlayerId.World) == VoxelEditResult.Applied)
                    _timers.Remove(cell);
            }
        }

        // Earth broken some other way (mined, blasted) is forgotten.
        void OnEdit(VoxelEdit edit)
        {
            if (edit.Before.BlockId == BlockIds.EarthenRock && edit.After.BlockId != BlockIds.EarthenRock)
                _timers.Remove(edit.Coord);
        }
    }
}
