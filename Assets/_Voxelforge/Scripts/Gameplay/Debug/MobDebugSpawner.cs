using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Play-test keys for mobs (Roadmap 5.2), host only. <b>F10</b> drops a mob some way from every hero, cycling through
    /// the kinds in the <see cref="MobBook"/> on each press; <b>Shift+F10</b> sends every mob it dropped away. The biome
    /// spawner (step D) puts mobs in the world for real.
    /// </summary>
    public sealed class MobDebugSpawner : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [SerializeField] PlayerRegistry players;

        [Tooltip("Data/Mobs/MobBook.asset: every kind of mob.")]
        [SerializeField] MobBook mobs;

        [Tooltip("How far from its hero a mob lands, in blocks.")]
        [SerializeField, Range(4, 32)] int spawnDistance = 14;

        [Tooltip("Most mobs at once.")]
        [SerializeField, Range(1, 64)] int maxMobs = 24;

        readonly List<Mob> _spawned = new List<Mob>();
        int _next;

        bool IsServer => networkManager != null && networkManager.IsServer;

        /// <summary>Host only: mobs it dropped that are still about.</summary>
        public int Count => _spawned.Count;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !IsServer || world == null || players == null || mobs == null)
                return;
            _spawned.RemoveAll(mob => mob == null || !mob.IsSpawned);
            if (!keyboard.f10Key.wasPressedThisFrame)
                return;
            if (keyboard.shiftKey.isPressed)
                DespawnAll();
            else
                SpawnNextForEveryHero();
        }

        /// <summary>Host only: drops the next kind of mob near every hero.</summary>
        public void SpawnNextForEveryHero()
        {
            if (mobs.Mobs.Count == 0)
                return;
            MobDefinition kind = null;
            for (int tries = 0; tries < mobs.Mobs.Count && (kind == null || kind.Prefab == null); tries++)
                kind = mobs.Mobs[_next++ % mobs.Mobs.Count];
            if (kind == null || kind.Prefab == null)
                return;

            foreach (PlayerController hero in players.Players)
            {
                if (hero == null || !hero.HasLanded || _spawned.Count >= maxMobs)
                    continue;
                float3 heroFeet = hero.transform.position;
                float angle = UnityEngine.Random.Range(0f, 2f * math.PI);
                int x = (int)math.floor(heroFeet.x + math.sin(angle) * spawnDistance);
                int z = (int)math.floor(heroFeet.z + math.cos(angle) * spawnDistance);
                int clearance = (int)math.ceil(kind.Motor.HalfExtents.y * 2f);
                if (!SpawnPointFinder.TryFindSurface(world.World, x, z, (int)heroFeet.y + 12, clearance, out float3 feet))
                {
                    Debug.Log($"[Voxelforge] No ground near {hero.PlayerId} for a {kind.DisplayName} yet.", this);
                    continue;
                }

                Mob mob = Instantiate(kind.Prefab, feet, Quaternion.identity);
                mob.ServerInitialize(world, players, feet, (uint)UnityEngine.Random.Range(1, int.MaxValue));
                mob.NetworkObject.Spawn(destroyWithScene: true);
                _spawned.Add(mob);
                Debug.Log($"[Voxelforge] A {kind.DisplayName} landed at {feet}, near {hero.PlayerId}.", this);
            }
        }

        /// <summary>Host only: sends every mob it dropped away.</summary>
        public void DespawnAll()
        {
            foreach (Mob mob in _spawned)
            {
                if (mob != null && mob.IsSpawned)
                    mob.NetworkObject.Despawn();
            }
            _spawned.Clear();
        }
    }
}
